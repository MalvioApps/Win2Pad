"""Tiny LAN server for the Safari pointer test: serves index.html and logs what the iPad reports."""
import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
PAGE = (HERE / "index.html").read_bytes()
LOG = HERE / "results.jsonl"
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8765


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(PAGE)

    def do_POST(self):
        if self.path != "/log":
            self.send_error(404)
            return
        length = min(int(self.headers.get("Content-Length", 0)), 1_000_000)
        data = json.loads(self.rfile.read(length) or b"{}")
        with LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps(data) + "\n")
        c, s = data.get("counts", {}), data.get("samples", [])
        tail = f" last=({s[-1]['x']},{s[-1]['y']}) buttons={s[-1]['buttons']}" if s else ""
        print(f"hover={c.get('pointermove_hover')} pressed={c.get('pointermove_pressed')} "
              f"types={data.get('types')}{tail}", flush=True)
        self.send_response(204)
        self.end_headers()

    def log_message(self, *args):
        pass


if __name__ == "__main__":
    print(f"Serving on 0.0.0.0:{PORT}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
