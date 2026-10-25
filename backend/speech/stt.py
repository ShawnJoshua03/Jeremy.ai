"""Speech-to-text for push-to-talk questions, via Cartesia batch STT.

Contract checked against the official docs on 2026-10-24
(https://docs.cartesia.ai/api-reference/stt/transcribe):
  POST https://api.cartesia.ai/stt, multipart/form-data with `file` (wav and others) and `model`
  (`ink-whisper`, the only listed value), optional `language` (ISO-639-1, default en).
  Headers: `Authorization: Bearer <CARTESIA_API_KEY>` and `Cartesia-Version: 2026-08-14`.
  200 response: JSON with required `type` ("transcript") and `text`; optional `duration`, `words`.
Not run against the live API (no key in the build environment): accuracy and latency are not verified.

Audio is never stored or logged. Expected input: 16 kHz mono 16-bit PCM WAV, at most ~10 s.
"""
from __future__ import annotations

import os
import struct
from dataclasses import dataclass

import httpx

API_URL = "https://api.cartesia.ai/stt"
API_VERSION = "2026-08-14"
DEFAULT_MODEL = "ink-whisper"
MAX_AUDIO_BYTES = 1_500_000  # ~47 s of 16 kHz mono 16-bit; the duration cap below is the tighter limit
MAX_DURATION_S = 15.0
MIN_DURATION_S = 0.2
MAX_TRANSCRIPT_CHARS = 300
TIMEOUT_S = 15.0


class SpeechUnavailable(RuntimeError):
    """Transcription failed or is not configured; the client should offer text input instead."""


class InvalidAudio(ValueError):
    """The upload is not a usable WAV clip (wrong format, too long, too short)."""


class AudioTooLarge(InvalidAudio):
    """The upload is larger than MAX_AUDIO_BYTES."""


@dataclass
class WavInfo:
    sample_rate: int
    channels: int
    bits: int
    duration_s: float


def inspect_wav(data: bytes) -> WavInfo:
    """Check the RIFF/WAVE header: PCM 16-bit, 1-2 channels, 8-48 kHz, MIN..MAX_DURATION_S seconds."""
    if len(data) > MAX_AUDIO_BYTES:
        raise AudioTooLarge(f"audio is larger than {MAX_AUDIO_BYTES} bytes")
    if len(data) < 44 or data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise InvalidAudio("expected a RIFF/WAVE file")
    fmt = None
    data_len = None
    pos = 12
    while pos + 8 <= len(data):
        cid, size = data[pos:pos + 4], struct.unpack("<I", data[pos + 4:pos + 8])[0]
        body = pos + 8
        if cid == b"fmt " and size >= 16:
            fmt = struct.unpack("<HHIIHH", data[body:body + 16])
        elif cid == b"data":
            data_len = min(size, len(data) - body)  # streamed WAVs may carry 0xFFFFFFFF here
            break
        pos = body + size + (size & 1)
    if fmt is None or data_len is None:
        raise InvalidAudio("missing fmt or data chunk")
    tag, channels, rate, _byte_rate, _align, bits = fmt
    if tag not in (1, 0xFFFE) or bits != 16:
        raise InvalidAudio("expected 16-bit PCM")
    if channels not in (1, 2) or not 8000 <= rate <= 48000:
        raise InvalidAudio("expected mono 16 kHz audio")
    duration = data_len / (rate * channels * 2)
    if duration < MIN_DURATION_S:
        raise InvalidAudio("audio is too short")
    if duration > MAX_DURATION_S:
        raise InvalidAudio(f"audio is longer than {MAX_DURATION_S:g} seconds")
    return WavInfo(rate, channels, bits, round(duration, 2))


def transcribe(wav_bytes: bytes, client: httpx.Client | None = None) -> str:
    """WAV bytes -> transcript text (possibly empty). Raises InvalidAudio or SpeechUnavailable.

    One request, no paid retries. Synchronous: call it from a thread pool, not the event loop.
    """
    inspect_wav(wav_bytes)
    api_key = os.getenv("CARTESIA_API_KEY", "").strip()
    if not api_key:
        raise SpeechUnavailable("Speech recognition is unavailable; type the question instead.")
    try:
        own = client is None
        client = client or httpx.Client(timeout=httpx.Timeout(TIMEOUT_S, connect=5.0), follow_redirects=False)
        try:
            r = client.post(
                API_URL,
                headers={"Authorization": f"Bearer {api_key}", "Cartesia-Version": API_VERSION},
                files={"file": ("question.wav", wav_bytes, "audio/wav")},
                data={"model": os.getenv("CARTESIA_STT_MODEL", "").strip() or DEFAULT_MODEL, "language": "en"},
            )
            r.raise_for_status()
            text = r.json()["text"]
        finally:
            if own:
                client.close()
    except (httpx.HTTPError, ValueError, KeyError, TypeError):
        # Never surface upstream bodies, headers or keys.
        raise SpeechUnavailable("Speech recognition is unavailable; type the question instead.") from None
    if not isinstance(text, str):
        raise SpeechUnavailable("Speech recognition is unavailable; type the question instead.")
    return " ".join(text.split())[:MAX_TRANSCRIPT_CHARS]
