// Search folding (spec §4.3) — must agree with Sahar.Core.Text.ArabicFold (tests/web/surfaces-fold.test.mjs).
const MAP = { 'أ': 'ا', 'إ': 'ا', 'آ': 'ا', 'ٱ': 'ا', 'ؤ': 'و', 'ئ': 'ي', 'ة': 'ه', 'ى': 'ي' };

const skip = (c) => {
  const n = c.charCodeAt(0);
  return (n >= 0x064B && n <= 0x065F) || n === 0x0670 || n === 0x0640 || (n >= 0x06D6 && n <= 0x06ED && n !== 0x06E5 && n !== 0x06E6);
};

export function fold(text) {
  let out = '';
  let space = false;
  for (let c of String(text ?? '')) {
    if (skip(c)) continue;
    const n = c.charCodeAt(0);
    if (MAP[c]) c = MAP[c];
    else if (n >= 0x0660 && n <= 0x0669) c = String.fromCharCode(48 + n - 0x0660);
    else if (n >= 0x06F0 && n <= 0x06F9) c = String.fromCharCode(48 + n - 0x06F0);
    else c = c.toLowerCase();
    if (/\s/.test(c)) { space = out.length > 0; continue; }
    if (space) { out += ' '; space = false; }
    out += c;
  }
  return out;
}

export function matches(query, name, number) {
  const q = fold(query);
  if (!q) return true;
  if (/^[0-9]+$/.test(q)) return number !== undefined && number !== null && String(number) === q;
  const n = fold(name);
  if (n.startsWith(q)) return true;
  return n.split(' ').some((w) => w.startsWith(q) || (w.startsWith('ال') && w.slice(2).startsWith(q)));
}
