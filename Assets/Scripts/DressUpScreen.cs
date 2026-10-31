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
    readonly SimpleToggle[] propToggles;
    readonly SimpleToggle[] costumeToggles;
    readonly SimpleToggle sheetToggle;

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

        UiKit.Bind(UiKit.Label(root, "PropsLabel", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(RightColumn, -76f), new Vector2(360f, 26f), UiKit.Dim), "dress.props");
        propToggles = new SimpleToggle[catalog.MaxPropCount];
        for (int p = 0; p < propToggles.Length; p++)
        {
            int slot = p;
            var toggle = UiKit.Toggle(root, "Prop" + p, "", false, 22, topLeft, topLeft,
                new Vector2(RightColumn, -104f - p * 34f), new Vector2(352f, 32f));
            toggle.Changed += on => SetListed(catalog.Props(current.kind), slot, on);
            propToggles[p] = toggle;
        }

        float costumeLabelY = -104f - propToggles.Length * 34f - 14f;
        UiKit.Bind(UiKit.Label(root, "CostumesLabel", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(RightColumn, costumeLabelY), new Vector2(360f, 26f), UiKit.Dim), "dress.costumes");
        costumeToggles = new SimpleToggle[catalog.MaxCostumeCount];
        for (int c = 0; c < costumeToggles.Length; c++)
        {
            int slot = c;
            var toggle = UiKit.Toggle(root, "Costume" + c, "", false, 22, topLeft, topLeft,
                new Vector2(RightColumn, costumeLabelY - 28f - c * 34f), new Vector2(352f, 32f));
            toggle.Changed += on => SetListed(catalog.Costumes(current.kind), slot, on);
            costumeToggles[c] = toggle;
        }

        float sheetY = -296f;
        sheetToggle = UiKit.Toggle(root, "Sheet", "", false, 22, topLeft, topLeft, new Vector2(24f, sheetY), new Vector2(352f, 32f));
        UiKit.Bind(sheetToggle.Label, "dress.sheet");
        sheetToggle.Changed += on =>
        {
            current.sheet = on;
            Apply();
        };

        float buttonY = sheetY - 58f;
        var random = UiKit.Button(root, "Random", "", 22, topLeft, topLeft, new Vector2(24f, buttonY), new Vector2(170f, 46f), Randomize);
        UiKit.Bind(random.Label, "dress.random");
        var reset = UiKit.Button(root, "Reset", "", 22, topLeft, topLeft, new Vector2(206f, buttonY), new Vector2(170f, 46f), Reset);
        UiKit.Bind(reset.Label, "dress.reset");

        var bottomRight = new Vector2(1f, 0f);
        var back = UiKit.Button(root, "Back", "", 33, bottomRight, bottomRight, new Vector2(-UiKit.MarginX, UiKit.MarginY), new Vector2(200f, 56f), () => Back?.Invoke());
        UiKit.Bind(back.Label, "back");
        descriptionText = UiKit.Label(root, "Description", "", 33, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(640f, 104f), new Vector2(440f, 90f), Color.white);

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

    void SetListed(IReadOnlyList<ItemDef> list, int slot, bool on)
    {
        if (slot < list.Count) SetItem(list[slot].Bit, on);
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
            + "</size>\n<size=22><color=#c8b8d9>" + Loc.T("drag.rotate") + "</color></size>";
    }

    void SyncControls()
    {
        for (int k = 0; k < 2; k++) speciesToggles[k].Set(k == (int)current.kind);
        furToggle.Set(current.fur);
        for (int d = 0; d < dyeToggles.Length; d++) dyeToggles[d].Set(current.fur && d == current.dyeLevel);

        SyncList(propToggles, catalog.Props(current.kind));
        SyncList(costumeToggles, catalog.Costumes(current.kind));
        sheetToggle.Set(current.sheet);
        for (int k = 0; k < 2; k++) speciesToggles[k].Text = OutfitCatalog.KindName((VisitorKind)k);
    }

    // One toggle per item of the list; spare toggles (the other kind has fewer items) are hidden.
    void SyncList(SimpleToggle[] toggles, IReadOnlyList<ItemDef> items)
    {
        for (int i = 0; i < toggles.Length; i++)
        {
            bool exists = i < items.Count;
            toggles[i].gameObject.SetActive(exists);
            if (!exists) continue;
            toggles[i].Text = catalog.ItemLabel(items[i]);
            toggles[i].Set(current.Has(items[i].Bit));
        }
    }
}
