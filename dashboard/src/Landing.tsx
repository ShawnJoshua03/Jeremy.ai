import "./landing.css";

// Public home page: what Jeremy.ai is, and doors into the console, the sample sessions and the live call.
// Static content only. Every claim here also appears in README.md with its status.

const API_DOCS = "https://api.jeremywantstobeasurgeon.us/docs";
const REPO = "https://github.com/ShawnJoshua03/Jeremy.ai";

const STEPS = [
  { title: "Put on the headset", body: "Passthrough keeps you in your room. A patient and a laparoscopic field anchor on your desk." },
  { title: "Operate with your hands", body: "Pinch to pick up the Veress needle, trocars, grasper, clip applier and scissors. Fifteen guided steps, from insufflation to dividing the cystic artery." },
  { title: "Get a score you can audit", body: "Time, path efficiency, port accuracy and safety errors, scored by fixed code. The same attempt always gets the same score." },
  { title: "Ask why, out loud", body: "The coach explains what cost you points and answers your questions in a spoken voice." },
];

const FEATURES = [
  { title: "Deterministic scoring", body: "Clipping the common bile duct or cutting before the critical view of safety is caught by rules, not guessed by a model." },
  { title: "A coach you can talk to", body: "Claude explains from your measured results. Cartesia speaks every line and transcribes your questions." },
  { title: "An instructor in the room, from anywhere", body: "The console mirrors the session live through Convex. Notes, highlights and pauses land in the headset." },
  { title: "The cost of a mistake", body: "Each error gets an itemized estimate from published OR rates, with supply prices kept current by an Apify Actor. Illustrative, not billing data." },
];

const STACK = ["Meta Quest 3", "Unity 6, OpenXR", "FastAPI", "Claude", "Cartesia", "Convex", "Apify", "React", "Render"];

export function Landing() {
  return (
    <div className="landing">
      <header className="landing-bar">
        <span className="landing-brand">
          <span className="workspace-mark">j</span> Jeremy.ai
        </span>
        <nav className="landing-nav" aria-label="Site">
          <a href="#sample">Sample sessions</a>
          <a href="#console">Instructor console</a>
          <a href="#call">Live call</a>
          <a href={API_DOCS} target="_blank" rel="noreferrer">API</a>
          <a href={REPO} target="_blank" rel="noreferrer">GitHub</a>
        </nav>
      </header>

      <main>
        <div className="landing-top">
        <section className="landing-hero">
          <h1><span className="verb">Practice</span> the gallbladder operation until it’s yours. Nobody gets hurt.</h1>
          <p className="landing-lede">
            Jeremy.ai turns a Meta Quest 3 into a laparoscopic skills lab. You operate with your own hands, get a
            score you can audit, ask a voice coach why, and an instructor can watch from any browser.
          </p>
          <div className="landing-actions">
            <a className="landing-btn primary" href="#sample">See a sample session</a>
            <a className="landing-btn" href="#console">Open the instructor console</a>
            <a className="landing-btn" href={REPO} target="_blank" rel="noreferrer">View on GitHub</a>
          </div>
          <p className="landing-kicker">AR surgical training, CalHacks 13</p>
        </section>

        <section className="landing-proof" aria-label="Survey">
          <div className="landing-stat">
            <strong>9 / 9</strong>
            <span>medical and bio respondents said they would use it</span>
          </div>
          <blockquote>
            “This would help with repetitions that students otherwise would not be able to get.”
            <cite>Medical student, Jeremy.ai survey</cite>
          </blockquote>
        </section>
        </div>

        <section className="landing-section">
          <h2>How a session works</h2>
          <ol className="landing-steps">
            {STEPS.map((step, i) => (
              <li key={step.title}>
                <span className="landing-num">{i + 1}</span>
                <h3>{step.title}</h3>
                <p>{step.body}</p>
              </li>
            ))}
          </ol>
        </section>

        <section className="landing-section">
          <h2>What makes it different</h2>
          <div className="landing-grid">
            {FEATURES.map((f) => (
              <article key={f.title} className="landing-card">
                <h3>{f.title}</h3>
                <p>{f.body}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="landing-section">
          <h2>Explore it now</h2>
          <div className="landing-grid">
            <a className="landing-card landing-door" href="#sample">
              <h3>Sample sessions</h3>
              <p>Browse recorded attempts with scores, a movement replay with errors marked, and the coach panel.</p>
            </a>
            <a className="landing-card landing-door" href="#console">
              <h3>Instructor console</h3>
              <p>The live view of real headset sessions. It fills in when a trainee starts an attempt.</p>
            </a>
            <a className="landing-card landing-door" href={API_DOCS} target="_blank" rel="noreferrer">
              <h3>API docs</h3>
              <p>Every endpoint: sessions, scoring, cost, coaching, voice and instructor commands.</p>
            </a>
            <a className="landing-card landing-door" href={REPO} target="_blank" rel="noreferrer">
              <h3>Source code</h3>
              <p>The Unity headset app, the FastAPI backend, this dashboard and the Apify Actor, all in one public repo.</p>
            </a>
          </div>
        </section>

        <section className="landing-section">
          <h2>Built with</h2>
          <ul className="landing-stack">
            {STACK.map((s) => <li key={s}>{s}</li>)}
          </ul>
        </section>
      </main>

      <footer className="landing-foot">
        <p>
          A training prototype, not a medical device. The practice score is not clinically validated, and every
          dollar figure is an illustrative estimate.
        </p>
        <p>Shawn Lasrado, Gabriel Shores, Christine Chen, Atharv Singh</p>
      </footer>
    </div>
  );
}
