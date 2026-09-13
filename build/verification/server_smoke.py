"""Exercise an isolated server and its persisted data without a desktop session."""

import argparse
import base64
from contextlib import contextmanager
import hashlib
import http.client
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
from urllib.parse import urlsplit


def request(base_url, path, authorization, method="GET", body=None, content_type=None):
    headers = {"Authorization": authorization} if authorization else {}
    if content_type:
        headers["Content-Type"] = content_type
    address = urlsplit(base_url)
    if address.scheme != "http" or address.hostname != "127.0.0.1" or not address.port:
        raise ValueError("Smoke requests must target the isolated loopback HTTP server")
    connection = http.client.HTTPConnection(address.hostname, address.port, timeout=10)
    try:
        connection.request(method, path, body=body, headers=headers)
        with connection.getresponse() as response:
            return response.status, response.read()
    finally:
        connection.close()


def expect(base_url, path, authorization, status=200, **kwargs):
    actual, body = request(base_url, path, authorization, **kwargs)
    if actual != status:
        raise AssertionError(f"{path}: expected {status}, got {actual}: {body[:1000]!r}")
    return body


def check_profile(base_url, authorization, text):
    profile = json.loads(expect(base_url, "/SyncClipboard.json", authorization))
    assert profile["text"] == text, profile
    assert profile["hash"].lower() == hashlib.sha256(text.encode()).hexdigest(), profile
    statistics = json.loads(expect(base_url, "/api/history/statistics", authorization))
    assert statistics["activeCount"] >= 1, statistics
    return profile


def exercise(base_url, authorization):
    expect(base_url, "/api/time", None, status=401)
    expect(base_url, "/api/time", "Basic " + base64.b64encode(b"wrong:wrong").decode(), status=401)
    json.loads(expect(base_url, "/api/time", authorization))
    expect(base_url, "/api/version", authorization)
    swagger = json.loads(expect(base_url, "/swagger/v1/swagger.json", None))
    assert "/api/history/query" in swagger["paths"], swagger.keys()
    negotiation = json.loads(expect(
        base_url, "/SyncClipboardHub/negotiate?negotiateVersion=1", authorization,
        method="POST", body=b""))
    assert negotiation["connectionToken"] and negotiation["availableTransports"], negotiation

    text = "Upgrade smoke: 中文 clipboard\n" + secrets.token_hex(8)
    dto = {"type": "Text", "text": text, "hasData": False,
           "hash": hashlib.sha256(text.encode()).hexdigest()}
    expect(base_url, "/SyncClipboard.json", authorization, method="PUT",
           body=json.dumps(dto).encode(), content_type="application/json")
    first = check_profile(base_url, authorization, text)
    # A repeated upload must not create another history record.
    before = json.loads(expect(base_url, "/api/history/statistics", authorization))
    expect(base_url, "/SyncClipboard.json", authorization, method="PUT",
           body=json.dumps(dto).encode(), content_type="application/json")
    after = json.loads(expect(base_url, "/api/history/statistics", authorization))
    assert after["activeCount"] == before["activeCount"], (before, after)
    invalid = dict(dto, hash="0" * 64)
    expect(base_url, "/SyncClipboard.json", authorization, status=400, method="PUT",
           body=json.dumps(invalid).encode(), content_type="application/json")
    assert check_profile(base_url, authorization, text) == first

    # File-backed text exercises binary transfer, integrity headers and download.
    payload = ("文件内容\n" * 4096).encode()
    digest = hashlib.sha256(payload).hexdigest()
    expect(base_url, "/file/smoke.txt", authorization, method="PUT", body=payload,
           content_type="application/octet-stream")
    transfer = {"type": "Text", "text": "文件内容", "hash": digest, "hasData": True,
                "dataName": "smoke.txt", "transferDataHash": digest}
    expect(base_url, "/SyncClipboard.json", authorization, method="PUT",
           body=json.dumps(transfer).encode(), content_type="application/json")
    downloaded = expect(base_url, "/file/smoke.txt", authorization)
    assert downloaded == payload, "File transfer did not round-trip"
    profile = json.loads(expect(base_url, "/SyncClipboard.json", authorization))
    assert profile["transferDataHash"].lower() == digest, profile
    return profile, payload


class TestServer:
    """Own either one child process or one disposable container."""

    def __init__(self, args, root, password, log):
        self.args = args
        self.root = root
        self.password = password
        self.log = log
        self.process = None
        self.container = None
        self.base_url = None

    def start(self):
        if self.args.container:
            self.container = subprocess.check_output([
                "docker", "run", "--detach", "--rm", "--user", f"{os.getuid()}:{os.getgid()}",
                "--publish", "127.0.0.1::5033",
                "--volume", f"{self.root}:/app/data", "--env", "ASPNETCORE_ENVIRONMENT=Development",
                "--env", "ASPNETCORE_URLS=http://+:5033", self.args.container], text=True).strip()
            address = subprocess.check_output(
                ["docker", "port", self.container, "5033/tcp"], text=True).strip()
            self.base_url = "http://" + address
        else:
            with socket.socket() as sock:
                sock.bind(("127.0.0.1", 0))
                port = sock.getsockname()[1]
            self.base_url = f"http://127.0.0.1:{port}"
            env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development",
                       SYNCCLIPBOARD_USERNAME="smoke", SYNCCLIPBOARD_PASSWORD=self.password)
            self.process = subprocess.Popen([
                self.args.dotnet, str(self.args.server_dll.resolve()), "--contentRoot", str(self.root),
                "--urls", self.base_url], env=env, stdout=self.log, stderr=subprocess.STDOUT)

    def wait_ready(self, authorization):
        for _ in range(120):
            if self.process and self.process.poll() is not None:
                raise RuntimeError(f"Server exited: {self.process.returncode}")
            try:
                if request(self.base_url, "/api/time", authorization)[0] == 200:
                    return
            except (OSError, http.client.HTTPException):
                # Docker can accept and reset connections before Kestrel is listening.
                pass
            time.sleep(0.5)
        raise TimeoutError("Server did not become ready")

    def print_log(self):
        if self.container:
            subprocess.run(["docker", "logs", self.container], check=False)
        else:
            self.log.flush()
            self.log.seek(0)
            print(self.log.read())

    def stop(self):
        if self.container:
            subprocess.run(["docker", "stop", "--time", "10", self.container], check=False,
                           stdout=subprocess.DEVNULL)
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()


@contextmanager
def running_server(args, root, password, authorization, attempt):
    with (root / f"server-{attempt}.log").open("w+") as log:
        server = TestServer(args, root, password, log)
        try:
            server.start()
            server.wait_ready(authorization)
            yield server.base_url
        except Exception:
            server.print_log()
            raise
        finally:
            server.stop()


def check_persistence(base_url, authorization, expected):
    profile, payload = expected
    assert json.loads(expect(base_url, "/SyncClipboard.json", authorization)) == profile
    assert expect(base_url, "/file/smoke.txt", authorization) == payload
    statistics = json.loads(expect(base_url, "/api/history/statistics", authorization))
    assert statistics["activeCount"] == 2, statistics


def main():
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--server-dll", type=Path)
    source.add_argument("--container", help="Local image to run; no image is published")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    password = secrets.token_hex(16)
    authorization = "Basic " + base64.b64encode(f"smoke:{password}".encode()).decode()
    with tempfile.TemporaryDirectory(prefix="syncclipboard-smoke-") as directory:
        root = Path(directory)
        (root / "appsettings.json").write_text(json.dumps({
            "AppSettings": {"UserName": "smoke", "Password": password,
                            "MaxSavedHistoryCount": 1000, "HistoryRetentionMinutes": 10080}}))
        expected = None
        for attempt in range(2):
            with running_server(args, root, password, authorization, attempt) as base_url:
                if expected is None:
                    expected = exercise(base_url, authorization)
                else:
                    check_persistence(base_url, authorization, expected)
                print(f"Server smoke pass {attempt + 1}: API, authentication, history and transfer data")


if __name__ == "__main__":
    main()
