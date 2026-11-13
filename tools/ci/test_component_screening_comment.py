"""Offline tests: notification permissions, attribution and stale-result guards."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("publisher", Path(__file__).with_name("component-screening-comment.py"))
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class PublisherTests(unittest.TestCase):
    def setUp(self):
        self.run = {"event": "pull_request", "path": publisher.WORKFLOW_PATH,
                    "repository": {"id": 42}, "pull_requests": [{"number": 7}],
                    "id": 123, "run_number": 10, "run_attempt": 1,
                    "head_sha": "a" * 40, "conclusion": "success"}
        self.calls = []
        self.comments = []
        self.heads = ["a" * 40, "a" * 40]

    def request(self, path, method="GET", payload=None):
        self.calls.append((path, method, payload))
        if method != "GET":
            return {"id": 90}
        if path == "repos/owner/repo":
            return {"id": 42}
        if path == "repos/owner/repo/pulls/7":
            return {"head": {"sha": self.heads.pop(0)}, "state": "closed", "merged": True}
        if "/comments?" in path:
            return self.comments
        raise AssertionError(path)

    def publish(self):
        return publisher.publish({"workflow_run": self.run}, "owner/repo", self.request)

    def writes(self):
        return [call for call in self.calls if call[1] != "GET"]

    def test_posts_for_matching_merged_head(self):
        self.assertEqual("published", self.publish())
        body = self.writes()[0][2]["body"]
        self.assertIn("actions/runs/123", body)
        self.assertIn("a" * 40, body)
        self.assertNotIn("退行なしと判定", body)

    def test_obsolete_head_never_posts(self):
        self.heads[0] = "b" * 40
        self.assertEqual("obsolete head", self.publish())
        self.assertFalse(self.writes())

    def test_head_moves_during_comment_lookup(self):
        self.heads[1] = "b" * 40
        self.assertEqual("obsolete head", self.publish())
        self.assertFalse(self.writes())

    def test_newer_same_sha_result_not_overwritten(self):
        self.comments = [{"id": 5, "user": {"login": "github-actions[bot]"},
                          "body": publisher.MARKER + "\n<!-- run: 11 attempt: 1 -->"}]
        self.assertEqual("newer result already published", self.publish())
        self.assertFalse(self.writes())

    def test_newer_attempt_not_overwritten(self):
        self.assertFalse(publisher.should_replace("<!-- run: 10 attempt: 2 -->", self.run))

    def test_updates_bot_comment_only(self):
        self.comments = [{"id": 5, "user": {"login": "human"}, "body": publisher.MARKER},
                         {"id": 6, "user": {"login": "github-actions[bot]"},
                          "body": publisher.MARKER + "\n<!-- run: 9 attempt: 1 -->"}]
        self.assertEqual("published", self.publish())
        self.assertEqual(("repos/owner/repo/issues/comments/6", "PATCH"), self.writes()[0][:2])

    def test_missing_pr_association_is_not_guessed(self):
        self.run["pull_requests"] = []
        self.assertIn("missing", self.publish())
        self.assertFalse(self.writes())

    def test_other_workflow_ignored(self):
        self.run["path"] = ".github/workflows/other.yml"
        self.assertEqual("ignored workflow", self.publish())
        self.assertFalse(self.calls)

    def test_repository_mismatch_rejected(self):
        self.run["repository"]["id"] = 43
        with self.assertRaises(ValueError):
            self.publish()
        self.assertFalse(self.writes())

    def test_failure_and_cancel_are_not_success(self):
        for conclusion in ("failure", "cancelled", "timed_out", "skipped", None):
            with self.subTest(conclusion=conclusion):
                run = copy.deepcopy(self.run)
                run["conclusion"] = conclusion
                self.assertNotIn("測定処理が完了しました", publisher.make_body(run, "owner/repo"))

    def test_invalid_sha_rejected(self):
        self.run["head_sha"] = "@everyone"
        with self.assertRaises(ValueError):
            publisher.make_body(self.run, "owner/repo")


if __name__ == "__main__":
    unittest.main()
