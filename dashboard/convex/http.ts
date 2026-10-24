import { anyApi, httpActionGeneric, httpRouter } from "convex/server";
import { maxBatchSize } from "./validators";

const maxBodyBytes = 1_048_576;

function getIngestSecret() {
  return (
    globalThis as typeof globalThis & {
      process?: { env?: Record<string, string | undefined> };
    }
  ).process?.env?.CONVEX_INGEST_SHARED_SECRET;
}

function constantTimeEqual(left: string, right: string) {
  const length = Math.max(left.length, right.length);
  let difference = left.length ^ right.length;
  for (let index = 0; index < length; index += 1) {
    difference |= (left.charCodeAt(index) || 0) ^ (right.charCodeAt(index) || 0);
  }
  return difference === 0;
}

function errorResponse(message: string, status: number) {
  return new Response(JSON.stringify({ error: message }), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const ingest = httpActionGeneric(async (ctx, request) => {
  const secret = getIngestSecret();
  if (!secret || secret.length < 32 || secret.length > 256) {
    return errorResponse("Ingest is not configured.", 503);
  }
  const authorization = request.headers.get("Authorization") ?? "";
  if (
    authorization.length > 263 ||
    !constantTimeEqual(authorization, `Bearer ${secret}`)
  ) {
    return errorResponse("Unauthorized.", 401);
  }

  const contentLength = Number(request.headers.get("Content-Length"));
  if (Number.isFinite(contentLength) && contentLength > maxBodyBytes) {
    return errorResponse("Request body is too large.", 413);
  }

  const body = await request.text();
  if (new TextEncoder().encode(body).byteLength > maxBodyBytes) {
    return errorResponse("Request body is too large.", 413);
  }

  let snapshot: unknown;
  try {
    snapshot = JSON.parse(body);
  } catch {
    return errorResponse("Request body must be valid JSON.", 400);
  }
  if (
    typeof snapshot !== "object" ||
    snapshot === null ||
    !("sessions" in snapshot) ||
    !("events" in snapshot) ||
    !("annotations" in snapshot) ||
    !("commands" in snapshot) ||
    !Array.isArray(snapshot.sessions) ||
    !Array.isArray(snapshot.events) ||
    !Array.isArray(snapshot.annotations) ||
    !Array.isArray(snapshot.commands)
  ) {
    return errorResponse("Snapshot must include sessions, events, annotations, and commands arrays.", 400);
  }
  if (
    snapshot.sessions.length > maxBatchSize ||
    snapshot.events.length > maxBatchSize ||
    snapshot.annotations.length > maxBatchSize ||
    snapshot.commands.length > maxBatchSize
  ) {
    return errorResponse("Each snapshot array is limited to 1,000 records.", 413);
  }

  await ctx.runMutation(anyApi.annotations.ingestSnapshot, snapshot);
  return new Response(null, { status: 204 });
});

const http = httpRouter();
http.route({ path: "/v1/mirror", method: "POST", handler: ingest });

export default http;
