using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Sandbox: pick a species and tick fur dye, props and the ghost sheet to dress the model up yourself.
// Everything is available here; it does not unlock gallery entries.
public class DressUpScreen
{
    public event Action Back;

    // Two side panels (controls left, props and costumes right) with the model between them.
    const float PanelWidth = 400f;
    const float RightColumn = 1280f - PanelWidth + 24f;
    const float ModelViewportX = 0.5f;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly GameObject panel;
    readonly Text descriptionText;
    readonly SimpleToggle[] speciesToggles = new SimpleToggle[2];
    readonly SimpleToggle furToggle;
    readonly SimpleToggle[] dyeToggles;
    readonly SimpleToggle[] partToggles; // rows of the shown part; spare rows are hidden
    readonly Text partLabel;
    readonly SimpleButton partPrev;
    readonly SimpleButton partNext;
    readonly SimpleToggle sheetToggle;
    readonly SimpleToggle pumpkinToggle;

    // The items a visitor can wear, grouped by the part of the body they go on. The right panel shows one
    // part at a time, with arrows to flip through them.
    enum Part
    {
        Head,
        Eyes,
        Face,
        Neck,
        Hand,
        Back,
        Body,
    }

    static readonly string[] PartKeys = { "part.head", "part.eyes", "part.face", "part.neck", "part.hand", "part.back", "part.body" };

    static Part PartOf(ItemDef def)
    {
        if (def.Category == ItemCategory.Costume)
        {
            switch (def.Slot)
            {
                case ItemSlot.Head: return Part.Head;
                case ItemSlot.Eyes:
                case ItemSlot.LeftEye:
                case ItemSlot.RightEye: return Part.Eyes;
                case ItemSlot.Neck: return Part.Neck;
                case ItemSlot.Hand: return Part.Hand;
                case ItemSlot.Back: return Part.Back;
                default: return Part.Body;
            }
        }
        // Props have no slot: head pieces, glasses, the nose, the tail.
        if (def.HeadPriority >= 0) return Part.Head;
        if (def.CoversEyes) return Part.Eyes;
        if (def.Id == "cone_nose") return Part.Face;
        return Part.Back;
    }

    // The parts the kind has items for (in body order) and their items.
    readonly List<Part> shownParts = new List<Part>();
    readonly List<List<ItemDef>> shownItems = new List<List<ItemDef>>();
    int partIndex;
    Part currentPart;

    Outfit current;

    public DressUpScreen(Transform parent, OutfitCatalog catalog, ModelStage stage)
    {
        this.catalog = catalog;
        this.stage = stage;

        var root = UiKit.Fill("DressUpScreen", parent);
        panel = root.gameObject;
        UiKit.SidePanel("Controls", root, PanelWidth);
        UiKit.SidePanelRight("Items", root, PanelWidth);

        var topLeft = new Vector2(0f, 1f);
        UiKit.Bind(UiKit.Label(root, "Header", "", 55, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -12f), new Vector2(360f, 60f), UiKit.Accent, true), "dress.header", true);

        UiKit.Bind(UiKit.Label(root, "SpeciesLabel", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -76f), new Vector2(360f, 26f), UiKit.Dim), "dress.who");
        for (int k = 0; k < 2; k++)
        {
            var kind = (VisitorKind)k;
            var toggle = UiKit.Toggle(root, "Species" + k, OutfitCatalog.KindName(kind), true, 22, topLeft, topLeft,
                new Vector2(24f, -102f - k * 34f), new Vector2(352f, 32f));
            toggle.Changed += _ => SetKind(kind);
            speciesToggles[k] = toggle;
        }

        UiKit.Bind(UiKit.Label(root, "FurLabel", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -184f), new Vector2(360f, 26f), UiKit.Dim), "dress.fur");
        furToggle = UiKit.Toggle(root, "Fur", "", false, 22, topLeft, topLeft, new Vector2(24f, -212f), new Vector2(352f, 32f));
        UiKit.Bind(furToggle.Label, "dress.furdye");
        furToggle.Changed += on => SetItem(ItemTable.FurBit, on);

        dyeToggles = new SimpleToggle[catalog.DyeLevelCount];
        for (int d = 0; d < dyeToggles.Length; d++)
        {
            int level = d;
            var toggle = UiKit.Toggle(root, "Dye" + d, catalog.DyeLabel(d), true, 22, topLeft, topLeft,
                new Vector2(24f + d * 88f, -250f), new Vector2(84f, 30f));
            toggle.Changed += _ => SetDye(level);
            dyeToggles[d] = toggle;
        }

        // Right panel: one part of the body at a time (head, eyes, ...), flipped with the arrows like the
        // gallery pages. The rows are a pool as big as the biggest part of either kind.
        int rows = 1;
        for (int k = 0; k < 2; k++)
        {
            var counts = new int[PartKeys.Length];
            foreach (var def in catalog.Props((VisitorKind)k)) counts[(int)PartOf(def)]++;
            foreach (var def in catalog.Costumes((VisitorKind)k)) counts[(int)PartOf(def)]++;
            foreach (int count in counts) rows = Mathf.Max(rows, count);
        }

        partPrev = UiKit.Button(root, "PartPrev", "←", 22, topLeft, topLeft, new Vector2(RightColumn, -76f), new Vector2(72f, 38f), () => ChangePart(-1));
        partLabel = UiKit.Label(root, "PartName", "", 22, TextAnchor.MiddleCenter, topLeft, topLeft, new Vector2(RightColumn + 80f, -76f), new Vector2(192f, 38f), UiKit.Accent);
        partNext = UiKit.Button(root, "PartNext", "→", 22, topLeft, topLeft, new Vector2(RightColumn + 280f, -76f), new Vector2(72f, 38f), () => ChangePart(1));

        partToggles = new SimpleToggle[rows];
        for (int p = 0; p < partToggles.Length; p++)
        {
            int slot = p;
            var toggle = UiKit.Toggle(root, "Item" + p, "", false, 22, topLeft, topLeft,
                new Vector2(RightColumn, -128f - p * 34f), new Vector2(352f, 32f));
            toggle.Changed += on => SetPartItem(slot, on);
            partToggles[p] = toggle;
        }

        float sheetY = -296f;
        sheetToggle = UiKit.Toggle(root, "Sheet", "", false, 22, topLeft, topLeft, new Vector2(24f, sheetY), new Vector2(352f, 32f));
        UiKit.Bind(sheetToggle.Label, "dress.sheet");
        sheetToggle.Changed += on =>
        {
            current.sheet = on;
            Apply();
        };

        pumpkinToggle = UiKit.Toggle(root, "Pumpkin", "", false, 22, topLeft, topLeft, new Vector2(24f, sheetY - 34f), new Vector2(352f, 32f));
        UiKit.Bind(pumpkinToggle.Label, "dress.pumpkin");
        pumpkinToggle.Changed += on =>
        {
            current.pumpkin = on;
            Apply();
        };

        float buttonY = sheetY - 92f;
        var random = UiKit.Button(root, "Random", "", 22, topLeft, topLeft, new Vector2(24f, buttonY), new Vector2(170f, 46f), Randomize);
        UiKit.Bind(random.Label, "dress.random");
        var reset = UiKit.Button(root, "Reset", "", 22, topLeft, topLeft, new Vector2(206f, buttonY), new Vector2(170f, 46f), Reset);
        UiKit.Bind(reset.Label, "dress.reset");

        // Bottom of the left panel, away from the item rows on the right.
        var bottomLeft = new Vector2(0f, 0f);
        var back = UiKit.Button(root, "Back", "", 33, bottomLeft, bottomLeft, new Vector2(24f, UiKit.MarginY), new Vector2(200f, 56f), () => Back?.Invoke());
        UiKit.Bind(back.Label, "back");
        // Top left of the middle area, away from the model in its center.
        var viewReset = UiKit.Button(root, "ViewReset", "", 22, topLeft, topLeft, new Vector2(424f, -UiKit.MarginY), new Vector2(190f, 44f), stage.ResetView);
        UiKit.Bind(viewReset.Label, "view.reset");
        // A low strip at the bottom edge, between the two side panels.
        descriptionText = UiKit.Label(root, "Description", "", 33, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(640f, 14f), new Vector2(440f, 84f), Color.white);

        panel.SetActive(false);
    }

    public void Show()
    {
        panel.SetActive(true);
        Reset();
    }

    public void Hide() => panel.SetActive(false);

    void Reset()
    {
        current = new Outfit { kind = current.kind, dyeLevel = catalog.DyeLevelCount - 1 };
        Apply(true);
    }

    void Randomize()
    {
        current.items = 0;
        current.fur = UnityEngine.Random.value < 0.5f;
        foreach (var def in catalog.Props(current.kind)) current.Set(def.Bit, UnityEngine.Random.value < 0.4f);
        foreach (var def in catalog.Costumes(current.kind)) current.Set(def.Bit, UnityEngine.Random.value < 0.3f);
        current.dyeLevel = UnityEngine.Random.Range(0, catalog.DyeLevelCount);
        current.sheet = UnityEngine.Random.value < 0.25f;
        if (current.sheet && UnityEngine.Random.value < 0.35f) current.pumpkin = true;
        Apply();
    }

    void SetKind(VisitorKind kind)
    {
        current.kind = kind; // items this kind cannot wear drop out in Apply
        Apply();
    }

    void SetItem(int bit, bool on)
    {
        current.Set(bit, on);
        Apply();
    }

    void SetPartItem(int slot, bool on)
    {
        if (partIndex < shownItems.Count && slot < shownItems[partIndex].Count) SetItem(shownItems[partIndex][slot].Bit, on);
    }

    void ChangePart(int delta)
    {
        partIndex = Mathf.Clamp(partIndex + delta, 0, shownParts.Count - 1);
        currentPart = shownParts[partIndex];
        SyncParts();
    }

    void SetDye(int level)
    {
        current.dyeLevel = level;
        current.fur = true;
        Apply();
    }

    // Applies the combination rules first (one head piece, the paint bucket brings the paint, no hat
    // under the bucket), so the toggles always show what is really worn.
    void Apply(bool rebuild = false)
    {
        current = catalog.Normalize(current);
        SyncControls();
        if (rebuild || stage.Count == 0) stage.Show(new List<Outfit> { current }, ModelViewportX);
        else stage.SetOutfit(0, current);

        descriptionText.text = OutfitCatalog.KindName(current.kind) + "\n<size=22>" + catalog.Describe(current)
            + "</size>\n<size=22><color=#c8b8d9>" + Loc.T(GameInput.UsingTouch ? "drag.rotateTouch" : "drag.rotate") + "</color></size>";
    }

    void SyncControls()
    {
        for (int k = 0; k < 2; k++) speciesToggles[k].Set(k == (int)current.kind);
        furToggle.Set(current.fur);
        for (int d = 0; d < dyeToggles.Length; d++) dyeToggles[d].Set(current.fur && d == current.dyeLevel);

        RebuildParts();
        SyncParts();
        sheetToggle.Set(current.Has(ItemTable.SheetBit));
        pumpkinToggle.Set(current.pumpkin);
        for (int k = 0; k < 2; k++) speciesToggles[k].Text = OutfitCatalog.KindName((VisitorKind)k);
    }

    // Groups the kind's items by part; keeps the shown part when the new kind has it too.
    void RebuildParts()
    {
        shownParts.Clear();
        shownItems.Clear();
        for (int p = 0; p < PartKeys.Length; p++)
        {
            var items = new List<ItemDef>();
            foreach (var def in catalog.Props(current.kind)) if ((int)PartOf(def) == p) items.Add(def);
            foreach (var def in catalog.Costumes(current.kind)) if ((int)PartOf(def) == p) items.Add(def);
            if (items.Count == 0) continue;
            shownParts.Add((Part)p);
            shownItems.Add(items);
        }
        partIndex = Mathf.Max(0, shownParts.IndexOf(currentPart));
        currentPart = shownParts.Count > 0 ? shownParts[partIndex] : Part.Head;
    }

    // Shows the rows and the title of the current part.
    void SyncParts()
    {
        var items = partIndex < shownItems.Count ? shownItems[partIndex] : null;
        partLabel.text = Loc.T(PartKeys[(int)currentPart]) + "  " + (partIndex + 1) + "/" + shownParts.Count;
        partPrev.Interactable = partIndex > 0;
        partNext.Interactable = partIndex < shownParts.Count - 1;
        for (int i = 0; i < partToggles.Length; i++)
        {
            bool exists = items != null && i < items.Count;
            partToggles[i].gameObject.SetActive(exists);
            if (!exists) continue;
            partToggles[i].Text = catalog.ItemLabel(items[i]);
            partToggles[i].Set(current.Has(items[i].Bit));
        }
    }
}
