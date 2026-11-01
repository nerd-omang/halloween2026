using System.Collections.Generic;
using UnityEngine;

public enum CostumeSlot { Head, Neck, Hand, Back, Body, Eyes, LeftEye, RightEye }

// Halloween costumes worn just for fun by either visitor (witch hat, pumpkin basket...). Unlike
// VisitorDisguise they imitate nobody, so they never point to the answer; they are visual noise, and
// the head piece can hide real parts. Kept apart from the disguise mask: bit i = costumes[i].
// One costume per slot (the first listed wins a clash), no head piece while a disguise prop covers the
// head (VisitorDisguise.HeadCovered, e.g. the paint bucket), no headband with a headband prop on
// (VisitorDisguise.HeadbandWorn) and no sunglasses over the X glasses (VisitorDisguise.EyesCovered).
// The same rules live in ItemTable.Normalize. A palm blast can knock loose costumes
// off (DropOne); the ones marked `stays` (bandage) cannot come off.
public class VisitorCostume : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public GameObject item;
        public CostumeSlot slot;
        public bool stays;
        // A headband (maid, bunny): left off while the disguise wears a headband prop.
        public bool headband;
        // Hides a look clue (the rib vest covers Omangi's cross): only picked by Randomize for two or more costumes,
        // which the game asks for later in a run.
        public bool hard;
    }

    [SerializeField] Entry[] costumes;

    [Header("Knocked-off costumes")]
    [SerializeField] PropFlight flight = new PropFlight();

    int appliedMask;
    int droppedMask;

    public int CostumeCount => costumes.Length;
    public string CostumeName(int i) => costumes[i].item.name;
    public CostumeSlot Slot(int i) => costumes[i].slot;

    // Costumes that a palm blast can still knock off.
    public int LooseCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < costumes.Length; i++)
            {
                if (IsWorn(i) && !costumes[i].stays) count++;
            }
            return count;
        }
    }

    // The mask last given to Apply, after Normalize.
    public int AppliedMask => appliedMask;

    // What is visible right now: applied costumes not yet knocked off.
    public int WornMask
    {
        get
        {
            int mask = 0;
            for (int i = 0; i < costumes.Length; i++)
            {
                if (IsWorn(i)) mask |= 1 << i;
            }
            return mask;
        }
    }

    // One costume per slot (the first listed wins), and what the disguise has on leaves no room for.
    public int Normalize(int mask)
    {
        bool hasDisguise = TryGetComponent(out VisitorDisguise disguise);
        return Normalize(mask, hasDisguise && disguise.HeadCovered, hasDisguise && disguise.HeadbandWorn,
            hasDisguise && disguise.EyesCovered);
    }

    int Normalize(int mask, bool headCovered, bool headbandWorn, bool eyesCovered)
    {
        int taken = 0; // slots already used, one bit per ItemSlot
        for (int i = 0; i < costumes.Length; i++)
        {
            int bit = 1 << i;
            if ((mask & bit) == 0) continue;
            var slot = (ItemSlot)((int)costumes[i].slot + 1); // CostumeSlot is ItemSlot without None
            int slotBit = 1 << (int)slot;
            var entry = costumes[i];
            if (ItemTable.SlotTaken(slot, (uint)taken) || (headCovered && entry.slot == CostumeSlot.Head)
                || (headbandWorn && entry.headband) || (eyesCovered && ItemTable.IsEyeSlot(slot)))
            {
                mask &= ~bit;
                continue;
            }
            taken |= slotBit;
        }
        return mask;
    }

    // False for combinations Apply would change (with the disguise as it is worn now).
    public bool IsValidMask(int mask) => Normalize(mask) == mask;

    // The mask is normalized first. Costumes already knocked off stay off.
    public void Apply(int mask)
    {
        mask = Normalize(mask);
        appliedMask = mask;
        for (int i = 0; i < costumes.Length; i++)
        {
            costumes[i].item.SetActive((mask & (1 << i)) != 0 && (droppedMask & (1 << i)) == 0);
        }
    }

    // Costumes that go together (by model node name). Each pick after the first has SetChance to come from the
    // sets of what is already worn, so matching outfits show up a bit more often than by luck.
    static readonly string[][] Sets =
    {
        new[] { "Costume_MaidHeadband", "Costume_MaidDress" },
        new[] { "Costume_BunnyHeadband", "Costume_BunnySuit" },
        new[] { "Costume_WitchHat", "Costume_Broom", "Costume_PumpkinBasket" },
        new[] { "Costume_PirateHat", "Costume_PirateEyePatch", "Costume_Hook" },
    };
    const float SetChance = 0.4f;

    // True when costume i is in a set with something in `mask`.
    bool InSetWith(int i, int mask)
    {
        foreach (var set in Sets)
        {
            if (System.Array.IndexOf(set, costumes[i].item.name) < 0) continue;
            for (int j = 0; j < costumes.Length; j++)
            {
                if ((mask & (1 << j)) != 0 && j != i && System.Array.IndexOf(set, costumes[j].item.name) >= 0) return true;
            }
        }
        return false;
    }

    // Wears up to `count` randomly chosen costumes, keeping to the slot rules (fewer when none fit).
    public void Randomize(int count)
    {
        int mask = 0;
        var candidates = new List<int>(costumes.Length);
        var partners = new List<int>(costumes.Length);
        for (int picked = 0; picked < count; picked++)
        {
            candidates.Clear();
            partners.Clear();
            for (int i = 0; i < costumes.Length; i++)
            {
                int next = mask | (1 << i);
                if (costumes[i].hard && count < 2) continue;
                if (next == mask || Normalize(next) != next) continue;
                candidates.Add(1 << i);
                if (InSetWith(i, mask)) partners.Add(1 << i);
            }
            if (candidates.Count == 0) break;
            var pool = partners.Count > 0 && Random.value < SetChance ? partners : candidates;
            mask |= pool[Random.Range(0, pool.Count)];
        }
        Apply(mask);
    }

    // Knocks one randomly chosen loose costume off, flying like a knocked-off fake prop. Returns false
    // (and does nothing) when none is worn, or while a ghost sheet covers the visitor.
    public bool DropOne(Vector3 direction)
    {
        if (TryGetComponent(out GhostSheet sheet) && sheet.Covered) return false;
        int loose = 0;
        for (int i = 0; i < costumes.Length; i++)
        {
            if (IsWorn(i) && !costumes[i].stays) loose++;
        }
        if (loose == 0) return false;

        int pick = Random.Range(0, loose);
        for (int i = 0; i < costumes.Length; i++)
        {
            if (!IsWorn(i) || costumes[i].stays || pick-- != 0) continue;
            droppedMask |= 1 << i;
            flight.Launch(transform, costumes[i].item.transform, direction);
            return true;
        }
        return false;
    }

    bool IsWorn(int i) => costumes[i].item.activeSelf && (droppedMask & (1 << i)) == 0;

    void Update() => flight.Update(transform);

    void OnDestroy() => flight.DestroyAll();

    [ContextMenu("Random Costume")]
    void RandomizePreview()
    {
        Randomize(Random.Range(1, costumes.Length + 1));
    }

    // Play-mode testing from the component's context menu.
    [ContextMenu("Drop One Costume")]
    void DropPreview()
    {
        DropOne(Camera.main != null ? transform.position - Camera.main.transform.position : -transform.forward);
    }
}
