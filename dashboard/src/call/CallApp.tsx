import { useState } from "react";
import { useSpacetimeDB } from "spacetimedb/react";
import { CallHome } from "./CallHome";
import { CallRoom } from "./CallRoom";
import type { Joined } from "./types";

const joinedKey = "jeremy.ai.call.joined";

// sessionStorage, so each browser tab keeps its own room. Storage can be
// unavailable (private mode, blocked site data), so every access is guarded.
function readJoined(): Joined | null {
  try {
    const raw = window.sessionStorage.getItem(joinedKey);
    if (!raw) return null;
    // After a reload the room must be joined again with the same identity.
    return { ...(JSON.parse(raw) as Joined), joinNeeded: true };
  } catch {
    return null;
  }
}

function writeJoined(joined: Joined | null) {
  try {
    if (joined) window.sessionStorage.setItem(joinedKey, JSON.stringify({ ...joined, joinNeeded: false }));
    else window.sessionStorage.removeItem(joinedKey);
  } catch {
    // Ignore: the page still works, it just will not rejoin after a reload.
  }
}

export function CallApp() {
  const { isActive, identity, connectionError } = useSpacetimeDB();
  const [joined, setJoined] = useState<Joined | null>(readJoined);

  function update(next: Joined | null) {
    writeJoined(next);
    setJoined(next);
  }

  if (connectionError) {
    return (
      <p className="call-error" role="alert">
        The call server is unreachable: {connectionError.message}
      </p>
    );
  }
  if (!isActive || !identity) {
    return <p className="panel-subtitle" role="status">Connecting to the call server…</p>;
  }
  if (joined) {
    return (
      <CallRoom
        key={joined.code}
        identity={identity}
        localHex={identity.toHexString()}
        joined={joined}
        onExit={() => update(null)}
      />
    );
  }
  return <CallHome onJoined={update} />;
}
