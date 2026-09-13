"""Run the actual S3 adapter against an isolated MinIO process and fault proxy."""

import argparse
from collections import Counter
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import tempfile
import threading
import time
from urllib.parse import urlsplit


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def available_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


class FaultProxy(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *_):
        pass

    def relay(self):
        target = urlsplit(self.path)
        if target.scheme != "http" or target.hostname != "127.0.0.1" or target.port != self.server.target_port:
            self.send_error(400, "Only the isolated MinIO endpoint is allowed")
            return
        key = self.command + " " + target.path
        with self.server.counter_lock:
            self.server.attempts[key] += 1
            attempt = self.server.attempts[key]
        body = self.read_body()
        if self.should_fail(target, attempt):
            self.send_failure()
            return
        self.forward(target, body)

    def read_body(self):
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            body = bytearray()
            while True:
                length = int(self.rfile.readline().split(b";", 1)[0], 16)
                if not length:
                    while self.rfile.readline().strip():
                        pass
                    break
                body.extend(self.rfile.read(length))
                require(self.rfile.read(2) == b"\r\n", "Malformed HTTP chunk")
        else:
            body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        return body

    def should_fail(self, target, attempt):
        retry_put = self.command == "PUT" and target.path.endswith("/retry-upload.bin")
        retry_get = self.command == "GET" and target.path.endswith("/retry-download.bin")
        always_fail = self.command == "PUT" and target.path.endswith("/always-fail.bin")
        return always_fail or ((retry_put or retry_get) and attempt <= 2)

    def send_failure(self):
        payload = b'<Error><Code>InternalError</Code><Message>Isolated retry test</Message></Error>'
        self.send_response(500)
        self.send_header("Content-Type", "application/xml")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(payload)
        self.close_connection = True
        return

    def forward(self, target, body):
        headers = {name: value for name, value in self.headers.items()
                   if name.lower() not in {"connection", "proxy-connection", "transfer-encoding", "expect"}}
        if body or "Content-Length" in headers:
            headers["Content-Length"] = str(len(body))
        upstream = http.client.HTTPConnection("127.0.0.1", self.server.target_port, timeout=30)
        try:
            path = target.path + ("?" + target.query if target.query else "")
            upstream.request(self.command, path, body=body, headers=headers)
            with upstream.getresponse() as response:
                self.send_response(response.status)
                for name, value in response.getheaders():
                    if name.lower() not in {"connection", "transfer-encoding", "server", "date"}:
                        self.send_header(name, value)
                self.send_header("Connection", "close")
                self.end_headers()
                shutil.copyfileobj(response, self.wfile)
            self.close_connection = True
        finally:
            upstream.close()

    do_GET = do_PUT = do_POST = do_HEAD = do_DELETE = relay


class FaultProxyServer(ThreadingHTTPServer):
    def __init__(self, port):
        self.target_port = port
        self.attempts = Counter()
        self.counter_lock = threading.Lock()
        super().__init__(("127.0.0.1", 0), FaultProxy)


def wait_for_minio(server, port):
    for _ in range(100):
        require(server.poll() is None, "MinIO exited before becoming ready.")
        connection = http.client.HTTPConnection("127.0.0.1", port, timeout=1)
        try:
            connection.request("GET", "/minio/health/live")
            if connection.getresponse().status == 200:
                break
        except OSError:
            pass
        finally:
            connection.close()
        time.sleep(0.1)
    else:
        raise RuntimeError("MinIO did not become ready.")


def verify_probe_result(args, result, output, attempts, access_key, secret_key):
    print(result.stdout.replace(access_key, "[redacted]").replace(secret_key, "[redacted]"))
    print("Fault proxy attempts:", json.dumps(dict(attempts), ensure_ascii=False))
    require(result.returncode == 0, f"S3 probe exited {result.returncode}")
    report = json.loads((output / "result.json").read_text())
    for method, name in [("PUT", "retry-upload.bin"), ("GET", "retry-download.bin")]:
        count = sum(n for key, n in attempts.items() if key.startswith(method + " ") and key.endswith("/" + name))
        require(count == 3, f"Expected two injected errors then success for {name}, got {count}")
    persistent = sum(n for key, n in attempts.items() if key.startswith("PUT ") and key.endswith("/always-fail.bin"))
    require(2 <= persistent <= report["maximumAttempts"], f"Unexpected persistent-error retry count: {persistent}")
    require(len(report["checks"]) == 7, "Incomplete S3 verification coverage")
    report.update(minioSha256=hashlib.sha256(args.minio.read_bytes()).hexdigest(),
                  retryAttempts={"upload": 3, "download": 3, "persistentFailure": persistent})
    args.report.write_text(json.dumps(report, indent=2, ensure_ascii=False))
    print("S3 smoke complete: 7 groups passed; isolated bucket cleaned up.")


def stop_server(server):
    server.terminate()
    try:
        server.wait(timeout=10)
    except subprocess.TimeoutExpired:
        server.kill()
        server.wait()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--minio", type=Path, required=True)
    parser.add_argument("--probe", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    args.minio = args.minio.resolve()
    args.probe = args.probe.resolve()
    require(args.minio.is_file() and args.probe.is_file(), "Missing MinIO or compiled probe.")
    require(not args.report.exists(), "Use a new report path; preserve previous evidence.")
    access_key, secret_key = "probe" + secrets.token_hex(8), secrets.token_hex(24)
    port = available_port()
    endpoint = f"http://127.0.0.1:{port}"
    proxy = FaultProxyServer(port)
    proxy_thread = threading.Thread(target=proxy.serve_forever, daemon=True)
    proxy_thread.start()
    try:
        with tempfile.TemporaryDirectory(prefix="syncclipboard-s3-") as temporary:
            root = Path(temporary)
            data = root / "data"
            data.mkdir()
            output = root / "probe"
            env = dict(os.environ, MINIO_ROOT_USER=access_key, MINIO_ROOT_PASSWORD=secret_key,
                       MINIO_BROWSER="off", MINIO_UPDATE="off",
                       SYNCCLIPBOARD_S3_TEST_ACCESS_KEY=access_key,
                       SYNCCLIPBOARD_S3_TEST_SECRET_KEY=secret_key)
            with (root / "minio.log").open("w+") as log:
                server = subprocess.Popen([str(args.minio), "server", str(data), "--address", f"127.0.0.1:{port}"],
                                          env=env, stdout=log, stderr=subprocess.STDOUT)
                try:
                    wait_for_minio(server, port)
                    result = subprocess.run([args.dotnet, str(args.probe), endpoint,
                                             f"http://127.0.0.1:{proxy.server_port}", str(output)],
                                            env=env, text=True, stdout=subprocess.PIPE,
                                            stderr=subprocess.STDOUT, timeout=300)
                    verify_probe_result(args, result, output, proxy.attempts, access_key, secret_key)
                finally:
                    stop_server(server)
    finally:
        proxy.shutdown()
        proxy.server_close()
        proxy_thread.join(timeout=5)


if __name__ == "__main__":
    main()
