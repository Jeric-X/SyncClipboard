"""Check standalone and built-in diagnostic HTTP endpoints without any desktop code."""

import argparse
import base64
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile

from openapi_contract import check_document
from server_smoke import TestServer, expect, require, running_server


class DiagnosticServer(TestServer):
    def start(self):
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        self.base_url = f"http://127.0.0.1:{port}"
        (self.root / "probe-settings.json").write_text(json.dumps({
            "Kestrel": {"Endpoints": {"Probe": {"Url": self.base_url}}}}))
        (self.root / "appsettings.json").write_text(json.dumps({
            "AppSettings": {"MaxSavedHistoryCount": 1000, "HistoryRetentionMinutes": 10080}}))
        env = dict(os.environ, ASPNETCORE_ENVIRONMENT=self.args.environment,
                   DOTNET_ENVIRONMENT=self.args.environment,
                   SYNCCLIPBOARD_USERNAME="smoke", SYNCCLIPBOARD_PASSWORD=self.password,
                   SYNC_PROBE_ROOT=str(self.root),
                   SYNC_PROBE_DIAGNOSE=str(self.args.diagnose).lower())
        dll = self.args.probe_dll if self.args.builtin else self.args.server_dll
        self.process = subprocess.Popen([
            self.args.dotnet, str(dll.resolve()), "--contentRoot", str(self.root),
            "--urls", self.base_url], env=env, stdout=self.log, stderr=subprocess.STDOUT)


def form_request(base_url, path, authorization, fields, data=None, status=200):
    boundary = "syncclipboard-" + secrets.token_hex(12)
    parts = []
    for name, value in fields.items():
        parts.append((f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n'
                      f'\r\n{value}\r\n').encode())
    if data is not None:
        parts.append((f'--{boundary}\r\nContent-Disposition: form-data; name="data"; '
                      'filename="text.txt"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()
                     + data + b"\r\n")
    parts.append(f"--{boundary}--\r\n".encode())
    return expect(base_url, path, authorization, status=status, method="POST",
                  body=b"".join(parts), content_type=f"multipart/form-data; boundary={boundary}")


def check_history_forms(base_url, authorization):
    now = datetime.now(timezone.utc)
    text = "Swagger form 中文 " + secrets.token_hex(8)
    fields = {"hash": hashlib.sha256(text.encode()).hexdigest(), "type": "Text", "text": text,
              "createTime": now.isoformat(), "lastModified": now.isoformat(),
              "lastAccessed": now.isoformat(), "starred": "true", "pinned": "true",
              "version": "7", "isDeleted": "false", "size": str(len(text)), "hasData": "false"}
    record = json.loads(form_request(base_url, "/api/history", authorization, fields))
    require(record["hash"].lower() == fields["hash"] and record["type"] == "Text"
            and record["starred"] and record["pinned"] and record["version"] == 7, record)
    for missing in ("hash", "type"):
        invalid = {name: value for name, value in fields.items() if name != missing}
        form_request(base_url, "/api/history", authorization, invalid, status=400)
    query = {"page": "1", "before": (now + timedelta(days=1)).isoformat(),
             "after": (now - timedelta(days=1)).isoformat(),
             "modifiedAfter": (now - timedelta(days=1)).isoformat(),
             "types": "Text", "searchText": text, "starred": "true", "sortByLastAccessed": "true"}
    matches = json.loads(form_request(base_url, "/api/history/query", authorization, query))
    require(len(matches) == 1 and matches[0]["hash"].lower() == fields["hash"], matches)
    for name, value in (("starred", "false"), ("types", "Image"), ("page", "2")):
        result = json.loads(form_request(base_url, "/api/history/query", authorization, dict(query, **{name: value})))
        require(result == [], f"Query filter ignored: {name}: {result}")
    form_request(base_url, "/api/history/query", authorization, dict(query, after=query["before"]), status=400)
    payload = ("Swagger multipart transfer 中文\n" * 2048).encode()
    digest = hashlib.sha256(payload).hexdigest()
    transfer = dict(fields, hash=digest, text="Swagger multipart transfer", hasData="true", size=str(len(payload)))
    uploaded = json.loads(form_request(base_url, "/api/history", authorization, transfer, data=payload))
    require(uploaded["hasData"] and uploaded["hash"].lower() == digest, uploaded)
    require(expect(base_url, f"/api/history/Text-{digest}/data", authorization) == payload,
            "Documented multipart upload did not round-trip")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server-dll", type=Path, required=True)
    parser.add_argument("--probe-dll", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    args.container = None
    cases = [(False, "Development", False, True), (False, "Production", False, False),
             (True, "Production", False, False), (True, "Production", True, True)]
    for builtin, environment, diagnose, visible in cases:
        args.builtin, args.environment, args.diagnose = builtin, environment, diagnose
        password = secrets.token_hex(16)
        authorization = "Basic " + base64.b64encode(f"smoke:{password}".encode()).decode()
        with tempfile.TemporaryDirectory(prefix="syncclipboard-openapi-") as directory:
            root = Path(directory).resolve()
            with running_server(args, root, password, authorization, "diagnostic", DiagnosticServer) as base_url:
                expect(base_url, "/api/time", None, status=401)
                expect(base_url, "/api/time", authorization)
                result = expect(base_url, "/swagger/v1/swagger.json", None, status=200 if visible else 404)
                if visible:
                    check_document(json.loads(result))
                    check_history_forms(base_url, authorization)
        print(f"Swagger HTTP pass: builtin={builtin}, environment={environment}, diagnose={diagnose}, visible={visible}")


if __name__ == "__main__":
    main()
