"""Verify first-start overrides and restart persistence using isolated test data."""

import argparse
import base64
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


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def request(url, authorization=None, method="GET", body=None):
    address = urlsplit(url)
    connection = http.client.HTTPConnection(address.hostname, address.port, timeout=2)
    headers = {"Authorization": authorization} if authorization else {}
    if body is not None:
        headers["Content-Type"] = "application/json"
    try:
        connection.request(method, address.path, body=body, headers=headers)
        response = connection.getresponse()
        return response.status, response.read()
    finally:
        connection.close()


class Server:
    def __init__(self, args, root, mode, password, log):
        self.args, self.root, self.mode = args, root, mode
        self.password, self.log = password, log
        self.process = None
        self.container = None

    def start(self):
        # Do not inherit personal server settings or URL overrides.
        env = {key: value for key, value in os.environ.items()
               if not key.upper().startswith(("SYNCCLIPBOARD_", "APPSETTINGS__", "KESTREL__", "ASPNETCORE_"))}
        env.pop("DOTNET_URLS", None)
        settings = {"ASPNETCORE_ENVIRONMENT": "Production"}
        if self.args.container:
            endpoint = "http://+:5034"  # Deliberately differs from the default 5033.
        else:
            with socket.socket() as sock:
                sock.bind(("127.0.0.1", 0))
                endpoint = f"http://127.0.0.1:{sock.getsockname()[1]}"
            self.url = endpoint
        overrides = {"Kestrel:Endpoints:http:Url": endpoint,
                     "AppSettings:UserName": "smoke", "AppSettings:Password": self.password}
        extra = []
        if self.mode == "command-line":
            for key, value in overrides.items():
                extra.extend(["--" + key, value])
        else:
            settings.update({key.replace(":", "__"): value for key, value in overrides.items()})
        env.update(settings)
        if self.args.container:
            command = ["docker", "run", "--detach", "--user", f"{os.getuid()}:{os.getgid()}",
                       "--publish", "127.0.0.1::5034", "--volume", f"{self.root}:/app/data"]
            for key in settings:
                command.extend(["--env", key])
            command.extend([self.args.container, *extra])
            self.container = subprocess.check_output(command, env=env, text=True).strip()
            address = subprocess.check_output(
                ["docker", "port", self.container, "5034/tcp"], text=True).strip()
            self.url = "http://" + address
        else:
            self.process = subprocess.Popen(
                [self.args.dotnet, str(self.args.server_dll.resolve()),
                 "--contentRoot", str(self.root), *extra], env=env,
                stdout=self.log, stderr=subprocess.STDOUT)

    def wait_ready(self, authorization):
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            if self.process is not None:
                require(self.process.poll() is None, "Server exited before becoming ready")
            try:
                status, _ = request(self.url + "/api/time", authorization)
                if status == 200:
                    return
            except (OSError, http.client.HTTPException):
                pass
            time.sleep(0.25)
        raise TimeoutError("Server did not honor the configured port and credentials")

    def stop(self):
        if self.container is not None:
            try:
                subprocess.run(["docker", "stop", "--time", "15", self.container],
                               check=True, stdout=subprocess.DEVNULL)
                result = subprocess.check_output(
                    ["docker", "inspect", "--format", "{{.State.ExitCode}}", self.container], text=True)
                require(result.strip() == "0", "Container did not shut down normally")
            finally:
                subprocess.run(["docker", "rm", "--force", self.container],
                               check=True, stdout=subprocess.DEVNULL)
        if self.process is not None:
            if self.process.poll() is None:
                self.process.terminate()
                try:
                    self.process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait()
                    raise RuntimeError("Server required forced shutdown") from None
            require(self.process.returncode == 0, "Server did not shut down normally")


def verify(server, root, authorization, first_start):
    server.wait_ready(authorization)
    require((root / "appsettings.json").is_file(), "Default configuration was not copied")
    for auth in (None, "Basic " + base64.b64encode(b"your_username:your_password").decode()):
        require(request(server.url + "/api/time", auth)[0] == 401,
                "Missing or default credentials were accepted")
    text = "Runtime upgrade 中文 persistence"
    digest = hashlib.sha256(text.encode()).hexdigest()
    if first_start:
        body = json.dumps({"type": "Text", "text": text, "hash": digest, "hasData": False}).encode()
        require(request(server.url + "/SyncClipboard.json", authorization, "PUT", body)[0] == 200,
                "Profile upload failed")
    status, body = request(server.url + "/SyncClipboard.json", authorization)
    require(status == 200, "Profile read failed")
    profile = json.loads(body)
    require(profile["text"] == text and profile["hash"].lower() == digest,
            "Profile data did not survive the restart")


def main():
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--server-dll", type=Path)
    source.add_argument("--container")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    password = secrets.token_hex(16)
    authorization = "Basic " + base64.b64encode(f"smoke:{password}".encode()).decode()
    for mode in ("command-line", "environment"):
        with tempfile.TemporaryDirectory(prefix="syncclipboard-startup-") as temporary:
            root = Path(temporary)
            for attempt in range(2):
                with (root / f"server-{attempt}.log").open("w") as log:
                    server = Server(args, root, mode, password, log)
                    try:
                        server.start()
                        verify(server, root, authorization, attempt == 0)
                    finally:
                        server.stop()
                print(f"Startup smoke passed: {mode}, run {attempt + 1}, port, credentials and persistence",
                      flush=True)


if __name__ == "__main__":
    main()
