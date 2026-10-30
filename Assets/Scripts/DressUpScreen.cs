using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Sandbox: pick a species and tick fur dye, props and the ghost sheet to dress the model up yourself.
// Everything is available here; it does not unlock gallery entries.
public class DressUpScreen
{
    public event Action Back;

    const float PanelWidth = 400f;
    const float ModelViewportX = 0.66f;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly GameObject panel;
    readonly Text descriptionText;
    readonly SimpleToggle[] speciesToggles = new SimpleToggle[2];
    readonly SimpleToggle furToggle;
    readonly SimpleToggle[] dyeToggles;
    readonly SimpleToggle[] propToggles;
    readonly SimpleToggle sheetToggle;

    Outfit current;

    public DressUpScreen(Transform parent, OutfitCatalog catalog, ModelStage stage)
    {
        this.catalog = catalog;
        this.stage = stage;

        var root = UiKit.Fill("DressUpScreen", parent);
        panel = root.gameObject;
        UiKit.SidePanel("Controls", root, PanelWidth);

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
        furToggle.Changed += on => SetBit(0, on);

        dyeToggles = new SimpleToggle[catalog.DyeLevelCount];
        for (int d = 0; d < dyeToggles.Length; d++)
        {
            int level = d;
            var toggle = UiKit.Toggle(root, "Dye" + d, catalog.DyeLabel(d), true, 22, topLeft, topLeft,
                new Vector2(24f + d * 88f, -250f), new Vector2(84f, 30f));
            toggle.Changed += _ => SetDye(level);
            dyeToggles[d] = toggle;
        }

        UiKit.Bind(UiKit.Label(root, "PropsLabel", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -296f), new Vector2(360f, 26f), UiKit.Dim), "dress.props");
        propToggles = new SimpleToggle[catalog.MaxPropCount];
        for (int p = 0; p < propToggles.Length; p++)
        {
            int bit = p + 1;
            var toggle = UiKit.Toggle(root, "Prop" + p, "", false, 22, topLeft, topLeft,
                new Vector2(24f, -324f - p * 34f), new Vector2(352f, 32f));
            toggle.Changed += on => SetBit(bit, on);
            propToggles[p] = toggle;
        }

        float sheetY = -324f - propToggles.Length * 34f - 14f;
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
        var back = UiKit.Button(root, "Back", "", 33, bottomRight, bottomRight, new Vector2(-24f, 24f), new Vector2(200f, 56f), () => Back?.Invoke());
        UiKit.Bind(back.Label, "back");
        descriptionText = UiKit.Label(root, "Description", "", 33, TextAnchor.UpperCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(PanelWidth + (1280f - PanelWidth) * 0.5f, 100f), new Vector2(780f, 90f), Color.white);

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
        int props = catalog.PropCount(current.kind);
        current.mask = UnityEngine.Random.Range(0, 1 << (1 + props));
        current.dyeLevel = UnityEngine.Random.Range(0, catalog.DyeLevelCount);
        current.sheet = UnityEngine.Random.value < 0.25f;
        Apply();
    }

    void SetKind(VisitorKind kind)
    {
        current.kind = kind;
        current.mask &= (1 << (1 + catalog.PropCount(kind))) - 1;
        Apply();
    }

    void SetBit(int bit, bool on)
    {
        if (on) current.mask |= 1 << bit;
        else current.mask &= ~(1 << bit);
        Apply();
    }

    void SetDye(int level)
    {
        current.dyeLevel = level;
        current.mask |= 1;
        Apply();
    }

    void Apply(bool rebuild = false)
    {
        SyncControls();
        if (rebuild || stage.Count == 0) stage.Show(new List<Outfit> { current }, ModelViewportX);
        else stage.SetOutfit(0, current);

        descriptionText.text = OutfitCatalog.KindName(current.kind) + "\n" + catalog.Describe(current)
            + "\n<size=22>" + Loc.T("drag.rotate") + "</size>";
    }

    void SyncControls()
    {
        for (int k = 0; k < 2; k++) speciesToggles[k].Set(k == (int)current.kind);
        furToggle.Set((current.mask & 1) != 0);
        for (int d = 0; d < dyeToggles.Length; d++) dyeToggles[d].Set((current.mask & 1) != 0 && d == current.dyeLevel);

        int props = catalog.PropCount(current.kind);
        for (int p = 0; p < propToggles.Length; p++)
        {
            bool exists = p < props;
            propToggles[p].gameObject.SetActive(exists);
            if (!exists) continue;
            propToggles[p].Text = catalog.PropLabel(current.kind, p);
            propToggles[p].Set((current.mask & (1 << (p + 1))) != 0);
        }
        sheetToggle.Set(current.sheet);
        for (int k = 0; k < 2; k++) speciesToggles[k].Text = OutfitCatalog.KindName((VisitorKind)k);
    }
}
