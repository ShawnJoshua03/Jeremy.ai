import { ApiError, getApiBase } from "./api";

// Wire format from backend/live: binary [kind:u8][t_ms:u64 LE][payload].
// Only LivePanel.tsx imports this file. No other module parses the live stream.
export const KIND_JPEG = 1;
export const KIND_PCM16 = 2;
export const HEADER_BYTES = 9;

export interface LiveHello {
  type: "hello";
  video?: { w?: number; h?: number; fps?: number };
  audio?: { rate?: number; channels?: number };
  passthrough?: boolean;
}

export interface LiveStatus {
  type: "status";
  live: boolean;
  viewers: number;
  passthrough: boolean;
}

export type LiveServerText = LiveHello | LiveStatus;

export interface LiveFrame {
  kind: number;
  tMs: number;
  payload: Uint8Array;
}

export function parseLiveFrame(buffer: ArrayBuffer): LiveFrame | null {
  if (buffer.byteLength < HEADER_BYTES) return null;
  const view = new DataView(buffer);
  return {
    kind: view.getUint8(0),
    // The sender's clock is a Unix ms value; it fits in a double.
    tMs: Number(view.getBigUint64(1, true)),
    payload: new Uint8Array(buffer, HEADER_BYTES),
  };
}

// PCM16 little-endian mono to Float32 in [-1, 1).
export function pcm16ToFloat32(payload: Uint8Array): Float32Array<ArrayBuffer> {
  const samples = payload.byteLength >> 1;
  const out = new Float32Array(samples);
  const view = new DataView(payload.buffer, payload.byteOffset, samples * 2);
  for (let i = 0; i < samples; i += 1) {
    out[i] = view.getInt16(i * 2, true) / 32768;
  }
  return out;
}

export async function fetchViewerToken(sessionId: string): Promise<string> {
  const response = await fetch(
    `${getApiBase()}/sessions/${encodeURIComponent(sessionId)}/live/viewer-token`,
    { method: "POST", headers: { Accept: "application/json" } },
  );
  if (!response.ok) throw new ApiError("Could not get a live view token.", response.status);
  const body = (await response.json()) as { token?: string };
  return body.token ?? "";
}

// Build a ws(s) URL from the same API base the REST calls use.
export function watchUrl(sessionId: string, token: string): string {
  const url = new URL(`${getApiBase()}/sessions/${encodeURIComponent(sessionId)}/live/watch`, window.location.href);
  url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
  if (token) url.searchParams.set("token", token);
  return url.toString();
}
