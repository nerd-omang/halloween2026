using System.Collections.Generic;

public enum ItemCategory { Fur, Prop, Costume, Sheet }

public enum ItemSlot { None, Head, Neck, Hand, Back, Body, Eyes, LeftEye, RightEye }

// One wearable thing a visitor can have on. Bit is its permanent place in an Outfit and in the look code
// sent to the server: never renumber or reuse a bit, only add new items at the next free one.
public sealed class ItemDef
{
    public int Bit;
    public string Id;
    // Name of the model node (prefab child) that shows it; null for the fur change and the covers (sheet, pumpkin helmet).
    public string Node;
    public ItemCategory Category;
    public ItemSlot Slot;
    // One bit per VisitorKind: who can wear it.
    public uint Kinds;
    // The prop brings the fur change with it (the paint bucket: the paint was poured from it).
    public bool BringsFur;
    // The prop covers the head, so no head costume goes on top of it.
    public bool CoversHead;
    // The prop covers the eyes (glasses), so no eyes costume goes with it.
    public bool CoversEyes;
    // A headband costume: not worn together with a headband prop (a head prop that does not cover the head).
    public bool Headband;
    // A palm blast cannot knock it off.
    public bool Stays;
    // Head props exclude each other; the lowest priority number wins. -1 = not a head prop.
    public int HeadPriority = -1;
    // A prop that is hard to see through only shows up in disguises from this score on.
    public int StartScore;
    // How likely a palm blast is to knock it off, against the other worn props (1 = the usual chance), at a combo of 0.
    // The more the player's combo grows, the closer every prop comes to 1 (see VisitorDisguise.DropOneFakeProp).
    public float DropWeight = 1f;

    public bool For(VisitorKind kind) => (Kinds & (1u << (int)kind)) != 0;
}

// The single list of everything wearable, with the combination rules in one place.
// Outfit.items has one bit per item (ItemDef.Bit); the look code (LookCode) has room for 29 of them.
public static class ItemTable
{
    public const int Capacity = 29;
    public const int FurBit = 0;
    public const int SheetBit = 12;
    public const int PumpkinBit = 21;

    const uint Kun = 1u << (int)VisitorKind.Kunmungchi;
    const uint Oma = 1u << (int)VisitorKind.Omangi;
    const uint Both = Kun | Oma;

    public static readonly ItemDef[] All =
    {
        new ItemDef { Bit = 0, Id = "fur", Category = ItemCategory.Fur, Kinds = Both },
        new ItemDef { Bit = 1, Id = "horn_band", Node = "Fake_HornBand", Category = ItemCategory.Prop, Kinds = Kun, HeadPriority = 1, DropWeight = 1.5f },
        new ItemDef { Bit = 2, Id = "goat_ears", Node = "Fake_GoatEars", Category = ItemCategory.Prop, Kinds = Kun, HeadPriority = 2 },
        new ItemDef { Bit = 3, Id = "cone_nose", Node = "Fake_ConeNose", Category = ItemCategory.Prop, Kinds = Oma, StartScore = 80, DropWeight = 3f },
        new ItemDef { Bit = 4, Id = "raccoon_tail", Node = "Fake_RaccoonTail", Category = ItemCategory.Prop, Kinds = Oma },
        new ItemDef { Bit = 5, Id = "x_glasses", Node = "Fake_XGlasses", Category = ItemCategory.Prop, Kinds = Both, CoversEyes = true, DropWeight = 2f },
        new ItemDef { Bit = 6, Id = "paint_bucket", Node = "Fake_PaintBucket", Category = ItemCategory.Prop, Kinds = Both,
            BringsFur = true, CoversHead = true, HeadPriority = 0 },
        new ItemDef { Bit = 7, Id = "witch_hat", Node = "Costume_WitchHat", Category = ItemCategory.Costume, Slot = ItemSlot.Head, Kinds = Both },
        new ItemDef { Bit = 8, Id = "bow_tie", Node = "Costume_BowTie", Category = ItemCategory.Costume, Slot = ItemSlot.Neck, Kinds = Both },
        new ItemDef { Bit = 9, Id = "pumpkin_basket", Node = "Costume_PumpkinBasket", Category = ItemCategory.Costume, Slot = ItemSlot.Hand, Kinds = Both },
        new ItemDef { Bit = 10, Id = "bat_wings", Node = "Costume_BatWings", Category = ItemCategory.Costume, Slot = ItemSlot.Back, Kinds = Both },
        new ItemDef { Bit = 11, Id = "bandage", Node = "Costume_Bandage", Category = ItemCategory.Costume, Slot = ItemSlot.Body, Kinds = Both, Stays = true },
        new ItemDef { Bit = 12, Id = "sheet", Category = ItemCategory.Sheet, Kinds = Both },
        new ItemDef { Bit = 13, Id = "maid_headband", Node = "Costume_MaidHeadband", Category = ItemCategory.Costume, Slot = ItemSlot.Head, Kinds = Both, Headband = true },
        new ItemDef { Bit = 14, Id = "bunny_headband", Node = "Costume_BunnyHeadband", Category = ItemCategory.Costume, Slot = ItemSlot.Head, Kinds = Both, Headband = true },
        new ItemDef { Bit = 15, Id = "star_sunglasses", Node = "Costume_StarSunglasses", Category = ItemCategory.Costume, Slot = ItemSlot.Eyes, Kinds = Both },
        new ItemDef { Bit = 16, Id = "alien_sunglasses", Node = "Costume_AlienSunglasses", Category = ItemCategory.Costume, Slot = ItemSlot.Eyes, Kinds = Both },
        new ItemDef { Bit = 17, Id = "pirate_eye_patch", Node = "Costume_PirateEyePatch", Category = ItemCategory.Costume, Slot = ItemSlot.LeftEye, Kinds = Both },
        new ItemDef { Bit = 18, Id = "broom", Node = "Costume_Broom", Category = ItemCategory.Costume, Slot = ItemSlot.Hand, Kinds = Both },
        new ItemDef { Bit = 19, Id = "lollipop", Node = "Costume_Lollipop", Category = ItemCategory.Costume, Slot = ItemSlot.Hand, Kinds = Both },
        new ItemDef { Bit = 20, Id = "rib_vest", Node = "Costume_RibVest", Category = ItemCategory.Costume, Slot = ItemSlot.Body, Kinds = Both },
        new ItemDef { Bit = 21, Id = "pumpkin_helmet", Category = ItemCategory.Sheet, Kinds = Both },
        new ItemDef { Bit = 22, Id = "medical_eye_patch", Node = "Costume_MedicalEyePatch", Category = ItemCategory.Costume, Slot = ItemSlot.RightEye, Kinds = Both },
        new ItemDef { Bit = 23, Id = "gold_bikini", Node = "Costume_GoldBikini", Category = ItemCategory.Costume, Slot = ItemSlot.Body, Kinds = Both },
        new ItemDef { Bit = 24, Id = "bunny_suit", Node = "Costume_BunnySuit", Category = ItemCategory.Costume, Slot = ItemSlot.Body, Kinds = Both },
        new ItemDef { Bit = 25, Id = "maid_dress", Node = "Costume_MaidDress", Category = ItemCategory.Costume, Slot = ItemSlot.Body, Kinds = Both },
    };

    static readonly Dictionary<string, ItemDef> ByNodeName = BuildNodeMap();

    static Dictionary<string, ItemDef> BuildNodeMap()
    {
        var map = new Dictionary<string, ItemDef>();
        foreach (var def in All)
        {
            if (def.Node != null) map[def.Node] = def;
        }
        return map;
    }

    public static uint Mask(int bit) => 1u << bit;

    public static ItemDef FromNode(string node)
    {
        return node != null && ByNodeName.TryGetValue(node, out var def) ? def : null;
    }

    public static ItemDef FromBit(int bit)
    {
        foreach (var def in All)
        {
            if (def.Bit == bit) return def;
        }
        return null;
    }

    // The items of a category that this kind of visitor can wear, in table order.
    public static List<ItemDef> For(VisitorKind kind, ItemCategory category)
    {
        var list = new List<ItemDef>();
        foreach (var def in All)
        {
            if (def.Category == category && def.For(kind)) list.Add(def);
        }
        return list;
    }

    // Applies the combination rules: only items this kind can wear, one head prop (lowest priority number
    // wins), a prop that brings the fur change turns it on, no head costume under a head-covering prop,
    // no headband costume with a headband prop, no eyes costume with glasses props, one costume per slot
    // (the first in the table wins), and one cover at a time (the pumpkin helmet gives way to the ghost sheet).
    public static uint Normalize(VisitorKind kind, uint items)
    {
        if ((items & Mask(SheetBit)) != 0) items &= ~Mask(PumpkinBit);
        bool coversHead = false;
        bool coversEyes = false;
        ItemDef headProp = null;
        foreach (var def in All)
        {
            if ((items & Mask(def.Bit)) == 0) continue;
            if (!def.For(kind))
            {
                items &= ~Mask(def.Bit);
                continue;
            }
            if (def.HeadPriority >= 0 && (headProp == null || def.HeadPriority < headProp.HeadPriority)) headProp = def;
        }

        foreach (var def in All)
        {
            if ((items & Mask(def.Bit)) == 0) continue;
            if (def.HeadPriority >= 0 && def != headProp) items &= ~Mask(def.Bit);
        }
        foreach (var def in All)
        {
            if ((items & Mask(def.Bit)) == 0) continue;
            if (def.BringsFur) items |= Mask(FurBit);
            if (def.CoversHead) coversHead = true;
            if (def.CoversEyes) coversEyes = true;
        }
        bool headbandProp = headProp != null && !headProp.CoversHead;

        uint usedSlots = 0;
        foreach (var def in All)
        {
            if (def.Category != ItemCategory.Costume || (items & Mask(def.Bit)) == 0) continue;
            uint slot = 1u << (int)def.Slot;
            if (SlotTaken(def.Slot, usedSlots) || (coversHead && def.Slot == ItemSlot.Head) || (headbandProp && def.Headband)
                || (coversEyes && IsEyeSlot(def.Slot)))
            {
                items &= ~Mask(def.Bit);
                continue;
            }
            usedSlots |= slot;
        }
        return items;
    }

    public static bool IsEyeSlot(ItemSlot slot) => slot == ItemSlot.Eyes || slot == ItemSlot.LeftEye || slot == ItemSlot.RightEye;

    // A slot is taken by an item in it. Sunglasses (Eyes) need both eyes free, while the left-eye and
    // right-eye patches fit together but not with sunglasses.
    public static bool SlotTaken(ItemSlot slot, uint used)
    {
        uint eyes = 1u << (int)ItemSlot.Eyes, left = 1u << (int)ItemSlot.LeftEye, right = 1u << (int)ItemSlot.RightEye;
        switch (slot)
        {
            case ItemSlot.Eyes: return (used & (eyes | left | right)) != 0;
            case ItemSlot.LeftEye: return (used & (left | eyes)) != 0;
            case ItemSlot.RightEye: return (used & (right | eyes)) != 0;
            default: return (used & (1u << (int)slot)) != 0;
        }
    }

    // False for combinations that can never occur in the game (the server uses this to reject forged codes).
    public static bool IsValid(VisitorKind kind, uint items) => Normalize(kind, items) == items;
}
