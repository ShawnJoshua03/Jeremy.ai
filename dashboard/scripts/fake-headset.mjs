#!/usr/bin/env node
// Fake headset: publishes a simulated lap chole attempt to the local
// SpacetimeDB at 10 Hz, through the same HTTP reducer calls the Quest makes
// (LiveTelemetry.cs), so the live dashboard can be demoed without a headset.
//
//   node scripts/fake-headset.mjs                 # one clean run at 1x speed
//   node scripts/fake-headset.mjs --sloppy --loop # errors and findings, forever
//   node scripts/fake-headset.mjs --speed 3       # 3x faster
//
// Options: --url http://127.0.0.1:3000  --db jeremy-ai-live  --session <id>
//          --trainee "Demo trainee"  --hz 10  --speed 1  --sloppy  --loop  --hold <step>

const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 && args[i + 1] && !args[i + 1].startsWith("--") ? args[i + 1] : fallback;
};
const has = (name) => args.includes(`--${name}`);

const BASE = opt("url", process.env.SPACETIME_URL || "http://127.0.0.1:3000").replace(/\/$/, "");
const DB = opt("db", process.env.SPACETIME_DB || "jeremy-ai-live");
const HZ = Math.max(1, Math.min(30, Number(opt("hz", 10))));
const SPEED = Math.max(0.1, Number(opt("speed", 1)));
const SLOPPY = has("sloppy");
const LOOP = has("loop");
const HOLD = opt("hold", null) === null ? -1 : Number(opt("hold", -1));
const TRAINEE = opt("trainee", SLOPPY ? "Resident B (sim)" : "Resident A (sim)");

// ---------------------------------------------------------------- transport

let failures = 0;
// Events and captions go out in order (one chain); frames are awaited by the loop.
let chain = Promise.resolve();
const post = (...a) => { chain = chain.then(() => call(...a)); };
async function call(reducer, ...params) {
  try {
    const response = await fetch(`${BASE}/v1/database/${DB}/call/${reducer}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(params),
    });
    if (!response.ok) {
      const text = await response.text();
      if (failures++ % 20 === 0) console.warn(`${reducer}: HTTP ${response.status} ${text.slice(0, 160)}`);
    } else failures = 0;
  } catch (error) {
    if (failures++ % 20 === 0) console.warn(`${reducer}: ${error.message} (is SpacetimeDB running at ${BASE}?)`);
  }
}

// ---------------------------------------------------------------- geometry (board-local metres)

const v = (x, y, z) => ({ x, y, z });
const add = (a, b) => v(a.x + b.x, a.y + b.y, a.z + b.z);
const sub = (a, b) => v(a.x - b.x, a.y - b.y, a.z - b.z);
const mul = (a, s) => v(a.x * s, a.y * s, a.z * s);
const len = (a) => Math.hypot(a.x, a.y, a.z);
const norm = (a) => mul(a, 1 / (len(a) || 1));
const cross = (a, b) => v(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
const lerp = (a, b, t) => add(a, mul(sub(b, a), t));
const clamp01 = (x) => Math.max(0, Math.min(1, x));
const smooth = (t) => { t = clamp01(t); return t * t * (3 - 2 * t); };
const rnd = (a = 1) => (Math.random() * 2 - 1) * a;

// The anatomy frame of LapCholeAnatomy.Create: origin at Calot's triangle, z from the umbilical port.
const CALOT = v(-0.11, -0.085, 0.06);
const UMB = v(0.03, 0.03, 0);
const fwd = norm(sub(CALOT, UMB));
const right = norm(cross(v(-1, 0.55, 0), fwd));
const up = cross(fwd, right);
const toBoard = (p) => add(CALOT, add(mul(right, p[0]), add(mul(up, p[1]), mul(fwd, p[2]))));
const path = (pts) => pts.map(toBoard);

const ANATOMY = {
  cystic_duct: path([[-0.005, -0.064, 0.013], [0.001, -0.046, 0.008], [0.003, -0.026, 0.005], [0.002, -0.01, 0.004], [0, -0.002, 0.004]]),
  cbd: path([[0.012, -0.17, 0.012], [0.004, -0.11, 0.014], [-0.006, -0.064, 0.016], [-0.014, -0.015, 0.022], [-0.02, 0.025, 0.032], [-0.024, 0.05, 0.045]]),
  hepatic_artery: path([[0.034, -0.17, 0.022], [0.028, -0.1, 0.019], [0.024, -0.055, 0.018], [0.022, -0.015, 0.026], [0.012, 0.025, 0.038], [0.004, 0.05, 0.05]]),
  cystic_artery: path([[0.024, -0.058, 0.018], [0.022, -0.036, 0.009], [0.016, -0.016, 0.002], [0.009, 0, -0.002], [0.004, 0.01, -0.004]]),
  gallbladder: path([[0, -0.004, 0.004], [-0.008, 0.008, 0.002], [-0.024, 0.02, 0], [-0.042, 0.032, 0.002], [-0.058, 0.044, 0.008], [-0.07, 0.054, 0.016]]),
};
const FUNDUS = toBoard([-0.066, 0.051, 0.013]);

function along(points, metres) {
  for (let i = 1; i < points.length; i++) {
    const d = len(sub(points[i], points[i - 1]));
    if (metres <= d) return lerp(points[i - 1], points[i], metres / d);
    metres -= d;
  }
  return points[points.length - 1];
}
const pathLength = (points) => points.slice(1).reduce((s, p, i) => s + len(sub(p, points[i])), 0);

function encodeAnatomy() {
  const f = (n) => n.toFixed(4);
  const parts = Object.entries(ANATOMY).map(([id, pts]) => `${id}:${pts.map((p) => `${f(p.x)},${f(p.y)},${f(p.z)}`).join(";")}`);
  parts.push(`fundus:${f(FUNDUS.x)},${f(FUNDUS.y)},${f(FUNDUS.z)}`);
  parts.push(`calot:${f(CALOT.x)},${f(CALOT.y)},${f(CALOT.z)}`);
  return parts.join("|");
}

// Planned port sites, as in LapCholeDressing.Ports (x, z), skin about 3 cm up once insufflated.
const SITES = { veress: [0, 0], umbilical: [0, 0], epigastric: [-0.15, 0.01], rsc_medial: [-0.11, 0.08], rsc_lateral: [-0.05, 0.13] };
const skin = (x, z, dome = 1) => v(x, 0.03 * dome * Math.exp(-(x * x + z * z) / 0.03), z);

// ---------------------------------------------------------------- the procedure

const STEPS = [
  ["veress", "Veress needle"], ["insufflate", "Insufflation"], ["port_umbilical", "Umbilical port"],
  ["port_epigastric", "Epigastric port"], ["port_rsc_medial", "Right subcostal port (medial)"],
  ["port_rsc_lateral", "Right subcostal port (lateral)"], ["retract", "Retract the gallbladder"],
  ["dissect", "Dissect Calot's triangle"], ["clip_duct_distal", "Distal clip, cystic duct"], ["ductotomy", "Ductotomy"],
  ["cholangiogram", "Cholangiogram"], ["clip_duct_proximal", "Proximal clips, cystic duct"], ["divide_duct", "Divide the cystic duct"],
  ["clip_artery", "Clip the cystic artery"], ["divide_artery", "Divide the cystic artery"],
];
const ENTRY = {
  veress: { name: "Veress needle", tol: 15, deg: 35, popA: 9, popB: 18, min: 22, max: 38, danger: 45, port: "veress" },
  port_umbilical: { name: "12 mm balloon trocar", tol: 10, deg: 30, popA: 14, popB: 26, min: 32, max: 50, danger: 60, port: "umbilical" },
  port_epigastric: { name: "10 mm trocar", tol: 15, deg: 32, popA: 12, popB: 24, min: 30, max: 48, danger: 58, port: "epigastric" },
  port_rsc_medial: { name: "5 mm trocar", tol: 15, deg: 35, popA: 10, popB: 20, min: 26, max: 44, danger: 54, port: "rsc_medial" },
  port_rsc_lateral: { name: "5 mm trocar", tol: 15, deg: 35, popA: 10, popB: 20, min: 26, max: 44, danger: 54, port: "rsc_lateral" },
};
const COACH_INTRO = {
  veress: "Pick up the Veress needle from the tray. Tip on the purple mark at the navel, shaft upright, then push through. Two pops.",
  insufflate: "The needle stays in. Pinch and hold to run CO2; let go between 12 and 15.",
  port_umbilical: "Take the 12 mm balloon trocar. Same puncture, straight down, until it pops through.",
  port_epigastric: "10 mm trocar below the breastbone, aimed toward the right upper quadrant.",
  port_rsc_medial: "5 mm trocar below the right ribs, on the mid-clavicular line.",
  port_rsc_lateral: "Second 5 mm trocar, out on the right flank.",
  retract: "Grasper through the lateral port. Take the fundus and lift it toward the right shoulder.",
  dissect: "Maryland dissector. Short sweeps across the yellow area. Stay off the blue common bile duct.",
  clip_duct_distal: "Clip applier. One clip on the cystic duct, close to the gallbladder.",
  ductotomy: "Scissors. A small cut in the cystic duct just below that clip.",
  cholangiogram: "Thread the catheter into the ductotomy, then hold to inject contrast.",
  clip_duct_proximal: "Two clips on the bile duct side of the ductotomy.",
  divide_duct: "Divide the duct in the protected gap between the clips.",
  clip_artery: "Now the salmon cystic artery: two clips hepatic side, one gallbladder side.",
  divide_artery: "Divide the artery between the clips.",
};

const DUCT_LEN = pathLength(ANATOMY.cystic_duct);
const ART_LEN = pathLength(ANATOMY.cystic_artery);

function targetFor(stepId) {
  switch (stepId) {
    case "veress": case "insufflate": case "port_umbilical": return skin(0, 0);
    case "port_epigastric": return skin(...SITES.epigastric);
    case "port_rsc_medial": return skin(...SITES.rsc_medial);
    case "port_rsc_lateral": return skin(...SITES.rsc_lateral);
    case "retract": return FUNDUS;
    case "dissect": return CALOT;
    case "clip_duct_distal": return along(ANATOMY.cystic_duct, DUCT_LEN * 0.82);
    case "ductotomy": case "cholangiogram": return along(ANATOMY.cystic_duct, DUCT_LEN * 0.7);
    case "clip_duct_proximal": return along(ANATOMY.cystic_duct, DUCT_LEN * 0.3);
    case "divide_duct": return along(ANATOMY.cystic_duct, DUCT_LEN * 0.5);
    case "clip_artery": return along(ANATOMY.cystic_artery, ART_LEN * 0.35);
    case "divide_artery": return along(ANATOMY.cystic_artery, ART_LEN * 0.55);
  }
  return CALOT;
}

// ---------------------------------------------------------------- findings (mirror of EventRecorder.LapFindings)

function findings(events) {
  const out = [];
  let cvs = Infinity, lastPressure = null;
  for (const e of events)
    if ((e.kind === "step_completed" && e.objectId === "dissect") || (e.kind === "critical_view" && e.value >= 100)) cvs = Math.min(cvs, e.elapsedMs);
  for (const e of events) {
    switch (e.kind) {
      case "injury": out.push(["injury", true]); break;
      case "hand_collision": out.push(["hand_collision", false]); break;
      case "port_placed":
        if ((e.hasPos && e.hasTarget && len(sub(v(e.x, e.y, e.z), v(e.tx, e.ty, e.tz))) * 1000 > 15) || Math.abs(e.value) > 20) out.push(["port_offset", false]);
        break;
      case "insufflation": lastPressure = e; break;
      case "clip_applied":
        if (e.objectId === "cbd") out.push(["cbd_clip", true]);
        if (e.detail === "crossing") out.push(["clip_crossing", false]);
        break;
      case "ductotomy": if (e.elapsedMs < cvs) out.push(["ductotomy_before_cvs", true]); break;
      case "cholangiogram": if (e.detail === "leak" || e.detail === "not_in_duct") out.push([`cholangiogram_${e.detail}`, false]); break;
      case "structure_cut":
        if (e.objectId === "cbd") out.push(["cbd_cut", true]);
        if (e.elapsedMs < cvs) out.push(["division_before_cvs", true]);
        break;
    }
  }
  if (lastPressure && (lastPressure.value < 12 || lastPressure.value > 15)) out.push(["insufflation_out_of_range", false]);
  return out;
}

// ---------------------------------------------------------------- one run

async function run(runIndex) {
  const sessionId = opt("session", `sim-${Date.now().toString(36)}`);
  const anatomy = encodeAnatomy();
  if (opt("session", null)) await call("live_clear", sessionId); // a reused id starts clean
  await call("live_start", sessionId, JSON.stringify({ trainee: TRAINEE, exercise: "lap_chole", device: "sim", anatomy }));
  console.log(`session ${sessionId} -> ${BASE} / ${DB}  (${SLOPPY ? "sloppy" : "clean"}, ${SPEED}x, ${HZ} Hz)`);

  const events = [];
  const ports = {}; // id -> {x, z}
  let seq = 0, eventSeq = 0, elapsed = 0, step = 0, state = "Ready";
  let caption = "", captionUntil = 0, feedback = "", coachStatus = "";
  let tip = v(0.1, 0.12, -0.12), tipValid = false;
  let pressure = 0, veil = 1, balloon = 0, contrast = 0, ductClips = 0, arteryClips = 0, contact = "";
  let entry = null; // live entry state
  let toolInHand = true;
  let phase = "start", phaseT = 0;
  const elapsedMs = () => Math.round(elapsed * 1000);

  const say = (speaker, text) => {
    if (speaker === "coach") { caption = text; captionUntil = elapsed + 5; }
    if (speaker === "sim") feedback = text;
    post("push_caption", sessionId, speaker, text, elapsedMs(), step);
  };
  const record = (kind, objectId, extra = {}) => {
    const e = { seq: ++eventSeq, elapsedMs: elapsedMs(), kind, objectId: objectId ?? "", instrument: "right", detail: "", hasValue: false, value: 0,
      hasPos: false, x: 0, y: 0, z: 0, hasTarget: false, tx: 0, ty: 0, tz: 0, stepIndex: step, message: feedback, ...extra };
    events.push(e);
    post("push_event", sessionId, JSON.stringify(e));
  };
  const complete = (text) => {
    say("sim", text);
    record("step_completed", STEPS[step][0], { hasValue: true, value: elapsed });
    step++;
    phase = "start"; phaseT = 0; contact = ""; entry = null;
    if (step >= STEPS.length) { state = "Completed"; say("coach", "That's the procedure complete. Nice, controlled work."); }
  };

  // Per-run randomness: which entry goes wide in sloppy mode.
  const wideStep = SLOPPY ? "port_rsc_medial" : null;
  const overPressure = SLOPPY;
  const cbdBrush = SLOPPY;

  const dt = 1 / HZ;
  let lastFrame = {};
  let wallLast = Date.now();
  state = "Running";
  say("coach", "Let's begin. " + COACH_INTRO.veress);

  while (state !== "Completed") {
    const now = Date.now();
    const realDt = Math.min(0.5, (now - wallLast) / 1000);
    wallLast = now;
    const h = realDt * SPEED;
    const id = STEPS[Math.min(step, STEPS.length - 1)][0];
    const target = targetFor(id);
    const holding = HOLD >= 0 && step === HOLD;
    elapsed += h;
    phaseT += holding ? 0 : h;

    if (phase === "start") {
      if (step > 0) say("coach", COACH_INTRO[id]);
      phase = ENTRY[id] ? "pickup" : id === "insufflate" ? "insufflate" : "approach";
      phaseT = 0;
      if (ENTRY[id]) {
        const s = ENTRY[id];
        const [sx, sz] = SITES[s.port];
        const wide = id === wideStep;
        entry = { s, site: skin(sx, sz), mm: 45, angle: 28, depth: 0, inserted: false, hovering: false, lined: false, blocked: false,
          popA: false, popB: false, aimMm: wide ? 17 + Math.random() * 3 : 2 + Math.random() * 4, aimDeg: wide ? 14 : 3 + Math.random() * 5,
          blockedOnce: wide, toward: "toward the head" };
        toolInHand = false;
      }
    }

    // ---- hand-held entry: pick up, hover onto the mark, insert, hold
    if (entry) {
      const e = entry, s = e.s;
      if (phase === "pickup") {
        tipValid = false;
        coachStatus = `First pick up the ${s.name}. It's glowing on the instrument tray on your right. Pinch it.`;
        if (phaseT > 2.2) { toolInHand = true; phase = "hover"; phaseT = 0; say("sim", `${s.name} in hand.`); }
      } else if (phase === "hover") {
        e.hovering = true;
        const k = smooth(phaseT / 6);
        e.mm = Math.max(e.aimMm, 45 * (1 - k) + e.aimMm * k + rnd(1.2));
        e.angle = Math.max(e.aimDeg, 28 * (1 - k) + e.aimDeg * k + rnd(1));
        e.lined = e.mm <= s.tol && e.angle <= s.deg * 0.5;
        const dirAway = norm(v(0.6, 0, -0.8));
        tip = add(e.site, mul(dirAway, e.mm / 1000));
        tipValid = true;
        e.toward = e.mm > s.tol ? "toward the head" : "";
        coachStatus = e.mm > s.tol ? `Not yet. The tip is ${e.mm.toFixed(0)} mm from the mark; move it ${e.toward}.`
          : `Yes. You're on the mark and lined up with the guide. Push straight in, slow and steady.`;
        if (phaseT > 2.5 && phaseT - h <= 2.5) say("coach", "Good, closer.");
        if (phaseT > 6.2) {
          if (e.blockedOnce) {
            e.blockedOnce = false; e.blocked = true; phase = "blocked"; phaseT = 0;
            say("sim", "Blocked: the entry was off the mark. Lift out and re-aim.");
            say("coach", "The skin stopped you. You were off the mark. Lift the tip out, re-aim, and go again.");
            record("hand_collision", "abdominal_wall", { hasPos: true, ...pos(tip) });
          } else { phase = "insert"; phaseT = 0; e.hovering = false; say("coach", "That's it. Lined up. Push in, slow and steady."); }
        }
      } else if (phase === "blocked") {
        coachStatus = "The skin stopped you because the entry was off the mark or at the wrong angle. Lift the tip out, re-aim, and go again.";
        if (phaseT > 2) { e.blocked = false; phase = "hover"; phaseT = 0; e.aimMm = 18 + Math.random() * 3; e.aimDeg = 12; }
      } else if (phase === "insert") {
        e.inserted = true;
        e.goal ??= (s.min + s.max) / 2 + (SLOPPY && id === "port_umbilical" ? 14 : 0);
        const goal = e.goal;
        e.depth = e.depth > goal ? Math.max(goal, e.depth - h * 6) : Math.min(goal, e.depth + h * 9);
        tip = sub(e.site, v(0, e.depth / 1000, 0));
        if (!e.popA && e.depth >= s.popA) { e.popA = true; say("sim", ENTRY_POP(id, 0)); }
        if (!e.popB && e.depth >= s.popB) { e.popB = true; say("sim", ENTRY_POP(id, 1)); }
        coachStatus = !e.popA ? `You're through the skin, ${e.depth.toFixed(0)} mm in. Keep pushing gently until the fascia gives.`
          : !e.popB ? `Through the fascia at ${e.depth.toFixed(0)} mm. One more layer, the peritoneum.`
          : e.depth > s.max ? `You're in, but ${e.depth.toFixed(0)} mm is a bit deep. Ease back a little.` : `Yes, you're in at ${e.depth.toFixed(0)} mm. Hold still and it will seat.`;
        if (e.depth > s.max && phaseT > 2 && !e.warned) { e.warned = true; e.goal = (s.min + s.max) / 2; say("coach", "Ease back a little. You're past the target depth."); }
        if (Math.abs(e.depth - goal) < 0.01) {
          if (phaseT > 4.5) {
            const hit = add(e.site, v(e.aimMm / 1000 * 0.6, 0, -e.aimMm / 1000 * 0.8));
            ports[s.port] = { x: hit.x, z: hit.z };
            if (s.port === "veress") ports.umbilical ??= { x: hit.x, z: hit.z };
            say("sim", `${s.name} placed: ${e.aimMm.toFixed(0)} mm from the mark, ${e.aimDeg.toFixed(0)}° off the planned axis.`);
            if (id !== "veress") record("port_placed", s.port, { hasValue: true, value: e.aimDeg, hasPos: true, ...pos(hit), hasTarget: true, ...tpos(e.site), detail: "hand" });
            if (id === "port_umbilical") { phase = "balloon"; phaseT = 0; }
            else complete(id === "veress" ? "Veress needle in. Ready to insufflate." : `${s.name} seated.`);
          }
        }
      } else if (phase === "balloon") {
        balloon = clamp01(phaseT / 4);
        coachStatus = `The trocar is seated. The balloon is at ${Math.round(balloon * 100)} percent; keep holding.`;
        if (balloon >= 1) complete("Balloon up, port sealed, scope in.");
      }
    } else if (phase === "insufflate") {
      const goal = overPressure ? 16.4 : 13.6;
      pressure = Math.min(goal, pressure + h * 1.6);
      tipValid = true; tip = skin(0, 0);
      coachStatus = `Pressure is ${pressure.toFixed(0)}. ${pressure < 12 ? "Keep holding, and let go between twelve and fifteen." : pressure <= 15 ? "Let go now, you're in range." : "That's too high. Let go."}`;
      if (pressure > 15 && pressure - h * 1.6 <= 15) say("coach", "That's too high. Let go.");
      if (pressure >= goal) {
        record("insufflation", "", { hasValue: true, value: pressure });
        complete(`Insufflated to ${pressure.toFixed(1)} mmHg.`);
      }
    } else if (phase === "approach" || phase === "work") {
      // ---- inside the abdomen: tip travels from the port toward the target, then works
      tipValid = true;
      const portId = id === "retract" ? "rsc_lateral" : "epigastric";
      const p = ports[portId] ?? { x: SITES[portId][0], z: SITES[portId][1] };
      const start = v(p.x, 0.0, p.z);
      if (phase === "approach") {
        const k = smooth(phaseT / 4.5);
        const wobble = v(rnd(0.003), rnd(0.002), rnd(0.003));
        tip = add(lerp(start, target, k), mul(wobble, 1 - k * 0.6));
        const d = len(sub(tip, target)) * 1000;
        coachStatus = `The tip is about ${d.toFixed(0)} mm from the target. Move toward the yellow glow.`;
        contact = "";
        // Sloppy: brush the common bile duct on the way to the duct clips.
        if (cbdBrush && id === "clip_duct_distal" && phaseT > 2.2 && phaseT < 3.6) {
          contact = "cbd"; tip = add(along(ANATOMY.cbd, 0.06), v(0, 0.002, 0));
          coachStatus = "Stop. Your jaws are on the common bile duct. Come up onto the cystic duct.";
          if (phaseT - h <= 2.2) say("coach", "Careful. That's the common bile duct under your jaws. Come up onto the cystic duct.");
        }
        if (phaseT > 4.8) { phase = "work"; phaseT = 0; }
      } else {
        const expected = id === "retract" ? "fundus" : id === "dissect" ? "veil" : id.includes("artery") ? "cystic_artery" : "cystic_duct";
        contact = expected;
        tip = add(target, v(rnd(0.0015), rnd(0.001), rnd(0.0015)));
        coachStatus = id === "dissect" ? `About ${Math.round(veil * 100)} percent of the peritoneum is left. Keep making short sweeps across the yellow area.`
          : `Yes. You're on the ${expected.replace("_", " ")}. Close the jaws.`;
        if (id === "retract" && phaseT > 1.5) complete("Fundus grasped and retracted over the liver.");
        else if (id === "dissect") {
          const sweep = Math.sin(elapsed * 5);
          tip = add(CALOT, v(sweep * 0.012, rnd(0.001), Math.cos(elapsed * 2.3) * 0.01));
          veil = Math.max(0, veil - h / 14);
          if (veil < 0.5 && veil + h / 14 >= 0.5) say("coach", "Keep sweeping. Short strokes across the yellow area.");
          if (veil <= 0.02) { record("critical_view", "", { hasValue: true, value: 100 }); complete("Critical view of safety: cystic duct and artery clearly exposed."); }
        } else if (id === "clip_duct_distal" && phaseT > 1.2) {
          ductClips = 1; record("clip_applied", "cystic_duct", { hasValue: true, value: DUCT_LEN * 820, hasPos: true, ...pos(target), detail: "ok" });
          complete("Distal clip on the cystic duct.");
        } else if (id === "ductotomy" && phaseT > 1.2) {
          record("ductotomy", "cystic_duct", { hasValue: true, value: DUCT_LEN * 700, hasPos: true, ...pos(target) });
          complete("Ductotomy made just below the clip.");
        } else if (id === "cholangiogram") {
          contrast = clamp01((phaseT - 1) / 5);
          coachStatus = `The catheter is in. Contrast is at ${Math.round(contrast * 100)} percent; keep holding.`;
          if (contrast >= 1) { record("cholangiogram", "cystic_duct", { hasValue: true, value: 5, detail: "ok" }); complete("Cholangiogram: normal filling, no leak."); }
        } else if (id === "clip_duct_proximal") {
          if (phaseT > 1.2 && ductClips < 2) { ductClips = 2; record("clip_applied", "cystic_duct", { hasValue: true, value: DUCT_LEN * 200, hasPos: true, ...pos(along(ANATOMY.cystic_duct, DUCT_LEN * 0.2)), detail: "ok" }); say("sim", "First proximal clip on."); }
          if (phaseT > 3 && ductClips < 3) {
            ductClips = 3;
            const crossing = SLOPPY;
            record("clip_applied", "cystic_duct", { hasValue: true, value: DUCT_LEN * 350, hasPos: true, ...pos(along(ANATOMY.cystic_duct, DUCT_LEN * 0.35)), detail: crossing ? "crossing" : "ok" });
            if (crossing) say("coach", "That clip went on at an angle. It's holding, but square it up next time.");
            complete("Two proximal clips on the cystic duct.");
          }
        } else if (id === "divide_duct" && phaseT > 1.4) {
          record("structure_cut", "cystic_duct", { hasValue: true, value: DUCT_LEN * 500, hasPos: true, ...pos(target) });
          complete("Cystic duct divided in the protected gap.");
        } else if (id === "clip_artery") {
          const plan = [0.2, 0.35, 0.8];
          for (let i = 0; i < 3; i++) {
            if (phaseT > 1 + i * 1.6 && arteryClips === i) {
              arteryClips = i + 1;
              const at = along(ANATOMY.cystic_artery, ART_LEN * plan[i]);
              tip = at;
              record("clip_applied", "cystic_artery", { hasValue: true, value: ART_LEN * plan[i] * 1000, hasPos: true, ...pos(at), detail: "ok" });
              if (i < 2) say("sim", `Artery clip ${i + 1} of 3.`);
            }
          }
          if (arteryClips === 3 && phaseT > 5.2) complete("Three clips on the cystic artery.");
        } else if (id === "divide_artery" && phaseT > 1.4) {
          record("structure_cut", "cystic_artery", { hasValue: true, value: ART_LEN * 550, hasPos: true, ...pos(target) });
          complete("Cystic artery divided between the clips.");
        }
      }
    }

    // ---- frame
    const found = findings(events);
    const major = found.filter((f) => f[1]).length;
    const minor = found.length - major;
    const done = Math.min(step, STEPS.length);
    const portScores = events.filter((e) => e.kind === "port_placed").map((e) => clamp01(1 - 0.5 * len(sub(v(e.x, e.y, e.z), v(e.tx, e.ty, e.tz))) * 1000 / 15 - 0.5 * Math.abs(e.value) / 20));
    const ins = events.findLast?.((e) => e.kind === "insufflation");
    let score = 100 * (0.35 * done / STEPS.length + 0.15 * (portScores.length ? portScores.reduce((a, b) => a + b, 0) / portScores.length : 0)
      + 0.1 * (ins && ins.value >= 12 && ins.value <= 15 ? 1 : 0) + 0.1 * (events.some((e) => e.kind === "critical_view") ? 1 : 0)
      + 0.1 * clamp01(1 - elapsed / 600) + 0.1 * clamp01(1 - minor / 5));
    if (major > 0) score = Math.min(score, 40);

    const sway = elapsed * 0.4;
    const head = v(0.02 + Math.sin(sway) * 0.03, 0.52 + Math.sin(sway * 1.7) * 0.01, -0.42 + Math.cos(sway) * 0.02);
    const stepNow = Math.min(step, STEPS.length - 1);
    const frame = {
      seq: ++seq, state, elapsedS: elapsed, stepIndex: stepNow, stepCount: STEPS.length, stepsDone: done,
      stepId: STEPS[stepNow][0], stepTitle: STEPS[stepNow][1],
      major, minor, score: Math.round(score * 10) / 10, findings: found.map(([c, m]) => `${c}:${m ? "M" : "m"}`).join("|"),
      headX: head.x, headY: head.y, headZ: head.z, headYaw: 8 + Math.sin(sway) * 12,
      leftValid: true, leftX: -0.02 + Math.sin(elapsed) * 0.01, leftY: 0.12, leftZ: -0.2, leftClosed: false,
      rightValid: tipValid, rightX: tip.x, rightY: Math.max(tip.y, 0.0) + 0.1, rightZ: tip.z - 0.05, rightClosed: phase === "work",
      tipValid, tipX: tip.x, tipY: tip.y, tipZ: tip.z,
      targetX: target.x, targetY: target.y, targetZ: target.z,
      targetMm: tipValid ? len(sub(tip, target)) * 1000 : -1,
      contact, pressure, veil, balloon, contrast, ductClips, arteryClips,
      entryActive: Boolean(entry), entryTool: entry?.s.name ?? "", entryInserted: Boolean(entry?.inserted), entryHovering: Boolean(entry?.hovering),
      entryMm: entry ? (entry.inserted ? entry.aimMm : entry.mm) : -1, entryAngle: entry ? (entry.inserted ? entry.aimDeg : entry.angle) : -1,
      entryDepth: entry?.inserted ? entry.depth : -1,
      entryTolMm: entry?.s.tol ?? 0, entryTolDeg: entry?.s.deg ?? 0, entryMinDepth: entry?.s.min ?? 0, entryMaxDepth: entry?.s.max ?? 0, entryDanger: entry?.s.danger ?? 0,
      entryPopA: entry?.s.popA ?? 0, entryPopB: entry?.s.popB ?? 0, entryPops: entry ? (entry.popB ? 2 : entry.popA ? 1 : 0) : 0,
      entryLined: Boolean(entry?.lined), entryBlocked: Boolean(entry?.blocked), entryToward: entry?.toward ?? "",
      entryReadout: entry ? (entry.inserted ? `depth ${entry.depth.toFixed(0)} mm` : `${entry.mm.toFixed(0)} mm · ${entry.angle.toFixed(0)}°`) : "",
      siteX: entry?.site.x ?? 0, siteZ: entry?.site.z ?? 0, toolInHand,
      ports: [
        ...Object.entries(SITES).filter(([k]) => k !== "veress").map(([k, [x, z]]) => `${k}:${x.toFixed(4)}:${z.toFixed(4)}:0`),
        ...Object.entries(ports).filter(([k]) => k !== "veress").map(([k, p]) => `${k}:${p.x.toFixed(4)}:${p.z.toFixed(4)}:1`),
      ].join("|"),
      feedback, coachStatus, caption: elapsed < captionUntil ? caption : "",
      micLevel: Math.max(0, Math.sin(elapsed * 3) * 0.3 + rnd(0.1)), listening: false, onDemand: false, fps: 72 + rnd(1.5),
    };
    lastFrame = frame;
    await call("push_frame", sessionId, JSON.stringify(frame));
    await sleep(dt * 1000);
  }

  // A few more frames so the dashboard sees "Completed".
  for (let i = 0; i < 5; i++) {
    const found = findings(events);
    const major = found.filter((f) => f[1]).length;
    await call("push_frame", sessionId, JSON.stringify({ ...lastFrame, seq: ++seq, state: "Completed", stepsDone: STEPS.length,
      major, minor: found.length - major, score: major > 0 ? Math.min(lastFrame.score ?? 0, 40) : Math.max(lastFrame.score ?? 0, 0) + 100 * 0.35 / STEPS.length,
      findings: found.map(([c, m]) => `${c}:${m ? "M" : "m"}`).join("|"), entryActive: false, tipValid: false, contact: "",
      coachStatus: "The procedure is complete.", caption: "" }));
    await sleep(200);
  }
  console.log(`run ${runIndex + 1} complete in ${elapsed.toFixed(0)} s (sim time)`);
}

function ENTRY_POP(id, which) {
  if (id === "veress") return which === 0 ? "First pop: through the fascia." : "Second pop: peritoneum. The safety stylet springs forward. You're in.";
  if (id === "port_umbilical") return which === 0 ? "Fascia giving." : "Through the peritoneum. Stop pushing.";
  return which === 0 ? "Fascia giving." : "Through. The tip is in the abdomen.";
}
const pos = (p) => ({ x: p.x, y: p.y, z: p.z });
const tpos = (p) => ({ tx: p.x, ty: p.y, tz: p.z });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let runs = 0;
do {
  await run(runs++);
  if (LOOP) await sleep(4000);
} while (LOOP);
