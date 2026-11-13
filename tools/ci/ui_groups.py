"""Turns the ui-test `stages` input into the matrix of parallel Windows jobs (GitHub Actions output `groups=<json>`).

Groups are balanced from the stage times of run 42868496323 (seconds): motion 285 | smoke 2 + idle 10 + gallery 31
+ life 198 + face (new, ~30) | events 28 + menu 109 + prayer 112 | perf 92 + senses 75 + games 49 + shell (new, ~90)
| settings (0.12, new: the settings app and the engine applying settings live, its own job).
"""
import json
import sys

GROUPS = [
    ("motion", ["motion"]),
    ("life", ["smoke", "idle", "gallery", "life", "face"]),
    ("menu", ["events", "menu", "prayer"]),
    ("senses", ["perf", "senses", "games", "shell"]),
    ("settings", ["settings"]),
]
ALL = [s for _, stages in GROUPS for s in stages]
SMALL = 3  # this many stages or fewer run in one job: a second Windows job would cost more than it saves


def plan(arg):
    asked = [s.strip() for s in arg.split(",") if s.strip()]
    if not asked or asked == ["all"]:
        asked = ALL
    unknown = [s for s in asked if s not in ALL]
    if unknown:
        raise SystemExit(f"unknown stage(s): {', '.join(unknown)}")
    if len(asked) <= SMALL:
        return [{"name": "-".join(asked), "stages": ",".join(asked)}]
    groups = []
    for name, stages in GROUPS:
        mine = [s for s in stages if s in asked]
        if mine:
            groups.append({"name": name, "stages": ",".join(mine)})
    return groups


if __name__ == "__main__":
    print("groups=" + json.dumps(plan(sys.argv[1] if len(sys.argv) > 1 else "all"), separators=(",", ":")))
