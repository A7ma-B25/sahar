// Her words to you follow your gender (plan 0.10c Ruling 8): the host sends Address.cs's whole-word pairs.
const LETTER = /\p{L}/u;

/** The text for whoever she talks to: for 'female', each male word of a pair becomes its female form (whole words only,
 *  longest first); 'male' and 'unknown' keep the text as it is. */
export function toYou(text, gender, pairs) {
  if (!text || String(gender).toLowerCase() !== 'female' || !pairs || !pairs.length) return text;
  const sorted = [...pairs].sort((a, b) => b[0].length - a[0].length);
  let out = '';
  let i = 0;
  outer: while (i < text.length) {
    if (i === 0 || !LETTER.test(text[i - 1])) {
      for (const [male, female] of sorted) {
        if (text.startsWith(male, i)) {
          const after = text[i + male.length];
          if (after === undefined || !LETTER.test(after)) {
            out += female;
            i += male.length;
            continue outer;
          }
        }
      }
    }
    out += text[i];
    i++;
  }
  return out;
}
