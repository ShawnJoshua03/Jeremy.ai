import { useEffect, useState } from "react";
import App from "./App";
import { CallApp } from "./call/CallApp";

type Mode = "call" | "console";

// The call room is the home page. The instructor console stays reachable at
// #console. Hash routing keeps this working on a static host with no router.
function modeFromHash(): Mode {
  return window.location.hash === "#console" ? "console" : "call";
}

interface RootProps {
  convexReady: boolean;
  convexConfigError: string;
  callConfigured: boolean;
}

export default function Root({ convexReady, convexConfigError, callConfigured }: RootProps) {
  const [mode, setMode] = useState<Mode>(modeFromHash);

  useEffect(() => {
    const onHashChange = () => setMode(modeFromHash());
    window.addEventListener("hashchange", onHashChange);
    return () => window.removeEventListener("hashchange", onHashChange);
  }, []);

  function openConsole() {
    window.location.hash = "console";
  }

  function openCall() {
    // Leave the hash empty so the call room is the default view.
    window.history.replaceState(null, "", window.location.pathname + window.location.search);
    setMode("call");
  }

  if (mode === "console") {
    return <App convexReady={convexReady} convexConfigError={convexConfigError} onOpenCall={openCall} />;
  }

  return (
    <div className="call-shell">
      <header className="call-topbar">
        <span className="call-brand">Jeremy.ai · Live call</span>
        <button type="button" className="button button-secondary" onClick={openConsole}>
          Instructor console
        </button>
      </header>
      <main className="call-main">
        {callConfigured ? (
          <CallApp />
        ) : (
          <p className="call-error" role="alert">
            The live call is not configured. Set VITE_SPACETIMEDB_URI and VITE_SPACETIMEDB_DB.
          </p>
        )}
      </main>
    </div>
  );
}
