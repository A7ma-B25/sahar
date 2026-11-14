"""A fake of GitHub's release downloads for the install stage, on 127.0.0.1 (CI never reaches GitHub).

  GET /A7ma-B25/sahar/releases/download/test-build/<name>  serves <root>/<name>; a setup (*.exe) answers 302 to
                                                          /assets/<name> instead: one redirect hop, as GitHub sends
                                                          release assets to its asset host
  GET /assets/<name>                                      serves <root>/<name>

Every request is logged as "<path> -> <code> UA=<User-Agent>" (the stage checks she sends only "Sahar/<version>").
Run:  python -I tests/ui/fake_github.py --port 43210 --root DIR --log FILE
"""
import argparse
import os
import threading
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PREFIX = "/A7ma-B25/sahar/releases/download/test-build/"


def handler(root, log):
    lock = threading.Lock()

    class H(BaseHTTPRequestHandler):
        def note(self, code):
            with lock, open(log, "a", encoding="utf-8") as f:
                f.write(f"{self.path} -> {code} UA={self.headers.get('User-Agent')}\n")

        def do_GET(self):
            path = urllib.parse.unquote(urllib.parse.urlsplit(self.path).path)
            name = None
            if path.startswith(PREFIX):
                name = path[len(PREFIX):]
                if name.endswith(".exe") and "/" not in name:
                    self.note(302)
                    self.send_response(302)
                    self.send_header("Location", "/assets/" + urllib.parse.quote(name))
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
            elif path.startswith("/assets/"):
                name = path[len("/assets/"):]
            file = os.path.join(root, name) if name and "/" not in name and "\\" not in name and name not in (".", "..") else None
            if not file or not os.path.isfile(file):
                self.note(404)
                self.send_error(404)
                return
            with open(file, "rb") as f:
                body = f.read()
            self.note(200)
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass

    return H


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--root", required=True)
    ap.add_argument("--log", required=True)
    a = ap.parse_args()
    ThreadingHTTPServer(("127.0.0.1", a.port), handler(os.path.abspath(a.root), a.log)).serve_forever()
