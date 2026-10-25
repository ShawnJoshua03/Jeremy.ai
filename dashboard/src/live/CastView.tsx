// The Meta Quest casting window, captured locally with getDisplayMedia.
import { useEffect, useRef, useState } from "react";

export type CastSource = { kind: "screen" | "file" | "remote"; label: string; stream?: MediaStream; url?: string };

interface CastViewProps {
  source: CastSource | null;
  onSource: (source: CastSource | null) => void;
  alert: string;
  caption: string;
  remoteNote?: string;
  relay?: { on: boolean; status: string; toggle: () => void };
}

function errorText(error: unknown): string {
  if (error instanceof DOMException && error.name === "NotAllowedError") return "Screen capture was cancelled.";
  return error instanceof Error ? error.message : "Could not start the capture.";
}

export function CastView({ source, onSource, alert, caption, remoteNote, relay }: CastViewProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const frameRef = useRef<HTMLDivElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [error, setError] = useState("");
  const [muted, setMuted] = useState(true);
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

  async function pickWindow() {
    setError("");
    try {
      const stream = await navigator.mediaDevices.getDisplayMedia({
        video: { frameRate: { ideal: 30 }, width: { ideal: 1920 } },
        audio: true, // a casting tab can share its sound
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
    if (file) onSource({ kind: "file", label: file.name, url: URL.createObjectURL(file) });
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
    <section className={`lv-cast${alert ? " lv-cast-alert" : ""}`} aria-label="Headset view" ref={frameRef}>
      <video ref={videoRef} autoPlay playsInline muted={muted} hidden={!source} />
      {!source && (
        <div className="lv-cast-empty">
          <p>Cast the headset to this PC (meta.com/casting in Chrome, then Camera → Cast → Computer in the headset), then pick that tab.</p>
          <p>
            <button type="button" className="lv-button" onClick={pickWindow}>Pick casting window</button>
            <button type="button" className="lv-link" onClick={() => fileRef.current?.click()}>or play a recording</button>
            <input ref={fileRef} type="file" accept="video/*" hidden onChange={(e) => pickFile(e.currentTarget.files?.[0])} />
          </p>
          {remoteNote && <p className="lv-muted">{remoteNote}</p>}
          {error && <p className="lv-bad">{error}</p>}
        </div>
      )}
      {caption && <p className="lv-caption">{caption}</p>}
      {source && (
        <div className="lv-cast-tools">
          {relay && <button type="button" className="lv-link" onClick={relay.toggle}>{relay.on ? relay.status || "Sharing with other dashboards" : "Not shared"}</button>}
          {hasAudio && <button type="button" className="lv-link" onClick={() => setMuted(!muted)}>{muted ? "Sound off" : "Sound on"}</button>}
          <button type="button" className="lv-link" onClick={fullscreen}>Full screen</button>
          {source.kind !== "remote" && (
            <>
              <button type="button" className="lv-link" onClick={pickWindow}>Change</button>
              <button type="button" className="lv-link" onClick={stop}>Stop</button>
            </>
          )}
        </div>
      )}
    </section>
  );
}
