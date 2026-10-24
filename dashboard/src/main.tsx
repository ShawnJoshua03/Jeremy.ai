import React from "react";
import ReactDOM from "react-dom/client";
import { ConvexProvider, ConvexReactClient } from "convex/react";
import App from "./App";
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

const app = <App convexReady={Boolean(convexClient)} convexConfigError={convexConfigError} />;

ReactDOM.createRoot(rootElement).render(
  <React.StrictMode>
    {convexClient ? (
      <ConvexProvider client={convexClient}>{app}</ConvexProvider>
    ) : (
      app
    )}
  </React.StrictMode>,
);
