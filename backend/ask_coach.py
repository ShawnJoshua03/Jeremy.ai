"""Ask the coach: a spoken (or typed) question during the lap chole sim, answered with the live sim state.

The headset sends the question with a snapshot of what is happening right now (step, live tool readout,
recent feedback, errors). The answer is short spoken coaching. Claude phrases it when ANTHROPIC_API_KEY is
set; otherwise a deterministic answer is built from the snapshot. Either way the answer may only use
numbers that are in the snapshot: a Claude reply with any other number is discarded for the fallback.
Nothing here scores or stores anything.
"""
from __future__ import annotations

import json
import os
import re
import time

import llm
from pydantic import BaseModel, ConfigDict, Field

TIMEOUT_S = 4.0  # one Claude call; the trainee never waits longer than this for the template
MAX_REPLY_WORDS = 55


class SimContext(BaseModel):
    """What the trainee is doing right now, as the headset sees it. All fields optional."""
    model_config = ConfigDict(extra="ignore")
    step: str = Field(default="", max_length=60)           # "Step 4 of 15 · Epigastric port"
    what: str = Field(default="", max_length=300)          # the step's goal
    how: str = Field(default="", max_length=400)           # how to do it on this device
    status: str = Field(default="", max_length=300)        # the coach's own read of the moment
    readout: str = Field(default="", max_length=200)       # live tool readout (offset, angle, depth)
    last_feedback: str = Field(default="", max_length=300)
    errors: str = Field(default="", max_length=200)        # "0 major, 1 minor"
    elapsed_s: float | None = None
    step_s: float | None = None
    mode: str = Field(default="guide", max_length=20)      # guide | on_demand
    device: str = Field(default="headset", max_length=20) # headset | desktop
    recent: list[str] = Field(default_factory=list, max_length=6)


class AskBody(BaseModel):
    model_config = ConfigDict(extra="forbid")
    text: str | None = Field(default=None, max_length=300)
    audio_wav_b64: str | None = Field(default=None, max_length=2_100_000)
    context: SimContext = Field(default_factory=SimContext)
    speak: bool = True
    hands_free: bool = False  # always-listening mic: answer only speech addressed to the coach


_INTENTS = [
    ("hear", r"\b(can you hear|do you hear|hear me|are you there|you there|hello|hey coach|testing)\b"),
    ("repeat", r"\b(repeat|again|say that|what did you say)\b"),
    ("why", r"\b(why|what for|purpose|point of)\b"),
    ("explain", r"\b(what (does|is|are) (this|that|it|the)\b|what does .* do|what'?s (this|that) (for|do)|explain|tell me about|what am i doing)"),
    ("score", r"\b(score|how am i doing|how did i do|errors?|mistakes?|grade)\b"),
    ("check", r"\b(am i|is this|doing (it|this|ok|okay|right|well)|correct|good\??$|how('?s| is) (it|this|that) look)"),
    ("next", r"\b(what('?s| is)? next|what now|what do i do|what should i do|now what)\b"),
    ("how", r"\b(how do i|how to|how should|show me|help|stuck|can'?t)\b"),
    ("where", r"\b(where|which (spot|place|way|direction))\b"),
]


_ADDRESSED = re.compile(
    r"(\?|\bcoach\b|\bhelp\b|\b(am i|is (this|it|that)|are we|should i|can i|do i|how|what|where|which|when|why|next|"
    r"right|correct|wrong|stuck|lost|again|repeat|score|doing)\b)", re.I)


def addressed_to_coach(transcript: str) -> bool:
    """Hands-free: does this utterance look like a question or a request to the coach (not room chatter)?"""
    t = transcript.strip()
    return len(t.split()) >= 2 and bool(_ADDRESSED.search(t))


def intent_of(question: str) -> str:
    q = question.lower().strip()
    for name, pattern in _INTENTS:
        if re.search(pattern, q):
            return name
    return "open"


def fallback(question: str, c: SimContext) -> str:
    """Deterministic answer from the snapshot (no LLM)."""
    intent = intent_of(question)
    step = c.step.split("·")[-1].strip() if c.step else "this step"
    if intent == "check":
        return c.status or (f"You're on {step}. " + (c.readout + ". " if c.readout else "") + "Keep going.")
    if intent == "next":
        return f"{step}. {c.what} {c.how}".strip()
    if intent == "how":
        return (c.status + " " if c.status else "") + (c.how or c.what)
    if intent == "where":
        return c.status or c.how or c.what
    if intent == "why":
        return _why(step, c.what)
    if intent == "explain":
        # What this step is, then why it matters (two short sentences).
        return f"{step}: {c.what}".strip() + " " + _first_sentences(_why(step, c.what), 1)
    if intent == "score":
        return f"So far: {c.errors or 'no errors'}." + (f" {c.status}" if c.status else "")
    if intent == "hear":
        return "Yes, I can hear you. " + (_first_sentences(c.status, 1) or "Ask me anything, any time.")
    if intent == "repeat":
        return c.recent[-1] if c.recent else (c.last_feedback or c.how)
    return _first_sentences(c.status, 1) + " " + (_first_sentences(c.how, 1) or "Ask me if you're doing it right, what's next, or how to do this step.")


_WHY = {
    "veress": "The Veress needle lets us inflate the abdomen safely before any sharp trocar goes in. Its blunt stylet springs forward once you're through, protecting the bowel.",
    "insufflation": "CO2 lifts the abdominal wall off the organs to make working space. Twelve to fifteen keeps it safe for breathing and blood return.",
    "umbilical": "The camera goes through the navel: it's thin there and central, so the scope sees the whole upper abdomen.",
    "epigastric": "The epigastric port is the main working port. Its angle points straight at the gallbladder neck.",
    "subcostal": "The subcostal ports give the assistant graspers a good angle to lift the gallbladder, triangulating with the main port.",
    "retract": "Pushing the fundus up over the liver opens Calot's triangle so you can see the duct and artery.",
    "calot": "The critical view of safety proves you have exactly two structures entering the gallbladder before anything is clipped. It's the main protection against a bile duct injury.",
    "clip": "Clips seal the structure before it's cut. Two stay on the patient side, one on the gallbladder side.",
    "ductotomy": "The small cut lets the catheter into the cystic duct for the cholangiogram.",
    "cholangiogram": "Contrast shows the bile duct anatomy and any stones before you divide anything.",
    "divide": "Cut only between secure clips, so both ends stay sealed.",
}


def _why(step: str, what: str) -> str:
    s = step.lower()
    for key, text in _WHY.items():
        if key in s:
            return text
    return what or "Each step sets up the next one safely."


def _numbers(text: str) -> set[str]:
    return set(re.findall(r"\d+(?:\.\d+)?", text))




def _prompt(question: str, c: SimContext) -> str:
    facts = "\n".join(f"- {k}: {v}" for k, v in [
        ("current step", c.step), ("goal", c.what), ("how on this device", c.how), ("coach's read right now", c.status),
        ("live tool readout", c.readout), ("last feedback shown", c.last_feedback), ("errors so far", c.errors),
        ("seconds on this step", f"{c.step_s:.0f}" if c.step_s is not None else ""),
        ("recent coach lines", " | ".join(c.recent[-3:])),
    ] if v)
    return (
        "You are an attending surgeon coaching a trainee through a laparoscopic cholecystectomy in a VR simulator. "
        "The trainee just asked you something out loud. Answer like a calm, encouraging coach standing beside them: "
        f"spoken English, at most {MAX_REPLY_WORDS} words, one or two sentences, concrete and specific to what they are doing now. "
        "If they ask whether they're doing it right, judge from the coach's read and the live readout and tell them the one thing to adjust, "
        "or confirm and tell them what to do next. If they ask what something is or why, explain it briefly in plain words. "
        "Use only numbers that appear in the facts. No lists, no markdown. "
        "This is a training simulator with practice tolerances, not clinical advice.\n\n"
        f"Facts:\n{facts}\n\nTrainee: \"{question}\"\nCoach:"
    )


def _clean(text: str, c: SimContext) -> str | None:
    text = " ".join((text or "").replace("*", "").split())
    if not text:
        return None
    allowed = _numbers(" ".join([c.step, c.what, c.how, c.status, c.readout, c.last_feedback, c.errors, " ".join(c.recent)]))
    if not _numbers(text) <= allowed:
        return None  # a number the sim never measured: use another answer instead
    return " ".join(text.split()[:MAX_REPLY_WORDS + 10])


def claude_answer(question: str, c: SimContext) -> str | None:
    """Claude Haiku via llm.complete_text (ANTHROPIC_API_KEY; model ASK_CLAUDE_MODEL or ANTHROPIC_MODEL).

    Estimated spend is tracked on disk and Claude stops being called once ASK_CLAUDE_BUDGET_USD
    (default 5) is reached; after a failure it is skipped for two minutes.
    """
    try:
        out = llm.complete_text(_prompt(question, c), max_tokens=160, timeout_s=TIMEOUT_S)
    except llm.LLMUnavailable:
        return None
    return _clean(out.text, c)


def answer(question: str, c: SimContext) -> tuple[str, str]:
    """(reply, source): every question goes to Claude Haiku with the live sim facts; the deterministic
    answer from the snapshot is the fallback when Claude is off, capped, slow or fails the number check."""
    reply = claude_answer(question, c)
    if reply:
        return reply, "claude"
    return _first_sentences(" ".join(fallback(question, c).split()), 2), "template"


def _first_sentences(text: str, n: int) -> str:
    parts = re.split(r"(?<=[.!?])\s+", (text or "").strip())
    return " ".join(p for p in parts[:n] if p).strip()
