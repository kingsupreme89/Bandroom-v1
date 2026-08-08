"""
Tiny HTTP server to serve dashboard.html + TASK_BOARD.md locally.
Run: python serve_dashboard.py
Open: http://localhost:8765/dashboard.html
"""
import http.server
import socketserver
import os

PORT = 8765
DIR = os.path.dirname(os.path.abspath(__file__))

class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIR, **kwargs)
    
    def end_headers(self):
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Cache-Control', 'no-cache, no-store, must-revalidate')
        super().end_headers()

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