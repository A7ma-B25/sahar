import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = fileURLToPath(new URL('../../src/Sahar.Settings/web', import.meta.url));
function* files(dir) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) yield* files(p);
    else yield p;
  }
}

test('no-network: no page file names an address outside https://sahar.settings, and no @import url()', () => {
  let n = 0;
  for (const p of files(web)) {
    if (!['.html', '.css', '.js', '.svg', '.mjs'].includes(extname(p))) continue;
    n++;
    const text = readFileSync(p, 'utf8');
    const urls = text.match(/https?:\/\/[^\s"'`)<>]+/g) ?? [];
    for (const u of urls) {
      // SVG namespaces are names, not requests.
      if (u.startsWith('http://www.w3.org/')) continue;
      assert.ok(u.startsWith('https://sahar.settings'), `${p}: ${u}`);
    }
    assert.doesNotMatch(text, /@import\s+url\(/, p);
  }
  assert.ok(n >= 5, `only ${n} files checked`);
});
