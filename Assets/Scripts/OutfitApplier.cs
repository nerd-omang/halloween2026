using UnityEngine;

// Bridge between an Outfit (item bits from ItemTable) and the model components. VisitorDisguise and
// VisitorCostume keep their own per-prefab bit order; items are matched to them by model node name.
public static class OutfitApplier
{
    // Puts the outfit on a visitor: fur change and props, costumes, and the ghost sheet.
    public static void Apply(Visitor visitor, Outfit outfit)
    {
        outfit.items = ItemTable.Normalize(outfit.kind, outfit.items);

        if (visitor.TryGetComponent(out VisitorDisguise disguise))
        {
            int mask = outfit.fur ? 1 : 0;
            for (int i = 0; i < disguise.PropCount; i++)
            {
                var def = ItemTable.FromNode(disguise.PropName(i));
                if (def != null && outfit.Has(def.Bit)) mask |= 1 << (i + 1);
            }
            disguise.Apply(mask, Mathf.Clamp(outfit.dyeLevel, 0, disguise.DyeLevelCount - 1));
        }

        if (visitor.TryGetComponent(out VisitorCostume costume))
        {
            int mask = 0;
            for (int i = 0; i < costume.CostumeCount; i++)
            {
                var def = ItemTable.FromNode(costume.CostumeName(i));
                if (def != null && outfit.Has(def.Bit)) mask |= 1 << i;
            }
            costume.Apply(mask);
        }

        if (outfit.sheet && visitor.TryGetComponent(out GhostSheet sheet)) sheet.Wear();
    }

    // The look the player sees right now (props already knocked off are not counted). With a sheet on,
    // the items are what is hidden underneath.
    public static Outfit Read(Visitor visitor)
    {
        var outfit = new Outfit { kind = visitor.kind };

        if (visitor.TryGetComponent(out VisitorDisguise disguise))
        {
            int mask = disguise.WornMask;
            outfit.fur = (mask & 1) != 0;
            outfit.dyeLevel = outfit.fur ? disguise.DyeLevel : 0;
            for (int i = 0; i < disguise.PropCount; i++)
            {
                var def = ItemTable.FromNode(disguise.PropName(i));
                if (def != null && (mask & (1 << (i + 1))) != 0) outfit.Set(def.Bit, true);
            }
        }

        if (visitor.TryGetComponent(out VisitorCostume costume))
        {
            int mask = costume.WornMask;
            for (int i = 0; i < costume.CostumeCount; i++)
            {
                var def = ItemTable.FromNode(costume.CostumeName(i));
                if (def != null && (mask & (1 << i)) != 0) outfit.Set(def.Bit, true);
            }
        }

        outfit.sheet = visitor.IsCoveredBySheet;
        return outfit;
    }
}
