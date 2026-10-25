// Connects this browser to the other participants in a call room. Offers,
// answers, and ICE candidates travel through SpacetimeDB signal rows; media
// goes peer-to-peer through PeerMesh.

import { useEffect, useRef, useState } from "react";
import type { Identity } from "spacetimedb";
import { PeerMesh, type OutgoingSignal, type SignalKind } from "./peers";
import type { Role } from "./types";

export interface SignalRow {
  id: bigint;
  fromIdentity: Identity;
  kind: string;
  payload: string;
}

export interface MeshParticipant {
  identity: Identity;
  role: string;
}

interface MeshInput {
  role: Role;
  localHex: string;
  participants: readonly MeshParticipant[];
  signals: readonly SignalRow[];
  sendSignal: (to: Identity, kind: SignalKind, payload: string) => Promise<void>;
  ackSignal: (id: bigint) => Promise<void>;
}

export interface RoomMesh {
  // Remote MediaStreams keyed by peer identity hex. Updated as tracks arrive.
  remoteStreams: Map<string, MediaStream>;
  setMic: (track: MediaStreamTrack | null) => Promise<void>;
  setScreen: (track: MediaStreamTrack | null) => Promise<void>;
  closeAll: () => void;
}

export function useRoomMesh(input: MeshInput): RoomMesh {
  const { role, localHex, participants, signals, sendSignal, ackSignal } = input;
  const [remoteStreams, setRemoteStreams] = useState<Map<string, MediaStream>>(() => new Map());

  // Latest reducer functions and identities, read inside PeerMesh callbacks.
  const latest = useRef({ sendSignal, ackSignal });
  latest.current = { sendSignal, ackSignal };
  const identities = useRef(new Map<string, Identity>());
  const meshRef = useRef<PeerMesh | null>(null);
  const handled = useRef(new Set<bigint>());
  const connected = useRef(new Set<string>());

  // Create the mesh once per mounted room.
  useEffect(() => {
    const mesh = new PeerMesh({
      role,
      send: (signal: OutgoingSignal) => {
        const to = identities.current.get(signal.to);
        if (!to) return;
        void latest.current.sendSignal(to, signal.kind, signal.payload).catch(() => undefined);
      },
      onRemoteTrack: (peerId, stream) => {
        setRemoteStreams((previous) => new Map(previous).set(peerId, stream));
      },
      onPeerClosed: (peerId) => {
        setRemoteStreams((previous) => {
          const next = new Map(previous);
          next.delete(peerId);
          return next;
        });
      },
    });
    meshRef.current = mesh;
    return () => {
      mesh.closeAll();
      meshRef.current = null;
      connected.current.clear();
    };
  }, [role]);

  // Keep the identity lookup current for signaling.
  useEffect(() => {
    identities.current = new Map(participants.map((p) => [p.identity.toHexString(), p.identity]));
  }, [participants]);

  // Trainee: offer to each mentor who is present; drop those who left.
  useEffect(() => {
    const mesh = meshRef.current;
    if (!mesh || role !== "trainee") return;
    const present = new Set(
      participants.filter((p) => p.role === "mentor").map((p) => p.identity.toHexString()),
    );
    for (const peerId of [...connected.current]) {
      if (!present.has(peerId)) {
        mesh.dropPeer(peerId);
        connected.current.delete(peerId);
      }
    }
    for (const peerId of present) {
      if (peerId === localHex || connected.current.has(peerId)) continue;
      connected.current.add(peerId);
      void mesh.connectTo(peerId).catch(() => connected.current.delete(peerId));
    }
  }, [participants, role, localHex]);

  // Apply incoming signals once each, then remove them from the server.
  useEffect(() => {
    const mesh = meshRef.current;
    if (!mesh) return;
    for (const signal of signals) {
      if (handled.current.has(signal.id)) continue;
      handled.current.add(signal.id);
      const from = signal.fromIdentity.toHexString();
      void mesh
        .handleSignal(from, signal.kind as SignalKind, signal.payload)
        .catch(() => undefined)
        .finally(() => void latest.current.ackSignal(signal.id).catch(() => undefined));
    }
  }, [signals]);

  return {
    remoteStreams,
    setMic: async (track) => meshRef.current?.setMic(track),
    setScreen: async (track) => meshRef.current?.setScreen(track),
    closeAll: () => meshRef.current?.closeAll(),
  };
}
