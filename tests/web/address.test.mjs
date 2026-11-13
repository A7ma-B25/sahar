import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toYou } from '../../src/Sahar.Settings/web/js/address.js';

// A copy of the start of Address.cs's table (the host sends the whole table as caps pairs).
const pairs = [['ما وقّفها', 'ما توقّفيها'], ['نسخته', 'نسختيه'], ['اكتب', 'اكتبي'], ['امسح', 'امسحي'], ['اختار', 'اختاري'], ['اكتبلي', 'اكتبيلي'], ['تشغّل', 'تشغّلي']];

test('address: «اكتب اسمك» → «اكتبي اسمك» for female', () => assert.equal(toYou('اكتب اسمك', 'female', pairs), 'اكتبي اسمك'));
test('address: unchanged for male and unknown', () => {
  assert.equal(toYou('اكتب اسمك', 'male', pairs), 'اكتب اسمك');
  assert.equal(toYou('اكتب اسمك', 'unknown', pairs), 'اكتب اسمك');
});
test('address: only whole words, longest first', () => {
  assert.equal(toYou('اكتبلي شي', 'female', pairs), 'اكتبيلي شي');
  assert.equal(toYou('ماكتبوا', 'female', pairs), 'ماكتبوا');
  assert.equal(toYou('امسح يلي نسخته', 'female', pairs), 'امسحي يلي نسختيه');
  assert.equal(toYou('لما تشغّله', 'female', pairs), 'لما تشغّله');
});
test('address: gender names are case-insensitive', () => assert.equal(toYou('اكتب', 'Female', pairs), 'اكتبي'));
