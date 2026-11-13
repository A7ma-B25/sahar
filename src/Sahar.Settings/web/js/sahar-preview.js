// The living Sahar in the page (plan 0.10c Task 5; spec §6): the rig SVG from design.py, moved by the engine's maths
// (sahar-motion.js) with requestAnimationFrame — only while the page is visible and motion is allowed.
import * as M from './sahar-motion.js';

const NS = 'http://www.w3.org/2000/svg';
let svgText = null;

async function loadSvg() {
  if (svgText === null) svgText = await (await fetch('sahar/sahar.svg')).text();
  return svgText;
}

const rand = (a, b) => a + Math.random() * (b - a);

/** Mounts her into el. Returns { hop(power), say(line), setSize(px), setGender(g), destroy(), hops() }. */
export async function mount(el, { size = 160, caption = null, reduced = () => false } = {}) {
  const doc = new DOMParser().parseFromString(await loadSvg(), 'image/svg+xml');
  const svg = document.importNode(doc.documentElement, true);
  svg.setAttribute('role', 'img');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('width', String(size));
  svg.setAttribute('height', String(size));
  svg.style.overflow = 'visible';
  el.replaceChildren(svg);
  const $ = (id) => svg.querySelector(`#${id}`);
  const floatG = $('float'), squash = $('squash'), leafG = $('leaf'), eyes = $('eyes'), eyesJoy = $('eyes-joy');
  const mouth = $('mouth'), mouthJoy = $('mouth-joy'), blush = $('blush'), shadow = $('shadow'), moon = $('moon');
  const moonBack = $('moon-back'), moonFront = $('moon-front');
  const stars = [...svg.querySelectorAll('.star')], istars = [...svg.querySelectorAll('.istar')], idots = [...svg.querySelectorAll('.idot')];

  const start = performance.now();
  let raf = 0, hopAt = -1, hopPower = 1, lastHop = -10, joyUntil = 0, hops = 0;
  let lookTarget = { x: 0, y: 0, lean: 0 }, lookNow = { x: 0, y: 0, lean: 0 }, asideUntil = 0;
  let blinkAt = rand(M.BLINK_MIN_GAP, M.BLINK_MAX_GAP), idleAt = rand(M.IDLE_MIN_GAP, M.IDLE_MAX_GAP);
  let alive = true;

  const now = () => (performance.now() - start) / 1000;

  function face(joy) {
    eyes.setAttribute('display', joy ? 'none' : 'inline');
    eyesJoy.setAttribute('display', joy ? 'inline' : 'none');
    mouth.setAttribute('display', joy ? 'none' : 'inline');
    mouthJoy.setAttribute('display', joy ? 'inline' : 'none');
    blush.setAttribute('opacity', joy ? '0.85' : '0.55');
  }

  // Look: the pointer over the preview box, eased; nothing beyond 400 DIP (spec §4).
  const box = el.closest('.preview') || el;
  function onMove(e) {
    const r = svg.getBoundingClientRect();
    const eyesPx = { x: r.left + r.width * (120 / 300), y: r.top + r.height * (154 / 300) };
    const d = Math.hypot(e.clientX - eyesPx.x, e.clientY - eyesPx.y);
    lookTarget = M.look({ x: e.clientX, y: e.clientY }, eyesPx, r.width / 2, d);
  }
  function onLeave() { lookTarget = { x: 0, y: 0, lean: 0 }; }
  box.addEventListener('pointermove', onMove);
  box.addEventListener('pointerleave', onLeave);

  function pose(t) {
    const still = reduced();
    const j = hopAt >= 0 ? t - hopAt : -1;
    const h = still ? { rise: 0, scaleY: 1, tilt: 0 } : M.hop(j, hopPower);
    const fy = still ? 0 : M.floatY(t);
    const sy = still ? 1 : M.scaleY(t) * h.scaleY;
    const lean = lookNow.lean * M.LEAN_DEG;
    const rot = (still ? 0 : M.sway(t)) + h.tilt + lean;
    floatG.setAttribute('transform', `translate(0 ${(fy + h.rise).toFixed(3)})`);
    squash.setAttribute('transform', `translate(120 212) rotate(${rot.toFixed(3)}) scale(${(1 / sy).toFixed(4)} ${sy.toFixed(4)}) translate(-120 -212)`);
    const leafDeg = (still ? 0 : M.leaf(t) + M.leafKick(j, hopPower)) - M.LEAF_LEAN_DEG * lookNow.lean;
    leafG.setAttribute('transform', `rotate(${leafDeg.toFixed(3)} 122 60)`);
    const height = Math.max(0, -(fy + h.rise));
    shadow.setAttribute('rx', M.shadowRx(height).toFixed(2));
    shadow.setAttribute('opacity', M.shadowOpacity(height).toFixed(3));
    // Blink: 120 ms, every 2.6–6 s.
    let ey = 1;
    if (!still && t >= blinkAt) {
      const k = (t - blinkAt) / M.BLINK_SECONDS;
      if (k >= 1) blinkAt = t + rand(M.BLINK_MIN_GAP, M.BLINK_MAX_GAP);
      else ey = 1 - (1 - M.BLINK_CLOSED_SCALE) * Math.sin(Math.PI * k);
    }
    eyes.setAttribute('transform', `translate(${lookNow.x.toFixed(3)} ${(124 + lookNow.y).toFixed(3)}) scale(1 ${ey.toFixed(3)}) translate(0 -124)`);
    // The moon: 16 s a lap on the dashed ellipse (rx 112, ry 26, −10°), behind her on the far half.
    const a = still ? Math.PI / 2 : (2 * Math.PI * t) / M.MOON_LAP_SECONDS;
    const ox = 112 * Math.cos(a), oy = 26 * Math.sin(a), c = Math.cos(-Math.PI / 18), s = Math.sin(-Math.PI / 18);
    moon.setAttribute('transform', `translate(${(150 + ox * c - oy * s).toFixed(2)} ${(150 + ox * s + oy * c).toFixed(2)})`);
    const front = Math.sin(a) > 0;
    if (front && moon.parentNode !== moonFront) moonFront.append(moon);
    else if (!front && moon.parentNode !== moonBack) moonBack.append(moon);
    // Twinkles (outer stars 0.3–1, inner stars 0.15–1 and 0.6–1.1, dots 0.2–0.85).
    const tw = (p, b, lo, hi) => (still ? hi : lo + (hi - lo) * (0.5 - 0.5 * Math.cos((2 * Math.PI * (t - b)) / p)));
    for (const st of stars) st.setAttribute('opacity', tw(+st.dataset.period, +st.dataset.begin, 0.3, 1).toFixed(3));
    for (const st of istars) {
      const p = +st.dataset.period, b = +st.dataset.begin;
      st.setAttribute('opacity', tw(p, b, 0.15, 1).toFixed(3));
      const k = +st.dataset.scale * tw(p, b, 0.6, 1.1);
      st.setAttribute('transform', `translate(${st.dataset.x} ${st.dataset.y}) scale(${k.toFixed(4)})`);
    }
    for (const d of idots) d.setAttribute('opacity', tw(+d.dataset.period, +d.dataset.begin, 0.2, 0.85).toFixed(3));
    face(t < joyUntil);
  }

  function frame() {
    raf = 0;
    if (!alive) return;
    const t = now();
    // Idle: every 6–12 s a small hop or a glance aside (not under reduced motion).
    if (!reduced() && t >= idleAt) {
      idleAt = t + rand(M.IDLE_MIN_GAP, M.IDLE_MAX_GAP);
      const r = Math.random();
      if (r < 0.5) hop(M.IDLE_HOP_POWER, true);
      else { asideUntil = t + M.IDLE_LOOK_SECONDS; lookTarget = { x: (r < 0.75 ? -1 : 1) * M.LOOK_UNITS.x, y: 0, lean: 0 }; }
    }
    if (asideUntil && t > asideUntil) { asideUntil = 0; lookTarget = { x: 0, y: 0, lean: 0 }; }
    const ease = reduced() ? 1 : 0.12;
    lookNow = { x: lookNow.x + (lookTarget.x - lookNow.x) * ease, y: lookNow.y + (lookTarget.y - lookNow.y) * ease, lean: lookNow.lean + (lookTarget.lean - lookNow.lean) * ease };
    if (hopAt >= 0 && t - hopAt >= M.HOP_SECONDS) hopAt = -1;
    pose(t);
    schedule();
  }

  function schedule() {
    if (!alive || raf || document.hidden) return;
    if (reduced()) { pose(now()); if (now() < joyUntil) setTimeout(() => { if (alive) pose(now()); }, (joyUntil - now()) * 1000 + 20); return; }
    raf = requestAnimationFrame(frame);
  }
  const onVisible = () => schedule();
  document.addEventListener('visibilitychange', onVisible);

  /** A hop with her joy face (any change on the page); reduced motion: the face alone. */
  function hop(power = 1, idle = false) {
    const t = now();
    if (!idle) hops++;
    joyUntil = t + M.HAPPY_SECONDS;
    if (!reduced() && t - lastHop >= M.HOP_COOLDOWN) { hopAt = t; hopPower = power; lastHop = t; }
    if (!idle) svg.dataset.hops = String(hops);
    schedule();
    if (reduced()) pose(t);
  }

  pose(0);
  schedule();
  return {
    hop,
    say(line) { if (caption) caption.textContent = line; },
    setSize(px) { svg.setAttribute('width', String(px)); svg.setAttribute('height', String(px)); },
    setGender() { /* her look does not change with yours; the caption's words do (the page's toYou) */ },
    hops: () => hops,
    destroy() {
      alive = false;
      if (raf) cancelAnimationFrame(raf);
      document.removeEventListener('visibilitychange', onVisible);
      box.removeEventListener('pointermove', onMove);
      box.removeEventListener('pointerleave', onLeave);
    },
  };
}
