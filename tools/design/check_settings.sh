#!/bin/sh
# The settings page's design checks (plan 0.10c Task 8): the impeccable detector on the page's source and on the
# rendered snapshots the UI stage keeps (settings-html/, from `gh run download -R A7ma-B25/sahar -n ui-settings -D <dir>`).
# Usage: tools/design/check_settings.sh [<snapshots dir>]   (IMPECCABLE overrides the detector's path)
set -eu
IMPECCABLE="${IMPECCABLE:-$(ls -d /root/.claude/skills/synced/*/impeccable/scripts/impeccable 2>/dev/null | head -1)}"
[ -n "$IMPECCABLE" ] || { echo "impeccable detector not found (set IMPECCABLE)"; exit 2; }
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
fail=0
check() {
  out="$(sh "$IMPECCABLE" detect --json "$1")"
  echo "== $1"
  echo "$out"
  # Every finding fails the check (spec §8: 0 findings).
  if [ "$(printf '%s' "$out" | tr -d ' \n')" != "[]" ]; then fail=1; fi
}
check "$ROOT/src/Sahar.Settings/web"
if [ $# -ge 1 ]; then
  # The snapshots link css/*.css relatively: check them beside the page's own stylesheets.
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  cp "$1"/*.html "$tmp"/
  cp -r "$ROOT/src/Sahar.Settings/web/css" "$tmp"/css
  check "$tmp"
fi
exit $fail
