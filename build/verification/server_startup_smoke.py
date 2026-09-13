"""Check first-start configuration precedence in an isolated Production server."""

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

from server_smoke import TestServer, check_profile, expect, require, running_server


class StartupServer(TestServer):
    def start(self):
        env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Production",
                   SYNCCLIPBOARD_USERNAME="smoke", SYNCCLIPBOARD_PASSWORD=self.password)
        if self.args.container:
            # A different port from the copied default (5033) proves the override took effect.
            endpoint = "http://+:5034"
        else:
            with socket.socket() as sock:
                sock.bind(("127.0.0.1", 0))
                port = sock.getsockname()[1]
            endpoint = f"http://127.0.0.1:{port}"
            self.base_url = endpoint

        extra_args = []
        if self.args.override == "command-line":
            extra_args = ["--Kestrel:Endpoints:http:Url", endpoint]
        else:
            env["Kestrel__Endpoints__http__Url"] = endpoint

        if self.args.container:
            command = [
                "docker", "run", "--detach", "--user", f"{os.getuid()}:{os.getgid()}",
                "--publish", "127.0.0.1::5034", "--volume", f"{self.root}:/app/data",
                "--env", "ASPNETCORE_ENVIRONMENT", "--env", "SYNCCLIPBOARD_USERNAME",
                "--env", "SYNCCLIPBOARD_PASSWORD"]
            if self.args.override == "environment":
                command.extend(["--env", "Kestrel__Endpoints__http__Url"])
            command.extend([self.args.container, *extra_args])
            self.container = subprocess.check_output(command, env=env, text=True).strip()
            address = subprocess.check_output(
                ["docker", "port", self.container, "5034/tcp"], text=True).strip()
            self.base_url = "http://" + address
        else:
            self.process = subprocess.Popen([
                self.args.dotnet, str(self.args.server_dll.resolve()),
                "--contentRoot", str(self.root), *extra_args], env=env,
                stdout=self.log, stderr=subprocess.STDOUT)


def main():
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--server-dll", type=Path)
    source.add_argument("--container")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    password = secrets.token_hex(16)
    authorization = "Basic " + base64.b64encode(f"smoke:{password}".encode()).decode()
    for override in ("command-line", "environment"):
        args.override = override
        with tempfile.TemporaryDirectory(prefix="syncclipboard-startup-") as temporary:
            root = Path(temporary)
            require(not (root / "appsettings.json").exists(), "Expected an empty configuration directory")
            for attempt in range(2):
                with running_server(args, root, password, authorization, attempt, StartupServer) as url:
                    require((root / "appsettings.json").is_file(), "Default configuration was not copied")
                    expect(url, "/api/time", None, status=401)
                    default_auth = "Basic " + base64.b64encode(b"your_username:your_password").decode()
                    expect(url, "/api/time", default_auth, status=401)
                    expect(url, "/swagger/v1/swagger.json", authorization, status=404)
                    text = "Production startup 中文"
                    if attempt == 0:
                        dto = {"type": "Text", "text": text, "hasData": False,
                               "hash": hashlib.sha256(text.encode()).hexdigest()}
                        expect(url, "/SyncClipboard.json", authorization, method="PUT",
                               body=json.dumps(dto).encode(), content_type="application/json")
                    check_profile(url, authorization, text)
                print(f"Production startup pass: {override}, run {attempt + 1}, credentials, port and persistence")


if __name__ == "__main__":
    main()
