"""Cartesia bytes API. Contract checked against the official docs on 2026-10-24.

https://docs.cartesia.ai/api-reference/tts/bytes
The 2026-08-14 API takes a voice ID string, not the older voice object.
"""
from __future__ import annotations

import os
import struct

import httpx

MAX_TEXT_LENGTH = 2000
MAX_AUDIO_BYTES = 10 * 1024 * 1024
API_URL = "https://api.cartesia.ai/tts/bytes"
API_VERSION = "2026-08-14"
DEFAULT_MODEL = "sonic-3.6"
DEFAULT_VOICE = "db6b0ed5-d5d3-463d-ae85-518a07d3c2b4"


class SpeechUnavailable(RuntimeError):
    """Speech failed; callers should keep displaying the coaching text."""


def text_to_audio(text: str) -> bytes:
    """Return 44.1 kHz signed 16-bit PCM WAV bytes, without storing audio.

    Raises ValueError for invalid text and SpeechUnavailable for configuration,
    network, provider, or invalid-audio failures. No automatic paid retries.
    This synchronous function belongs in a sync FastAPI route/thread pool.
    """
    if not isinstance(text, str) or not text.strip() or len(text) > MAX_TEXT_LENGTH:
        raise ValueError(f"text must contain 1-{MAX_TEXT_LENGTH} characters")
    api_key = os.getenv("CARTESIA_API_KEY", "").strip()
    if not api_key:
        raise SpeechUnavailable("Speech is unavailable; use text feedback.")

    payload = {
        "model_id": os.getenv("CARTESIA_MODEL_ID", "").strip() or DEFAULT_MODEL,
        "transcript": text,
        "voice": os.getenv("CARTESIA_VOICE_ID", "").strip() or DEFAULT_VOICE,
        "output_format": {
            "container": "wav", "encoding": "pcm_s16le", "sample_rate": 44100,
        },
        "language": "en",
    }
    try:
        with httpx.Client(timeout=httpx.Timeout(30.0, connect=5.0), follow_redirects=False) as client:
            with client.stream("POST", API_URL, headers={
                "Authorization": f"Bearer {api_key}",
                "Cartesia-Version": API_VERSION,
                "Accept": "audio/wav",
            }, json=payload) as response:
                response.raise_for_status()
                audio = bytearray()
                for chunk in response.iter_bytes(chunk_size=65536):
                    if len(audio) + len(chunk) > MAX_AUDIO_BYTES:
                        raise SpeechUnavailable("Speech is unavailable; use text feedback.")
                    audio.extend(chunk)
    except (httpx.HTTPError, ValueError):
        # Never return upstream bodies, request headers, transcripts or keys.
        raise SpeechUnavailable("Speech is unavailable; use text feedback.") from None

    if len(audio) <= 44 or audio[:4] != b"RIFF" or audio[8:12] != b"WAVE":
        raise SpeechUnavailable("Speech is unavailable; use text feedback.")
    return finalize_wav_sizes(bytes(audio))


def finalize_wav_sizes(wav: bytes) -> bytes:
    """Cartesia streams WAV with 0xFFFFFFFF size fields; write real sizes so strict decoders (Unity) accept it."""
    data_at = wav.find(b"data", 12)
    if data_at < 0:
        raise SpeechUnavailable("Speech is unavailable; use text feedback.")
    out = bytearray(wav)
    out[4:8] = struct.pack("<I", len(wav) - 8)
    out[data_at + 4:data_at + 8] = struct.pack("<I", len(wav) - data_at - 8)
    return bytes(out)
