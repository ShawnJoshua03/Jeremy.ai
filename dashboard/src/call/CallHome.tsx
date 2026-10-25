import { useState, type FormEvent } from "react";
import { useReducer } from "spacetimedb/react";
import { reducers } from "../module_bindings";
import type { Joined, Role } from "./types";

// No 0/O or 1/I, so a code read aloud or typed from a screen is unambiguous.
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
const CODE_LENGTH = 6;
const MAX_CREATE_ATTEMPTS = 3;

function newCode(): string {
  const bytes = new Uint32Array(CODE_LENGTH);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (value) => CODE_ALPHABET[value % CODE_ALPHABET.length]).join("");
}

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : "Something went wrong. Try again.";
}

interface CallHomeProps {
  onJoined: (joined: Joined) => void;
}

export function CallHome({ onJoined }: CallHomeProps) {
  const createRoom = useReducer(reducers.createRoom);
  const joinRoom = useReducer(reducers.joinRoom);
  const inviteCode = new URLSearchParams(window.location.search).get("room")?.toUpperCase() ?? "";

  const [mentorName, setMentorName] = useState("");
  const [title, setTitle] = useState("Lap chole practice");
  const [createError, setCreateError] = useState("");
  const [creating, setCreating] = useState(false);

  const [code, setCode] = useState(inviteCode);
  const [traineeName, setTraineeName] = useState("");
  const [role, setRole] = useState<Role>("trainee");
  const [joinError, setJoinError] = useState("");
  const [joining, setJoining] = useState(false);

  async function onCreate(event: FormEvent) {
    event.preventDefault();
    setCreating(true);
    setCreateError("");
    try {
      let created: string | null = null;
      // A code collision is rare, but retry a few times with a new code.
      for (let attempt = 0; attempt < MAX_CREATE_ATTEMPTS && !created; attempt += 1) {
        const candidate = newCode();
        try {
          await createRoom({ code: candidate, title: title.trim() || "Session", name: mentorName.trim() || "Mentor" });
          created = candidate;
        } catch (error) {
          if (!errorText(error).includes("already in use")) throw error;
        }
      }
      if (!created) throw new Error("Could not create a session. Try again.");
      onJoined({ code: created, name: mentorName.trim() || "Mentor", role: "mentor", joinNeeded: false });
    } catch (error) {
      setCreateError(errorText(error));
    } finally {
      setCreating(false);
    }
  }

  async function onJoin(event: FormEvent) {
    event.preventDefault();
    const cleanCode = code.trim().toUpperCase();
    setJoining(true);
    setJoinError("");
    try {
      await joinRoom({ code: cleanCode, name: traineeName.trim() || role, role });
      onJoined({ code: cleanCode, name: traineeName.trim() || role, role, joinNeeded: false });
    } catch (error) {
      setJoinError(errorText(error));
    } finally {
      setJoining(false);
    }
  }

  return (
    <div className="call-home">
      <section className="panel call-card">
        <h2>Start a session</h2>
        <p className="panel-subtitle">You are the mentor. Share the code or invite link with the trainee.</p>
        <form onSubmit={onCreate} className="call-form">
          <label>
            Your name
            <input value={mentorName} maxLength={40} onChange={(event) => setMentorName(event.currentTarget.value)} required />
          </label>
          <label>
            Session title
            <input value={title} maxLength={120} onChange={(event) => setTitle(event.currentTarget.value)} />
          </label>
          <button type="submit" className="button" disabled={creating || !mentorName.trim()}>
            {creating ? "Creating…" : "Create session"}
          </button>
          {createError && <p className="call-error" role="alert">{createError}</p>}
        </form>
      </section>

      <section className="panel call-card">
        <h2>Join a session</h2>
        <p className="panel-subtitle">Trainees join with the code, then share their Meta Quest Link window.</p>
        <form onSubmit={onJoin} className="call-form">
          <label>
            Session code
            <input
              value={code}
              maxLength={CODE_LENGTH}
              autoCapitalize="characters"
              onChange={(event) => setCode(event.currentTarget.value.toUpperCase())}
              required
            />
          </label>
          <label>
            Your name
            <input value={traineeName} maxLength={40} onChange={(event) => setTraineeName(event.currentTarget.value)} required />
          </label>
          <fieldset className="call-role">
            <legend>I am the</legend>
            <label>
              <input type="radio" name="role" checked={role === "trainee"} onChange={() => setRole("trainee")} /> Trainee
            </label>
            <label>
              <input type="radio" name="role" checked={role === "mentor"} onChange={() => setRole("mentor")} /> Mentor
            </label>
          </fieldset>
          <button type="submit" className="button button-secondary" disabled={joining || code.length !== CODE_LENGTH || !traineeName.trim()}>
            {joining ? "Joining…" : "Join session"}
          </button>
          {joinError && <p className="call-error" role="alert">{joinError}</p>}
        </form>
      </section>
    </div>
  );
}
