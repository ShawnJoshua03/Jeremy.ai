"""In-memory fan-out for the live headset view.

One publisher (the headset) per session sends binary frames:
[kind:u8][t_ms:u64 LE][payload], kind 1 = JPEG, kind 2 = PCM16 mono.
Viewers (the dashboard) each get their own bounded queue, so a slow viewer
drops its own oldest packets and never blocks the publisher or other viewers.
Nothing here is persisted and nothing feeds scoring.
"""
from __future__ import annotations

import asyncio
import json
from dataclasses import dataclass, field

from fastapi import WebSocket

MAX_FRAME_BYTES = 512 * 1024
MAX_VIEWERS = 8
QUEUE_LIMIT = 64  # about 1.5 s of audio packets at 40 ms, or 6 s of video at 10 fps
KIND_JPEG = 1
KIND_PCM16 = 2

CLOSE_REPLACED = 4409
CLOSE_UNAUTHORIZED = 4401
CLOSE_NOT_FOUND = 4404
CLOSE_TOO_LARGE = 4413
CLOSE_FORBIDDEN = 4403
CLOSE_FULL = 4429


@dataclass
class Viewer:
    queue: asyncio.Queue
    dropped: int = 0


@dataclass
class Room:
    publisher: WebSocket | None = None
    hello: str | None = None  # last JSON hello text from the publisher
    last_frame: bytes | None = None  # newest JPEG, sent to late joiners
    viewers: list[Viewer] = field(default_factory=list)

    def passthrough(self) -> bool:
        if not self.hello:
            return False
        try:
            return bool(json.loads(self.hello).get("passthrough"))
        except ValueError:
            return False

    def broadcast(self, item: tuple[str, bytes | str]) -> None:
        for viewer in self.viewers:
            if viewer.queue.full():
                viewer.queue.get_nowait()  # drop this viewer's oldest packet
                viewer.dropped += 1
            viewer.queue.put_nowait(item)


class LiveHub:
    def __init__(self) -> None:
        self.rooms: dict[str, Room] = {}

    def room(self, sid: str) -> Room:
        room = self.rooms.get(sid)
        if room is None:
            room = self.rooms[sid] = Room()
        return room

    def status(self, sid: str) -> dict:
        room = self.rooms.get(sid)
        if room is None:
            return {"live": False, "viewers": 0, "passthrough": False}
        return {
            "live": room.publisher is not None,
            "viewers": len(room.viewers),
            "passthrough": room.passthrough(),
        }

    def join(self, sid: str) -> Viewer | None:
        room = self.room(sid)
        if len(room.viewers) >= MAX_VIEWERS:
            return None
        viewer = Viewer(queue=asyncio.Queue(maxsize=QUEUE_LIMIT))
        room.viewers.append(viewer)
        return viewer

    def leave(self, sid: str, viewer: Viewer) -> None:
        room = self.rooms.get(sid)
        if room is not None and viewer in room.viewers:
            room.viewers.remove(viewer)
            if room.publisher is None and not room.viewers:
                self.rooms.pop(sid, None)

    def claim_publisher(self, sid: str, ws: WebSocket) -> Room:
        room = self.room(sid)
        room.publisher = ws
        room.hello = None
        room.last_frame = None
        return room

    def release_publisher(self, sid: str, ws: WebSocket) -> None:
        room = self.rooms.get(sid)
        if room is None or room.publisher is not ws:
            return  # already replaced by a newer publisher
        room.publisher = None
        room.hello = None
        room.last_frame = None
        if not room.viewers:
            self.rooms.pop(sid, None)


HUB = LiveHub()
