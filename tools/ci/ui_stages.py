"""Which UI stages a change touches (ui-test `stages=auto`), and whether it touches her brain (mind-eval).

    python3 -I tools/ci/ui_stages.py stages --base origin/main    ->  stages=smoke,games
    python3 -I tools/ci/ui_stages.py brain  --base origin/main    ->  brain=true

The changed paths come from `git diff <base>...HEAD` (what this branch did since it left the base). Rules, first match:
- docs, notes, CI tooling, unit tests, art/font generators, the other workflows: no stage (smoke still runs);
- her brain (src/Sahar.Mind, Core/Engine Mind, Core assets/mind, tools/mind with its pins): the mind stage;
- a few parts with one owner each (games, prayer, the Quran, the installer, the look/mind test files);
- tests/ui/run.ps1: the stage whose `function Stage-X { ... }` holds each changed line; a line outside every stage
  (the shared helpers) runs every stage;
- anything else (the engine, the settings app and its surface host, Core, build files) and anything unknown: every
  stage. When in doubt it runs more, never less; a release always runs `all`.
The smoke stage always runs (a few seconds: she starts, answers and quits).
"""
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ui_groups import ALL  # noqa: E402

SMOKE = "smoke"
EVERY = "*"   # a rule's answer: every stage

# Her brain: a change here runs the mind stage, and mind-eval (the real model) when that is on auto.
BRAIN = (
    "src/Sahar.Mind/",
    "src/Sahar.Core/Mind/",
    "src/Sahar.Engine/Mind/",
    "src/Sahar.Core/assets/mind/",
    "tools/mind/",          # pins.json: the llama.cpp runtime, the CI model and the eval models
    "tests/mind/",          # the golden lines mind-eval types
    "tests/ui/mind-eval.ps1",
    "tools/ci/mind_eval_score.py",
    ".github/workflows/mind-eval.yml",
)

# (path prefix, stages). First match wins.
RULES = [
    # Nothing a UI stage can see.
    ("docs/", []),
    ("README.md", []),
    ("PRODUCT.md", []),
    (".gitignore", []),
    ("tests/Sahar.Core.Tests/", []),   # unit tests (build.yml, test-build's core job)
    ("tests/web/", []),                # the settings page's pure JS (node --test)
    ("tools/ci/", []),                 # CI helpers and their own tests
    ("tools/art/", []),                # generators: what they make is committed under src/
    ("tools/fonts/", []),
    ("tools/design/", []),
    (".github/workflows/build.yml", []),
    (".github/workflows/test-build.yml", []),
    (".github/workflows/ui-test.yml", []),   # the run that uses it is its own check
    ("packaging/README-test-build.txt", []),
    ("packaging/THIRD-PARTY.txt", []),
    # Mind-eval's own pieces (not a UI stage).
    ("tests/mind/", []),
    ("tests/ui/mind-eval.ps1", []),
    (".github/workflows/mind-eval.yml", []),
    # Her brain.
    ("src/Sahar.Mind/", ["mind"]),
    ("src/Sahar.Core/Mind/", ["mind"]),
    ("src/Sahar.Engine/Mind/", ["mind"]),
    ("src/Sahar.Core/assets/mind/", ["mind"]),
    ("tools/mind/", ["mind"]),
    ("tests/ui/mind-", ["mind"]),       # mind-runtime/-memory/-autonomy/-online/-tools.ps1
    ("tests/ui/fake_tools.py", ["mind"]),   # 0.19: the fake MCP server and media services
    ("tests/fakes/", ["mind"]),         # 0.19: the stdio MCP test program
    # Parts with one owner.
    ("src/Sahar.Core/Games/", ["games", "face"]),   # face starts a game in its burst
    ("src/Sahar.Engine/Games/", ["games", "face"]),
    ("src/Sahar.Core/Prayer/", ["prayer"]),
    ("src/Sahar.Core/Quran/", ["menu", "quranbar"]),
    ("packaging/installer/", ["install"]),
    ("tests/ui/fake_github.py", ["install"]),
    ("tests/ui/look.ps1", ["look"]),
    # tests/ui/run.ps1 is split by stage (see run_ps1_regions); everything else under src/ and the build files: all.
]

RUN_PS1 = "tests/ui/run.ps1"


def rule_for(path):
    for prefix, stages in RULES:
        if path.startswith(prefix):
            return stages
    return EVERY


def run_ps1_regions(text):
    """[(stage, first line, last line)] of every `function Stage-X {` in run.ps1 (1-based, inclusive; it ends at the
    first `}` in column 0)."""
    lines = text.split("\n")
    regions = []
    for i, line in enumerate(lines):
        m = re.match(r"function Stage-(\w+) \{", line)
        if not m:
            continue
        j = i + 1
        while j < len(lines) and lines[j].rstrip("\r") != "}":
            j += 1
        regions.append((m.group(1).lower(), i + 1, j + 1))
    return regions


def stages_for_run_ps1(changed_lines, regions):
    """The stages holding these lines of run.ps1; a line outside every stage function (or None: unknown) → every stage."""
    if changed_lines is None:
        return EVERY
    out = []
    for n in changed_lines:
        hit = [s for s, a, b in regions if a <= n <= b]
        if not hit:
            return EVERY
        out += hit
    return out


def select(changes, regions=()):
    """changes: {path: [changed line numbers in the new file] or None}. The stages to run, in ui_groups.ALL order,
    smoke first; every stage when any change needs it."""
    asked = {SMOKE}
    for path, lines in changes.items():
        stages = stages_for_run_ps1(lines, regions) if path == RUN_PS1 else rule_for(path)
        if stages == EVERY:
            return list(ALL)
        unknown = [s for s in stages if s not in ALL]
        if unknown:   # a Stage-X function the groups do not know: run everything rather than guess
            return list(ALL)
        asked.update(stages)
    return [SMOKE] + [s for s in ALL if s in asked and s != SMOKE]


def touches_brain(paths):
    return any(p.startswith(b) for p in paths for b in BRAIN)


# ---- git ----

def git(*args):
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout


def changed_paths(base):
    # Renames count on both sides (a file moved out of a stage's folder still touches it).
    out = git("diff", "--name-status", "--no-renames", f"{base}...HEAD")
    return [line.split("\t", 1)[1] for line in out.splitlines() if "\t" in line]


def changed_lines(base, path):
    """Line numbers of the new file a diff touches (a pure deletion counts the line it sits before)."""
    out = git("diff", "-U0", f"{base}...HEAD", "--", path)
    lines = []
    for m in re.finditer(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@", out, re.M):
        start, count = int(m.group(1)), int(m.group(2) or 1)
        lines += range(start, start + count) if count else [max(start, 1)]
    return lines


def changes_since(base):
    changes = {}
    for path in changed_paths(base):
        changes[path] = changed_lines(base, path) if path == RUN_PS1 and os.path.exists(path) else None
    return changes


def main(argv):
    if len(argv) < 3 or argv[1] not in ("stages", "brain") or argv[2] != "--base":
        raise SystemExit("usage: ui_stages.py stages|brain --base <ref>")
    base = argv[3]
    changes = changes_since(base)
    for path in sorted(changes):
        print(f"changed: {path}", file=sys.stderr)
    if argv[1] == "brain":
        print("brain=" + ("true" if touches_brain(changes) else "false"))
        return
    regions = ()
    if RUN_PS1 in changes and os.path.exists(RUN_PS1):
        with open(RUN_PS1, encoding="utf-8-sig") as f:
            regions = run_ps1_regions(f.read())
    print("stages=" + ",".join(select(changes, regions)))


if __name__ == "__main__":
    main(sys.argv)
