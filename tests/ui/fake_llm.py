"""A fake OpenAI-compatible provider for the 'mind' stage (0.17): 127.0.0.1 only, never the internet.

Routes (each provider under its own prefix, as SAHAR_MIND_ONLINE_BASE=http://127.0.0.1:<port> makes the Mind use them):
  POST /<p>/chat/completions   SSE words, or the scripted failure for <p> (/_mode)
  GET  /<p>/models             200 for the good key ("good-<p>"), else 401
  GET  /openrouter/key         the free daily counter (used 3 of 50)
  POST /_mode?p=<p>&mode=ok|429|429day|500|401|midstream   what <p> does next (sticky); p=orkey mode=full: the
                               OpenRouter free counter says 50 of 50
  GET  /_stats                 JSON: requests per route, and whether any body held a marker word (/_marker?w=...)
Bodies are never written to disk; only counts and the marker check leave this process.
"""
import argparse
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

LOCK = threading.Lock()
MODE = {}
COUNTS = {}
MARKERS = []
HITS = {}
WORDS = ["أهلين! ", "هاد ", "جواب ", "أونلاين ", "**طويل** ", "شوي 🌙"]


def count(route):
    with LOCK:
        COUNTS[route] = COUNTS.get(route, 0) + 1


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):  # no request lines (they could hold nothing personal, but stay quiet anyway)
        pass

    def _json(self, code, obj, headers=None):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def _key_ok(self, p):
        return self.headers.get("Authorization", "") == f"Bearer good-{p}"

    def do_GET(self):
        u = urlparse(self.path)
        parts = u.path.strip("/").split("/")
        if u.path == "/_stats":
            with LOCK:
                return self._json(200, {"counts": COUNTS, "hits": HITS})
        p = parts[0]
        count(u.path)
        if not self._key_ok(p):
            return self._json(401, {"error": {"message": "bad key"}})
        if u.path == "/openrouter/key":
            with LOCK:
                used = 50 if MODE.get("orkey") == "full" else 3
            return self._json(200, {"data": {"label": "t", "is_free_tier": True, "free_model_daily_requests": {"used": used, "limit": 50, "remaining": 50 - used}}})
        if parts[-1] == "models":
            return self._json(200, {"data": [{"id": "openai/gpt-oss-120b"}, {"id": "google/gemma-4-26b-a4b-it:free"}, {"id": "google/gemma-4-31b-it:free"}, {"id": "fake"}]})
        self._json(404, {})

    def do_POST(self):
        u = urlparse(self.path)
        n = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(n).decode("utf-8", "replace") if n else ""
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
        p = u.path.strip("/").split("/")[0]
        count(u.path)
        with LOCK:
            for m in MARKERS:
                if m in body:
                    HITS[m] = HITS.get(m, 0) + 1
            mode = MODE.get(p, "ok")
        if not self._key_ok(p):
            return self._json(401, {"error": {"message": "bad key"}})
        if mode == "429":  # a per-minute limit: a cooldown, not the day's end
            return self._json(429, {"error": {"message": "rate"}}, {"retry-after": "1"})
        if mode == "429day":  # Groq's own count says none left today
            return self._json(429, {"error": {"message": "rate"}}, {"retry-after": "1", "x-ratelimit-remaining-requests": "0", "x-ratelimit-limit-requests": "1000"})
        if mode == "500":
            return self._json(500, {"error": {"message": "down"}})
        if mode == "401":
            return self._json(401, {"error": {"message": "bad key"}})
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("x-ratelimit-remaining-requests", "990")
        self.send_header("x-ratelimit-limit-requests", "1000")
        self.end_headers()

        def ev(obj):
            self.wfile.write(b"data: " + json.dumps(obj, ensure_ascii=False).encode() + b"\n\n")
            self.wfile.flush()

        self.wfile.write(b": PROCESSING\n\n")
        for i, w in enumerate(WORDS):
            if mode == "midstream" and i == 2:
                ev({"error": {"code": 502, "message": "upstream"}, "choices": [{"delta": {}, "finish_reason": "error"}]})
                return
            ev({"choices": [{"index": 0, "delta": {"content": w}}]})
        ev({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}], "usage": {"prompt_tokens": 100, "completion_tokens": len(WORDS)}})
        self.wfile.write(b"data: [DONE]\n\n")


if __name__ == "__main__":
    a = argparse.ArgumentParser()
    a.add_argument("--port", type=int, required=True)
    args = a.parse_args()
    ThreadingHTTPServer(("127.0.0.1", args.port), H).serve_forever()
