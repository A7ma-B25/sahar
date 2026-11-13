import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as M from '../../src/Sahar.Settings/web/js/sahar-motion.js';

// The 0.10a SoulMath table (SoulTests.cs): the page moves her with the engine's own numbers.
const near = (a, b, tol = 1e-3) => assert.ok(Math.abs(a - b) <= tol, `${a} ≉ ${b}`);

test('motion: float', () => { near(M.floatY(0), 0); near(M.floatY(1.05), -6); near(M.floatY(3.15), 6); });
test('motion: follow-through and breath', () => { near(M.scaleY(0), 1.012); near(M.scaleY(1.05), 1.01159); near(M.scaleY(2.1), 0.982); });
test('motion: sway and leaf lag', () => { near(M.sway(2.1), 1.6); near(M.leaf(0), -4.70, 5e-3); });
test('motion: the hop', () => {
  const eq = (p, rise, sy, tilt) => { near(p.rise, rise); near(p.scaleY, sy); near(p.tilt, tilt); };
  eq(M.hop(0.045, 1), 0, 0.92, 0);
  eq(M.hop(0.29, 1), -38, 1.14, 3);
  eq(M.hop(0.49, 1), 0, 0.80, 0);
  eq(M.hop(0.29, 0.6), -22.8, 1.084, 3);
  eq(M.hop(1.3, 1), 0, 1, 0);
});
test('motion: the leaf kick', () => near(M.leafKick(0.1309, 1), -5.9238));
test('motion: the shadow', () => { near(M.shadowRx(38), 43); near(M.shadowOpacity(38), 0.084); });
test('motion: look beyond 400 DIP is zero', () => {
  const l = M.look({ x: 1000, y: 0 }, { x: 0, y: 0 }, 100, 401);
  near(l.x, 0); near(l.y, 0); near(l.lean, 0);
  const n = M.look({ x: 100, y: 50 }, { x: 0, y: 0 }, 100, 200);
  near(n.x, 3.2); near(n.y, 1.2); near(n.lean, 1);
});
test('motion: constants', () => { assert.equal(M.FLOAT_PERIOD, 4.2); assert.equal(M.HOP_SECONDS, 1.2); assert.equal(M.HAPPY_SECONDS, 1.1); assert.equal(M.MOON_LAP_SECONDS, 16); });
