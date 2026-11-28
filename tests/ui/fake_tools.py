"""Fakes for the 'mind' stage's tools (0.19): 127.0.0.1 only, never the internet. SAHAR_MIND_ONLINE_BASE points here.

Routes:
  POST /mcp                       a Streamable HTTP MCP server (2025-11-25 JSON answers; server/discover is refused, so
                                  the client falls back to initialize): tools lookup (fits), sum_numbers (a number: does
                                  not fit), delete_note (sensitive)
  POST /mcp-oauth                 always 401 with a Bearer challenge (a server that needs a sign-in)
  GET  /pollinations/prompt/<p>   a 320x320 PNG (a moon over the sea), or 402 + Retry-After: 30 (/_mode?p=image&mode=402)
  POST /groq/audio/speech         a 1 s WAV for the good key ("good-groq"), or 429 (/_mode?p=voice&mode=429)
  GET  /groq/models               200 for the good key (the 0.17 key test), else 401
  POST /_mode?p=<p>&mode=<m>      what <p> does next (sticky)
  GET  /_stats                    JSON: requests per route, and whether any body or URL held a marker (/_marker?w=...)
Bodies are never written to disk; only counts and the marker check leave this process.
"""
import argparse
import json
import math
import struct
import threading
import zlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs, unquote

LOCK = threading.Lock()
MODE = {}
COUNTS = {}
MARKERS = []
HITS = {}

TOOLS = [
    {"name": "lookup", "description": "Looks a word up and says what it found.",
     "inputSchema": {"type": "object", "properties": {"q": {"type": "string", "maxLength": 100}}, "required": ["q"]}},
    {"name": "sum_numbers", "description": "Adds two numbers.",
     "inputSchema": {"type": "object", "properties": {"a": {"type": "number"}, "b": {"type": "number"}}, "required": ["a", "b"]}},
    {"name": "delete_note", "description": "Deletes one of your notes.",
     "inputSchema": {"type": "object", "properties": {"id": {"type": "string", "maxLength": 40}}, "required": ["id"]}},
]


def png():
    w = h = 320
    rows = []
    for y in range(h):
        row = bytearray([0])
        for x in range(w):
            sea = y > 210
            r, g, b = (20, 30 + y // 6, 70 + y // 4) if not sea else (16, 40 + (y - 210) // 3, 90 + (y - 210))
            d = math.hypot(x - 200, y - 110)
            if d < 38:
                r, g, b = 246, 236, 200
            elif sea and abs(x - 200) < 34 - (y - 210) // 5 and (y // 3) % 2 == 0:
                r, g, b = 200, 200, 170
            row += bytes((r, g, b))
        rows.append(bytes(row))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(b"".join(rows))) + chunk(b"IEND", b"")


def wav():
    rate, n = 16000, 16000
    data = b"".join(struct.pack("<h", int(8000 * math.sin(2 * math.pi * 440 * i / rate))) for i in range(n))
    return b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVEfmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16) + b"data" + struct.pack("<I", len(data)) + data


PNG = png()
WAV = wav()


def count(route):
    with LOCK:
        COUNTS[route] = COUNTS.get(route, 0) + 1


def mark(text):
    with LOCK:
        for m in MARKERS:
            if m in text:
                HITS[m] = HITS.get(m, 0) + 1


class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _send(self, code, body=b"", ctype="application/json", headers=None):
        self.send_response(code)
        if body:
            self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _json(self, code, obj, headers=None):
        self._send(code, json.dumps(obj, ensure_ascii=False).encode(), headers=headers)

    def do_GET(self):
        u = urlparse(self.path)
        if u.path == "/_stats":
            with LOCK:
                return self._json(200, {"counts": COUNTS, "hits": HITS})
        mark(unquote(u.path))
        if u.path.startswith("/pollinations/prompt/"):
            count("/pollinations/prompt")
            with LOCK:
                mode = MODE.get("image", "ok")
            if mode == "402":
                return self._json(402, {"error": "Payment Required"}, {"Retry-After": "30"})
            return self._send(200, PNG, "image/png")
        count(u.path)
        if u.path == "/groq/models":
            if self.headers.get("Authorization", "") != "Bearer good-groq":
                return self._json(401, {"error": {"message": "bad key"}})
            return self._json(200, {"data": [{"id": "openai/gpt-oss-120b"}]})
        self._json(405 if u.path.startswith("/mcp") else 404, {})

    def do_DELETE(self):
        self._send(405)

    def _body(self):
        if "chunked" in (self.headers.get("Transfer-Encoding") or "").lower():   # HttpClient streams JSON content chunked
            out = b""
            while True:
                size = int(self.rfile.readline().split(b";")[0].strip() or b"0", 16)
                if size == 0:
                    self.rfile.readline()
                    return out.decode("utf-8", "replace")
                out += self.rfile.read(size)
                self.rfile.readline()
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n).decode("utf-8", "replace") if n else ""

    def do_POST(self):
        u = urlparse(self.path)
        body = self._body()
        if u.path == "/_mode":
            q = parse_qs(u.query)
            with LOCK:
                MODE[q["p"][0]] = q.get("mode", ["ok"])[0]
            return self._json(200, {"ok": True})
        if u.path == "/_marker":
            q = parse_qs(u.query)
            with LOCK:
                MARKERS.append(q["w"][0])
            return self._json(200, {"ok": True})
        count(u.path)
        mark(body)
        if u.path == "/mcp-oauth":
            return self._json(401, {"error": "unauthorized"}, {"WWW-Authenticate": 'Bearer resource_metadata="http://127.0.0.1/.well-known/oauth-protected-resource"'})
        if u.path == "/groq/audio/speech":
            if self.headers.get("Authorization", "") != "Bearer good-groq":
                return self._json(401, {"error": {"message": "bad key"}})
            with LOCK:
                mode = MODE.get("voice", "ok")
            if mode == "429":
                return self._json(429, {"error": {"message": "rate"}}, {"retry-after": "3600"})
            return self._send(200, WAV, "audio/wav")
        if u.path == "/mcp":
            return self._mcp(body)
        self._json(404, {})

    def _mcp(self, body):
        try:
            msg = json.loads(body)
        except ValueError:
            return self._json(400, {"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "parse error"}})
        if isinstance(msg, list) or "id" not in msg:   # a notification (or a batch we do not need): accepted
            return self._send(202)
        mid, method, params = msg["id"], msg.get("method"), msg.get("params") or {}

        def ok(result):
            self._json(200, {"jsonrpc": "2.0", "id": mid, "result": result}, {"Mcp-Session-Id": "fake-1"})

        if method == "initialize":
            return ok({"protocolVersion": "2025-11-25", "capabilities": {"tools": {"listChanged": False}}, "serverInfo": {"name": "fake-web", "version": "1.0"}})
        if method == "ping":
            return ok({})
        if method == "tools/list":
            return ok({"tools": TOOLS})
        if method == "tools/call":
            name = params.get("name")
            args = params.get("arguments") or {}
            if name == "lookup":
                return ok({"content": [{"type": "text", "text": f"SENTINELOUT found for {args.get('q', '')}: a small moon ​\U000e0041"}], "isError": False})
            return ok({"content": [{"type": "text", "text": "not here"}], "isError": True})
        self._json(200, {"jsonrpc": "2.0", "id": mid, "error": {"code": -32601, "message": "Method not found"}})


if __name__ == "__main__":
    a = argparse.ArgumentParser()
    a.add_argument("--port", type=int, required=True)
    args = a.parse_args()
    ThreadingHTTPServer(("127.0.0.1", args.port), H).serve_forever()
