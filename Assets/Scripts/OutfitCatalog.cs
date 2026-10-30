using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Knows the visitor prefabs and every look they can be put together in. Builds display models from an
// Outfit, names looks, and remembers which looks the player has met (the gallery).
// A gallery entry is one disguise mask per visitor (the fur change level does not matter) plus one
// "ghost sheet" entry. Entry i of a kind is mask i; the last entry is the sheet.
public class OutfitCatalog : MonoBehaviour
{
    [SerializeField] Visitor kunmungchiPrefab;
    [SerializeField] Visitor omangiPrefab;

    const string SaveKey = "gallery_v1";
    static readonly int[] DefaultDyePercent = { 30, 60, 90, 100 };

    VisitorDisguise[] disguises;
    int stride;
    bool[] unlocked;

    public int EntryCount => stride;
    public int SheetEntry => stride - 1;
    public int DyeLevelCount => disguises[0].DyeLevelCount;
    public int MaxPropCount { get; private set; }

    void Awake()
    {
        disguises = new[]
        {
            kunmungchiPrefab.GetComponent<VisitorDisguise>(),
            omangiPrefab.GetComponent<VisitorDisguise>(),
        };
        MaxPropCount = Mathf.Max(disguises[0].PropCount, disguises[1].PropCount);
        stride = (1 << (1 + MaxPropCount)) + 1;
        Load();
    }

    public int PropCount(VisitorKind kind) => disguises[(int)kind].PropCount;

    public static string KindName(VisitorKind kind) => Loc.T(kind == VisitorKind.Kunmungchi ? "kind.kun" : "kind.oma");

    Visitor Prefab(VisitorKind kind) => kind == VisitorKind.Kunmungchi ? kunmungchiPrefab : omangiPrefab;

    public bool EntryValid(VisitorKind kind, int entry)
    {
        return entry == SheetEntry || entry < (1 << (1 + PropCount(kind)));
    }

    public int TotalCount
    {
        get
        {
            int total = 0;
            for (int k = 0; k < 2; k++)
            {
                for (int e = 0; e < stride; e++)
                {
                    if (EntryValid((VisitorKind)k, e)) total++;
                }
            }
            return total;
        }
    }

    public int UnlockedCount
    {
        get
        {
            int count = 0;
            for (int k = 0; k < 2; k++)
            {
                for (int e = 0; e < stride; e++)
                {
                    if (EntryValid((VisitorKind)k, e) && unlocked[k * stride + e]) count++;
                }
            }
            return count;
        }
    }

    public bool IsUnlocked(VisitorKind kind, int entry) => unlocked[(int)kind * stride + entry];

    // The look shown for a gallery entry (the full fur change level when the fur is changed).
    public Outfit EntryOutfit(VisitorKind kind, int entry)
    {
        if (entry == SheetEntry) return new Outfit { kind = kind, sheet = true };
        return new Outfit { kind = kind, mask = entry, dyeLevel = (entry & 1) != 0 ? DyeLevelCount - 1 : 0 };
    }

    // Marks the look as met. Returns true the first time.
    public bool Unlock(Outfit outfit)
    {
        int entry = outfit.sheet ? SheetEntry : outfit.mask;
        if (entry < 0 || entry >= stride || !EntryValid(outfit.kind, entry)) return false;
        int index = (int)outfit.kind * stride + entry;
        if (unlocked[index]) return false;
        unlocked[index] = true;
        Save();
        return true;
    }

    // The gallery number of a look ("07"); the same number the gallery list shows.
    public string EntryNumber(Outfit outfit)
    {
        return ((outfit.sheet ? SheetEntry : outfit.mask) + 1).ToString("00");
    }

    public string Describe(Outfit outfit)
    {
        if (outfit.sheet) return Loc.T("outfit.sheet");
        var parts = new List<string>();
        if ((outfit.mask & 1) != 0) parts.Add(Loc.T("outfit.dye", DyeLabel(outfit.dyeLevel)));
        for (int i = 0; i < PropCount(outfit.kind); i++)
        {
            if ((outfit.mask & (1 << (i + 1))) != 0) parts.Add(PropLabel(outfit.kind, i));
        }
        return parts.Count == 0 ? Loc.T("outfit.none") : string.Join(" + ", parts);
    }

    public string DyeLabel(int level)
    {
        int count = DyeLevelCount;
        int percent = count == DefaultDyePercent.Length ? DefaultDyePercent[level] : Mathf.RoundToInt(100f * (level + 1) / count);
        return percent + "%";
    }

    public string PropLabel(VisitorKind kind, int index)
    {
        string name = disguises[(int)kind].PropName(index);
        if (name.StartsWith("Fake_")) name = name.Substring(5);
        if (Loc.Has("prop." + name)) return Loc.T("prop." + name);
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
        if (visitor.TryGetComponent(out VisitorDisguise disguise))
        {
            disguise.Apply(outfit.mask, Mathf.Clamp(outfit.dyeLevel, 0, disguise.DyeLevelCount - 1));
        }
        if (outfit.sheet && visitor.TryGetComponent(out GhostSheet sheet)) sheet.Wear();
        visitor.enabled = false;
        return visitor.gameObject;
    }

    void Load()
    {
        unlocked = new bool[stride * 2];
        string saved = PlayerPrefs.GetString(SaveKey, "");
        for (int i = 0; i < unlocked.Length && i < saved.Length; i++)
        {
            unlocked[i] = saved[i] == '1';
        }
    }

    void Save()
    {
        var sb = new StringBuilder(unlocked.Length);
        foreach (bool b in unlocked) sb.Append(b ? '1' : '0');
        PlayerPrefs.SetString(SaveKey, sb.ToString());
        PlayerPrefs.Save();
    }

    [ContextMenu("Forget All Looks")]
    void ForgetAll()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        Load();
    }
}
