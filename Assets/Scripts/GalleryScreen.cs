using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Every look the visitors can wear, per species. Looks the player has met are listed by name and can be
// viewed (and turned with the mouse); the rest show as ???.
public class GalleryScreen
{
    public event Action Back;

    const float ListWidth = 400f;
    const float ModelViewportX = 0.66f;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly GameObject panel;
    readonly Text countText;
    readonly Text nameText;
    readonly SimpleButton[] tabs = new SimpleButton[2];
    readonly List<SimpleButton> rows = new List<SimpleButton>();

    VisitorKind kind;
    int selected = -1;

    public GalleryScreen(Transform parent, OutfitCatalog catalog, ModelStage stage)
    {
        this.catalog = catalog;
        this.stage = stage;

        var root = UiKit.Fill("GalleryScreen", parent);
        panel = root.gameObject;
        UiKit.SidePanel("List", root, ListWidth);

        var topLeft = new Vector2(0f, 1f);
        UiKit.Bind(UiKit.Label(root, "Header", "", 55, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -12f), new Vector2(360f, 60f), UiKit.Accent, true), "gallery.header", true);
        countText = UiKit.Label(root, "Count", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -70f), new Vector2(360f, 26f), UiKit.Dim);

        for (int k = 0; k < 2; k++)
        {
            var tabKind = (VisitorKind)k;
            tabs[k] = UiKit.Button(root, "Tab" + k, OutfitCatalog.KindName(tabKind), 22, topLeft, topLeft,
                new Vector2(24f + k * 182f, -100f), new Vector2(172f, 38f), () => SetKind(tabKind));
        }

        for (int i = 0; i < catalog.EntryCount; i++)
        {
            int entry = i;
            var row = UiKit.Button(root, "Row" + i, "", 22, topLeft, topLeft, new Vector2(24f, -150f - i * 32f), new Vector2(352f, 30f), () => Select(entry));
            row.Label.alignment = TextAnchor.MiddleLeft;
            row.Label.rectTransform.offsetMin = new Vector2(10f, 0f);
            rows.Add(row);
        }

        var bottomRight = new Vector2(1f, 0f);
        var back = UiKit.Button(root, "Back", "", 33, bottomRight, bottomRight, new Vector2(-UiKit.MarginX, UiKit.MarginY), new Vector2(200f, 56f), () => Back?.Invoke());
        UiKit.Bind(back.Label, "back");
        nameText = UiKit.Label(root, "Name", "", 33, TextAnchor.UpperCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(ListWidth + (1280f - ListWidth) * 0.5f, 100f), new Vector2(780f, 90f), Color.white);

        panel.SetActive(false);
    }

    public void Show()
    {
        panel.SetActive(true);
        SetKind(kind);
    }

    public void Hide() => panel.SetActive(false);

    void SetKind(VisitorKind newKind)
    {
        kind = newKind;
        countText.text = Loc.T("gallery.found", catalog.UnlockedCount, catalog.TotalCount);
        for (int k = 0; k < 2; k++) tabs[k].Text = OutfitCatalog.KindName((VisitorKind)k);
        for (int k = 0; k < 2; k++) tabs[k].Highlighted = k == (int)kind;

        int firstUnlocked = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            bool valid = catalog.EntryValid(kind, i);
            rows[i].gameObject.SetActive(valid);
            if (!valid) continue;

            bool open = catalog.IsUnlocked(kind, i);
            rows[i].Text = catalog.EntryNumber(i) + "  " + (open ? catalog.EntryLabel(kind, i) : "???");
            rows[i].Interactable = open;
            if (open && firstUnlocked < 0) firstUnlocked = i;
        }
        Select(firstUnlocked);
    }

    void Select(int entry)
    {
        selected = entry;
        for (int i = 0; i < rows.Count; i++) rows[i].Highlighted = i == selected;

        if (selected < 0)
        {
            stage.Show(new List<Outfit>(), ModelViewportX);
            nameText.text = Loc.T("gallery.empty");
            return;
        }

        var outfit = catalog.EntryOutfit(kind, selected);
        stage.Show(new List<Outfit> { outfit }, ModelViewportX);
        nameText.text = OutfitCatalog.KindName(kind) + "\n" + catalog.EntryLabel(kind, selected) + "\n<size=22>" + Loc.T("drag.rotate") + "</size>";
    }
}
