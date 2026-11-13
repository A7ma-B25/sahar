// Her soul's maths (spec §4), a line-for-line port of Sahar.Core.Motion.SoulMath (0.10a): the page's preview moves with
// the engine's numbers (tests/web/sahar-motion.test.mjs checks the same table as SoulTests.cs). Distances in rig units.
export const FLOAT_PERIOD = 4.2, BREATH_PERIOD = 3.6, MOON_LAP_SECONDS = 16;
export const FLOAT_UNITS = 6, FOLLOW_AMP = 0.012, BREATH_AMP = 0.012, SWAY_DEG = 1.6, LEAF_DEG = 6, LEAF_LAG = 0.9;
export const HOP_ANTICIPATION = 0.09, HOP_AIR = 0.40, HOP_SECONDS = 1.2, HOP_PEAK_UNITS = 38, HOP_SQUASH = 0.16,
  HOP_STRETCH = 0.14, HOP_TILT_DEG = 3, LAND_AMP = 0.2, LAND_DECAY = 7, LAND_FREQ = 16;
export const KICK_DEG = -10, KICK_DECAY = 4, KICK_FREQ = 12, HOP_COOLDOWN = 0.75, HAPPY_SECONDS = 1.1;
export const LOOK_UNITS = { x: 3.2, y: 2.4 };
export const LEAN_DEG = 1.8, LEAF_LEAN_DEG = 2.4, LOOK_RADIUS_DIP = 400, LOOK_EASE_SECONDS = 0.4;
export const BLINK_SECONDS = 0.12, BLINK_MIN_GAP = 2.6, BLINK_MAX_GAP = 6, BLINK_CLOSED_SCALE = 0.14;
export const IDLE_MIN_GAP = 6, IDLE_MAX_GAP = 12, IDLE_HOP_POWER = 0.6, IDLE_LOOK_SECONDS = 1.2;

const phi = (t) => (2 * Math.PI * t) / FLOAT_PERIOD;

export const floatY = (t) => -FLOAT_UNITS * Math.sin(phi(t));
export const scaleY = (t) => 1 + FOLLOW_AMP * Math.cos(phi(t)) + BREATH_AMP * Math.sin((2 * Math.PI * t) / BREATH_PERIOD);
export const sway = (t) => SWAY_DEG * Math.sin(phi(t) / 2);
export const leaf = (t) => LEAF_DEG * Math.sin(phi(t) - LEAF_LAG);

/** The hop at j seconds: {rise (negative = up), scaleY, tilt}. */
export function hop(j, power) {
  const ant = 0.09, air = 0.40, end = 1.2;
  if (j < 0 || j >= end) return { rise: 0, scaleY: 1, tilt: 0 };
  if (j < ant) return { rise: 0, scaleY: 1 - HOP_SQUASH * power * (j / ant), tilt: 0 };
  if (j < ant + air) {
    const k = (j - ant) / air;
    return { rise: -HOP_PEAK_UNITS * power * 4 * k * (1 - k), scaleY: 1 + HOP_STRETCH * power * (1 - Math.abs(2 * k - 1)), tilt: HOP_TILT_DEG * Math.sin(Math.PI * k) };
  }
  const l = j - ant - air;
  return { rise: 0, scaleY: 1 - LAND_AMP * power * Math.exp(-LAND_DECAY * l) * Math.cos(LAND_FREQ * l), tilt: 0 };
}

export const leafKick = (j, power) => (j < 0 || j >= 1.2 ? 0 : KICK_DEG * Math.exp(-KICK_DECAY * j) * Math.sin(KICK_FREQ * j) * power);
export const shadowRx = (h) => Math.max(0, 62 - 0.5 * h);
export const shadowOpacity = (h) => Math.max(0, 0.16 - 0.002 * h);

/** Where her pupils look (rig units) and how far she leans (−1…1); nothing beyond 400 DIP. */
export function look(cursor, eyes, halfCanvasPx, distanceDip) {
  if (distanceDip > LOOK_RADIUS_DIP || halfCanvasPx <= 0) return { x: 0, y: 0, lean: 0 };
  const clamp = (v) => Math.max(-1, Math.min(1, v));
  const nx = clamp((cursor.x - eyes.x) / halfCanvasPx), ny = clamp((cursor.y - eyes.y) / halfCanvasPx);
  return { x: nx * LOOK_UNITS.x, y: ny * LOOK_UNITS.y, lean: nx };
}
