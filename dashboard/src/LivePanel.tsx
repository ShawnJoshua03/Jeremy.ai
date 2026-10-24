import { useEffect, useRef, useState } from "react";
import {
  KIND_JPEG,
  KIND_PCM16,
  fetchViewerToken,
  parseLiveFrame,
  pcm16ToFloat32,
  watchUrl,
  type LiveHello,
  type LiveServerText,
} from "./liveStream";

interface LivePanelProps {
  sessionId: string;
}

type LinkState = "connecting" | "waiting" | "live" | "reconnecting" | "stopped";

const MIN_BACKOFF_MS = 1000;
const MAX_BACKOFF_MS = 10000;
// Jitter buffer: schedule audio this far ahead so network bursts do not cut out.
const AUDIO_LEAD_S = 0.15;

const STATE_TEXT: Record<LinkState, string> = {
  connecting: "Connecting",
  waiting: "Waiting for headset",
  live: "Live",
  reconnecting: "Reconnecting",
  stopped: "Stopped",
};

export function LivePanel({ sessionId }: LivePanelProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const audioRef = useRef<AudioContext | null>(null);
  const nextAudioTime = useRef(0);
  const sampleRate = useRef(24000);
  const frameCount = useRef(0);
  const [link, setLink] = useState<LinkState>("connecting");
  const [viewers, setViewers] = useState(0);
  const [passthrough, setPassthrough] = useState(false);
  const [fps, setFps] = useState(0);
  const [audioOn, setAudioOn] = useState(false);
  const [note, setNote] = useState("");

  useEffect(() => {
    let socket: WebSocket | null = null;
    let timer: number | undefined;
    let backoff = MIN_BACKOFF_MS;
    let stopped = false;

    const schedule = (delay: number, action: () => void) => {
      timer = window.setTimeout(action, delay);
    };

    const handleText = (text: string) => {
      let body: LiveServerText;
      try {
        body = JSON.parse(text) as LiveServerText;
      } catch {
        return;
      }
      if (body.type === "hello") {
        const hello = body as LiveHello;
        setPassthrough(Boolean(hello.passthrough));
        if (hello.audio?.rate) sampleRate.current = hello.audio.rate;
        const canvas = canvasRef.current;
        if (canvas && hello.video?.w && hello.video?.h) {
          canvas.width = hello.video.w;
          canvas.height = hello.video.h;
        }
        setLink("live");
        return;
      }
      if (body.type === "status") {
        setViewers(body.viewers);
        if (!body.live) {
          // The headset is gone: stop showing the last picture as live.
          setLink("waiting");
          setPassthrough(false);
        } else {
          setPassthrough(body.passthrough);
        }
      }
    };

    const handleBinary = (buffer: ArrayBuffer) => {
      const frame = parseLiveFrame(buffer);
      if (!frame) return;
      if (frame.kind === KIND_JPEG) {
        const canvas = canvasRef.current;
        const context = canvas?.getContext("2d");
        if (!canvas || !context) return;
        const blob = new Blob([frame.payload as BlobPart], { type: "image/jpeg" });
        createImageBitmap(blob).then((bitmap) => {
          context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
          bitmap.close();
        }).catch(() => {
          setNote("A video frame could not be decoded.");
        });
        frameCount.current += 1;
      } else if (frame.kind === KIND_PCM16) {
        playAudio(frame.payload);
      }
    };

    const playAudio = (payload: Uint8Array) => {
      const context = audioRef.current;
      if (!context || context.state !== "running") return;
      const samples = pcm16ToFloat32(payload);
      if (samples.length === 0) return;
      const buffer = context.createBuffer(1, samples.length, sampleRate.current);
      buffer.copyToChannel(samples, 0);
      const source = context.createBufferSource();
      source.buffer = buffer;
      source.connect(context.destination);
      const start = Math.max(nextAudioTime.current, context.currentTime + AUDIO_LEAD_S);
      source.start(start);
      nextAudioTime.current = start + buffer.duration;
    };

    const connect = async () => {
      if (stopped) return;
      try {
        const token = await fetchViewerToken(sessionId);
        if (stopped) return;
        socket = new WebSocket(watchUrl(sessionId, token));
        socket.binaryType = "arraybuffer";
        socket.onopen = () => {
          backoff = MIN_BACKOFF_MS;
          setNote("");
          setLink((current) => (current === "live" ? current : "waiting"));
        };
        socket.onmessage = (event) => {
          if (typeof event.data === "string") handleText(event.data);
          else if (event.data instanceof ArrayBuffer) handleBinary(event.data);
        };
        socket.onclose = (event) => {
          socket = null;
          if (stopped) return;
          if (event.code === 4401 || event.code === 4404 || event.code === 4403) {
            setLink("stopped");
            setNote(
              event.code === 4404
                ? "This session is not on the server."
                : "The live view refused this connection.",
            );
            return;
          }
          setLink("reconnecting");
          schedule(backoff, connect);
          backoff = Math.min(backoff * 2, MAX_BACKOFF_MS);
        };
      } catch {
        if (stopped) return;
        setLink("reconnecting");
        schedule(backoff, connect);
        backoff = Math.min(backoff * 2, MAX_BACKOFF_MS);
      }
    };

    setLink("connecting");
    connect();

    // Frames per second, once a second.
    const fpsTimer = window.setInterval(() => {
      setFps(frameCount.current);
      frameCount.current = 0;
    }, 1000);

    return () => {
      stopped = true;
      window.clearTimeout(timer);
      window.clearInterval(fpsTimer);
      socket?.close();
      audioRef.current?.close();
      audioRef.current = null;
    };
  }, [sessionId]);

  const unmute = () => {
    if (audioRef.current) return;
    const context = new AudioContext();
    audioRef.current = context;
    nextAudioTime.current = 0;
    void context.resume().then(() => setAudioOn(true));
  };

  return (
    <section className="panel live-panel" aria-labelledby="live-heading">
      <div className="panel-heading">
        <div>
          <h2 id="live-heading">Live headset view</h2>
          <p className="panel-subtitle">
            {passthrough ? "Rendered scene over passthrough" : "Rendered scene only"} · {viewers} watching
          </p>
        </div>
        <button
          type="button"
          className="live-audio-toggle"
          onClick={unmute}
          disabled={audioOn}
          aria-pressed={audioOn}
        >
          {audioOn ? "Audio on" : "Unmute"}
        </button>
      </div>
      <div className="live-stage">
        <canvas ref={canvasRef} className="live-canvas" width={640} height={480} aria-label="Live view of the headset" />
        {link !== "live" && (
          <div className="live-overlay" role="status">
            {STATE_TEXT[link]}
          </div>
        )}
      </div>
      <div className="live-status-row" role="status">
        <span className={link === "live" ? "live-dot is-live" : "live-dot"} aria-hidden="true" />
        <span>{STATE_TEXT[link]}</span>
        {link === "live" && <span>{fps} fps</span>}
        {note && <span className="live-note">{note}</span>}
      </div>
    </section>
  );
}
