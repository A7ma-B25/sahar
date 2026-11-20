// 0.14.1 fix 5: settings.log had 4 145 «mind-status» lines in minutes — every re-render of «النماذج» left one more 1 s
// timer behind (render awaited before it set its timer, so a second render's leave() ran first). One poller per page.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { startPoll, stopPoll, polling } from '../../src/Sahar.Settings/web/js/poll.js';

function clock() {
  const timers = new Map(); let next = 1;
  return {
    timers,
    setInterval: (fn, ms) => { const id = next++; timers.set(id, { fn, ms }); return id; },
    clearInterval: (id) => { timers.delete(id); },
    document: { visibilityState: 'visible' },
    tick() { return Promise.all([...timers.values()].map((t) => t.fn())); },
  };
}

test('poll: one timer per key however often the page re-renders', () => {
  const t = clock();
  for (let i = 0; i < 20; i++) startPoll('models', () => {}, 1000, t);
  assert.equal(t.timers.size, 1);
  assert.equal(polling(), 1);
  stopPoll('models', t);
  assert.equal(t.timers.size, 0);
  assert.equal(polling(), 0);
});

test('poll: at most every 1.5 s, nothing while hidden or minimised, never two at once', async () => {
  const t = clock();
  let calls = 0; let release;
  startPoll('models', () => { calls++; return new Promise((r) => { release = r; }); }, 200, t);
  assert.ok([...t.timers.values()][0].ms >= 1500);
  t.document.visibilityState = 'hidden';
  await t.tick();
  assert.equal(calls, 0);
  t.document.visibilityState = 'visible';
  t.tick();
  t.tick();   // the first answer has not come: no second request
  assert.equal(calls, 1);
  release();
  await new Promise((r) => setImmediate(r));
  await Promise.race([t.tick(), new Promise((r) => setImmediate(r))]);
  assert.equal(calls, 2);
  stopPoll('models', t);
});

test('models page: polls through poll.js only, and only if it is still the page', () => {
  const src = readFileSync(new URL('../../src/Sahar.Settings/web/js/pages/models.js', import.meta.url), 'utf8');
  assert.ok(!/setInterval\(/.test(src), 'a raw setInterval in models.js');
  assert.match(src, /startPoll\('models'/);
  assert.match(src, /if \(mine === gen\) startPoll/);
});
