using System.Collections.Generic;
using UnityEngine;

// Costume options for one visitor: changing the fur (dye or paint) and wearing prop parts.
// Swaps between shared material assets (no material instances) and toggles prop children,
// so any combination of the visitor's disguise elements can be put together at runtime.
// A palm blast can knock worn props off one at a time (DropOneFakeProp); a dropped prop is gone
// for good. Never changes who the visitor is (Visitor.kind).
public class VisitorDisguise : MonoBehaviour
{
    [SerializeField] Material naturalFur;
    // Fur change levels from sloppiest (patchy, partial dye) to a full dye job.
    [SerializeField] Material[] dyeLevels;
    // All of these are fake (look-alike) props; real parts are never listed here.
    [SerializeField] GameObject[] props;
    // Props sharing the head: at most one of them is worn. Listed by priority, the first one wins a clash.
    [SerializeField] GameObject[] headProps;
    // Props that always come with the fur change (the paint bucket: the paint was poured from it).
    [SerializeField] GameObject[] furProps;

    // Props that cover the head (the paint bucket): a costume head piece (VisitorCostume) is not worn with them.
    [SerializeField] GameObject[] headCoverProps;

    [Header("Knocked-off props")]
    [SerializeField] PropFlight flight = new PropFlight();

    Renderer[] renderers;
    int droppedMask;

    // Disguise elements: the fur change plus each prop.
    public int ElementCount => 1 + props.Length;

    int appliedMask;
    int appliedDye;

    public int PropCount => props.Length;
    public int DyeLevelCount => dyeLevels.Length;
    public int DyeLevel => appliedDye;
    public string PropName(int i) => props[i].name;

    // What is visible right now in Apply's mask format: the fur change plus the props not yet knocked off.
    public int WornMask
    {
        get
        {
            int mask = appliedMask & 1;
            for (int i = 0; i < props.Length; i++)
            {
                if (IsWorn(i)) mask |= 1 << (i + 1);
            }
            return mask;
        }
    }

    // Fake props currently worn (dropped ones no longer count).
    public int FakePropCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < props.Length; i++)
            {
                if (IsWorn(i)) count++;
            }
            return count;
        }
    }

    // Wears `count` randomly chosen elements (clamped to 1..ElementCount). skill (0..1) picks how
    // thorough the fur change is when it is one of them: low skill dyes a few patches, high skill all of it.
    // Only valid combinations come out (see Normalize); a prop that brings the fur change along counts it
    // as one of the elements, and fewer than `count` are worn when the head slot rules leave no more.
    public void Randomize(int count, float skill)
    {
        int n = ElementCount;
        count = Mathf.Clamp(count, 1, n);
        int mask = 0;
        var candidates = new List<int>(n);
        while (CountBits(mask) < count)
        {
            candidates.Clear();
            for (int i = 0; i < n; i++)
            {
                int bit = 1 << i;
                int next = mask | bit;
                // Skip a bit that Normalize would drop again (a second head prop).
                if ((mask & bit) == 0 && (Normalize(next) & next) == next) candidates.Add(bit);
            }
            if (candidates.Count == 0) break;
            mask = Normalize(mask | candidates[Random.Range(0, candidates.Count)]);
        }
        float expected = Mathf.Clamp01(skill) * (dyeLevels.Length - 1);
        int level = Mathf.Clamp(Mathf.RoundToInt(expected + Random.Range(-0.6f, 0.6f)), 0, dyeLevels.Length - 1);
        Apply(mask, level);
    }

    // Combination rules for a mask in Apply's format: only one head prop (the first listed in headProps
    // wins), and a fur prop turns the fur change on.
    public int Normalize(int mask)
    {
        int head = mask & HeadMask;
        if (head != 0) mask = (mask & ~HeadMask) | FirstHeadBit(head);
        if ((mask & FurPropMask) != 0) mask |= 1;
        return mask;
    }

    // False for combinations Apply would change (two head props, or a fur prop without the fur change).
    public bool IsValidMask(int mask) => Normalize(mask) == mask;

    int headMask = -1;
    int furPropMask = -1;
    int HeadMask => headMask >= 0 ? headMask : headMask = PropMask(headProps);
    int FurPropMask => furPropMask >= 0 ? furPropMask : furPropMask = PropMask(furProps);

    int PropMask(GameObject[] list)
    {
        int mask = 0;
        if (list == null) return mask;
        foreach (var p in list)
        {
            int i = System.Array.IndexOf(props, p);
            if (i >= 0) mask |= 1 << (i + 1);
        }
        return mask;
    }

    int FirstHeadBit(int head)
    {
        foreach (var p in headProps)
        {
            int bit = 1 << (System.Array.IndexOf(props, p) + 1);
            if ((head & bit) != 0) return bit;
        }
        return 0;
    }

    static int CountBits(int mask)
    {
        int count = 0;
        for (; mask != 0; mask &= mask - 1) count++;
        return count;
    }

    // Bit 0 = fur change at dyeLevels[dyeLevel], bit i (i >= 1) = props[i - 1]. 0 removes the disguise.
    // The mask is normalized first (see Normalize). Props already knocked off stay off.
    public void Apply(int mask, int dyeLevel)
    {
        mask = Normalize(mask);
        appliedMask = mask;
        appliedDye = dyeLevel;
        SetFur((mask & 1) != 0 ? dyeLevels[dyeLevel] : naturalFur);
        for (int i = 0; i < props.Length; i++)
        {
            props[i].SetActive((mask & (1 << (i + 1))) != 0 && (droppedMask & (1 << i)) == 0);
        }
        // A costume head piece gives way to a head-covering prop, whichever was applied first.
        if (TryGetComponent(out VisitorCostume costume)) costume.Apply(costume.AppliedMask);
    }

    // Knocks one randomly chosen worn fake prop off, flying along `direction` (camera -> visitor) and
    // up, tumbling, bouncing once and vanishing. Returns false (and does nothing) when no fake prop is
    // worn, or while a ghost sheet covers the visitor.
    public bool DropOneFakeProp(Vector3 direction)
    {
        if (TryGetComponent(out GhostSheet sheet) && sheet.Covered) return false;
        int worn = FakePropCount;
        if (worn == 0) return false;

        int pick = Random.Range(0, worn);
        int index = -1;
        for (int i = 0; i < props.Length; i++)
        {
            if (IsWorn(i) && pick-- == 0)
            {
                index = i;
                break;
            }
        }
        droppedMask |= 1 << index;

        flight.Launch(transform, props[index].transform, direction);
        return true;
    }

    bool IsWorn(int i) => props[i].activeSelf && (droppedMask & (1 << i)) == 0;

    // Whether the mask (Apply's format) has a head-covering prop on.
    public bool CoversHead(int mask)
    {
        if (headCoverProps == null) return false;
        foreach (var p in headCoverProps)
        {
            int i = System.Array.IndexOf(props, p);
            if (i >= 0 && (mask & (1 << (i + 1))) != 0) return true;
        }
        return false;
    }

    // True while a worn prop covers the head (VisitorCostume leaves its head piece off then).
    public bool HeadCovered
    {
        get
        {
            if (headCoverProps == null) return false;
            foreach (var p in headCoverProps)
            {
                int i = System.Array.IndexOf(props, p);
                if (i >= 0 && IsWorn(i)) return true;
            }
            return false;
        }
    }

    void Update() => flight.Update(transform);

    void OnDestroy() => flight.DestroyAll();

    [ContextMenu("Random Disguise")]
    void RandomizePreview()
    {
        Randomize(Random.Range(1, ElementCount + 1), Random.value);
    }

    // Play-mode testing from the component's context menu.
    [ContextMenu("Drop One Fake Prop")]
    void DropPreview()
    {
        DropOneFakeProp(Camera.main != null ? transform.position - Camera.main.transform.position : -transform.forward);
    }

    void SetFur(Material fur)
    {
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && (mats[i] == naturalFur || System.Array.IndexOf(dyeLevels, mats[i]) >= 0))
                {
                    mats[i] = fur;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}
