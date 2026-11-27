"""What already passed on this commit, so a workflow does not run it again (GitHub's API through `gh`, GITHUB_TOKEN).

    python -I tools/ci/prior_runs.py ui --repo OWNER/NAME --sha SHA [--wait-minutes 45]
        A "ui-test all" run on SHA whose build and UI jobs all passed (every UI stage; a mind-eval beside them does
        not count): prints run=<id>, or run= when there is none. While one is still running (or re-running) on SHA
        it waits for it, up to --wait-minutes, then decides.
    python -I tools/ci/prior_runs.py mindeval --repo OWNER/NAME --sha SHA
        A mind-eval that succeeded, or is still going, on SHA (a ui-test or test-build run's mind-eval job): prints
        run=<id> or run=.

With GITHUB_OUTPUT set, the line is also appended there.
"""
import json
import os
import subprocess
import sys
import time

FULL_TITLE = "ui-test all"   # ui-test.yml's run-name for stages=all (and every push to main)
ACTIVE = {"queued", "in_progress", "waiting", "requested", "pending"}


def ui_passed(jobs):
    """A full run's UI part passed: its build and every "ui (...)" job succeeded (a mind-eval beside them, still
    running or failed, is not a UI stage)."""
    ui = [j for j in jobs if j.get("name", "").startswith("ui (")]
    build = [j for j in jobs if j.get("name") == "build"]
    return bool(ui) and all(j.get("conclusion") == "success" for j in ui + build)


def decide_ui(runs, jobs_by_run):
    """runs: ui-test runs on one commit (the API's workflow_runs); jobs_by_run: {run id: its jobs} for the full ones.
    ('pass', id) when a full run's UI part passed, ('wait', id) when one is still going, else ('none', None)."""
    full = [r for r in runs if r.get("display_title") == FULL_TITLE]
    for r in full:
        if (r.get("status") == "completed" and r.get("conclusion") == "success") or ui_passed(jobs_by_run.get(r["id"], [])):
            return "pass", r["id"]
    for r in full:
        if r.get("status") in ACTIVE:
            return "wait", r["id"]
    return "none", None


def mindeval_passed(jobs_by_run):
    """{run id: [jobs]}: the first run whose mind-eval job (the reusable workflow's "mind-eval / eval") succeeded or is
    still going (a test-build started beside a ui-test that runs it does not run it twice)."""
    for run_id, jobs in jobs_by_run.items():
        for j in jobs:
            if j.get("name", "").startswith("mind-eval") and (
                    j.get("conclusion") == "success" or j.get("status") in ACTIVE):
                return run_id
    return None


def gh_json(path):
    out = subprocess.run(["gh", "api", path], check=True, capture_output=True, text=True).stdout
    return json.loads(out)


def jobs_of(repo, run_id):
    return gh_json(f"repos/{repo}/actions/runs/{run_id}/jobs?per_page=100")["jobs"]


def runs_on(repo, workflow, sha):
    return gh_json(f"repos/{repo}/actions/workflows/{workflow}/runs?head_sha={sha}&per_page=100")["workflow_runs"]


def emit(line):
    print(line)
    out = os.environ.get("GITHUB_OUTPUT")
    if out:
        with open(out, "a", encoding="utf-8") as f:
            f.write(line + "\n")


def ui(repo, sha, wait_minutes):
    deadline = time.monotonic() + wait_minutes * 60
    while True:
        runs = runs_on(repo, "ui-test.yml", sha)
        jobs = {r["id"]: jobs_of(repo, r["id"]) for r in runs if r.get("display_title") == FULL_TITLE}
        verdict, run = decide_ui(runs, jobs)
        if verdict == "pass":
            print(f"a full UI pass on {sha}: run {run}", file=sys.stderr)
            return emit(f"run={run}")
        if verdict == "wait" and time.monotonic() < deadline:
            print(f"ui-test run {run} on {sha} is still going: waiting for it", file=sys.stderr)
            time.sleep(60)
            continue
        print(f"no full UI pass on {sha}" + (" (waited long enough)" if verdict == "wait" else ""), file=sys.stderr)
        return emit("run=")


def mindeval(repo, sha):
    jobs = {}
    for workflow in ("ui-test.yml", "test-build.yml"):
        for r in runs_on(repo, workflow, sha):
            if r["id"] != int(os.environ.get("GITHUB_RUN_ID", "0")):   # never this run's own
                jobs[r["id"]] = jobs_of(repo, r["id"])
    run = mindeval_passed(jobs)
    print(f"mind-eval on {sha}: " + (f"passed or going in run {run}" if run else "none yet"), file=sys.stderr)
    emit(f"run={run or ''}")


def main(argv):
    args = dict(zip(argv[2::2], argv[3::2]))
    if len(argv) < 2 or argv[1] not in ("ui", "mindeval") or "--repo" not in args or "--sha" not in args:
        raise SystemExit(__doc__)
    if argv[1] == "ui":
        ui(args["--repo"], args["--sha"], float(args.get("--wait-minutes", "0")))
    else:
        mindeval(args["--repo"], args["--sha"])


if __name__ == "__main__":
    main(sys.argv)
