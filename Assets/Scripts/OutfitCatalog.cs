using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Knows the visitor prefabs and what they can wear (ItemTable). Builds display models from an Outfit,
// names looks, and remembers which items the player has met (the gallery).
// A gallery entry is one item per visitor, not a combination. Entries of a kind, in order: 0 = natural
// look, 1 = fur change, then its props and costumes in table order, and the ghost sheet last. Unlocks are
// saved by item bit, so adding items to the table never scrambles old saves.
public class OutfitCatalog : MonoBehaviour
{
    [SerializeField] Visitor kunmungchiPrefab;
    [SerializeField] Visitor omangiPrefab;

    const string SaveKey = "gallery_v3";
    const int NaturalBit = 31;
    static readonly int[] DefaultDyePercent = { 15, 30, 60, 90 };

    VisitorDisguise[] disguises;
    // Per kind: entry -> item (null = natural look).
    readonly List<ItemDef>[] entries = new List<ItemDef>[2];
    readonly List<ItemDef>[] props = new List<ItemDef>[2];
    readonly List<ItemDef>[] costumes = new List<ItemDef>[2];
    readonly uint[] unlocked = new uint[2];

    public int EntryCount { get; private set; }
    public int DyeLevelCount => disguises[0].DyeLevelCount;
    public int MaxPropCount { get; private set; }
    public int MaxCostumeCount { get; private set; }

    void OnEnable() => Progress.Cleared += ForgetAll;
    void OnDisable() => Progress.Cleared -= ForgetAll;

    void Awake()
    {
        disguises = new[]
        {
            kunmungchiPrefab.GetComponent<VisitorDisguise>(),
            omangiPrefab.GetComponent<VisitorDisguise>(),
        };
        for (int k = 0; k < 2; k++)
        {
            var kind = (VisitorKind)k;
            props[k] = ItemTable.For(kind, ItemCategory.Prop);
            costumes[k] = ItemTable.For(kind, ItemCategory.Costume);
            var list = new List<ItemDef> { null };
            list.AddRange(ItemTable.For(kind, ItemCategory.Fur));
            list.AddRange(props[k]);
            list.AddRange(costumes[k]);
            list.AddRange(ItemTable.For(kind, ItemCategory.Sheet));
            entries[k] = list;
            EntryCount = Mathf.Max(EntryCount, list.Count);
            MaxPropCount = Mathf.Max(MaxPropCount, props[k].Count);
            MaxCostumeCount = Mathf.Max(MaxCostumeCount, costumes[k].Count);
        }
        Load();
    }

    public static string KindName(VisitorKind kind) => Loc.T(kind == VisitorKind.Kunmungchi ? "kind.kun" : "kind.oma");

    Visitor Prefab(VisitorKind kind) => kind == VisitorKind.Kunmungchi ? kunmungchiPrefab : omangiPrefab;

    // The props and costumes this kind can wear, in the order the dress-up screen lists them.
    public IReadOnlyList<ItemDef> Props(VisitorKind kind) => props[(int)kind];
    public IReadOnlyList<ItemDef> Costumes(VisitorKind kind) => costumes[(int)kind];

    public bool EntryValid(VisitorKind kind, int entry) => entry >= 0 && entry < entries[(int)kind].Count;

    public int TotalCount => entries[0].Count + entries[1].Count;

    public int UnlockedCount
    {
        get
        {
            int count = 0;
            for (int k = 0; k < 2; k++)
            {
                for (int e = 0; e < entries[k].Count; e++)
                {
                    if (IsUnlocked((VisitorKind)k, e)) count++;
                }
            }
            return count;
        }
    }

    static int BitOf(ItemDef def) => def == null ? NaturalBit : def.Bit;

    public bool IsUnlocked(VisitorKind kind, int entry)
    {
        return (unlocked[(int)kind] & (1u << BitOf(entries[(int)kind][entry]))) != 0;
    }

    // The look shown for a gallery entry: just that item (a prop that comes with the fur change brings it,
    // the fur change shows at the full level).
    public Outfit EntryOutfit(VisitorKind kind, int entry)
    {
        var outfit = new Outfit { kind = kind, dyeLevel = DyeLevelCount - 1 };
        var def = entries[(int)kind][entry];
        if (def != null) outfit.Set(def.Bit, true);
        return Normalize(outfit);
    }

    // Applies the combination rules so the outfit is one the visitors can really wear.
    public Outfit Normalize(Outfit outfit)
    {
        outfit.items = ItemTable.Normalize(outfit.kind, outfit.items);
        outfit.dyeLevel = Mathf.Clamp(outfit.dyeLevel, 0, DyeLevelCount - 1);
        return outfit;
    }

    // Entries of the items an outfit shows. A cover (sheet or pumpkin helmet) hides everything under it,
    // so a covered outfit counts as that cover only.
    void CollectEntries(Outfit outfit, List<int> result)
    {
        var list = entries[(int)outfit.kind];
        for (int e = 1; e < list.Count; e++)
        {
            var def = list[e];
            bool isCover = def.Category == ItemCategory.Sheet;
            if (outfit.sheet ? (isCover && outfit.Has(def.Bit)) : (!isCover && outfit.Has(def.Bit))) result.Add(e);
        }
        if (result.Count == 0) result.Add(0);
    }

    // Marks every item of the outfit as met. Entries met for the first time are added to newEntries.
    public void Unlock(Outfit outfit, List<int> newEntries)
    {
        var found = new List<int>(4);
        CollectEntries(outfit, found);
        var list = entries[(int)outfit.kind];
        bool changed = false;
        foreach (int entry in found)
        {
            uint bit = 1u << BitOf(list[entry]);
            if ((unlocked[(int)outfit.kind] & bit) != 0) continue;
            unlocked[(int)outfit.kind] |= bit;
            changed = true;
            newEntries?.Add(entry);
        }
        if (changed) Save();
    }

    // Opens every gallery entry of both species (and every special guest), as if all had been met.
    public void UnlockAll()
    {
        for (int k = 0; k < 2; k++)
        {
            foreach (var def in entries[k]) unlocked[k] |= 1u << BitOf(def);
        }
        SpecialGuest.UnlockAll();
        Save();
    }

    // Gallery numbers of the items in an outfit ("07" or "02+05").
    public string EntryNumber(Outfit outfit)
    {
        var found = new List<int>(4);
        CollectEntries(outfit, found);
        var sb = new StringBuilder();
        foreach (int entry in found)
        {
            if (sb.Length > 0) sb.Append('+');
            sb.Append(EntryNumber(entry));
        }
        return sb.ToString();
    }

    public string EntryNumber(int entry) => (entry + 1).ToString("00");

    public string EntryLabel(VisitorKind kind, int entry)
    {
        var def = entries[(int)kind][entry];
        return def == null ? Loc.T("outfit.none") : ItemLabel(def);
    }

    public string ItemLabel(ItemDef def)
    {
        switch (def.Category)
        {
            case ItemCategory.Fur: return Loc.T("dress.furdye");
            case ItemCategory.Sheet: return Loc.T(def.Bit == ItemTable.PumpkinBit ? "outfit.pumpkin" : "outfit.sheet");
            case ItemCategory.Costume: return NodeLabel(def.Node, "Costume_", "costume.");
            default: return NodeLabel(def.Node, "Fake_", "prop.");
        }
    }

    public string Describe(Outfit outfit)
    {
        if (outfit.sheet) return Loc.T(outfit.pumpkin ? "outfit.pumpkin" : "outfit.sheet");
        var parts = new List<string>();
        if (outfit.fur) parts.Add(Loc.T("outfit.dye", DyeLabel(outfit.dyeLevel)));
        foreach (var def in props[(int)outfit.kind])
        {
            if (outfit.Has(def.Bit)) parts.Add(ItemLabel(def));
        }
        foreach (var def in costumes[(int)outfit.kind])
        {
            if (outfit.Has(def.Bit)) parts.Add(ItemLabel(def));
        }
        return parts.Count == 0 ? Loc.T("outfit.none") : string.Join(" + ", parts);
    }

    public string DyeLabel(int level)
    {
        int count = DyeLevelCount;
        int percent = count == DefaultDyePercent.Length ? DefaultDyePercent[level] : Mathf.RoundToInt(100f * (level + 1) / count);
        return percent + "%";
    }

    // Localized name from a model node name; falls back to the node name split into words.
    static string NodeLabel(string name, string prefix, string keyPrefix)
    {
        if (name.StartsWith(prefix)) name = name.Substring(prefix.Length);
        if (Loc.Has(keyPrefix + name)) return Loc.T(keyPrefix + name);
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_')
            {
                sb.Append(' ');
                continue;
            }
            if (i > 0 && char.IsUpper(c))
            {
                char prev = name[i - 1];
                bool nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(prev) || (char.IsUpper(prev) && nextLower)) sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // A still copy of a visitor in the given look, facing the camera at the parent's local origin.
    // Its Visitor component is switched off so it neither walks nor bobs; tail wag and sheet flutter keep going.
    public GameObject CreateModel(Outfit outfit, Transform parent)
    {
        var visitor = Instantiate(Prefab(outfit.kind), parent);
        visitor.transform.localPosition = Vector3.zero;
        visitor.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        OutfitApplier.Apply(visitor, outfit);
        visitor.enabled = false;
        return visitor.gameObject;
    }

    void Load()
    {
        string[] parts = PlayerPrefs.GetString(SaveKey, "").Split(',');
        for (int k = 0; k < 2 && k < parts.Length; k++)
        {
            if (uint.TryParse(parts[k], System.Globalization.NumberStyles.HexNumber, null, out uint bits)) unlocked[k] = bits;
        }
    }

    void Save()
    {
        PlayerPrefs.SetString(SaveKey, unlocked[0].ToString("x") + "," + unlocked[1].ToString("x"));
        PlayerPrefs.Save();
    }

    [ContextMenu("Forget All Looks")]
    void ForgetAll()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        unlocked[0] = unlocked[1] = 0;
    }
}
