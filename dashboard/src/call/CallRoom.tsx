import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import type { Identity } from "spacetimedb";
import { useReducer, useTable } from "spacetimedb/react";
import { reducers, tables } from "../module_bindings";
import { getMic, getScreen } from "./media";
import { useRoomMesh } from "./useRoomMesh";
import type { Joined } from "./types";

const MAX_CHAT = 500;

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : "Something went wrong.";
}

// Plays one remote stream. The stage video is muted; audio plays through a
// separate element so each remote voice is heard once.
function StreamElement({ stream, kind }: { stream: MediaStream; kind: "video" | "audio" }) {
  const ref = useRef<HTMLVideoElement & HTMLAudioElement>(null);
  useEffect(() => {
    if (ref.current) ref.current.srcObject = stream;
  }, [stream]);
  if (kind === "audio") return <audio ref={ref} autoPlay />;
  return <video ref={ref} autoPlay playsInline muted className="call-stage-video" />;
}

interface CallRoomProps {
  identity: Identity;
  localHex: string;
  joined: Joined;
  onExit: () => void;
}

export function CallRoom({ identity, localHex, joined, onExit }: CallRoomProps) {
  const { code, name, role, joinNeeded } = joined;

  const joinRoom = useReducer(reducers.joinRoom);
  const leaveRoom = useReducer(reducers.leaveRoom);
  const endRoom = useReducer(reducers.endRoom);
  const setSharing = useReducer(reducers.setSharing);
  const sendSignal = useReducer(reducers.sendSignal);
  const ackSignal = useReducer(reducers.ackSignal);
  const sendChat = useReducer(reducers.sendChat);

  const roomQuery = useMemo(() => tables.room.where((r) => r.code.eq(code)), [code]);
  const participantQuery = useMemo(() => tables.participant.where((r) => r.roomCode.eq(code)), [code]);
  const chatQuery = useMemo(() => tables.chatMessage.where((r) => r.roomCode.eq(code)), [code]);
  const signalQuery = useMemo(() => tables.signal.where((r) => r.toIdentity.eq(identity)), [identity]);

  const [rooms] = useTable(roomQuery);
  const [participants] = useTable(participantQuery);
  const [chat] = useTable(chatQuery);
  const [signals] = useTable(signalQuery);

  const room = rooms[0];
  const ended = room?.ended ?? false;
  const me = participants.find((p) => p.identity.toHexString() === localHex);
  const sharing = participants.some((p) => p.role === "trainee" && p.sharing);

  const [joinError, setJoinError] = useState("");
  const [actionError, setActionError] = useState("");
  const [muted, setMuted] = useState(false);
  const [shareOn, setShareOn] = useState(false);
  const [copied, setCopied] = useState(false);
  const [chatText, setChatText] = useState("");
  const micTrack = useRef<MediaStreamTrack | null>(null);
  const screenTrack = useRef<MediaStreamTrack | null>(null);
  const joinStarted = useRef(false);

  const mesh = useRoomMesh({
    role,
    localHex,
    participants,
    signals,
    sendSignal: (to, kind, payload) => sendSignal({ to, kind, payload }),
    ackSignal: (id) => ackSignal({ id }),
  });

  // After a reload, join the room again. A fresh create/join already did this.
  useEffect(() => {
    if (!joinNeeded || joinStarted.current) return;
    joinStarted.current = true;
    joinRoom({ code, name, role }).catch((error) => setJoinError(errorText(error)));
  }, [code, joinNeeded, joinRoom, name, role]);

  // Microphone for both roles, on entry.
  useEffect(() => {
    let cancelled = false;
    getMic()
      .then(async (track) => {
        if (cancelled) {
          track.stop();
          return;
        }
        micTrack.current = track;
        await mesh.setMic(track);
      })
      .catch((error) => setActionError(`Microphone unavailable: ${errorText(error)}`));
    return () => {
      cancelled = true;
      micTrack.current?.stop();
      micTrack.current = null;
    };
    // The mesh object is stable per mounted room; mic is set once on entry.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Stop the screen capture if this view unmounts. Leaving the room is explicit
  // (Leave or End) or happens on disconnect, so StrictMode remounts are safe.
  useEffect(() => {
    return () => {
      screenTrack.current?.stop();
      screenTrack.current = null;
    };
  }, []);

  // Keep the trainee's share flag in step with the server row.
  useEffect(() => {
    setShareOn(Boolean(me?.sharing));
  }, [me?.sharing]);

  const chatRows = useMemo(() => [...chat].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)), [chat]);
  const remoteStreams = [...mesh.remoteStreams.entries()];
  const stageStream = remoteStreams.find(([, stream]) => stream.getVideoTracks().length > 0)?.[1];

  function toggleMute() {
    const next = !muted;
    if (micTrack.current) micTrack.current.enabled = !next;
    setMuted(next);
  }

  async function startShare() {
    setActionError("");
    try {
      const track = await getScreen(() => void stopShare());
      screenTrack.current = track;
      await mesh.setScreen(track);
      await setSharing({ sharing: true });
      setShareOn(true);
    } catch (error) {
      setActionError(`Could not share the screen: ${errorText(error)}`);
    }
  }

  async function stopShare() {
    screenTrack.current?.stop();
    screenTrack.current = null;
    await mesh.setScreen(null);
    setShareOn(false);
    await setSharing({ sharing: false }).catch(() => undefined);
  }

  async function onChat(event: FormEvent) {
    event.preventDefault();
    const text = chatText.trim().slice(0, MAX_CHAT);
    if (!text) return;
    setChatText("");
    await sendChat({ text }).catch((error) => setActionError(errorText(error)));
  }

  async function onEnd() {
    await endRoom().catch((error) => setActionError(errorText(error)));
    onExit();
  }

  async function onLeave() {
    mesh.closeAll();
    screenTrack.current?.stop();
    await leaveRoom().catch(() => undefined);
    onExit();
  }

  async function copyInvite() {
    const url = `${window.location.origin}${window.location.pathname}?room=${code}`;
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      setActionError(`Copy failed. Invite link: ${url}`);
    }
  }

  if (joinError) {
    return (
      <section className="panel call-card">
        <h2>Could not join</h2>
        <p className="call-error" role="alert">{joinError}</p>
        <button type="button" className="button button-secondary" onClick={onExit}>Back</button>
      </section>
    );
  }

  if (ended) {
    return (
      <section className="panel call-card">
        <h2>Session ended</h2>
        <p className="panel-subtitle">The mentor ended this session. Nothing was recorded.</p>
        <button type="button" className="button" onClick={onExit}>Back to call home</button>
      </section>
    );
  }

  return (
    <div className="call-room">
      <header className="call-header">
        <div>
          <h2>{room?.title ?? "Session"}</h2>
          <p className="panel-subtitle">
            Code <strong>{code}</strong> · you are the {role}
          </p>
        </div>
        <div className="call-header-actions">
          <button type="button" className="button button-secondary" onClick={copyInvite}>
            {copied ? "Copied" : "Copy invite link"}
          </button>
          {role === "mentor" && (
            <button type="button" className="button button-secondary" onClick={onEnd}>End session</button>
          )}
          <button type="button" className="button button-secondary" onClick={onLeave}>Leave</button>
        </div>
      </header>

      {role === "trainee" && (
        <p className="call-banner" role="status">
          Your shared window is visible to everyone in this room. Nothing is recorded.
        </p>
      )}
      {actionError && <p className="call-error" role="alert">{actionError}</p>}

      <div className="call-grid">
        <section className="call-stage" aria-label="Shared screen">
          {stageStream && sharing ? (
            <StreamElement stream={stageStream} kind="video" />
          ) : (
            <p className="call-stage-empty">
              {role === "trainee"
                ? shareOn
                  ? "Sharing your window. Waiting for the mentor."
                  : "Share your Meta Quest Link window to show the mentor."
                : "Waiting for the trainee to share their screen."}
            </p>
          )}
          {remoteStreams.map(([peerId, stream]) => (
            <StreamElement key={peerId} stream={stream} kind="audio" />
          ))}
        </section>

        <aside className="call-side">
          <section className="call-people" aria-label="Participants">
            <h3>In this session</h3>
            <ul>
              {participants.map((p) => (
                <li key={p.identity.toHexString()}>
                  <span>{p.name}</span>
                  <span className="call-badge">{p.role}</span>
                  {p.role === "trainee" && p.sharing && <span className="call-badge call-badge-live">sharing</span>}
                </li>
              ))}
            </ul>
          </section>

          <section className="call-chat" aria-label="Chat">
            <ol>
              {chatRows.map((row) => (
                <li key={row.id.toString()}>
                  <strong>{row.senderName}</strong> {row.text}
                </li>
              ))}
            </ol>
            <form onSubmit={onChat} className="call-chat-form">
              <input
                value={chatText}
                maxLength={MAX_CHAT}
                placeholder="Message"
                aria-label="Chat message"
                onChange={(event) => setChatText(event.currentTarget.value)}
              />
              <button type="submit" className="button button-secondary" disabled={!chatText.trim()}>Send</button>
            </form>
          </section>
        </aside>
      </div>

      <footer className="call-controls">
        <button type="button" className="button button-secondary" onClick={toggleMute} aria-pressed={muted}>
          {muted ? "Unmute mic" : "Mute mic"}
        </button>
        {role === "trainee" && (
          shareOn ? (
            <button type="button" className="button" onClick={() => void stopShare()}>Stop sharing</button>
          ) : (
            <button type="button" className="button" onClick={startShare}>Share Meta Quest Link window</button>
          )
        )}
      </footer>
    </div>
  );
}
