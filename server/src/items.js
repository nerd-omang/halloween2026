// Mirror of Assets/Scripts/ItemTable.cs (bits are permanent). test/lookcode.test.js compares this table with
// the one the C# generator wrote into test/vectors.json, so a client-side change that is not copied here fails a test.
export const KUN = 1; // VisitorKind.Kunmungchi = 0 -> kind mask bit 0
export const OMA = 2; // VisitorKind.Omangi = 1 -> kind mask bit 1
const BOTH = KUN | OMA;

export const FUR_BIT = 0;
export const SHEET_BIT = 12;
export const PUMPKIN_BIT = 21;

// category: 0 Fur, 1 Prop, 2 Costume, 3 Sheet. slot: 0 None, 1 Head, 2 Neck, 3 Hand, 4 Back, 5 Body, 6 Eyes, 7 LeftEye, 8 RightEye.
const def = (bit, id, category, slot, kinds, extra = {}) => ({
  bit, id, category, slot, kinds, bringsFur: false, coversHead: false, coversEyes: false, headband: false, headPriority: -1,
  ...extra,
});

export const ITEMS = [
  def(0, 'fur', 0, 0, BOTH),
  def(1, 'horn_band', 1, 0, KUN, { headPriority: 1 }),
  def(2, 'goat_ears', 1, 0, KUN, { headPriority: 2 }),
  def(3, 'cone_nose', 1, 0, OMA),
  def(4, 'raccoon_tail', 1, 0, OMA),
  def(5, 'x_glasses', 1, 0, BOTH, { coversEyes: true }),
  def(6, 'paint_bucket', 1, 0, BOTH, { bringsFur: true, coversHead: true, headPriority: 0 }),
  def(7, 'witch_hat', 2, 1, BOTH),
  def(8, 'bow_tie', 2, 2, BOTH),
  def(9, 'pumpkin_basket', 2, 3, BOTH),
  def(10, 'bat_wings', 2, 4, BOTH),
  def(11, 'bandage', 2, 5, BOTH),
  def(12, 'sheet', 3, 0, BOTH),
  def(13, 'maid_headband', 2, 1, BOTH, { headband: true }),
  def(14, 'bunny_headband', 2, 1, BOTH, { headband: true }),
  def(15, 'star_sunglasses', 2, 6, BOTH),
  def(16, 'alien_sunglasses', 2, 6, BOTH),
  def(17, 'pirate_eye_patch', 2, 7, BOTH),
  def(18, 'broom', 2, 3, BOTH),
  def(19, 'lollipop', 2, 3, BOTH),
  def(20, 'rib_vest', 2, 5, BOTH),
  def(21, 'pumpkin_helmet', 3, 0, BOTH),
  def(22, 'medical_eye_patch', 2, 8, BOTH),
  def(23, 'gold_bikini', 2, 5, BOTH),
  def(24, 'bunny_suit', 2, 5, BOTH),
  def(25, 'maid_dress', 2, 5, BOTH),
];

export const KNOWN_MASK = ITEMS.reduce((m, d) => (m | (1 << d.bit)) >>> 0, 0);

const has = (items, bit) => (items & (1 << bit)) !== 0;

// ItemTable.Normalize. Items are a uint32 held as a JS number; all bit work is done with >>> 0.
export function normalize(kind, items) {
  items >>>= 0;
  if (has(items, SHEET_BIT)) items = (items & ~(1 << PUMPKIN_BIT)) >>> 0; // one cover at a time
  const kindBit = 1 << kind;
  let headProp = null;
  for (const d of ITEMS) {
    if (!has(items, d.bit)) continue;
    if ((d.kinds & kindBit) === 0) { items = (items & ~(1 << d.bit)) >>> 0; continue; }
    if (d.headPriority >= 0 && (headProp === null || d.headPriority < headProp.headPriority)) headProp = d;
  }
  for (const d of ITEMS) {
    if (!has(items, d.bit)) continue;
    if (d.headPriority >= 0 && d !== headProp) items = (items & ~(1 << d.bit)) >>> 0;
  }
  let coversHead = false;
  let coversEyes = false;
  for (const d of ITEMS) {
    if (!has(items, d.bit)) continue;
    if (d.bringsFur) items = (items | (1 << FUR_BIT)) >>> 0;
    if (d.coversHead) coversHead = true;
    if (d.coversEyes) coversEyes = true;
  }
  const headbandProp = headProp !== null && !headProp.coversHead;
  let usedSlots = 0;
  for (const d of ITEMS) {
    if (d.category !== 2 || !has(items, d.bit)) continue;
    const slot = 1 << d.slot;
    if (slotTaken(d.slot, usedSlots) || (coversHead && d.slot === 1) || (headbandProp && d.headband)
      || (coversEyes && d.slot >= 6)) { items = (items & ~(1 << d.bit)) >>> 0; continue; }
    usedSlots |= slot;
  }
  return items;
}

// Sunglasses (6) need both eyes free; the left (7) and right (8) eye patches fit together, not with sunglasses.
function slotTaken(slot, used) {
  if (slot === 6) return (used & (1 << 6 | 1 << 7 | 1 << 8)) !== 0;
  if (slot === 7) return (used & (1 << 7 | 1 << 6)) !== 0;
  if (slot === 8) return (used & (1 << 8 | 1 << 6)) !== 0;
  return (used & (1 << slot)) !== 0;
}

export const isValid = (kind, items) => normalize(kind, items) === items >>> 0;

// LookCode.IsPossible: known bits only, a fur level only with the fur change, and the combination rules.
export function isPossible(kind, furLevel, items) {
  items >>>= 0;
  if ((items & ~KNOWN_MASK) >>> 0 !== 0) return false;
  if (!has(items, FUR_BIT) && furLevel !== 0) return false;
  return isValid(kind, items);
}
