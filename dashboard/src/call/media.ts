// Browser media for the call room. Screen capture is video-only: the mic
// already carries voice, and captured system audio would echo the mentor back.

export const ICE_SERVERS: RTCIceServer[] = [{ urls: "stun:stun.l.google.com:19302" }];

export const MAX_SCREEN_BITRATE = 2_500_000;

export async function getMic(deviceId?: string): Promise<MediaStreamTrack> {
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: {
      echoCancellation: true,
      noiseSuppression: true,
      deviceId: deviceId ? { exact: deviceId } : undefined,
    },
    video: false,
  });
  const [track] = stream.getAudioTracks();
  if (!track) throw new Error("No microphone track was returned.");
  return track;
}

export async function listMics(): Promise<MediaDeviceInfo[]> {
  const devices = await navigator.mediaDevices.enumerateDevices();
  return devices.filter((device) => device.kind === "audioinput");
}

// Asks the trainee to pick a window or screen (for example the Meta Quest Link
// mirror window). onEnded fires when the browser's "Stop sharing" is used.
export async function getScreen(onEnded: () => void): Promise<MediaStreamTrack> {
  const stream = await navigator.mediaDevices.getDisplayMedia({
    video: { frameRate: 30 },
    audio: false,
  });
  const [track] = stream.getVideoTracks();
  if (!track) throw new Error("No screen track was returned.");
  track.contentHint = "motion";
  track.onended = onEnded;
  return track;
}
