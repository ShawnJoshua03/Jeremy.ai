// Live telemetry tables for the local instructor dashboard.
//
// The headset (LiveTelemetry.cs) or the fake publisher calls the live*
// reducers over SpacetimeDB's HTTP API with one JSON string per call, so the
// Unity side only needs JsonUtility and UnityWebRequest. The reducers parse
// that JSON into the typed rows below, which the dashboard subscribes to.
//
// Coordinates are board-local metres: origin at the navel, +x toward the feet,
// +y up, +z toward the patient's right.
import { table, t } from 'spacetimedb/server';

export const liveTables = {
  // One row per attempt. Small, updated with every frame.
  liveSession: table(
    { public: true },
    {
      sessionId: t.string().primaryKey(),
      trainee: t.string(),
      exercise: t.string(),
      device: t.string(), // quest | editor | sim
      state: t.string(), // Ready | Running | Paused | Completed | Cancelled
      startedAt: t.u64(), // server epoch ms
      lastSeen: t.u64(),
      elapsedS: t.f64(),
      stepIndex: t.u32(),
      stepCount: t.u32(),
      stepsDone: t.u32(),
      major: t.u32(),
      minor: t.u32(),
      score: t.f32(), // -1 = not computed
      findings: t.string(), // "code:M|code:m|..." (M major, m minor), in time order
      frames: t.u64(),
      // Board-local polylines of the anatomy, sent once by liveStart:
      // 'cystic_duct:x,y,z;x,y,z|cbd:...|fundus:x,y,z'. Empty until sent.
      anatomy: t.string(),
    },
  ),
  // Latest snapshot per session, replaced in place at 5-10 Hz.
  liveFrame: table(
    { public: true },
    {
      sessionId: t.string().primaryKey(),
      seq: t.u64(),
      at: t.u64(),
      elapsedS: t.f64(),
      stepIndex: t.u32(),
      stepId: t.string(),
      stepTitle: t.string(),
      // Head (board-local) and yaw in degrees.
      headX: t.f32(), headY: t.f32(), headZ: t.f32(), headYaw: t.f32(),
      // Instrument tips, board-local.
      leftValid: t.bool(), leftX: t.f32(), leftY: t.f32(), leftZ: t.f32(), leftClosed: t.bool(),
      rightValid: t.bool(), rightX: t.f32(), rightY: t.f32(), rightZ: t.f32(), rightClosed: t.bool(),
      // Working tip inside the abdomen and the current step's target.
      tipValid: t.bool(), tipX: t.f32(), tipY: t.f32(), tipZ: t.f32(),
      targetX: t.f32(), targetY: t.f32(), targetZ: t.f32(),
      targetMm: t.f32(), // -1 = no tip
      contact: t.string(), // structure under the jaws, '' = none
      pressure: t.f32(), veil: t.f32(), balloon: t.f32(), contrast: t.f32(),
      ductClips: t.u32(), arteryClips: t.u32(),
      // Hand-held entry (Veress and trocars).
      entryActive: t.bool(), entryTool: t.string(), entryInserted: t.bool(), entryHovering: t.bool(),
      entryMm: t.f32(), entryAngle: t.f32(), entryDepth: t.f32(), // -1 = unknown
      entryTolMm: t.f32(), entryTolDeg: t.f32(),
      entryMinDepth: t.f32(), entryMaxDepth: t.f32(), entryDanger: t.f32(),
      entryPopA: t.f32(), entryPopB: t.f32(), entryPops: t.u32(), // pop depths (mm) and pops felt so far
      entryLined: t.bool(), entryBlocked: t.bool(), entryToward: t.string(), entryReadout: t.string(),
      siteX: t.f32(), siteZ: t.f32(),
      toolInHand: t.bool(),
      ports: t.string(), // "id:x:z|..." board-local x/z of placed or planned ports
      feedback: t.string(),
      coachStatus: t.string(),
      caption: t.string(),
      micLevel: t.f32(), listening: t.bool(), onDemand: t.bool(),
      fps: t.f32(),
    },
  ),
  // Recorded sim events (EventRecorder lap events).
  liveEvent: table(
    { public: true },
    {
      id: t.u64().primaryKey().autoInc(),
      sessionId: t.string().index('btree'),
      seq: t.u64(), // the recorder's sequence, used to drop retries
      at: t.u64(),
      elapsedMs: t.u64(),
      kind: t.string(),
      objectId: t.string(),
      instrument: t.string(),
      detail: t.string(),
      hasValue: t.bool(), value: t.f32(),
      hasPos: t.bool(), x: t.f32(), y: t.f32(), z: t.f32(),
      hasTarget: t.bool(), tx: t.f32(), ty: t.f32(), tz: t.f32(),
      stepIndex: t.u32(),
      message: t.string(), // the sim's feedback line at the time
    },
  ),
  // Coach captions, sim feedback lines and trainee speech, in order.
  liveCaption: table(
    { public: true },
    {
      id: t.u64().primaryKey().autoInc(),
      sessionId: t.string().index('btree'),
      at: t.u64(),
      elapsedMs: t.u64(),
      speaker: t.string(), // coach | sim | trainee | instructor
      text: t.string(),
      stepIndex: t.u32(),
    },
  ),
};

// ---- JSON helpers for the reducers ----

export type Json = Record<string, unknown>;

export function parseJson(data: string, max: number): Json {
  if (data.length > max) throw new Error('payload too large');
  const value = JSON.parse(data);
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('payload must be an object');
  return value as Json;
}

export function num(o: Json, key: string, fallback = 0): number {
  const v = o[key];
  return typeof v === 'number' && Number.isFinite(v) ? v : fallback;
}

export function int(o: Json, key: string, min = 0, max = 0xffffffff): number {
  return Math.max(min, Math.min(max, Math.round(num(o, key, min))));
}

export function flag(o: Json, key: string): boolean {
  return o[key] === true || o[key] === 1;
}

export function str(o: Json, key: string, max = 200): string {
  const v = o[key];
  if (typeof v !== 'string') return '';
  return v.replace(/[\u0000-\u0008\u000b-\u001f\u007f]/g, '').slice(0, max);
}
