// The pages (spec §6; 0.14 adds her brain's four as their own group, mockups «الإصدار 14 — الذكاء»), grouped by
// spacing; two stay hidden until they work (plan Ruling 1). Group 5 is the foot (about).
export const pages = [
  { id: 'sahar', group: 1, icon: 'sparkle', available: true, title: 'pageSahar' },
  { id: 'menu', group: 1, icon: 'squares-four', available: true, title: 'pageMenu' },
  { id: 'brain', group: 2, icon: 'brain', available: true, title: 'pageBrain' },
  { id: 'models', group: 2, icon: 'cpu', available: true, title: 'pageModels' },
  { id: 'you', group: 2, icon: 'user-circle', available: true, title: 'pageYou' },
  { id: 'today', group: 2, icon: 'clock-counter-clockwise', available: true, title: 'pageToday' },
  { id: 'sounds', group: 3, icon: 'speaker-high', available: false, title: 'pageSounds' },
  { id: 'prayer', group: 3, icon: 'moon-stars', available: true, title: 'pagePrayer' },
  { id: 'listen', group: 3, icon: 'headphones', available: true, title: 'pageListen' },
  { id: 'general', group: 4, icon: 'sliders-horizontal', available: true, title: 'pageGeneral' },
  { id: 'privacy', group: 4, icon: 'shield-check', available: true, title: 'pagePrivacy' },
  { id: 'claude', group: 4, icon: 'terminal-window', available: false, title: 'pageClaude' },
  { id: 'about', group: 5, icon: 'info', available: true, title: 'aboutTitle' },
];

/** The pages shown: available ones the host lists (caps.pages), in sidebar order. */
export function visiblePages(caps) {
  const listed = new Set((caps && caps.pages) || []);
  return pages.filter((p) => p.available && listed.has(p.id));
}
