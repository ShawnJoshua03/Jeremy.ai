// Instructor call room: rooms, participants, WebRTC signaling, and chat.
// Video and audio travel peer-to-peer between browsers; this module only
// carries the small signaling messages (SDP and ICE) needed to connect them.
//
// Live telemetry (live* tables, bottom of this file): the headset publishes a
// small snapshot of the lap chole sim several times a second, plus events and
// coach captions, and the local instructor dashboard subscribes to them.
import { schema, table, t, SenderError } from 'spacetimedb/server';
import { liveTables, parseJson, num, int, flag, str, type Json } from './live_tables';

const ROLES = new Set(['trainee', 'mentor']);
const SIGNAL_KINDS = new Set(['offer', 'answer', 'ice']);
const CODE_PATTERN = /^[A-Z0-9]{6}$/;
const MAX_NAME = 40;
const MAX_TITLE = 120;
const MAX_CHAT = 500;
const MAX_PAYLOAD = 64 * 1024;

const spacetimedb = schema({
  room: table(
    { public: true },
    {
      code: t.string().primaryKey(),
      title: t.string(),
      hostIdentity: t.identity(),
      createdAt: t.u64(),
      ended: t.bool(),
    },
  ),
  participant: table(
    { public: true },
    {
      identity: t.identity().primaryKey(),
      roomCode: t.string().index('btree'),
      name: t.string(),
      role: t.string(),
      sharing: t.bool(),
      joinedAt: t.u64(),
    },
  ),
  signal: table(
    { public: true },
    {
      id: t.u64().primaryKey().autoInc(),
      roomCode: t.string().index('btree'),
      fromIdentity: t.identity(),
      toIdentity: t.identity().index('btree'),
      kind: t.string(),
      payload: t.string(),
      createdAt: t.u64(),
    },
  ),
  chatMessage: table(
    { public: true },
    {
      id: t.u64().primaryKey().autoInc(),
      roomCode: t.string().index('btree'),
      senderName: t.string(),
      text: t.string(),
      sentAt: t.u64(),
    },
  ),
  ...liveTables,
});

export default spacetimedb;

function clean(value: string, max: number): string {
  // Strip control characters, trim, and cap the length.
  return value.replace(/[\u0000-\u001f\u007f]/g, '').trim().slice(0, max);
}

function requireParticipant(ctx: any) {
  const row = ctx.db.participant.identity.find(ctx.sender);
  if (!row) throw new SenderError('join a session first');
  return row;
}

// Remove a participant, along with any signals queued to or from them.
// Used on rejoin, leave, and disconnect.
function removeParticipant(ctx: any, identity: any) {
  if (!ctx.db.participant.identity.find(identity)) return;
  ctx.db.participant.identity.delete(identity);
  ctx.db.signal.toIdentity.delete(identity);
  for (const signal of [...ctx.db.signal.iter()]) {
    if (signal.fromIdentity.isEqual(identity)) ctx.db.signal.id.delete(signal.id);
  }
}

// A room with nobody left in it can no longer be joined.
function endRoomIfEmpty(ctx: any, code: string) {
  const room = ctx.db.room.code.find(code);
  if (!room || room.ended) return;
  if (ctx.db.participant.roomCode.filter(code).next().done) {
    ctx.db.room.code.update({ ...room, ended: true });
  }
}

export const createRoom = spacetimedb.reducer(
  { code: t.string(), title: t.string(), name: t.string() },
  (ctx, { code, title, name }) => {
    if (!CODE_PATTERN.test(code)) throw new SenderError('invalid session code');
    if (ctx.db.room.code.find(code)) throw new SenderError('session code already in use');
    const now = ctx.timestamp.toMillis();
    ctx.db.room.insert({
      code,
      title: clean(title, MAX_TITLE) || 'Session',
      hostIdentity: ctx.sender,
      createdAt: now,
      ended: false,
    });
    removeParticipant(ctx, ctx.sender);
    ctx.db.participant.insert({
      identity: ctx.sender,
      roomCode: code,
      name: clean(name, MAX_NAME) || 'Mentor',
      role: 'mentor',
      sharing: false,
      joinedAt: now,
    });
  },
);

export const joinRoom = spacetimedb.reducer(
  { code: t.string(), name: t.string(), role: t.string() },
  (ctx, { code, name, role }) => {
    const room = ctx.db.room.code.find(code);
    if (!room) throw new SenderError('no session with that code');
    if (room.ended) throw new SenderError('this session has ended');
    if (!ROLES.has(role)) throw new SenderError('role must be trainee or mentor');
    if (role === 'trainee') {
      for (const row of ctx.db.participant.roomCode.filter(code)) {
        if (row.role === 'trainee' && !row.identity.isEqual(ctx.sender)) {
          throw new SenderError('this session already has a trainee');
        }
      }
    }
    removeParticipant(ctx, ctx.sender);
    ctx.db.participant.insert({
      identity: ctx.sender,
      roomCode: code,
      name: clean(name, MAX_NAME) || role,
      role,
      sharing: false,
      joinedAt: ctx.timestamp.toMillis(),
    });
  },
);

export const leaveRoom = spacetimedb.reducer((ctx) => {
  removeParticipant(ctx, ctx.sender);
});

export const endRoom = spacetimedb.reducer((ctx) => {
  const me = requireParticipant(ctx);
  const room = ctx.db.room.code.find(me.roomCode);
  if (!room) throw new SenderError('session not found');
  if (!room.hostIdentity.isEqual(ctx.sender)) throw new SenderError('only the host can end the session');
  ctx.db.room.code.update({ ...room, ended: true });
  for (const row of [...ctx.db.participant.roomCode.filter(room.code)]) {
    removeParticipant(ctx, row.identity);
  }
});

export const setSharing = spacetimedb.reducer({ sharing: t.bool() }, (ctx, { sharing }) => {
  const me = requireParticipant(ctx);
  if (me.role !== 'trainee') throw new SenderError('only the trainee can share a screen');
  ctx.db.participant.identity.update({ ...me, sharing });
});

export const sendSignal = spacetimedb.reducer(
  { to: t.identity(), kind: t.string(), payload: t.string() },
  (ctx, { to, kind, payload }) => {
    const me = requireParticipant(ctx);
    if (!SIGNAL_KINDS.has(kind)) throw new SenderError('unknown signal kind');
    if (payload.length > MAX_PAYLOAD) throw new SenderError('signal payload is too large');
    const peer = ctx.db.participant.identity.find(to);
    if (!peer || peer.roomCode !== me.roomCode) throw new SenderError('peer is not in this session');
    ctx.db.signal.insert({
      id: 0n,
      roomCode: me.roomCode,
      fromIdentity: ctx.sender,
      toIdentity: to,
      kind,
      payload,
      createdAt: ctx.timestamp.toMillis(),
    });
  },
);

export const ackSignal = spacetimedb.reducer({ id: t.u64() }, (ctx, { id }) => {
  const row = ctx.db.signal.id.find(id);
  if (!row) return;
  if (!row.toIdentity.isEqual(ctx.sender)) throw new SenderError('not your signal');
  ctx.db.signal.id.delete(id);
});

export const sendChat = spacetimedb.reducer({ text: t.string() }, (ctx, { text }) => {
  const me = requireParticipant(ctx);
  const body = clean(text, MAX_CHAT);
  if (!body) throw new SenderError('message is empty');
  ctx.db.chatMessage.insert({
    id: 0n,
    roomCode: me.roomCode,
    senderName: me.name,
    text: body,
    sentAt: ctx.timestamp.toMillis(),
  });
});

export const onDisconnect = spacetimedb.clientDisconnected((ctx) => {
  const row = ctx.db.participant.identity.find(ctx.sender);
  if (!row) return;
  removeParticipant(ctx, ctx.sender);
  endRoomIfEmpty(ctx, row.roomCode);
});

// ---------------- live telemetry ----------------
// Anyone who can reach this database can publish: it is meant for a local
// SpacetimeDB on the instructor's PC. Inputs are size-capped and sanitised.

const MAX_FRAME_JSON = 16 * 1024;
const MAX_EVENTS_PER_SESSION = 600;
const MAX_CAPTIONS_PER_SESSION = 300;
const MAX_SESSIONS = 24;
const SPEAKERS = new Set(['coach', 'sim', 'trainee', 'instructor']);

function sessionKey(sessionId: string): string {
  const id = clean(sessionId, 64);
  if (!/^[A-Za-z0-9_.:-]{1,64}$/.test(id)) throw new SenderError('invalid session id');
  return id;
}

function parsePayload(data: string, max: number): Json {
  try {
    return parseJson(data, max);
  } catch (error) {
    throw new SenderError(error instanceof Error ? error.message : 'invalid JSON');
  }
}

// Create the session row on first contact; also prunes the oldest sessions.
function touchSession(ctx: any, id: string, o: Json | null, now: bigint) {
  const existing = ctx.db.liveSession.sessionId.find(id);
  if (existing) return existing;
  const all = [...ctx.db.liveSession.iter()].sort((a: any, b: any) => (a.lastSeen < b.lastSeen ? -1 : 1));
  while (all.length >= MAX_SESSIONS) deleteSession(ctx, all.shift().sessionId);
  const row = {
    sessionId: id,
    trainee: (o && str(o, 'trainee', 60)) || 'trainee',
    exercise: (o && str(o, 'exercise', 40)) || 'lap_chole',
    device: (o && str(o, 'device', 20)) || 'quest',
    state: 'Ready',
    startedAt: now,
    lastSeen: now,
    elapsedS: 0,
    stepIndex: 0,
    stepCount: 15,
    stepsDone: 0,
    major: 0,
    minor: 0,
    score: -1,
    findings: '',
    frames: 0n,
    anatomy: '',
  };
  ctx.db.liveSession.insert(row);
  return row;
}

function deleteSession(ctx: any, id: string) {
  ctx.db.liveSession.sessionId.delete(id);
  ctx.db.liveFrame.sessionId.delete(id);
  ctx.db.liveEvent.sessionId.delete(id);
  ctx.db.liveCaption.sessionId.delete(id);
}

// Keep only the newest `max` rows of a per-session log table.
function prune(ctx: any, tableName: 'liveEvent' | 'liveCaption', id: string, max: number) {
  const rows = [...ctx.db[tableName].sessionId.filter(id)];
  if (rows.length <= max) return;
  rows.sort((a: any, b: any) => (a.id < b.id ? -1 : 1));
  for (const row of rows.slice(0, rows.length - max)) ctx.db[tableName].id.delete(row.id);
}

/** Session header: trainee, exercise, device. Optional; a frame creates the session too. */
export const liveStart = spacetimedb.reducer(
  { sessionId: t.string(), data: t.string() },
  (ctx, { sessionId, data }) => {
    const id = sessionKey(sessionId);
    const o = parsePayload(data, 12 * 1024);
    const now = ctx.timestamp.toMillis();
    const row = touchSession(ctx, id, o, now);
    const anatomy = str(o, 'anatomy', 10 * 1024);
    ctx.db.liveSession.sessionId.update({
      ...row,
      trainee: str(o, 'trainee', 60) || row.trainee,
      exercise: str(o, 'exercise', 40) || row.exercise,
      device: str(o, 'device', 20) || row.device,
      anatomy: anatomy || row.anatomy,
      lastSeen: now,
    });
  },
);

/** Latest snapshot (5-10 Hz). Replaces the session's frame row and refreshes its header. */
export const pushFrame = spacetimedb.reducer(
  { sessionId: t.string(), data: t.string() },
  (ctx, { sessionId, data }) => {
    const id = sessionKey(sessionId);
    const o = parsePayload(data, MAX_FRAME_JSON);
    const now = ctx.timestamp.toMillis();
    const session = touchSession(ctx, id, o, now);
    const seq = BigInt(int(o, 'seq', 0, Number.MAX_SAFE_INTEGER));
    const previous = ctx.db.liveFrame.sessionId.find(id);
    // Out-of-order HTTP retries: keep the newer frame. A restarted publisher
    // starts at seq 1 again, so a large drop is accepted as a new run.
    if (previous && seq <= previous.seq && previous.seq - seq < 50n) return;
    const frame = {
      sessionId: id,
      seq,
      at: now,
      elapsedS: num(o, 'elapsedS'),
      stepIndex: int(o, 'stepIndex', 0, 99),
      stepId: str(o, 'stepId', 40),
      stepTitle: str(o, 'stepTitle', 80),
      headX: num(o, 'headX'), headY: num(o, 'headY'), headZ: num(o, 'headZ'), headYaw: num(o, 'headYaw'),
      leftValid: flag(o, 'leftValid'), leftX: num(o, 'leftX'), leftY: num(o, 'leftY'), leftZ: num(o, 'leftZ'), leftClosed: flag(o, 'leftClosed'),
      rightValid: flag(o, 'rightValid'), rightX: num(o, 'rightX'), rightY: num(o, 'rightY'), rightZ: num(o, 'rightZ'), rightClosed: flag(o, 'rightClosed'),
      tipValid: flag(o, 'tipValid'), tipX: num(o, 'tipX'), tipY: num(o, 'tipY'), tipZ: num(o, 'tipZ'),
      targetX: num(o, 'targetX'), targetY: num(o, 'targetY'), targetZ: num(o, 'targetZ'),
      targetMm: num(o, 'targetMm', -1),
      contact: str(o, 'contact', 30),
      pressure: num(o, 'pressure'), veil: num(o, 'veil'), balloon: num(o, 'balloon'), contrast: num(o, 'contrast'),
      ductClips: int(o, 'ductClips', 0, 99), arteryClips: int(o, 'arteryClips', 0, 99),
      entryActive: flag(o, 'entryActive'), entryTool: str(o, 'entryTool', 40),
      entryInserted: flag(o, 'entryInserted'), entryHovering: flag(o, 'entryHovering'),
      entryMm: num(o, 'entryMm', -1), entryAngle: num(o, 'entryAngle', -1), entryDepth: num(o, 'entryDepth', -1),
      entryTolMm: num(o, 'entryTolMm'), entryTolDeg: num(o, 'entryTolDeg'),
      entryMinDepth: num(o, 'entryMinDepth'), entryMaxDepth: num(o, 'entryMaxDepth'), entryDanger: num(o, 'entryDanger'),
      entryPopA: num(o, 'entryPopA'), entryPopB: num(o, 'entryPopB'), entryPops: int(o, 'entryPops', 0, 2),
      entryLined: flag(o, 'entryLined'), entryBlocked: flag(o, 'entryBlocked'),
      entryToward: str(o, 'entryToward', 40), entryReadout: str(o, 'entryReadout', 200),
      siteX: num(o, 'siteX'), siteZ: num(o, 'siteZ'),
      toolInHand: flag(o, 'toolInHand'),
      ports: str(o, 'ports', 400),
      feedback: str(o, 'feedback', 300),
      coachStatus: str(o, 'coachStatus', 400),
      caption: str(o, 'caption', 400),
      micLevel: num(o, 'micLevel'), listening: flag(o, 'listening'), onDemand: flag(o, 'onDemand'),
      fps: num(o, 'fps'),
    };
    if (previous) ctx.db.liveFrame.sessionId.update(frame);
    else ctx.db.liveFrame.insert(frame);
    ctx.db.liveSession.sessionId.update({
      ...session,
      state: str(o, 'state', 20) || session.state,
      lastSeen: now,
      elapsedS: frame.elapsedS,
      stepIndex: frame.stepIndex,
      stepCount: int(o, 'stepCount', 1, 99),
      stepsDone: int(o, 'stepsDone', 0, 99),
      major: int(o, 'major', 0, 999),
      minor: int(o, 'minor', 0, 999),
      score: num(o, 'score', -1),
      findings: str(o, 'findings', 600),
      frames: session.frames + 1n,
    });
  },
);

/** One recorded sim event. Retries with an already-stored seq are ignored. */
export const pushEvent = spacetimedb.reducer(
  { sessionId: t.string(), data: t.string() },
  (ctx, { sessionId, data }) => {
    const id = sessionKey(sessionId);
    const o = parsePayload(data, 4096);
    const now = ctx.timestamp.toMillis();
    touchSession(ctx, id, o, now);
    const seq = BigInt(int(o, 'seq', 0, Number.MAX_SAFE_INTEGER));
    if (seq > 0n) {
      for (const row of ctx.db.liveEvent.sessionId.filter(id)) if (row.seq === seq) return;
    }
    const kind = str(o, 'kind', 40);
    if (!kind) throw new SenderError('event kind required');
    ctx.db.liveEvent.insert({
      id: 0n,
      sessionId: id,
      seq,
      at: now,
      elapsedMs: BigInt(int(o, 'elapsedMs', 0, Number.MAX_SAFE_INTEGER)),
      kind,
      objectId: str(o, 'objectId', 40),
      instrument: str(o, 'instrument', 10),
      detail: str(o, 'detail', 64),
      hasValue: flag(o, 'hasValue'), value: num(o, 'value'),
      hasPos: flag(o, 'hasPos'), x: num(o, 'x'), y: num(o, 'y'), z: num(o, 'z'),
      hasTarget: flag(o, 'hasTarget'), tx: num(o, 'tx'), ty: num(o, 'ty'), tz: num(o, 'tz'),
      stepIndex: int(o, 'stepIndex', 0, 99),
      message: str(o, 'message', 300),
    });
    if (seq % 20n === 0n) prune(ctx, 'liveEvent', id, MAX_EVENTS_PER_SESSION);
  },
);

/** A caption line: what the coach said, a sim feedback line, or an instructor note. */
export const pushCaption = spacetimedb.reducer(
  { sessionId: t.string(), speaker: t.string(), text: t.string(), elapsedMs: t.u64(), stepIndex: t.u32() },
  (ctx, { sessionId, speaker, text, elapsedMs, stepIndex }) => {
    const id = sessionKey(sessionId);
    if (!SPEAKERS.has(speaker)) throw new SenderError('unknown speaker');
    const body = clean(text, 400);
    if (!body) return;
    const now = ctx.timestamp.toMillis();
    touchSession(ctx, id, null, now);
    const row = ctx.db.liveCaption.insert({
      id: 0n,
      sessionId: id,
      at: now,
      elapsedMs,
      speaker,
      text: body,
      stepIndex: Math.min(stepIndex, 99),
    });
    if (row.id % 25n === 0n) prune(ctx, 'liveCaption', id, MAX_CAPTIONS_PER_SESSION);
  },
);

/** Remove a session and everything recorded under it (dashboard "clear"). */
export const liveClear = spacetimedb.reducer({ sessionId: t.string() }, (ctx, { sessionId }) => {
  deleteSession(ctx, sessionKey(sessionId));
});
