#!/usr/bin/env python3
"""Dami — local text-to-speech sidecar (L4).

POST /speak  {"text": "...", "voice": "en_US-ljspeech-medium"}  -> audio/wav
GET  /health                                                   -> {"status":"ok","voice":...}

Binds loopback only. Piper runs on CPU; a medium voice renders a sentence in well
under a second. Voices live in DAMI_TTS_VOICES (default /home/steve/Data/piper) and
are never fetched by this process: download them deliberately, read the model card,
and record it. Run:  uv run --with piper-tts tools/tts/server.py
"""
import io
import json
import os
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from piper import PiperVoice

VOICES_DIR = os.environ.get("DAMI_TTS_VOICES", "/home/steve/Data/piper")
DEFAULT_VOICE = os.environ.get("DAMI_TTS_VOICE", "en_US-ljspeech-medium")
PORT = int(os.environ.get("DAMI_TTS_PORT", "8091"))
MAX_CHARS = 4000

loaded = {}


def voice(name):
    if name not in loaded:
        path = os.path.join(VOICES_DIR, f"{name}.onnx")
        if not os.path.exists(path):
            raise FileNotFoundError(name)
        loaded[name] = PiperVoice.load(path)
    return loaded[name]


def render(text, name):
    v = voice(name)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as out:
        v.synthesize_wav(text, out)
    return buffer.getvalue()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):  # quiet; the host traces the call
        pass

    def _json(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == "/health":
            self._json(200, {"status": "ok", "voice": DEFAULT_VOICE, "voices_dir": VOICES_DIR})
        else:
            self._json(404, {"error": "not found"})

    def do_POST(self):
        if self.path != "/speak":
            self._json(404, {"error": "not found"})
            return
        length = int(self.headers.get("Content-Length", "0"))
        try:
            body = json.loads(self.rfile.read(length) or b"{}")
            text = (body.get("text") or "").strip()
            name = body.get("voice") or DEFAULT_VOICE
        except (ValueError, AttributeError):
            self._json(400, {"error": "body must be JSON with a text field"})
            return
        if not text:
            self._json(400, {"error": "text is empty"})
            return
        if len(text) > MAX_CHARS:
            self._json(413, {"error": f"text longer than {MAX_CHARS} characters"})
            return
        try:
            audio = render(text, name)
        except FileNotFoundError:
            self._json(404, {"error": f"voice {name} is not installed in {VOICES_DIR}"})
            return
        self.send_response(200)
        self.send_header("Content-Type", "audio/wav")
        self.send_header("Content-Length", str(len(audio)))
        self.send_header("X-Dami-Voice", name)
        self.end_headers()
        self.wfile.write(audio)


if __name__ == "__main__":
    voice(DEFAULT_VOICE)  # fail at start, not on the first request
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
