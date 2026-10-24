import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "");
  return {
    plugins: [react()],
    server: {
      proxy: {
        "/api": {
          target: env.VITE_PROXY_TARGET || "http://localhost:8000",
          changeOrigin: true,
          ws: true, // live headset view (WebSocket upgrade); only the dev server reads this
          // Dev-server only: the key is added here, never shipped to the browser.
          headers: env.DASHBOARD_API_KEY ? { "X-API-Key": env.DASHBOARD_API_KEY } : undefined,
        },
      },
    },
  };
});
