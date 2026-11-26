"""Scores mind-eval.yml's mind-eval.jsonl (one row per golden line: what became visible, the tools her brain called,
whether the fast path took it) on five checks — visible, right tool, Syrian, short, on topic — and prints a Markdown
summary (for the job summary and the artifact). Test data only: golden prompts and her replies, never his.

    python -I tools/ci/mind_eval_score.py artifacts/ui/mind-eval.jsonl [--golden tests/mind/golden/eval.jsonl] [--json out.json] [--md out.md]

The Syrian check mirrors Core's DialectScore word lists (test_mind_eval_score keeps them equal).
"""
import json
import re
import sys

# Mirrors src/Sahar.Core/Mind/Bench/BenchSuite.cs DialectScore (normalised the same way below).
SYRIAN = ["شو", "هلق", "منيح", "منيحة", "كتير", "بدي", "هيك", "لسا", "كمان", "تمام", "شي", "فيك", "فيني", "عم", "رح", "ليش", "وين", "هون", "مو", "مش", "يعني", "يلا", "شوي", "هاد", "هاي", "بس", "خلص", "ماشي", "إلك", "الك", "معك", "حلو", "اشتقت"]
MSA = ["سوف", "لماذا", "ذلك", "هذا", "هذه", "الذي", "التي", "ليس", "لقد", "إنني", "ماذا", "لسؤالك", "أستطيع", "يمكنك", "يمكنني"]

# Her fixed lines when something went wrong (Core ToolTemplates): visible, but not a reply.
FALLBACKS = ["ما فهمت عليك منيح", "عم فكّر كتير", "ما عملت شي لسا", "عم تعلّم", "لسا عم اتعلم"]
# What the chat parser runs itself (no brain turn): a fast-path line counts as the right tool for these.
FAST = {"timer.start", "timer.cancel", "note.save", "hush.on", "hush.off", "sleep.hide", "game.start", "move.play"}
SHORT = 120

_DIACRITICS = re.compile("[ً-ٰٟـ]")


def normalize(text):
    t = _DIACRITICS.sub("", text or "")
    for a, b in (("أ", "ا"), ("إ", "ا"), ("آ", "ا"), ("ى", "ي"), ("ة", "ه")):
        t = t.replace(a, b)
    return t.lower()


def _bare(w):
    """A word without the punctuation or emoji around it (0.15.0: he wants emoji; «هلق🎶» is still «هلق»)."""
    w = w.strip("،.!؟?,«»:\"'()")
    while w and not (w[0].isalnum()):
        w = w[1:]
    while w and not (w[-1].isalnum()):
        w = w[:-1]
    return w


def words(text):
    return {_bare(w) for w in normalize(text).split()}


def syrian(text):
    t = (text or "").strip()
    if not t or len(t) > 220:
        return False
    if not any("؀" <= c <= "ۿ" for c in t):
        return False
    w = words(t)
    return any(normalize(s) in w for s in SYRIAN) and not any(normalize(m) in w for m in MSA)


def fallback(text):
    return any(f in (text or "") for f in FALLBACKS)


def her_line(row):
    """What she said: the header or her bubble (the list bubble only for a draft — otherwise it is a tool's list)."""
    seen, where = row.get("seen") or [], row.get("where") or []
    if seen and len(seen) == len(where):
        want = ("list",) if row.get("kind") == "draft" else ("header", "bubble")
        mine = [t for t, w in zip(seen, where) if w in want]
        if mine:
            return mine[-1]
    return row.get("visible") or ""


def score(row, golden=None):
    g = golden or {}
    visible = her_line(row).strip()
    expect = row.get("expectTool")
    tools = [c.split(" ")[0] for c in row.get("calls") or []]
    acted = [c for c in row.get("calls") or [] if not c.endswith(" refuse")]
    kind = row.get("kind")
    shown = bool(visible) and not fallback(visible)
    if kind == "draft":
        right = not tools and "list" in (row.get("where") or [])
    elif expect:
        right = expect in tools or (row.get("fastPath") and expect in FAST and bool(visible))
    else:
        right = not acted
    text = visible.replace("[list] ", "")
    on_topic = bool(right and expect) or any(normalize(k) in normalize(text) for k in g.get("topic", []))
    checks = {
        "visible": shown,
        "tool": bool(right),
        "syrian": shown and (syrian(text) if kind != "draft" else bool(text)),
        "short": shown and len(text) <= (400 if kind == "draft" else SHORT),
        "topic": bool(shown and on_topic),
    }
    return checks


def summarize(rows, golden_by_id):
    scored = []
    for r in rows:
        c = score(r, golden_by_id.get(r.get("id")))
        scored.append((r, c))
    names = ["visible", "tool", "syrian", "short", "topic"]
    totals = {n: sum(1 for _, c in scored if c[n]) for n in names}
    points = sum(totals.values())
    out = {"rows": len(scored), "totals": totals, "score": round(100 * points / (5 * len(scored)), 1) if scored else 0.0,
           "fallbacks": sum(1 for r, _ in scored if fallback(her_line(r))), "model": rows[0].get("model") if rows else None}
    return out, scored


def markdown(summary, scored):
    lines = [f"### mind-eval — {summary['model']}: {summary['score']} %", "",
             f"{summary['rows']} lines · visible {summary['totals']['visible']} · right tool {summary['totals']['tool']} · Syrian {summary['totals']['syrian']}"
             f" · short {summary['totals']['short']} · on topic {summary['totals']['topic']} · fallback lines {summary['fallbacks']}", "",
             "| id | prompt | visible reply | calls | ✓ |", "|---|---|---|---|---|"]
    for r, c in scored:
        ok = "".join(k[0].upper() if v else "·" for k, v in c.items())
        reply = (her_line(r) or "(nothing)").replace("|", "/").replace("\n", " ")
        calls = ", ".join(r.get("calls") or []) or ("fast path" if r.get("fastPath") else "")
        lines.append(f"| {r.get('id')} | {r.get('prompt')} | {reply} | {calls} | {ok} |")
    return "\n".join(lines) + "\n"


def main(argv):
    if not argv:
        raise SystemExit(__doc__)
    path = argv[0]
    golden_path = argv[argv.index("--golden") + 1] if "--golden" in argv else "tests/mind/golden/eval.jsonl"
    with open(path, encoding="utf-8-sig") as f:
        rows = [json.loads(l) for l in f if l.strip()]
    with open(golden_path, encoding="utf-8") as f:
        golden = {g["id"]: g for g in (json.loads(l) for l in f if l.strip())}
    summary, scored = summarize(rows, golden)
    if "--json" in argv:
        with open(argv[argv.index("--json") + 1], "w", encoding="utf-8") as f:
            json.dump(summary, f, ensure_ascii=False, indent=1)
    md = markdown(summary, scored)
    if "--md" in argv:
        with open(argv[argv.index("--md") + 1], "w", encoding="utf-8") as f:
            f.write(md)
    sys.stdout.reconfigure(encoding="utf-8")
    print(md)


if __name__ == "__main__":
    main(sys.argv[1:])
