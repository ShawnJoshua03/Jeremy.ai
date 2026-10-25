// Relays the captured casting window to other open dashboards (another tab, or
// another PC on the LAN that loads this dashboard). Reuses the call room's
// peer-to-peer WebRTC (call/peers.ts) and SpacetimeDB signaling: the capturing
// dashboard is the "trainee" (sender) of a fixed room, viewers are "mentors".
import { useEffect, useMemo, useState } from "react";
import type { Identity } from "spacetimedb";
import { useReducer, useTable } from "spacetimedb/react";
import { reducers, tables } from "../module_bindings";
import { useRoomMesh } from "../call/useRoomMesh";

export const RELAY_CODE = "LIVE00";

function message(error: unknown) {
  return error instanceof Error ? error.message : "relay failed";
}

interface RelayProps {
  identity: Identity;
  onStatus: (status: string) => void;
}

/** Sender: publishes the local capture to every viewer that joins. */
export function RelaySender({ identity, stream, onStatus }: RelayProps & { stream: MediaStream }) {
  const localHex = identity.toHexString();
  const openCastRoom = useReducer(reducers.openCastRoom);
  const joinRoom = useReducer(reducers.joinRoom);
  const leaveRoom = useReducer(reducers.leaveRoom);
  const setSharing = useReducer(reducers.setSharing);
  const sendSignal = useReducer(reducers.sendSignal);
  const ackSignal = useReducer(reducers.ackSignal);
  const participantQuery = useMemo(() => tables.participant.where((r) => r.roomCode.eq(RELAY_CODE)), []);
  const signalQuery = useMemo(() => tables.signal.where((r) => r.toIdentity.eq(identity)), [identity]);
  const [participants] = useTable(participantQuery);
  const [signals] = useTable(signalQuery);
  const [joined, setJoined] = useState(false);

  const mesh = useRoomMesh({
    role: "trainee",
    localHex,
    participants: joined ? participants : [],
    signals,
    sendSignal: (to, kind, payload) => sendSignal({ to, kind, payload }),
    ackSignal: (id) => ackSignal({ id }),
  });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        await openCastRoom({ code: RELAY_CODE });
        await joinRoom({ code: RELAY_CODE, name: "Casting PC", role: "trainee" });
        await setSharing({ sharing: true });
        if (!cancelled) setJoined(true);
      } catch (error) {
        if (!cancelled) onStatus(`Relay off: ${message(error)}`);
      }
    })();
    return () => {
      cancelled = true;
      void leaveRoom().catch(() => undefined);
    };
    // Once per mount; the parent remounts this for a new capture.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const track = stream.getVideoTracks()[0] ?? null;
  useEffect(() => {
    void mesh.setScreen(track);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [track, joined]);

  const viewers = participants.filter((p) => p.role === "mentor").length;
  useEffect(() => {
    if (joined) onStatus(viewers ? `Relaying to ${viewers} viewer${viewers > 1 ? "s" : ""}` : "Relay ready: no other viewers");
  }, [joined, viewers, onStatus]);
  return null;
}

/** Viewer: joins the relay room when another dashboard is sharing and hands its stream up. */
export function RelayViewer({ identity, onStream, onStatus }: RelayProps & { onStream: (stream: MediaStream | null) => void }) {
  const localHex = identity.toHexString();
  const joinRoom = useReducer(reducers.joinRoom);
  const leaveRoom = useReducer(reducers.leaveRoom);
  const sendSignal = useReducer(reducers.sendSignal);
  const ackSignal = useReducer(reducers.ackSignal);
  const participantQuery = useMemo(() => tables.participant.where((r) => r.roomCode.eq(RELAY_CODE)), []);
  const signalQuery = useMemo(() => tables.signal.where((r) => r.toIdentity.eq(identity)), [identity]);
  const [participants] = useTable(participantQuery);
  const [signals] = useTable(signalQuery);

  const sharing = participants.some((p) => p.role === "trainee" && p.sharing && p.identity.toHexString() !== localHex);
  const mesh = useRoomMesh({
    role: "mentor",
    localHex,
    participants,
    signals,
    sendSignal: (to, kind, payload) => sendSignal({ to, kind, payload }),
    ackSignal: (id) => ackSignal({ id }),
  });

  // Join while someone is sharing; leave when they stop or this unmounts.
  useEffect(() => {
    if (!sharing) return;
    let active = true;
    joinRoom({ code: RELAY_CODE, name: "Dashboard viewer", role: "mentor" })
      .then(() => active && onStatus("Watching the relayed headset view"))
      .catch((error) => active && onStatus(`Relay unavailable: ${message(error)}`));
    return () => {
      active = false;
      void leaveRoom().catch(() => undefined);
    };
  }, [sharing, joinRoom, leaveRoom, onStatus]);

  const stream = [...mesh.remoteStreams.values()].find((s) => s.getVideoTracks().length > 0) ?? null;
  useEffect(() => {
    onStream(sharing ? stream : null);
  }, [stream, sharing, onStream]);
  return null;
}

/** True when some other dashboard is currently relaying a capture. */
export function useRelayAvailable(localHex: string | undefined) {
  const participantQuery = useMemo(() => tables.participant.where((r) => r.roomCode.eq(RELAY_CODE)), []);
  const [participants] = useTable(participantQuery);
  return participants.some((p) => p.role === "trainee" && p.sharing && p.identity.toHexString() !== localHex);
}
