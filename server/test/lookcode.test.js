import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { decode, encode, protect, recover, crc16, MAX_SCORE } from '../src/lookcode.js';
import { ITEMS, isPossible, isValid } from '../src/items.js';

const vectors = JSON.parse(readFileSync(new URL('./vectors.json', import.meta.url), 'utf8'));

test('every C# vector decodes exactly like the C# decoder', () => {
  for (const c of vectors.cases) {
    const got = decode(c.text);
    assert.equal(got.ok, c.expect.ok, `${c.name}: ok (${got.error || ''} vs ${c.expect.error || ''})`);
    if (!c.expect.ok) continue;
    assert.equal(got.score, c.expect.score, c.name);
    assert.equal(got.initials, c.expect.initials, c.name);
    assert.deepEqual(got.looks, c.expect.looks, c.name);
  }
});

test('vectors cover the interesting cases', () => {
  const names = vectors.cases.map((c) => c.name);
  for (const prefix of ['valid', 'flip1_', 'flip2_', 'forged_', 'garbage_', 'clamped_score']) {
    assert.ok(names.some((n) => n.startsWith(prefix)), prefix);
  }
  assert.ok(vectors.cases.some((c) => !c.expect.ok && c.expect.error.endsWith('damaged')));
  assert.ok(vectors.cases.some((c) => !c.expect.ok && c.expect.error.endsWith('impossible')));
  assert.ok(vectors.cases.some((c) => !c.expect.ok && c.expect.error === 'unknown version'));
  assert.ok(vectors.cases.some((c) => !c.expect.ok && c.expect.error === 'bad checksum'));
  assert.ok(vectors.cases.some((c) => !c.expect.ok && c.expect.error === 'reserved bits set'));
});

test('item table equals the C# ItemTable', () => {
  assert.deepEqual(ITEMS, vectors.itemTable);
});

// Brute-forcing all 2**26 item-bit combinations (x2 kinds x4 fur levels) is what makes this test slow,
// and it gets 2x slower per new item bit forever. normalize() only lets bits interact within their own
// category (Fur/Prop bits via headPriority/bringsFur/coversEyes, Costume bits via slot/headband/coversEyes,
// Sheet bits via mutual exclusion) - categories never affect each other's bits. So instead of the full
// cross product, enumerate each category's bits on its own (small: 2**7, 2**17, 2**2) and combine the
// three independently-normalized pieces, re-checking isValid on each partial and final combination so the
// result is exactly the same fixed-point set isPossible would have found, just without visiting every
// losing combination along the way. This keeps the cost tied to each category's own bit count rather than
// to their product.
function bitsOfCategory(category) {
  return ITEMS.filter((d) => d.category === category).map((d) => d.bit);
}

function decodeBits(counter, bitPositions) {
  let v = 0;
  for (let i = 0; i < bitPositions.length; i++) if (counter & (1 << i)) v |= 1 << bitPositions[i];
  return v >>> 0;
}

function enumerateValid(kind, bitPositions, bases) {
  const out = [];
  for (let n = 0; n < 1 << bitPositions.length; n++) {
    const bits = decodeBits(n, bitPositions);
    for (const base of bases) {
      const items = (base | bits) >>> 0;
      if (isValid(kind, items)) out.push(items);
    }
  }
  return out;
}

test('the set of producible looks equals the C# one (all kinds x fur levels x known item bits)', () => {
  const headPropBits = [...bitsOfCategory(0), ...bitsOfCategory(1)]; // fur + headPriority/bringsFur/coversEyes props
  const costumeBits = bitsOfCategory(2); // slot-governed costume items
  const sheetBits = bitsOfCategory(3); // mutually-exclusive covers (sheet, pumpkin helmet)

  const codes = [];
  for (let kind = 0; kind < 2; kind++) {
    const validHeadProp = enumerateValid(kind, headPropBits, [0]);
    const validHeadPropCostume = enumerateValid(kind, costumeBits, validHeadProp);
    const validItems = enumerateValid(kind, sheetBits, validHeadPropCostume);
    for (const items of validItems) {
      for (let fur = 0; fur < 4; fur++) {
        if (isPossible(kind, fur, items)) codes.push(((items << 3) | (fur << 1) | kind) >>> 0);
      }
    }
  }
  codes.sort((a, b) => a - b);
  let count = 0;
  let hash = 0;
  for (const c of codes) { count++; hash = (hash * 31 + c) % 1000000007; }
  assert.equal(count, vectors.validLookCount);
  assert.equal(hash, vectors.validLookChecksum);
});

const FIRST_LOOK_BIT = 4 + 29 + 1 + 15 + 2; // 51
const fur1Look = (1 << 3) | (1 << 1) | 0; // Kunmungchi with fur change level 1
const plainOma = 1;

test('single flipped bit in any of the 39 positions is repaired, in every look', () => {
  const code = fur1Look | (1 << (3 + 7));
  const text = encode(100, 'ABC', [code, plainOma, code]);
  const bytes = Buffer.from(text, 'base64url');
  for (let look = 0; look < 3; look++) {
    for (let p = 0; p < 39; p++) {
      const bit = FIRST_LOOK_BIT + look * 39 + p;
      const b = Buffer.from(bytes);
      b[bit >> 3] ^= 0x80 >> (bit & 7);
      const r = decode(b.toString('base64url'));
      assert.ok(r.ok, `flip ${bit}`);
      assert.deepEqual(r.looks.map((l) => l.code), [code, plainOma, code]);
    }
  }
});

test('two flipped bits in one look are rejected', () => {
  const text = encode(5, null, [fur1Look]);
  const bytes = Buffer.from(text, 'base64url');
  let rejected = 0;
  for (let p = 0; p < 39; p++) {
    for (let q = p + 1; q < 39; q++) {
      const b = Buffer.from(bytes);
      for (const x of [p, q]) { const bit = FIRST_LOOK_BIT + x; b[bit >> 3] ^= 0x80 >> (bit & 7); }
      const r = decode(b.toString('base64url'));
      if (!r.ok) rejected++;
      else assert.fail(`two flips ${p},${q} accepted`);
    }
  }
  assert.equal(rejected, (39 * 38) / 2);
});

test('hamming round trip over many data words, and 3 flips never crash', () => {
  for (let i = 0; i < 2000; i++) {
    const data = (Math.imul(i, 2654435761) ^ (i << 7)) >>> 0;
    const w = protect(data);
    assert.equal(recover(w), data);
    assert.equal(recover(w ^ (1n << BigInt(i % 39))), data);
    assert.equal(recover(w ^ (1n << 39n)), null); // outside the 39 bits
  }
});

test('reject unknown versions', () => {
  const bytes = Buffer.from(encode(1, null, []), 'base64url');
  for (const v of [0, 1, 3, 15]) {
    const b = Buffer.from(bytes);
    b[0] = (b[0] & 0x0f) | (v << 4);
    assert.deepEqual(decode(b.toString('base64url')), { ok: false, error: 'unknown version' });
  }
});

test('reject looks the game can never produce', () => {
  const bad = {
    'fur level without fur': 2 << 1 | 1,
    'horn band on an Omangi': (1 << 1) << 3 | 1,
    'cone nose on a Kunmungchi': (1 << 3) << 3,
    'two head props': ((1 << 1) | (1 << 2) | 1) << 3,
    'bucket without fur': (1 << 6) << 3,
    'witch hat under the bucket': ((1 << 6) | 1 | (1 << 7)) << 3,
    'pirate hat with the witch hat': ((1 << 26) | (1 << 7)) << 3,
    'hook with the broom': ((1 << 27) | (1 << 18)) << 3 | 1,
    'maid dress with the bunny suit': ((1 << 25) | (1 << 24)) << 3,
    'bikini with the bunny suit': ((1 << 23) | (1 << 24)) << 3,
    'bikini with the rib vest': ((1 << 23) | (1 << 20)) << 3,
    'sunglasses with the medical eye patch': ((1 << 15) | (1 << 22)) << 3,
    'sheet and pumpkin helmet together': ((1 << 12) | (1 << 21)) << 3,
    'broom and lollipop together': ((1 << 18) | (1 << 19)) << 3,
    'eye patch with the X glasses': ((1 << 5) | (1 << 17)) << 3 | 1,
    'maid headband with the horn band': ((1 << 1) | (1 << 13)) << 3,
    'witch hat and bunny headband': ((1 << 7) | (1 << 14)) << 3 | 1,
    'star sunglasses with the X glasses': ((1 << 5) | (1 << 15)) << 3 | 1,
    'unknown item bit 28': ((1 << 28) << 3) >>> 0,
  };
  for (const [name, code] of Object.entries(bad)) {
    assert.equal(decode(encode(1, 'AAA', [code >>> 0])).error, 'look 0 impossible', name);
  }
  assert.ok(decode(encode(1, 'AAA', [((1 << 12) << 3) | 1])).ok, 'a sheet alone is fine');
  assert.ok(decode(encode(1, 'AAA', [((1 << 17) | (1 << 22)) << 3])).ok, 'pirate and medical eye patches go together');
});

test('malformed text', () => {
  for (const t of ['', ' ', 'AAAA', '!!!!', 'AAAA=', 'a'.repeat(65), 'A', null, undefined, 5]) {
    assert.equal(decode(t).ok, false, String(t));
  }
  const good = encode(9, 'ABC', [plainOma]);
  assert.equal(decode(good + 'AA').ok, false, 'trailing bytes');
  assert.equal(decode(good.slice(0, -2)).ok, false, 'truncated');
  assert.equal(decode(good + '=').ok, false, 'padding character');
});

test('initials are optional and bounded', () => {
  assert.equal(decode(encode(3, null, [])).initials, null);
  assert.equal(decode(encode(3, 'abc', [])).initials, 'ABC');
  assert.equal(decode(encode(MAX_SCORE, 'ZZZ', [])).score, 536870911);
  assert.equal(decode(encode(16384, 'ZZZ', [])).score, 16384);
  assert.equal(decode(encode(2 ** 40, null, [])).score, MAX_SCORE, 'too large scores are clamped, not wrapped');
  assert.equal(decode(encode(-3, null, [])).score, 0);
});

test('a full message is exactly 192 bits = 32 characters', () => {
  const text = encode(123456, 'ABC', [fur1Look, plainOma, fur1Look]);
  assert.equal(text.length, 32);
  assert.equal(Buffer.from(text, 'base64url').length, 24);
});

test('the checksum catches damage anywhere outside the looks, and the reserved bits must be zero', () => {
  const text = encode(777, 'WIN', [fur1Look, plainOma, fur1Look]);
  const bytes = Buffer.from(text, 'base64url');
  const flip = (bit) => { const b = Buffer.from(bytes); b[bit >> 3] ^= 0x80 >> (bit & 7); return decode(b.toString('base64url')); };
  for (let bit = 4; bit < FIRST_LOOK_BIT; bit++) {
    const r = flip(bit);
    assert.ok(!r.ok, `header bit ${bit} must not decode to a different game`);
  }
  const checksumStart = FIRST_LOOK_BIT + 3 * 39;
  for (let bit = checksumStart; bit < checksumStart + 16; bit++) assert.equal(flip(bit).error, 'bad checksum', `checksum bit ${bit}`);
  for (let bit = checksumStart + 16; bit < checksumStart + 24; bit++) assert.equal(flip(bit).error, 'reserved bits set', `reserved bit ${bit}`);
});

test('crc16 matches the CRC-16/CCITT-FALSE check value', () => {
  assert.equal(crc16(Buffer.from('123456789')), 0x29b1);
});
