import { useEffect, useState } from "react";
import App from "./App";
import { CallApp } from "./call/CallApp";
import { Landing } from "./Landing";
import { LiveApp } from "./live/LiveApp";

type Mode = "home" | "call" | "console" | "sample" | "live";

// The landing page is home (the live view when VITE_HOME=live, as in the local stack).
// The instructor console is at #console, its sample sessions at #sample, the call room at #call
// and the live headset view at #live. Hash routing keeps this working on a static host with no router.
const HOME: Mode = import.meta.env.VITE_HOME === "live" ? "live" : "home";

function modeFromHash(): Mode {
  if (window.location.hash === "#console") return "console";
  if (window.location.hash === "#sample") return "sample";
  if (window.location.hash === "#live") return "live";
  if (window.location.hash === "#call") return "call";
  return HOME;
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
    window.location.hash = "call";
  }

  if (mode === "live") {
    if (!callConfigured) {
      return (
        <p className="call-error" role="alert">
          The live view needs SpacetimeDB. Set VITE_SPACETIMEDB_URI and VITE_SPACETIMEDB_DB (local: ws://localhost:3000 and jeremy-ai-live).
        </p>
      );
    }
    return (
      <LiveApp
        nav={
          <nav aria-label="Views">
            <a href="#call">Call room</a>
            <a href="#console">Console</a>
          </nav>
        }
      />
    );
  }

  if (mode === "console" || mode === "sample") {
    // key: switching between #console and #sample remounts, so the sample flag is re-read.
    return (
      <App
        key={mode}
        convexReady={convexReady}
        convexConfigError={convexConfigError}
        onOpenCall={openCall}
        startInSample={mode === "sample"}
      />
    );
  }

  if (mode === "home") return <Landing />;

  return (
    <div className="call-shell">
      <header className="call-topbar">
        <a className="call-brand" href="#home">Jeremy.ai · Live call</a>
        <span className="call-header-actions">
          {callConfigured && (
            <a className="button button-secondary" href="#live">Live headset view</a>
          )}
          <button type="button" className="button button-secondary" onClick={openConsole}>
            Instructor console
          </button>
        </span>
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
