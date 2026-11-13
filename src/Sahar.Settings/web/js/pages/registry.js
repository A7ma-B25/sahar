// The nine pages (spec §6), grouped by spacing; two stay hidden until they work (plan Ruling 1).
export const pages = [
  { id: 'sahar', group: 1, icon: 'sparkle', available: true, title: 'pageSahar' },
  { id: 'menu', group: 1, icon: 'squares-four', available: true, title: 'pageMenu' },
  { id: 'sounds', group: 2, icon: 'speaker-high', available: false, title: 'pageSounds' },
  { id: 'prayer', group: 2, icon: 'moon-stars', available: true, title: 'pagePrayer' },
  { id: 'listen', group: 2, icon: 'headphones', available: true, title: 'pageListen' },
  { id: 'general', group: 3, icon: 'sliders-horizontal', available: true, title: 'pageGeneral' },
  { id: 'privacy', group: 3, icon: 'shield-check', available: true, title: 'pagePrivacy' },
  { id: 'claude', group: 3, icon: 'terminal-window', available: false, title: 'pageClaude' },
  { id: 'about', group: 4, icon: 'info', available: true, title: 'aboutTitle' },
];

/** The pages shown: available ones the host lists (caps.pages), in sidebar order. */
export function visiblePages(caps) {
  const listed = new Set((caps && caps.pages) || []);
  return pages.filter((p) => p.available && listed.has(p.id));
}
