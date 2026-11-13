// Digits (0.10b Ruling 5): Arabic-Indic in Arabic, each in a fixed cell so times never jiggle.
const INDIC = '٠١٢٣٤٥٦٧٨٩';

export function toArabicIndic(s) {
  return String(s).replace(/[0-9]/g, (d) => INDIC[d.charCodeAt(0) - 48]);
}

const isDigit = (ch) => (ch >= '0' && ch <= '9') || INDIC.includes(ch);

/** Fills el with text nodes and one <span class="d"> per digit (never innerHTML). */
export function cells(el, text) {
  const doc = el.ownerDocument;
  const parts = [];
  let run = '';
  for (const ch of String(text)) {
    if (isDigit(ch)) {
      if (run) { parts.push(doc.createTextNode(run)); run = ''; }
      const d = doc.createElement('span');
      d.className = 'd';
      d.textContent = ch;
      parts.push(d);
    } else run += ch;
  }
  if (run) parts.push(doc.createTextNode(run));
  el.replaceChildren(...parts);
}
