// The Meta Quest casting window, captured locally with getDisplayMedia and
// shown large, with the live telemetry drawn over it as a light HUD.
import { useEffect, useRef, useState, type ReactNode } from "react";
import { Icon } from "./icons";

export type CastSource = { kind: "screen" | "file" | "remote"; label: string; stream?: MediaStream; url?: string };

interface CastViewProps {
  source: CastSource | null;
  onSource: (source: CastSource | null) => void;
  danger: boolean;
  overlay: ReactNode;
  caption: string;
  remoteNote?: string;
  relay?: { on: boolean; status: string; toggle: () => void };
}

function errorText(error: unknown): string {
  if (error instanceof DOMException && error.name === "NotAllowedError") return "Screen capture was cancelled or blocked.";
  return error instanceof Error ? error.message : "Could not start the capture.";
}

export function CastView({ source, onSource, danger, overlay, caption, remoteNote, relay }: CastViewProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const frameRef = useRef<HTMLDivElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [error, setError] = useState("");
  const [muted, setMuted] = useState(true);
  const [size, setSize] = useState("");
  const hasAudio = Boolean(source?.stream?.getAudioTracks().length) || source?.kind === "file";

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    if (source?.stream) {
      video.srcObject = source.stream;
      video.removeAttribute("src");
    } else if (source?.url) {
      video.srcObject = null;
      video.src = source.url;
      video.loop = true;
    } else {
      video.srcObject = null;
      video.removeAttribute("src");
    }
    void video.play().catch(() => undefined);
  }, [source]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const onResize = () => setSize(video.videoWidth ? `${video.videoWidth}×${video.videoHeight}` : "");
    video.addEventListener("resize", onResize);
    video.addEventListener("loadedmetadata", onResize);
    return () => {
      video.removeEventListener("resize", onResize);
      video.removeEventListener("loadedmetadata", onResize);
    };
  }, []);

  async function pickWindow() {
    setError("");
    try {
      const stream = await navigator.mediaDevices.getDisplayMedia({
        video: { frameRate: { ideal: 30 }, width: { ideal: 1920 } },
        audio: true, // a casting tab can share its sound; window capture usually cannot
      });
      const [track] = stream.getVideoTracks();
      if (!track) throw new Error("No video track was returned.");
      track.contentHint = "motion";
      track.onended = () => onSource(null);
      onSource({ kind: "screen", label: track.label || "Shared window", stream });
    } catch (e) {
      setError(errorText(e));
    }
  }

  function pickFile(file: File | undefined) {
    if (!file) return;
    onSource({ kind: "file", label: file.name, url: URL.createObjectURL(file) });
  }

  function stop() {
    source?.stream?.getTracks().forEach((track) => track.stop());
    if (source?.url) URL.revokeObjectURL(source.url);
    onSource(null);
  }

  function fullscreen() {
    const el = frameRef.current;
    if (!el) return;
    if (document.fullscreenElement) void document.exitFullscreen();
    else void el.requestFullscreen().catch(() => undefined);
  }

  return (
    <section className={`lv-card lv-cast${danger ? " lv-cast-danger" : ""}`} aria-label="Headset view">
      <div className="lv-cast-frame" ref={frameRef}>
        <video ref={videoRef} autoPlay playsInline muted={muted} className={source ? "" : "lv-hidden"} />
        {!source && (
          <div className="lv-cast-empty">
            <div className="lv-cast-empty-icon" aria-hidden="true">
              <svg viewBox="0 0 48 48" width="44" height="44" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M6 17c0-3 2-5 5-5h26c3 0 5 2 5 5v11c0 3-2 5-5 5h-7l-3-4h-6l-3 4h-7c-3 0-5-2-5-5z" />
                <circle cx="16" cy="22" r="3.2" /><circle cx="32" cy="22" r="3.2" />
              </svg>
            </div>
            <h3>Show the headset view</h3>
            <p>
              Cast the Quest to this PC (headset: <em>Camera → Cast → Computer</em>, or open{" "}
              <a href="https://www.meta.com/casting" target="_blank" rel="noreferrer">meta.com/casting</a> in Chrome),
              then pick that window or tab here.
            </p>
            <div className="lv-row">
              <button type="button" className="lv-btn lv-btn-primary" onClick={pickWindow}><Icon name="cast" />Pick casting window</button>
              <button type="button" className="lv-btn" onClick={() => fileRef.current?.click()}><Icon name="film" />Play a recording</button>
              <input ref={fileRef} type="file" accept="video/*" hidden onChange={(e) => pickFile(e.currentTarget.files?.[0])} />
            </div>
            {remoteNote && <p className="lv-faint">{remoteNote}</p>}
            {error && <p className="lv-error" role="alert">{error}</p>}
          </div>
        )}
        {overlay}
        {caption && (
          <div className="lv-caption" aria-live="polite">
            <span className="lv-caption-who">Coach</span>
            {caption}
          </div>
        )}
        {source && (
          <div className="lv-cast-tools">
            <span className="lv-chip lv-chip-dark" title={source.label}>
              <span className="lv-dot lv-dot-live" aria-hidden="true" />
              {source.kind === "screen" ? "Casting" : source.kind === "remote" ? "Relayed" : "Recording"}{size && ` · ${size}`}
            </span>
            {relay && (
              <button type="button" className={`lv-chip lv-chip-dark lv-relay${relay.on ? " lv-relay-on" : ""}`} onClick={relay.toggle} aria-pressed={relay.on}
                title={relay.on ? "Stop sharing this view with other dashboards" : "Share this view with other open dashboards (LAN)"}>
                <Icon name="share" size={13} />{relay.on ? relay.status || "Relay starting" : "Relay off"}
              </button>
            )}
            {hasAudio && (
              <button type="button" className="lv-icon-btn" onClick={() => setMuted(!muted)} aria-pressed={!muted} title={muted ? "Hear the headset" : "Mute"}>
                <Icon name={muted ? "mute" : "volume"} />
              </button>
            )}
            <button type="button" className="lv-icon-btn" onClick={fullscreen} title="Full screen"><Icon name="expand" /></button>
            {source.kind !== "remote" && (
              <>
                <button type="button" className="lv-icon-btn" onClick={pickWindow} title="Pick another window"><Icon name="swap" /></button>
                <button type="button" className="lv-icon-btn" onClick={stop} title="Stop"><Icon name="close" /></button>
              </>
            )}
          </div>
        )}
      </div>
    </section>
  );
}
