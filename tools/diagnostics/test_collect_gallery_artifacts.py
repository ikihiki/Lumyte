import datetime as dt
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile


SPEC = importlib.util.spec_from_file_location("collector", Path(__file__).with_name("collect-gallery-artifacts.py"))
collector = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(collector)
REPOSITORY = "ikihiki/Lumyte"
BASE = f"repos/{REPOSITORY}"
SHA_A = "a" * 40
SHA_B = "b" * 40
SHA_C = "c" * 40
NOW = dt.datetime(2026, 10, 10, 12, tzinfo=dt.timezone.utc)


def pull(number, head=SHA_A, repository=REPOSITORY, state="open"):
    return {"number": number, "state": state, "title": f"PR {number}",
            "head": {"sha": head, "repo": {"full_name": repository}},
            "base": {"repo": {"full_name": REPOSITORY}}}


def run(identifier, number=None, head=SHA_A, status="completed", conclusion="success", **changes):
    result = {"id": identifier, "workflow_id": 42, "path": collector.WORKFLOW_PATH,
              "repository": {"full_name": REPOSITORY}, "head_repository": {"full_name": REPOSITORY},
              "event": "pull_request" if number else "push", "head_branch": f"feature-{number}" if number else "main",
              "pull_requests": [{"number": number}] if number else [], "head_sha": head,
              "run_number": identifier, "run_attempt": 1, "status": status, "conclusion": conclusion,
              "created_at": "2026-10-10T12:00:00Z", "updated_at": "2026-10-10T12:00:00Z"}
    result.update(changes)
    return result


def archive(source=SHA_A, repository=REPOSITORY, legacy=False, extra=None, manifest=True):
    metadata = {"version": 1, "repository": repository, "commit": SHA_C,
                "sourceCommit": source, "label": "PR", "capturedAt": "2026-10-10T12:00:00.000Z", "screenshotCount": 1}
    if legacy:
        metadata.pop("sourceCommit")
    files = {"metadata.json": json.dumps(metadata), "screenshots/overview.png": b"PNG checked by trusted Node assembler",
             "index.html": "<script>untrusted PR HTML</script>", "gallery.js": "untrusted JavaScript",
             "server.log": "private log"}
    if manifest:
        files["gallery.json"] = json.dumps({"version": 1, "capturedAt": "2026-10-10T12:00:00.000Z", "screenshots": []})
    files.update(extra or {})
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as zipped:
        for name, content in files.items():
            zipped.writestr(name, content)
    return stream.getvalue()


class FakeApi:
    def __init__(self, pulls=None, runs=None):
        self.pulls = pulls or []
        self.runs = runs or []
        self.artifacts = {}
        self.downloads = {}
        self.overrides = {}
        self.calls = []
        self.workflow = {"id": 42, "path": collector.WORKFLOW_PATH}

    def add(self, run_id, artifact_id=None, data=None, expired=False):
        artifact_id = artifact_id or run_id * 10
        self.artifacts.setdefault(run_id, []).append({"id": artifact_id, "name": "diagnostics-gallery", "expired": expired})
        self.downloads[artifact_id] = data if data is not None else archive()
        return self.artifacts[run_id][-1]

    def json(self, endpoint):
        self.calls.append(endpoint)
        if endpoint in self.overrides:
            value = self.overrides[endpoint]
            if isinstance(value, Exception):
                raise value
            return value
        if endpoint == f"{BASE}/actions/workflows/composition.yml":
            return self.workflow
        if endpoint == f"{BASE}/commits/main":
            return {"sha": SHA_C}
        if endpoint.startswith(f"{BASE}/actions/runs/") and "/artifacts" not in endpoint:
            identifier = int(endpoint.rsplit("/", 1)[1])
            return next(item for item in self.runs if item["id"] == identifier)
        page = int(endpoint.rsplit("page=", 1)[1])
        if endpoint.startswith(f"{BASE}/pulls?"):
            return self.pulls[(page - 1) * 100:page * 100]
        if endpoint.startswith(f"{BASE}/actions/workflows/42/runs?"):
            return {"workflow_runs": self.runs[(page - 1) * 100:page * 100]}
        if endpoint.startswith(f"{BASE}/actions/runs/"):
            identifier = int(endpoint.split("/runs/")[1].split("/")[0])
            return {"artifacts": self.artifacts.get(identifier, [])[(page - 1) * 100:page * 100]}
        raise AssertionError(f"Unexpected API request: {endpoint}")

    def download(self, endpoint):
        self.calls.append(endpoint)
        identifier = int(endpoint.split("/artifacts/")[1].split("/")[0])
        value = self.downloads[identifier]
        if isinstance(value, Exception):
            raise value
        return value


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name) / "inputs"

    def collect(self, api):
        return collector.collect(REPOSITORY, self.output, api, NOW)

    def test_multiple_prs_and_main_survive_failed_newest_run(self):
        api = FakeApi([pull(26, SHA_B), pull(27)], [
            run(15, 26, SHA_B, conclusion="failure"), run(14, 27), run(13, 26), run(12)])
        for identifier in (12, 13, 14):
            api.add(identifier)
        result = self.collect(api)
        self.assertEqual([entry["key"] for entry in result["entries"]], ["main", "pr/26", "pr/27"])
        pr26 = result["entries"][1]
        self.assertEqual(pr26["headCommit"], SHA_B)
        self.assertEqual(pr26["latestRun"]["conclusion"], "failure")
        self.assertEqual(pr26["preview"]["runId"], 13)
        self.assertEqual(pr26["preview"]["headCommit"], SHA_A)
        self.assertEqual(result["generatedAt"], "2026-10-10T12:00:00.000Z")
        self.assertEqual(json.loads((self.output / "catalog.json").read_text()), result)
        names = {path.name for path in (self.output / "galleries/13-1").rglob("*") if path.is_file()}
        self.assertEqual(names, {"gallery.json", "metadata.json", "overview.png"})

    def test_in_progress_latest_run_keeps_previous_success(self):
        api = FakeApi([pull(26, SHA_B)], [run(2, 26, SHA_B, status="in_progress", conclusion=None), run(1, 26)])
        api.add(1)
        entry = self.collect(api)["entries"][1]
        self.assertEqual(entry["latestRun"]["status"], "in_progress")
        self.assertEqual(entry["preview"]["runId"], 1)

    def test_closed_and_fork_prs_and_non_pr_workflows_are_excluded(self):
        api = FakeApi([pull(26), pull(27, state="closed"), pull(28, repository="fork/Lumyte")], [
            run(10, 26, event="pull_request_target"), run(9, 27), run(8, 28),
            run(7, 26, head_repository={"full_name": "fork/Lumyte"}), run(6, 26, pull_requests=[]),
            run(5, head_branch="feature"), run(4, 26)])
        api.add(4)
        result = self.collect(api)
        self.assertEqual([entry["key"] for entry in result["entries"]], ["main", "pr/26"])
        self.assertEqual(result["entries"][1]["preview"]["runId"], 4)

    def test_workflow_dispatch_main_is_supported(self):
        api = FakeApi(runs=[run(1, event="workflow_dispatch")])
        api.add(1)
        self.assertEqual(self.collect(api)["entries"][0]["preview"]["runId"], 1)

    def test_newer_rerun_of_old_run_does_not_replace_newer_commit(self):
        api = FakeApi([pull(26, SHA_B)], [
            run(1, 26, updated_at="2026-10-11T12:00:00Z", run_attempt=5),
            run(2, 26, SHA_B, created_at="2026-10-10T14:00:00Z")])
        api.add(1)
        api.add(2, data=archive(SHA_B))
        entry = self.collect(api)["entries"][1]
        self.assertEqual(entry["latestRun"]["id"], 2)
        self.assertEqual(entry["preview"]["directory"], "galleries/2-1")

    def test_run_number_not_id_controls_latest_run(self):
        api = FakeApi([pull(26)], [run(100, 26, run_number=1), run(2, 26, run_number=2)])
        api.add(2)
        self.assertEqual(self.collect(api)["entries"][1]["preview"]["runId"], 2)

    def test_all_pull_run_and_artifact_pages_are_consumed(self):
        api = FakeApi([pull(number) for number in range(1, 102)], [run(number, 101, conclusion="failure") for number in range(200, 100, -1)] + [run(1, 101)])
        api.artifacts[1] = [{"id": number, "name": "test-results", "expired": False} for number in range(1, 101)]
        api.add(1, artifact_id=1001)
        result = self.collect(api)
        self.assertEqual(len(result["entries"]), 102)
        self.assertEqual(result["entries"][-1]["preview"]["runId"], 1)
        self.assertIn(f"{BASE}/pulls?state=open&per_page=100&page=2", api.calls)
        self.assertIn(f"{BASE}/actions/workflows/42/runs?per_page=100&page=2", api.calls)
        self.assertIn(f"{BASE}/actions/runs/1/artifacts?per_page=100&page=2", api.calls)

    def test_expired_and_legacy_artifacts_fall_back(self):
        api = FakeApi([pull(26)], [run(number, 26) for number in (4, 3, 2, 1)])
        api.add(4, expired=True)
        api.add(3, data=archive(manifest=False))
        api.add(2, data=archive(legacy=True))
        api.add(1)
        self.assertEqual(self.collect(api)["entries"][1]["preview"]["runId"], 1)
        self.assertNotIn(f"{BASE}/actions/artifacts/40/zip", api.calls)

    def test_no_usable_artifact_has_placeholder_without_fake_preview(self):
        api = FakeApi([pull(26)], [run(1, 26)])
        api.add(1, expired=True)
        entry = self.collect(api)["entries"][1]
        self.assertIsNone(entry["preview"])
        self.assertIn("保存期限", entry["unavailableReason"])
        self.assertIn("再実行", entry["unavailableReason"])

    def test_deleted_download_race_falls_back(self):
        for status in (404, 410):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as directory:
                api = FakeApi([pull(26)], [run(2, 26), run(1, 26)])
                api.add(2, data=collector.ApiError("removed", status))
                api.add(1)
                result = collector.collect(REPOSITORY, Path(directory) / "inputs", api, NOW)
                self.assertEqual(result["entries"][1]["preview"]["runId"], 1)

    def test_deleted_run_race_falls_back(self):
        api = FakeApi([pull(26)], [run(2, 26), run(1, 26)])
        api.overrides[f"{BASE}/actions/runs/2/artifacts?per_page=100&page=1"] = collector.ApiError("removed", 404)
        api.add(1)
        self.assertEqual(self.collect(api)["entries"][1]["preview"]["runId"], 1)

    def test_unknown_or_server_download_errors_abort_instead_of_empty_publication(self):
        for status in (None, 403, 429, 500):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as directory:
                api = FakeApi([pull(26)], [run(1, 26)])
                api.add(1, data=collector.ApiError("failed", status))
                output = Path(directory) / "inputs"
                with self.assertRaises(collector.ApiError):
                    collector.collect(REPOSITORY, output, api, NOW)
                self.assertFalse((output / "catalog.json").exists())

    def test_api_server_error_aborts(self):
        api = FakeApi()
        api.overrides[f"{BASE}/actions/workflows/42/runs?per_page=100&page=1"] = collector.ApiError("failed", 500)
        with self.assertRaises(collector.ApiError):
            self.collect(api)
        self.assertFalse((self.output / "catalog.json").exists())

    def test_workflow_and_run_provenance_mismatches_abort(self):
        variations = [{"workflow_id": 43}, {"path": ".github/workflows/other.yml"}, {"repository": {"full_name": "elsewhere/repo"}}]
        for variation in variations:
            with self.subTest(variation=variation), tempfile.TemporaryDirectory() as directory:
                api = FakeApi(runs=[run(1, **variation)])
                with self.assertRaises(collector.CollectionError):
                    collector.collect(REPOSITORY, Path(directory) / "inputs", api, NOW)
        api = FakeApi()
        api.workflow["path"] = ".github/workflows/untrusted.yml"
        with self.assertRaises(collector.CollectionError):
            self.collect(api)

    def test_artifact_provenance_mismatch_aborts(self):
        api = FakeApi(runs=[run(1)])
        artifact = api.add(1)
        artifact["workflow_run"] = {"id": 2, "head_sha": SHA_A}
        with self.assertRaisesRegex(collector.CollectionError, "workflow run"):
            self.collect(api)

    def test_metadata_source_commit_and_repository_must_match_api(self):
        for data in (archive(SHA_B), archive(repository="attacker/repo")):
            with self.subTest(data_size=len(data)), self.assertRaisesRegex(collector.CollectionError, "provenance"):
                collector.validate_archive(data, REPOSITORY, SHA_A)
        files = collector.validate_archive(archive(), REPOSITORY, SHA_A)
        # metadata.commit is the PR merge SHA, and is deliberately different from sourceCommit.
        self.assertEqual(json.loads(files["metadata.json"])["commit"], SHA_C)

    def test_site_total_size_limit_is_enforced_before_catalog(self):
        api = FakeApi([pull(26)], [run(2, 26), run(1)])
        api.add(1)
        api.add(2)
        size = sum(map(len, collector.validate_archive(archive(), REPOSITORY, SHA_A).values()))
        with patch.object(collector, "MAX_SITE_BYTES", size + 1), self.assertRaisesRegex(collector.CollectionError, "Combined"):
            self.collect(api)
        self.assertFalse((self.output / "catalog.json").exists())

    def test_shared_run_is_downloaded_once(self):
        api = FakeApi([pull(26), pull(27)], [run(1, 26, pull_requests=[{"number": 26}, {"number": 27}])])
        api.add(1)
        result = self.collect(api)
        self.assertEqual(result["entries"][1]["preview"], result["entries"][2]["preview"])
        self.assertEqual(api.calls.count(f"{BASE}/actions/artifacts/10/zip"), 1)

    def test_run_recheck_rejects_rerun_and_provenance_changes_after_download(self):
        variations = [
            {"run_attempt": 2, "status": "in_progress", "conclusion": None},
            {"run_attempt": 2}, {"head_sha": SHA_B}, {"status": "completed", "conclusion": "failure"},
            {"workflow_id": 43}, {"repository": {"full_name": "other/repo"}},
            {"head_repository": {"full_name": "fork/Lumyte"}},
        ]
        for changes in variations:
            with self.subTest(changes=changes), tempfile.TemporaryDirectory() as directory:
                api = FakeApi([pull(26)], [run(1, 26)])
                api.add(1)
                api.overrides[f"{BASE}/actions/runs/1"] = run(1, 26, **changes)
                output = Path(directory) / "inputs"
                with self.assertRaises(collector.CollectionError):
                    collector.collect(REPOSITORY, output, api, NOW)
                self.assertIn(f"{BASE}/actions/artifacts/10/zip", api.calls)
                self.assertFalse((output / "catalog.json").exists())
                self.assertFalse((output / "galleries").exists())

    def test_run_recheck_api_errors_preserve_previous_publication(self):
        for status in (404, 500):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as directory:
                api = FakeApi(runs=[run(1)])
                api.add(1)
                api.overrides[f"{BASE}/actions/runs/1"] = collector.ApiError("failed", status)
                output = Path(directory) / "inputs"
                with self.assertRaises(collector.ApiError):
                    collector.collect(REPOSITORY, output, api, NOW)
                self.assertFalse((output / "catalog.json").exists())

    def test_nonempty_or_symlink_output_is_rejected(self):
        self.output.mkdir()
        (self.output / "keep").write_text("existing")
        with self.assertRaisesRegex(collector.CollectionError, "empty"):
            self.collect(FakeApi())
        link = Path(self.temporary.name) / "linked"
        link.symlink_to(self.output, target_is_directory=True)
        with self.assertRaisesRegex(collector.CollectionError, "symbolic"):
            collector.collect(REPOSITORY, link / "child", FakeApi(), NOW)

    def test_invalid_repository_and_api_identity_are_rejected(self):
        for repository in ("../bad", "owner/repo?token=value", "owner/repo/more"):
            with self.subTest(repository=repository), self.assertRaises(collector.CollectionError):
                collector.collect(repository, self.output, FakeApi(), NOW)
        api = FakeApi([pull(26)])
        api.pulls[0]["number"] = "../../escape"
        with self.assertRaises(collector.CollectionError):
            self.collect(api)


class ArchiveTests(unittest.TestCase):
    def validate(self, data):
        return collector.validate_archive(data, REPOSITORY, SHA_A)

    def test_traversal_absolute_backslash_and_drive_paths_are_rejected_even_if_not_copied(self):
        for filename in ("../leak.txt", "/absolute.txt", "screenshots/../../leak.png", "screenshots\\escape.png", "C:/escape.png", "screenshots//escape.png", "./metadata.json"):
            with self.subTest(filename=filename), self.assertRaisesRegex(collector.CollectionError, "unsafe path"):
                self.validate(archive(extra={filename: "payload"}))

    def test_symlink_and_special_file_rejected(self):
        for mode in (stat.S_IFLNK | 0o777, stat.S_IFIFO | 0o600):
            info = zipfile.ZipInfo("ignored.txt")
            info.create_system = 3
            info.external_attr = mode << 16
            with self.subTest(mode=mode), self.assertRaisesRegex(collector.CollectionError, "special file"):
                self.validate(archive(extra={info: "target"}))

    def test_duplicate_path_is_rejected(self):
        stream = io.BytesIO(archive())
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            with zipfile.ZipFile(stream, "a") as zipped:
                zipped.writestr("gallery.json", "{}")
        with self.assertRaisesRegex(collector.CollectionError, "duplicate"):
            self.validate(stream.getvalue())

    def test_file_directory_collision_is_rejected(self):
        with self.assertRaisesRegex(collector.CollectionError, "duplicate"):
            self.validate(archive(extra={"screenshots/": "", "screenshots": "file"}))

    def test_archive_and_entry_count_limits(self):
        data = archive()
        with patch.object(collector, "MAX_ARCHIVE_BYTES", 10), self.assertRaisesRegex(collector.CollectionError, "archive exceeds"):
            self.validate(data)
        with patch.object(collector, "MAX_ZIP_ENTRIES", 2), self.assertRaisesRegex(collector.CollectionError, "too many"):
            self.validate(data)

    def test_expansion_limit_counts_ignored_files(self):
        data = archive(extra={"ignored.log": b"x" * 100000})
        with patch.object(collector, "MAX_ARCHIVE_BYTES", 10000), self.assertRaisesRegex(collector.CollectionError, "Expanded"):
            self.validate(data)

    def test_entry_size_and_metadata_size_limits(self):
        data = archive(extra={"ignored.log": b"x" * 2000})
        with patch.object(collector, "MAX_FILE_BYTES", 1000), self.assertRaisesRegex(collector.CollectionError, "entry exceeds"):
            self.validate(data)
        with patch.object(collector, "MAX_JSON_BYTES", 10), self.assertRaisesRegex(collector.CollectionError, "metadata exceeds"):
            self.validate(archive())

    def test_invalid_zip_or_json_aborts(self):
        with self.assertRaisesRegex(collector.CollectionError, "valid ZIP"):
            self.validate(b"not an archive")
        with self.assertRaisesRegex(collector.CollectionError, "Invalid JSON"):
            self.validate(archive(extra={"metadata.json": "{broken"}))

    def test_corrupt_zip_crc_aborts(self):
        data = bytearray(archive())
        stream = io.BytesIO(data)
        with zipfile.ZipFile(stream) as zipped:
            info = zipped.getinfo("screenshots/overview.png")
            offset = info.header_offset + 30 + len(info.filename.encode()) + len(info.extra)
        data[offset + 5] ^= 0x40
        with self.assertRaises(collector.CollectionError):
            self.validate(bytes(data))


class TransportTests(unittest.TestCase):
    def fake_process(self, script):
        launch = subprocess.Popen

        def execute(command, **options):
            self.assertEqual(command[:2], ["gh", "api"])
            self.assertIs(options["shell"], False)
            self.assertEqual(options["env"]["GH_PROMPT_DISABLED"], "1")
            return launch([sys.executable, "-c", script], **options)

        return execute

    def test_transport_returns_binary_and_uses_environment_token(self):
        with patch.dict(os.environ, {"GITHUB_TOKEN": "test-token"}, clear=True):
            execute = self.fake_process("import os,sys; assert os.environ['GH_TOKEN']=='test-token'; sys.stdout.buffer.write(bytes([0,255,3]))")
            with patch.object(collector.subprocess, "Popen", side_effect=execute):
                self.assertEqual(collector.GitHubApi()._request("repos/owner/repo/artifact", 100), bytes([0, 255, 3]))

    def test_transport_rejects_oversized_download(self):
        execute = self.fake_process("import sys; sys.stdout.buffer.write(b'x'*65536)")
        with patch.object(collector.subprocess, "Popen", side_effect=execute), self.assertRaisesRegex(collector.CollectionError, "size limit"):
            collector.GitHubApi()._request("repos/owner/repo/artifact", 100)

    def test_transport_http_error_does_not_expose_diagnostics(self):
        execute = self.fake_process("import sys; sys.stderr.write('gh: confidential-token body (HTTP 404)'); sys.exit(1)")
        with patch.object(collector.subprocess, "Popen", side_effect=execute), self.assertRaises(collector.ApiError) as error:
            collector.GitHubApi()._request("repos/owner/repo/artifact", 100)
        self.assertEqual(error.exception.status, 404)
        self.assertNotIn("confidential", str(error.exception))


if __name__ == "__main__":
    unittest.main()
