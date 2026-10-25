import React from "react";
import ReactDOM from "react-dom/client";
import { ConvexProvider, ConvexReactClient } from "convex/react";
import { SpacetimeDBProvider } from "spacetimedb/react";
import Root from "./Root";
import { DbConnection } from "./module_bindings";
import "./styles.css";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error("Dashboard root element was not found.");
}

const convexUrl = import.meta.env.VITE_CONVEX_URL?.trim();
let convexClient: ConvexReactClient | undefined;
let convexConfigError = "";

if (convexUrl) {
  try {
    const parsedUrl = new URL(convexUrl);
    if (parsedUrl.protocol !== "https:" && parsedUrl.protocol !== "http:") {
      throw new Error("Convex URL must use HTTP or HTTPS.");
    }
    convexClient = new ConvexReactClient(convexUrl);
  } catch (error) {
    convexConfigError =
      error instanceof Error ? error.message : "Convex URL is invalid.";
  }
}

// Live call (SpacetimeDB). The token lives in sessionStorage, so each tab is its
// own identity. That lets one machine test both the trainee and the mentor.
const spacetimeUri = import.meta.env.VITE_SPACETIMEDB_URI?.trim();
const spacetimeDb = import.meta.env.VITE_SPACETIMEDB_DB?.trim();
const callTokenKey = "jeremy.ai.call.token";

function readCallToken(): string | undefined {
  try {
    return window.sessionStorage.getItem(callTokenKey) ?? undefined;
  } catch {
    return undefined;
  }
}

function writeCallToken(token: string) {
  try {
    window.sessionStorage.setItem(callTokenKey, token);
  } catch {
    // Storage unavailable: the next load gets a new anonymous identity.
  }
}

const callBuilder =
  spacetimeUri && spacetimeDb
    ? DbConnection.builder()
        .withUri(spacetimeUri)
        .withDatabaseName(spacetimeDb)
        .withToken(readCallToken())
        .onConnect((_conn, _identity, token) => writeCallToken(token))
    : undefined;

const app = (
  <Root
    convexReady={Boolean(convexClient)}
    convexConfigError={convexConfigError}
    callConfigured={Boolean(callBuilder)}
  />
);
const withCall = callBuilder ? (
  <SpacetimeDBProvider connectionBuilder={callBuilder}>{app}</SpacetimeDBProvider>
) : (
  app
);

ReactDOM.createRoot(rootElement).render(
  <React.StrictMode>
    {convexClient ? (
      <ConvexProvider client={convexClient}>{withCall}</ConvexProvider>
    ) : (
      withCall
    )}
  </React.StrictMode>,
);
