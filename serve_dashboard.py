"""
Tiny HTTP server to serve dashboard.html + TASK_BOARD.md locally.
Run: python serve_dashboard.py
Open: http://localhost:8765/dashboard.html
"""
import http.server
import socketserver
import os
import json

PORT = 8765
DIR = os.path.dirname(os.path.abspath(__file__))

class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIR, **kwargs)
    
    def end_headers(self):
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Cache-Control', 'no-cache, no-store, must-revalidate')
        super().end_headers()

    def do_GET(self):
        # /health is a tiny liveness probe the watchdog hits. It reports whether the
        # pipeline's key input files exist, plus a simple "ok" body so the watchdog
        # can tell the server is actually serving (not just a wedged port holder).
        if self.path.startswith("/health"):
            board = os.path.join(DIR, "TASK_BOARD.md")
            body = {
                "ok": True,
                "service": "bandroom-dashboard",
                "taskBoardPresent": os.path.exists(board),
            }
            payload = json.dumps(body).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return
        super().do_GET()

    def log_message(self, format, *args):
        print(f"[dashboard] {args[0]}")

if __name__ == '__main__':
    print(f"Dashboard: http://localhost:{PORT}/dashboard.html")
    print(f"Task board: http://localhost:{PORT}/TASK_BOARD.md")
   
    print("Press Ctrl+C to stop")
    with socketserver.TCPServer(("", PORT), Handler) as httpd:
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nStopped.")