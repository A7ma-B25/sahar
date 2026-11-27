// 0.17 spec §8: her words as a tiny, safe markdown subset → h() trees (mount() makes text nodes and a few plain
// elements; never innerHTML). Kept: paragraphs, **bold**, *italic*, `code`, ``` fences, - / 1. lists, # headings (a bold
// line). Never: links (shown as «text (url)» in plain text, never clickable or opened), images (their alt text only),
// HTML (it stays text), attributes of any kind.
import { h } from './h.js';

const BULLET = /^\s*[-*•]\s+(.*)$/;
const NUMBER = /^\s*[0-9٠-٩]+[.)]\s+(.*)$/;
const HEADING = /^\s{0,3}#{1,6}\s+(.*)$/;
const FENCE = /^\s*```/;

/** Inline: `code` first (nothing inside it is parsed), then **bold**, then *italic*; links and images become text. */
function inline(s) {
  const out = [];
  const plain = (t) => t.replace(/!\[([^\]]*)\]\(([^)\s]*)\)/g, '$1').replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, '$1 ($2)');
  const parts = s.split(/(`[^`\n]+`)/);
  for (const part of parts) {
    if (part.length > 2 && part.startsWith('`') && part.endsWith('`')) { out.push(h('code', null, part.slice(1, -1))); continue; }
    for (const b of plain(part).split(/(\*\*[^*\n]+\*\*)/)) {
      if (b.length > 4 && b.startsWith('**') && b.endsWith('**')) { out.push(h('strong', null, b.slice(2, -2))); continue; }
      for (const i of b.split(/((?<![*\w])\*[^*\n]+\*(?![*\w]))/)) {
        if (i.length > 2 && i.startsWith('*') && i.endsWith('*')) out.push(h('em', null, i.slice(1, -1)));
        else if (i) out.push(i);
      }
    }
  }
  return out;
}

/** The blocks of a message. */
export function md(src) {
  const lines = String(src ?? '').replace(/\r\n?/g, '\n').split('\n');
  const blocks = [];
  let para = [];
  let list = null;
  const flushPara = () => {
    if (para.length) blocks.push(h('p', null, para.flatMap((l, i) => (i ? ['\n', ...inline(l)] : inline(l)))));
    para = [];
  };
  const flushList = () => { if (list) blocks.push(h(list.tag, null, list.items.map((it) => h('li', null, inline(it))))); list = null; };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (FENCE.test(line)) {
      flushPara(); flushList();
      const code = [];
      for (i++; i < lines.length && !FENCE.test(lines[i]); i++) code.push(lines[i]);
      blocks.push(h('pre', null, h('code', null, code.join('\n'))));
      continue;
    }
    if (!line.trim()) { flushPara(); flushList(); continue; }
    const heading = HEADING.exec(line);
    if (heading) { flushPara(); flushList(); blocks.push(h('p', { cls: 'mdh' }, inline(heading[1]))); continue; }
    const bullet = BULLET.exec(line);
    const number = bullet ? null : NUMBER.exec(line);
    if (bullet || number) {
      flushPara();
      const tag = bullet ? 'ul' : 'ol';
      if (list && list.tag !== tag) flushList();
      list ??= { tag, items: [] };
      list.items.push((bullet || number)[1]);
      continue;
    }
    flushList();
    para.push(line);
  }
  flushPara();
  flushList();
  return blocks;
}
