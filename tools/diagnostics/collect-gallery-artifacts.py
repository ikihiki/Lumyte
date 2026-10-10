#!/usr/bin/env python3
"""Collect inert gallery inputs from successful CI runs, never executable PR assets."""

import argparse
import datetime as dt
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import selectors
import stat
import subprocess
import sys
import tempfile
import time
import zipfile
import zlib


WORKFLOW_PATH = ".github/workflows/composition.yml"
MAX_API_BYTES = 10 * 1024 * 1024
MAX_ARCHIVE_BYTES = 50 * 1024 * 1024
MAX_FILE_BYTES = 32 * 1024 * 1024
MAX_JSON_BYTES = 1024 * 1024
MAX_SITE_BYTES = 500 * 1024 * 1024
MAX_ZIP_ENTRIES = 512
REPOSITORY_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,99}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\Z")
SHA_PATTERN = re.compile(r"[a-fA-F0-9]{40}\Z")
IMAGE_PATTERN = re.compile(r"screenshots/[a-z0-9][a-z0-9-]{0,119}\.png\Z")


class CollectionError(Exception):
    """An unsafe or incomplete collection must not replace the published site."""


class ApiError(CollectionError):
    def __init__(self, message, status=None):
        super().__init__(message)
        self.status = status


class LegacyArtifact(Exception):
    """A pre-collector artifact can be skipped while looking for a usable preview."""


def integer(value, description):
    if type(value) is not int or value <= 0:
        raise CollectionError(f"Invalid {description}.")
    return value


def sha(value):
    if not isinstance(value, str) or not SHA_PATTERN.fullmatch(value):
        raise CollectionError("Invalid GitHub commit SHA.")
    return value.lower()


def timestamp(value):
    if not isinstance(value, str):
        raise CollectionError("Invalid GitHub timestamp.")
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise CollectionError("Invalid GitHub timestamp.") from error
    if parsed.tzinfo is None:
        raise CollectionError("GitHub timestamps must include a time zone.")
    return parsed


def decode_json(data):
    try:
        return json.loads(data)
    except (UnicodeError, ValueError) as error:
        raise CollectionError("Invalid JSON in GitHub response or gallery metadata.") from error


class GitHubApi:
    """gh owns authentication/redirects; bounded pipes keep archive downloads finite."""

    def _request(self, endpoint, maximum):
        environment = os.environ.copy()
        if not environment.get("GH_TOKEN") and environment.get("GITHUB_TOKEN"):
            environment["GH_TOKEN"] = environment["GITHUB_TOKEN"]
        environment["GH_PROMPT_DISABLED"] = "1"
        command = ["gh", "api", endpoint, "-H", "X-GitHub-Api-Version: 2022-11-28"]
        with tempfile.TemporaryFile() as errors:
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=errors, env=environment, shell=False)
            result = bytearray()
            deadline = time.monotonic() + 120
            try:
                with selectors.DefaultSelector() as selector:
                    selector.register(process.stdout, selectors.EVENT_READ)
                    while True:
                        if time.monotonic() >= deadline:
                            raise ApiError("GitHub API request timed out.")
                        if not selector.select(timeout=1):
                            continue
                        block = os.read(process.stdout.fileno(), 65536)
                        if not block:
                            break
                        if len(result) + len(block) > maximum:
                            raise CollectionError("GitHub response exceeds its size limit.")
                        result.extend(block)
                return_code = process.wait(timeout=max(1, deadline - time.monotonic()))
                if return_code:
                    errors.seek(0)
                    message = errors.read(8192).decode("utf-8", errors="replace")
                    match = re.search(r"HTTP (\d{3})", message)
                    status = int(match.group(1)) if match else None
                    # Do not echo remote bodies, credentials, or gh diagnostics into public logs.
                    suffix = f" (HTTP {status})" if status else ""
                    raise ApiError(f"GitHub API request failed{suffix}.", status)
                return bytes(result)
            finally:
                if process.poll() is None:
                    process.kill()
                process.wait()
                process.stdout.close()

    def json(self, endpoint):
        return decode_json(self._request(endpoint, MAX_API_BYTES))

    def download(self, endpoint):
        return self._request(endpoint, MAX_ARCHIVE_BYTES)


def pages(api, endpoint, field=None):
    separator = "&" if "?" in endpoint else "?"
    for page in range(1, 1001):
        response = api.json(f"{endpoint}{separator}per_page=100&page={page}")
        items = response.get(field) if field and isinstance(response, dict) else response
        if not isinstance(items, list):
            raise CollectionError("GitHub pagination returned an invalid response.")
        yield from items
        if len(items) < 100:
            return
    raise CollectionError("GitHub pagination exceeded the collection limit.")


def require_repository(value, repository):
    if not isinstance(value, dict) or value.get("full_name", "").lower() != repository.lower():
        raise CollectionError("GitHub run belongs to an unexpected repository.")


def validate_archive(data, repository, head_commit):
    """Validate every ZIP entry before selecting only manifest, metadata and PNG data."""
    if len(data) > MAX_ARCHIVE_BYTES:
        raise CollectionError("Gallery archive exceeds 50 MiB.")
    try:
        archive = zipfile.ZipFile(io.BytesIO(data))
    except (zipfile.BadZipFile, OSError) as error:
        raise CollectionError("Gallery artifact is not a valid ZIP archive.") from error
    with archive:
        infos = archive.infolist()
        if len(infos) > MAX_ZIP_ENTRIES:
            raise CollectionError("Gallery archive contains too many entries.")
        names = set()
        extracted_size = 0
        selected = {}
        for info in infos:
            name = info.filename
            path = PurePosixPath(name)
            parts = name.rstrip("/").split("/")
            if (not name or name != info.orig_filename or "\\" in name or ":" in name or
                    "\x00" in name or path.is_absolute() or any(part in ("", ".", "..") for part in parts)):
                raise CollectionError("Gallery archive contains an unsafe path.")
            canonical_name = name.rstrip("/")
            if canonical_name in names:
                raise CollectionError("Gallery archive contains duplicate paths.")
            names.add(canonical_name)
            mode = info.external_attr >> 16
            kind = stat.S_IFMT(mode)
            if kind not in (0, stat.S_IFREG, stat.S_IFDIR) or (kind == stat.S_IFDIR) != info.is_dir() and kind != 0:
                raise CollectionError("Gallery archive contains a symbolic link or special file.")
            if info.flag_bits & 1 or info.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
                raise CollectionError("Gallery archive uses unsupported encryption or compression.")
            if info.file_size > MAX_FILE_BYTES or info.file_size < 0 or info.is_dir() and info.file_size:
                raise CollectionError("Gallery archive entry exceeds its size limit.")
            extracted_size += info.file_size
            if extracted_size > MAX_ARCHIVE_BYTES:
                raise CollectionError("Expanded gallery archive exceeds 50 MiB.")
            if name in ("gallery.json", "metadata.json") or IMAGE_PATTERN.fullmatch(name):
                if info.is_dir() or name.endswith(".json") and info.file_size > MAX_JSON_BYTES:
                    raise CollectionError("Gallery manifest or metadata exceeds its size limit.")
                selected[name] = info
        if "gallery.json" not in selected or "metadata.json" not in selected:
            raise LegacyArtifact("Artifact predates multi-PR gallery support.")
        try:
            metadata_data = archive.read(selected["metadata.json"])
            metadata = decode_json(metadata_data)
            if not isinstance(metadata, dict):
                raise CollectionError("Invalid gallery metadata.")
            if "sourceCommit" not in metadata:
                raise LegacyArtifact("Artifact lacks the source commit provenance.")
            if metadata.get("repository") != repository or sha(metadata.get("sourceCommit")) != head_commit:
                raise CollectionError("Gallery metadata does not match its GitHub run provenance.")
            files = {name: archive.read(info) for name, info in selected.items()}
        except (zipfile.BadZipFile, RuntimeError, EOFError, OSError, zlib.error) as error:
            raise CollectionError("Gallery archive could not be safely decoded.") from error
        return files


def ensure_empty_directory(output):
    output = Path(os.path.abspath(output))
    for path in (output, *output.parents):
        if path.is_symlink():
            raise CollectionError("Output paths must not contain symbolic links.")
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        raise CollectionError("Output directory must be empty.")
    output.mkdir(parents=True, exist_ok=True)
    return output


def verify_successful_run_snapshot(api, base, repository, run):
    """A rerun must not swap artifacts between the run listing and the download."""
    current = api.json(f"{base}/actions/runs/{run['id']}")
    if not isinstance(current, dict):
        raise CollectionError("Invalid workflow run confirmation response.")
    require_repository(current.get("repository"), repository)
    require_repository(current.get("head_repository"), repository)
    identity_fields = ("id", "workflow_id", "path", "head_sha", "head_branch", "event", "run_number", "run_attempt")
    if (any(current.get(field) != run.get(field) for field in identity_fields) or
            current.get("status") != "completed" or current.get("conclusion") != "success"):
        raise CollectionError("CI run changed while collecting its gallery; retry after the current CI run completes.")


def collect(repository, output, api=None, now=None):
    if not isinstance(repository, str) or not REPOSITORY_PATTERN.fullmatch(repository):
        raise CollectionError("Repository must be an owner/repository name.")
    api = api or GitHubApi()
    output = ensure_empty_directory(output)
    base = f"repos/{repository}"
    workflow = api.json(f"{base}/actions/workflows/composition.yml")
    if not isinstance(workflow, dict) or workflow.get("path") != WORKFLOW_PATH:
        raise CollectionError("Unexpected CI workflow path.")
    workflow_id = integer(workflow.get("id"), "workflow id")
    main = api.json(f"{base}/commits/main")
    entries = [{"key": "main", "number": None, "title": "main", "headCommit": sha(main.get("sha")), "latestRun": None, "preview": None}]
    pull_requests = {}
    for pr in pages(api, f"{base}/pulls?state=open"):
        if not isinstance(pr, dict):
            raise CollectionError("Invalid pull request response.")
        if pr.get("state") != "open":
            continue
        head = pr.get("head") or {}
        head_repository = head.get("repo") or {}
        if head_repository.get("full_name", "").lower() != repository.lower():
            continue
        require_repository((pr.get("base") or {}).get("repo"), repository)
        number = integer(pr.get("number"), "pull request number")
        if number in pull_requests:
            continue
        title = pr.get("title")
        if not isinstance(title, str) or not title.strip() or len(title) > 1000:
            raise CollectionError("Invalid pull request title.")
        entry = {"key": f"pr/{number}", "number": number, "title": title, "headCommit": sha(head.get("sha")), "latestRun": None, "preview": None}
        pull_requests[number] = entry
    entries.extend(pull_requests[number] for number in sorted(pull_requests))
    candidates = {entry["key"]: [] for entry in entries}
    seen_runs = set()
    for run in pages(api, f"{base}/actions/workflows/{workflow_id}/runs", "workflow_runs"):
        if not isinstance(run, dict):
            raise CollectionError("Invalid workflow run response.")
        run_id = integer(run.get("id"), "run id")
        if run_id in seen_runs:
            continue
        seen_runs.add(run_id)
        require_repository(run.get("repository"), repository)
        if run.get("workflow_id") != workflow_id or run.get("path", "").split("@")[0] != WORKFLOW_PATH:
            raise CollectionError("Workflow run does not match the expected CI workflow.")
        if (run.get("head_repository") or {}).get("full_name", "").lower() != repository.lower():
            continue
        event = run.get("event")
        keys = []
        associations = run.get("pull_requests")
        if not isinstance(associations, list):
            raise CollectionError("Invalid workflow run pull request associations.")
        if event in ("push", "workflow_dispatch") and run.get("head_branch") == "main" and not associations:
            keys.append("main")
        elif event == "pull_request":
            for association in associations:
                if not isinstance(association, dict):
                    raise CollectionError("Invalid workflow run pull request association.")
                number = integer(association.get("number"), "associated pull request number")
                if number in pull_requests:
                    keys.append(f"pr/{number}")
        if not keys:
            continue
        run["head_sha"] = sha(run.get("head_sha"))
        integer(run.get("run_number"), "workflow run number")
        integer(run.get("run_attempt"), "workflow run attempt")
        timestamp(run.get("created_at"))
        if not isinstance(run.get("status"), str) or len(run["status"]) > 40 or run.get("conclusion") is not None and not isinstance(run["conclusion"], str):
            raise CollectionError("Invalid workflow run status.")
        for key in set(keys):
            candidates[key].append(run)
    total_bytes = 0
    cached = {}
    for entry in entries:
        runs = sorted(candidates[entry["key"]], key=lambda run: (run["run_number"], timestamp(run["created_at"]), run["id"]), reverse=True)
        if runs:
            latest = runs[0]
            entry["latestRun"] = {"id": latest["id"], "status": latest["status"], "conclusion": latest.get("conclusion"), "headCommit": latest["head_sha"]}
        reason = "成功した CI の画面がまだありません。"
        for run in runs:
            if run["status"] != "completed" or run.get("conclusion") != "success":
                continue
            cache_key = (run["id"], run["run_attempt"])
            if cache_key in cached:
                entry["preview"] = cached[cache_key]
                if entry["preview"]:
                    break
                continue
            try:
                artifacts = list(pages(api, f"{base}/actions/runs/{run['id']}/artifacts", "artifacts"))
            except ApiError as error:
                if error.status == 404:
                    cached[cache_key] = None
                    reason = "画面を生成した CI の実行が削除されています。CI を再実行してください。"
                    continue
                raise
            galleries = []
            for artifact in artifacts:
                if not isinstance(artifact, dict):
                    raise CollectionError("Invalid artifact response.")
                if artifact.get("name") != "diagnostics-gallery":
                    continue
                integer(artifact.get("id"), "artifact id")
                if artifact.get("expired") is True:
                    reason = "画面の保存期限が切れています。CI を再実行してください。"
                    continue
                if artifact.get("expired") is not False:
                    raise CollectionError("Artifact expiration state is missing.")
                provenance = artifact.get("workflow_run")
                if provenance is not None and (not isinstance(provenance, dict) or provenance.get("id") != run["id"] or sha(provenance.get("head_sha")) != run["head_sha"]):
                    raise CollectionError("Artifact does not match its workflow run.")
                galleries.append(artifact)
            for artifact in sorted(galleries, key=lambda item: item["id"], reverse=True):
                try:
                    data = api.download(f"{base}/actions/artifacts/{artifact['id']}/zip")
                    files = validate_archive(data, repository, run["head_sha"])
                except ApiError as error:
                    if error.status not in (404, 410):
                        raise
                    reason = "画面が削除済み、または保存期限切れです。CI を再実行してください。"
                    continue
                except LegacyArtifact:
                    reason = "画面の再生成が必要です。CI を再実行してください。"
                    continue
                # The original successful run may have started another attempt while
                # its artifact was downloaded. Keep the published site on any change.
                verify_successful_run_snapshot(api, base, repository, run)
                size = sum(map(len, files.values()))
                total_bytes += size
                if total_bytes > MAX_SITE_BYTES:
                    raise CollectionError("Combined gallery inputs exceed 500 MiB.")
                directory = f"galleries/{run['id']}-{run['run_attempt']}"
                destination = output / directory
                destination.mkdir(parents=True)
                for filename, contents in files.items():
                    target = destination / filename
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(contents)
                entry["preview"] = {"directory": directory, "runId": run["id"], "headCommit": run["head_sha"]}
                break
            cached[cache_key] = entry["preview"]
            if entry["preview"]:
                break
        if not entry["preview"]:
            entry["unavailableReason"] = reason
    instant = now or dt.datetime.now(dt.timezone.utc)
    catalog = {"version": 1, "repository": repository, "generatedAt": instant.astimezone(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z"), "entries": entries}
    (output / "catalog.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return catalog


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--output", required=True)
    arguments = parser.parse_args()
    try:
        catalog = collect(arguments.repository, arguments.output)
        available = sum(entry["preview"] is not None for entry in catalog["entries"])
        print(f"Collected {available} galleries for {len(catalog['entries'])} main/PR entries.")
    except (CollectionError, OSError, subprocess.SubprocessError) as error:
        print(f"Gallery collection failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
