// Decoder for the result text produced by Assets/Scripts/LookCode.cs (Encode/TryDecode).
// [version 4][score 29][has initials 1][initials 3 x 5][look count 2][looks 39 bits each][checksum 16][reserved 8],
// MSB first, zero padded to bytes, unpadded base64url. Three looks make exactly 192 bits = 32 characters.
// Each look is an extended Hamming (39,32) word. The checksum is CRC-16/CCITT-FALSE over the whole message with the
// checksum bits set to 0 and the looks as repaired by the Hamming code; it protects the header, which Hamming does not.
import { isPossible } from './items.js';

export const VERSION = 2;
export const SCORE_BITS = 29;
export const MAX_SCORE = 2 ** SCORE_BITS - 1;
export const MAX_LOOKS = 3;
const WORD_BITS = 39;
const CHECKSUM_BITS = 16;
const RESERVED_BITS = 8;
const HEADER_BITS = 4 + SCORE_BITS + 1 + 15 + 2;
const ITEM_SHIFT = 3;

const DATA_POSITIONS = [];
for (let p = 1; p < WORD_BITS; p++) if ((p & (p - 1)) !== 0) DATA_POSITIONS.push(p);

const parity = (v) => {
  let count = 0;
  for (; v !== 0n; v &= v - 1n) count ^= 1;
  return count;
};

// Port of LookCode.Protect (used by tests and tools to build words).
export function protect(data) {
  let word = 0n;
  DATA_POSITIONS.forEach((pos, i) => { if ((data >>> i) & 1) word |= 1n << BigInt(pos); });
  for (let p = 1; p < WORD_BITS; p <<= 1) {
    let par = 0;
    for (let pos = 1; pos < WORD_BITS; pos++) if ((pos & p) !== 0 && ((word >> BigInt(pos)) & 1n) !== 0n) par ^= 1;
    if (par) word |= 1n << BigInt(p);
  }
  if (parity(word >> 1n)) word |= 1n;
  return word;
}

// Port of LookCode.TryRecover: returns the 32 data bits (unsigned number) or null when two or more bits are wrong.
export function recover(word) {
  if (word >> BigInt(WORD_BITS) !== 0n) return null;
  let syndrome = 0;
  for (let pos = 1; pos < WORD_BITS; pos++) if (((word >> BigInt(pos)) & 1n) !== 0n) syndrome ^= pos;
  if (parity(word)) {
    if (syndrome >= WORD_BITS) return null;
    word ^= 1n << BigInt(syndrome); // syndrome 0 = the overall parity bit itself
  } else if (syndrome !== 0) {
    return null;
  }
  let data = 0;
  DATA_POSITIONS.forEach((pos, i) => { if (((word >> BigInt(pos)) & 1n) !== 0n) data |= 1 << i; });
  return data >>> 0;
}

// CRC-16/CCITT-FALSE: poly 0x1021, init 0xFFFF, no reflection, no final xor.
export function crc16(bytes) {
  let crc = 0xffff;
  for (const b of bytes) {
    crc ^= b << 8;
    for (let i = 0; i < 8; i++) crc = (crc & 0x8000) !== 0 ? ((crc << 1) ^ 0x1021) & 0xffff : (crc << 1) & 0xffff;
  }
  return crc;
}

const B64URL = /^[A-Za-z0-9_-]+$/;

function toBytes(text) {
  if (typeof text !== 'string' || text.length === 0 || text.length > 64 || !B64URL.test(text)) return null;
  if (text.length % 4 === 1) return null; // no byte count produces this length
  return Buffer.from(text, 'base64url');
}

class Reader {
  constructor(bytes) { this.bytes = bytes; this.pos = 0; }
  has(n) { return this.pos + n <= this.bytes.length * 8; }
  read(n) { // n <= 39
    let v = 0n;
    for (let i = 0; i < n; i++, this.pos++) {
      v = (v << 1n) | BigInt((this.bytes[this.pos >> 3] >> (7 - (this.pos & 7))) & 1);
    }
    return v;
  }
}

// MSB-first bit writer; fields are [value, bits] pairs.
function pack(fields) {
  const bits = [];
  for (const [value, n] of fields) for (let i = n - 1; i >= 0; i--) bits.push(Number((BigInt(value) >> BigInt(i)) & 1n));
  while (bits.length % 8) bits.push(0);
  const out = Buffer.alloc(bits.length / 8);
  bits.forEach((b, i) => { if (b) out[i >> 3] |= 0x80 >> (i & 7); });
  return out;
}

function message(score, hasInitials, letters, words, checksum) {
  return pack([
    [VERSION, 4], [score, SCORE_BITS], [hasInitials ? 1 : 0, 1],
    [letters[0], 5], [letters[1], 5], [letters[2], 5],
    [words.length, 2], ...words.map((w) => [w, WORD_BITS]),
    [checksum, CHECKSUM_BITS], [0, RESERVED_BITS],
  ]);
}

// Returns { ok: true, score, initials (string|null), looks: [{code, kind, fur, items}] } or { ok: false, error }.
// Stricter than the client decoder in one way only: the text must be canonical (exact length, zero padding bits),
// which is what Encode always produces.
export function decode(text) {
  const bytes = toBytes(text);
  if (!bytes) return { ok: false, error: 'not base64url' };
  const r = new Reader(bytes);
  if (!r.has(HEADER_BITS)) return { ok: false, error: 'too short' };
  if (Number(r.read(4)) !== VERSION) return { ok: false, error: 'unknown version' };
  const score = Number(r.read(SCORE_BITS));
  const hasInitials = r.read(1) === 1n;
  const letters = [Number(r.read(5)), Number(r.read(5)), Number(r.read(5))];
  if (hasInitials && letters.some((l) => l > 25)) return { ok: false, error: 'bad initials' };
  const count = Number(r.read(2));
  if (count > MAX_LOOKS || !r.has(count * WORD_BITS + CHECKSUM_BITS + RESERVED_BITS)) return { ok: false, error: 'bad look count' };
  const looks = [];
  const words = [];
  for (let i = 0; i < count; i++) {
    const code = recover(r.read(WORD_BITS));
    if (code === null) return { ok: false, error: `look ${i} damaged` };
    const kind = code & 1;
    const fur = (code >>> 1) & 3;
    const items = code >>> ITEM_SHIFT;
    if (!isPossible(kind, fur, items)) return { ok: false, error: `look ${i} impossible` };
    looks.push({ code, kind, fur, items });
    words.push(protect(code));
  }
  const checksum = Number(r.read(CHECKSUM_BITS));
  if (r.read(RESERVED_BITS) !== 0n) return { ok: false, error: 'reserved bits set' };
  const usedBits = HEADER_BITS + count * WORD_BITS + CHECKSUM_BITS + RESERVED_BITS;
  if (bytes.length !== Math.ceil(usedBits / 8)) return { ok: false, error: 'bad length' };
  const padBits = bytes.length * 8 - usedBits;
  if (padBits > 0 && (bytes[bytes.length - 1] & ((1 << padBits) - 1)) !== 0) return { ok: false, error: 'bad padding' };
  if (crc16(message(score, hasInitials, letters, words, 0)) !== checksum) return { ok: false, error: 'bad checksum' };
  return { ok: true, score, initials: hasInitials ? String.fromCharCode(...letters.map((l) => 65 + l)) : null, looks };
}

// Port of LookCode.Encode for already-valid input (codes are the 32-bit look codes).
export function encode(score, initials, codes) {
  const clamped = Math.min(Math.max(score, 0), MAX_SCORE);
  const has = !!initials;
  const letters = [0, 1, 2].map((i) => (has && i < initials.length ? initials.toUpperCase().charCodeAt(i) - 65 : 0));
  const words = codes.map(protect);
  const checksum = crc16(message(clamped, has, letters, words, 0));
  return message(clamped, has, letters, words, checksum).toString('base64url');
}
