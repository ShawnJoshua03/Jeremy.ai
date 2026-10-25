// Peer-to-peer WebRTC for the call room, independent of React and SpacetimeDB.
// The caller supplies a `send` function for signaling and feeds incoming
// signals to handleSignal. Video and audio never pass through the server.
//
// Roles: the trainee always makes the offer (so there is no offer glare). The
// trainee sends screen video and mic audio; each mentor sends mic audio back.

import { ICE_SERVERS, MAX_SCREEN_BITRATE } from "./media";

export type SignalKind = "offer" | "answer" | "ice";

export interface OutgoingSignal {
  to: string; // peer identity, hex
  kind: SignalKind;
  payload: string; // JSON
}

export interface MeshOptions {
  role: "trainee" | "mentor";
  send: (signal: OutgoingSignal) => void;
  onRemoteTrack: (peerId: string, stream: MediaStream) => void;
  onPeerClosed: (peerId: string) => void;
}

interface PeerState {
  pc: RTCPeerConnection;
  remote: MediaStream;
  pendingIce: RTCIceCandidateInit[];
  hasRemoteDescription: boolean;
}

export class PeerMesh {
  private readonly peers = new Map<string, PeerState>();
  private mic: MediaStreamTrack | null = null;
  private screen: MediaStreamTrack | null = null;

  constructor(private readonly options: MeshOptions) {}

  // Trainee: create a connection and an offer for a mentor who has joined.
  async connectTo(peerId: string): Promise<void> {
    if (this.options.role !== "trainee") return;
    this.dropPeer(peerId);
    const state = this.createPeer(peerId);
    const { pc } = state;
    pc.addTransceiver("video", { direction: "sendonly" });
    pc.addTransceiver("audio", { direction: "sendrecv" });
    await this.attachTracks(pc);
    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    this.capScreenBitrate(pc);
    this.emit(peerId, "offer", pc.localDescription!.toJSON());
  }

  // Mentor: answer a trainee's offer. A new offer means the trainee reloaded,
  // so any old connection to them is replaced.
  async handleSignal(from: string, kind: SignalKind, payload: string): Promise<void> {
    if (kind === "offer") {
      if (this.options.role !== "mentor") return;
      this.dropPeer(from);
      const state = this.createPeer(from);
      await state.pc.setRemoteDescription(JSON.parse(payload));
      this.markRemoteDescription(state);
      await this.attachTracks(state.pc);
      const answer = await state.pc.createAnswer();
      await state.pc.setLocalDescription(answer);
      this.emit(from, "answer", state.pc.localDescription!.toJSON());
      return;
    }

    const state = this.peers.get(from);
    if (!state) return;

    if (kind === "answer") {
      await state.pc.setRemoteDescription(JSON.parse(payload));
      this.markRemoteDescription(state);
      return;
    }

    // ICE candidates can arrive before the remote description; queue them.
    const candidate = JSON.parse(payload) as RTCIceCandidateInit;
    if (!state.hasRemoteDescription) {
      state.pendingIce.push(candidate);
      return;
    }
    await state.pc.addIceCandidate(candidate);
  }

  // Trainee: replace the screen track on every connection. No renegotiation
  // needed because the video transceiver already exists.
  async setScreen(track: MediaStreamTrack | null): Promise<void> {
    this.screen = track;
    await this.replaceOnAll("video", track);
  }

  async setMic(track: MediaStreamTrack | null): Promise<void> {
    this.mic = track;
    await this.replaceOnAll("audio", track);
  }

  dropPeer(peerId: string): void {
    const state = this.peers.get(peerId);
    if (!state) return;
    state.pc.onicecandidate = null;
    state.pc.ontrack = null;
    state.pc.close();
    this.peers.delete(peerId);
    this.options.onPeerClosed(peerId);
  }

  closeAll(): void {
    for (const peerId of [...this.peers.keys()]) this.dropPeer(peerId);
  }

  private createPeer(peerId: string): PeerState {
    const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
    const state: PeerState = {
      pc,
      remote: new MediaStream(),
      pendingIce: [],
      hasRemoteDescription: false,
    };
    this.peers.set(peerId, state);

    pc.onicecandidate = (event) => {
      if (event.candidate) this.emit(peerId, "ice", event.candidate.toJSON());
    };
    pc.ontrack = (event) => {
      state.remote.addTrack(event.track);
      this.options.onRemoteTrack(peerId, state.remote);
    };
    return state;
  }

  private markRemoteDescription(state: PeerState): void {
    state.hasRemoteDescription = true;
    for (const candidate of state.pendingIce.splice(0)) {
      void state.pc.addIceCandidate(candidate).catch(() => undefined);
    }
  }

  private async attachTracks(pc: RTCPeerConnection): Promise<void> {
    for (const transceiver of pc.getTransceivers()) {
      const kind = transceiver.receiver.track.kind;
      const track = kind === "video" ? this.screen : this.mic;
      if (kind === "audio") {
        transceiver.direction = "sendrecv";
        await transceiver.sender.replaceTrack(this.mic);
      } else if (this.options.role === "trainee") {
        await transceiver.sender.replaceTrack(track);
      }
    }
  }

  private async replaceOnAll(kind: "audio" | "video", track: MediaStreamTrack | null): Promise<void> {
    for (const { pc } of this.peers.values()) {
      for (const transceiver of pc.getTransceivers()) {
        if (transceiver.receiver.track.kind !== kind) continue;
        if (transceiver.sender.track === track) continue;
        await transceiver.sender.replaceTrack(track);
      }
    }
  }

  private capScreenBitrate(pc: RTCPeerConnection): void {
    for (const sender of pc.getSenders()) {
      if (sender.track?.kind !== "video") continue;
      const params = sender.getParameters();
      if (!params.encodings.length) continue;
      params.encodings[0].maxBitrate = MAX_SCREEN_BITRATE;
      void sender.setParameters(params).catch(() => undefined);
    }
  }

  private emit(to: string, kind: SignalKind, body: RTCSessionDescriptionInit | RTCIceCandidateInit): void {
    this.options.send({ to, kind, payload: JSON.stringify(body) });
  }
}
