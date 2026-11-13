// Recording a new shortcut (plan 0.10c Task 6): physical keys (KeyboardEvent.code), so an Arabic layout records the
// same "Alt+S" as an English one. Written the way Core's Shortcut parses it ("Ctrl+Alt+K").
const MODIFIER_CODES = /^(Control|Alt|Shift|Meta|OS)(Left|Right)?$/;

/** "Ctrl+Alt+K", "cancel" for Escape, or null (a modifier alone, or a key Core does not know). */
export function fromKeyEvent({ code, ctrlKey, altKey, shiftKey, metaKey }) {
  if (code === 'Escape') return 'cancel';
  if (!code || MODIFIER_CODES.test(code)) return null;
  let key = null;
  let m;
  if ((m = /^Key([A-Z])$/.exec(code))) key = m[1];
  else if ((m = /^Digit([0-9])$/.exec(code))) key = m[1];
  else if ((m = /^F([0-9]{1,2})$/.exec(code)) && +m[1] >= 1 && +m[1] <= 24) key = `F${m[1]}`;
  else if (code === 'Space') key = 'Space';
  if (!key) return null;
  const parts = [];
  if (ctrlKey) parts.push('Ctrl');
  if (altKey) parts.push('Alt');
  if (shiftKey) parts.push('Shift');
  if (metaKey) parts.push('Win');
  parts.push(key);
  return parts.join('+');
}
