// Instructor call room: rooms, participants, WebRTC signaling, and chat.
// Video and audio travel peer-to-peer between browsers; this module only
// carries the small signaling messages (SDP and ICE) needed to connect them.
import { schema, table, t, SenderError } from 'spacetimedb/server';

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
