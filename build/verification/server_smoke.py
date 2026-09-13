"""Exercise an isolated server and its persisted data without a desktop session."""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def request(base_url, path, authorization, method="GET", body=None, content_type=None):
    headers = {"Authorization": authorization} if authorization else {}
    if content_type:
        headers["Content-Type"] = content_type
    req = urllib.request.Request(base_url + path, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


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
            process = None
            container = None
            with (root / f"server-{attempt}.log").open("w+") as log:
                try:
                    if args.container:
                        container = subprocess.check_output([
                            "docker", "run", "--detach", "--rm", "--publish", "127.0.0.1::5033",
                            "--volume", f"{root}:/app/data", "--env", "ASPNETCORE_ENVIRONMENT=Development",
                            "--env", "ASPNETCORE_URLS=http://+:5033", args.container], text=True).strip()
                        address = subprocess.check_output(
                            ["docker", "port", container, "5033/tcp"], text=True).strip()
                        base_url = "http://" + address
                    else:
                        with socket.socket() as sock:
                            sock.bind(("127.0.0.1", 0))
                            port = sock.getsockname()[1]
                        base_url = f"http://127.0.0.1:{port}"
                        env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development",
                                   SYNCCLIPBOARD_USERNAME="smoke", SYNCCLIPBOARD_PASSWORD=password)
                        process = subprocess.Popen([
                            args.dotnet, str(args.server_dll.resolve()), "--contentRoot", str(root),
                            "--urls", base_url], env=env, stdout=log, stderr=subprocess.STDOUT)
                    for _ in range(120):
                        if process and process.poll() is not None:
                            raise RuntimeError(f"Server exited: {process.returncode}")
                        try:
                            if request(base_url, "/api/time", authorization)[0] == 200:
                                break
                        except (urllib.error.URLError, TimeoutError):
                            pass
                        time.sleep(0.5)
                    else:
                        raise TimeoutError("Server did not become ready")
                    if expected is None:
                        expected = exercise(base_url, authorization)
                    else:
                        profile, payload = expected
                        assert json.loads(expect(base_url, "/SyncClipboard.json", authorization)) == profile
                        assert expect(base_url, "/file/smoke.txt", authorization) == payload
                        statistics = json.loads(expect(base_url, "/api/history/statistics", authorization))
                        assert statistics["activeCount"] == 2, statistics
                    print(f"Server smoke pass {attempt + 1}: API, authentication, history and transfer data")
                except Exception:
                    if container:
                        subprocess.run(["docker", "logs", container], check=False)
                    else:
                        log.flush()
                        print((root / f"server-{attempt}.log").read_text())
                    raise
                finally:
                    if container:
                        subprocess.run(["docker", "stop", "--time", "10", container], check=False,
                                       stdout=subprocess.DEVNULL)
                    if process and process.poll() is None:
                        process.terminate()
                        try:
                            process.wait(timeout=15)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait()


if __name__ == "__main__":
    main()
