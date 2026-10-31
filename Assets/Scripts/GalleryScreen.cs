using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Every look the visitors can wear, per species. Looks the player has met are listed by name and can be
// viewed (and turned with the mouse); the rest show as ???. The list is paged when it does not fit the
// screen height (21:9, phones in landscape).
public class GalleryScreen
{
    public event Action Back;

    const float ListWidth = 400f;
    const float ModelViewportX = 0.66f;
    const float RowTop = 150f;
    const float RowStep = 44f;
    const float RowHeight = 38f;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly RectTransform screen;
    readonly GameObject panel;
    readonly Text countText;
    readonly Text nameText;
    readonly Text pageText;
    readonly SimpleButton[] tabs = new SimpleButton[2];
    readonly SimpleButton prevButton;
    readonly SimpleButton nextButton;
    readonly List<SimpleButton> rows = new List<SimpleButton>();

    VisitorKind kind;
    int selected = -1;
    int page;
    int perPage = 1;

    public GalleryScreen(Transform parent, OutfitCatalog catalog, ModelStage stage)
    {
        this.catalog = catalog;
        this.stage = stage;

        var root = UiKit.Fill("GalleryScreen", parent);
        screen = root;
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
            int slot = i;
            var row = UiKit.Button(root, "Row" + i, "", 22, topLeft, topLeft, new Vector2(24f, -RowTop - i * RowStep), new Vector2(352f, RowHeight), () => Select(page * perPage + slot));
            row.Label.alignment = TextAnchor.MiddleLeft;
            row.Label.rectTransform.offsetMin = new Vector2(14f, 0f);
            rows.Add(row);
        }

        var bottomLeft = new Vector2(0f, 0f);
        prevButton = UiKit.Button(root, "Prev", "←", 22, bottomLeft, bottomLeft, new Vector2(24f, 22f), new Vector2(72f, 38f), () => ChangePage(-1));
        pageText = UiKit.Label(root, "Page", "", 22, TextAnchor.MiddleCenter, bottomLeft, bottomLeft, new Vector2(104f, 22f), new Vector2(100f, 38f), UiKit.Dim);
        nextButton = UiKit.Button(root, "Next", "→", 22, bottomLeft, bottomLeft, new Vector2(212f, 22f), new Vector2(72f, 38f), () => ChangePage(1));

        var bottomRight = new Vector2(1f, 0f);
        var back = UiKit.Button(root, "Back", "", 33, bottomRight, bottomRight, new Vector2(-UiKit.MarginX, UiKit.MarginY), new Vector2(200f, 56f), () => Back?.Invoke());
        UiKit.Bind(back.Label, "back");
        nameText = UiKit.Label(root, "Name", "", 33, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(ListWidth + (1280f - ListWidth) * 0.5f, 104f), new Vector2(780f, 90f), Color.white);

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
        page = 0;
        selected = -1;
        Layout();

        int first = -1;
        for (int i = 0; i < EntryTotal() && first < 0; i++)
        {
            if (catalog.IsUnlocked(kind, i)) first = i;
        }
        if (first >= 0)
        {
            page = first / perPage;
            selected = first;
            Layout();
        }
        ShowModel();
    }

    int EntryTotal()
    {
        int count = 0;
        while (catalog.EntryValid(kind, count)) count++;
        return count;
    }

    // Rows fill the list height; with more entries than fit, the list is split into pages.
    void Layout()
    {
        countText.text = Loc.T("gallery.found", catalog.UnlockedCount, catalog.TotalCount);
        for (int k = 0; k < 2; k++)
        {
            tabs[k].Text = OutfitCatalog.KindName((VisitorKind)k);
            tabs[k].Highlighted = k == (int)kind;
        }

        int total = EntryTotal();
        float height = screen.rect.height;
        int fit = Mathf.Max(1, Mathf.FloorToInt((height - RowTop - 24f) / RowStep));
        bool paged = total > fit;
        perPage = paged ? Mathf.Max(3, Mathf.FloorToInt((height - RowTop - 84f) / RowStep)) : Mathf.Max(1, total);
        int pages = Mathf.Max(1, Mathf.CeilToInt(total / (float)perPage));
        page = Mathf.Clamp(page, 0, pages - 1);

        prevButton.gameObject.SetActive(paged);
        nextButton.gameObject.SetActive(paged);
        pageText.gameObject.SetActive(paged);
        prevButton.Interactable = page > 0;
        nextButton.Interactable = page < pages - 1;
        pageText.text = (page + 1) + " / " + pages;

        for (int slot = 0; slot < rows.Count; slot++)
        {
            int entry = page * perPage + slot;
            bool shown = slot < perPage && catalog.EntryValid(kind, entry);
            rows[slot].gameObject.SetActive(shown);
            if (!shown) continue;

            bool open = catalog.IsUnlocked(kind, entry);
            rows[slot].Text = catalog.EntryNumber(entry) + "  " + (open ? catalog.EntryLabel(kind, entry) : "???");
            rows[slot].Interactable = open;
            rows[slot].Highlighted = entry == selected;
        }
    }

    void ChangePage(int delta)
    {
        page += delta;
        Layout();
    }

    void Select(int entry)
    {
        selected = entry;
        Layout();
        ShowModel();
    }

    void ShowModel()
    {
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
