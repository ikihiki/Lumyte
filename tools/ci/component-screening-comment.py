"""Publish a link-only advisory comment using trusted GitHub metadata, not artifacts."""

import json
import os
from pathlib import Path
import re
import subprocess

MARKER = "<!-- lumyte-component-screening -->"
WORKFLOW_PATH = ".github/workflows/component-screening.yml"
SHA = re.compile(r"^[0-9a-f]{40}$")


def api(path, method="GET", payload=None):
    command = ["gh", "api", "--method", method, path]
    if payload is not None:
        command += ["--input", "-"]
    result = subprocess.run(command, input=json.dumps(payload) if payload else None,
                            text=True, capture_output=True, check=True, timeout=60)
    return json.loads(result.stdout)


def make_body(run, repository):
    conclusion = run.get("conclusion")
    status = {
        "success": "測定処理が完了しました。退行候補・未測定範囲は実行結果を確認してください。",
        "failure": "測定処理が失敗しました。退行なしとは判定できません。",
        "timed_out": "測定が時間上限に達しました。退行なしとは判定できません。",
        "cancelled": "測定は取り消されました。完了した比較結果ではありません。",
    }.get(conclusion, "測定は正常完了していません。実行結果を確認してください。")
    run_id = int(run["id"])
    number = int(run["run_number"])
    attempt = int(run.get("run_attempt", 1))
    sha = run["head_sha"]
    if not SHA.fullmatch(sha) or not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Invalid trusted run identity")
    return (
        f"{MARKER}\n<!-- run: {number} attempt: {attempt} -->\n"
        f"**部品性能スクリーニング（非ブロッキング）**\n\n{status}\n\n"
        f"対象: \x60{sha}\x60\n"
        f"[Job Summary・ログ・生データ](https://github.com/{repository}/actions/runs/{run_id})\n\n"
        "共有ランナーの短時間測定です。退行候補は確定診断ではありません。"
    )


def should_replace(body, run):
    match = re.search(r"<!-- run: (\d+) attempt: (\d+) -->", body)
    return not match or (int(match[1]), int(match[2])) <= (
        int(run["run_number"]), int(run.get("run_attempt", 1)))


def publish(event, repository, request=api):
    run = event["workflow_run"]
    if run.get("event") != "pull_request" or run.get("path") != WORKFLOW_PATH:
        return "ignored workflow"
    repo = request(f"repos/{repository}")
    if int(run["repository"]["id"]) != int(repo["id"]):
        raise ValueError("Run repository mismatch")
    if not SHA.fullmatch(run["head_sha"]):
        raise ValueError("Invalid head SHA")
    # GitHub may omit PR association (notably fork runs); do not guess a target.
    pulls = run.get("pull_requests", [])
    if len(pulls) != 1:
        return "missing or ambiguous PR association; summary remains in Actions"
    number = int(pulls[0]["number"])
    pr = request(f"repos/{repository}/pulls/{number}")
    if pr["head"]["sha"] != run["head_sha"]:
        return "obsolete head"
    # Closed/merged PRs are deliberately included if their final head still matches.
    body = make_body(run, repository)
    comments = []
    page = 1
    while True:
        batch = request(f"repos/{repository}/issues/{number}/comments?per_page=100&page={page}")
        comments.extend(batch)
        if len(batch) < 100:
            break
        page += 1
    owned = [item for item in comments
             if item.get("user", {}).get("login") == "github-actions[bot]"
             and item.get("body", "").startswith(MARKER)]
    # Recheck immediately before mutation; global workflow concurrency serializes publishers.
    if request(f"repos/{repository}/pulls/{number}")["head"]["sha"] != run["head_sha"]:
        return "obsolete head"
    if owned:
        item = max(owned, key=lambda value: int(value["id"]))
        if not should_replace(item["body"], run):
            return "newer result already published"
        request(f"repos/{repository}/issues/comments/{int(item['id'])}", "PATCH", {"body": body})
    else:
        request(f"repos/{repository}/issues/{number}/comments", "POST", {"body": body})
    return "published"


if __name__ == "__main__":
    print(publish(json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text()),
                  os.environ["GITHUB_REPOSITORY"]))
