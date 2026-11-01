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
    readonly SimpleButton[] tabs = new SimpleButton[3]; // Kunmungchi, Omangi, special guests
    readonly GameObject confirm;
    readonly SimpleButton prevButton;
    readonly SimpleButton nextButton;
    readonly List<SimpleButton> rows = new List<SimpleButton>();

    VisitorKind kind;
    bool special; // the special-guest tab
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

        for (int k = 0; k < tabs.Length; k++)
        {
            int tabIndex = k;
            tabs[k] = UiKit.Button(root, "Tab" + k, k < 2 ? OutfitCatalog.KindName((VisitorKind)k) : "", 22, topLeft, topLeft,
                new Vector2(24f + k * 120f, -100f), new Vector2(112f, 38f), () => SetTab(tabIndex));
        }
        UiKit.Bind(tabs[2].Label, "gallery.special");

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
        // Top right, away from the model in the middle of the area.
        var topRight = new Vector2(1f, 1f);
        var viewReset = UiKit.Button(root, "ViewReset", "", 22, topRight, topRight, new Vector2(-UiKit.MarginX, -UiKit.MarginY), new Vector2(200f, 44f), stage.ResetView);
        UiKit.Bind(viewReset.Label, "view.reset");
        // Above the Back button; asks first because it cannot be undone.
        var unlock = UiKit.Button(root, "UnlockAll", "", 22, bottomRight, bottomRight, new Vector2(-UiKit.MarginX, UiKit.MarginY + 68f), new Vector2(200f, 44f), OpenConfirm);
        UiKit.Bind(unlock.Label, "gallery.unlockAll");
        confirm = BuildConfirm(root);
        // A low strip at the bottom edge, left of the Back button.
        nameText = UiKit.Label(root, "Name", "", 33, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(750f, 14f), new Vector2(520f, 76f), Color.white);

        panel.SetActive(false);
    }

    GameObject BuildConfirm(RectTransform root)
    {
        var center = new Vector2(0.5f, 0.5f);
        var modal = UiKit.Fill("ConfirmUnlock", root);
        modal.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.01f, 0.06f, 0.75f);
        var card = UiKit.Card("Card", modal, new Vector2(620f, 260f));
        card.gameObject.AddComponent<ScreenFit>().Init(modal, card, 260f, 24f);
        UiKit.Bind(UiKit.Label(card, "Text", "", 33, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 40f), new Vector2(560f, 110f), Color.white), "gallery.unlockAsk");
        var yes = UiKit.Button(card, "Yes", "", 33, center, center, new Vector2(-130f, -80f), new Vector2(230f, 52f), () =>
        {
            CloseConfirm();
            catalog.UnlockAll();
            SetTab(special ? 2 : (int)kind);
        });
        UiKit.Bind(yes.Label, "gallery.unlockYes");
        var no = UiKit.Button(card, "No", "", 33, center, center, new Vector2(130f, -80f), new Vector2(230f, 52f), CloseConfirm);
        UiKit.Bind(no.Label, "gallery.unlockNo");
        modal.gameObject.SetActive(false);
        return modal.gameObject;
    }

    void OpenConfirm()
    {
        confirm.SetActive(true);
        confirm.transform.SetAsLastSibling();
        UiKit.ModalRoot = confirm.transform;
    }

    void CloseConfirm()
    {
        confirm.SetActive(false);
        if (UiKit.ModalRoot == confirm.transform) UiKit.ModalRoot = null;
    }

    public void Show()
    {
        panel.SetActive(true);
        SetTab(special ? 2 : (int)kind);
    }

    public void Hide()
    {
        CloseConfirm();
        panel.SetActive(false);
    }

    bool Valid(int entry) => special ? entry < SpecialGuest.Count : catalog.EntryValid(kind, entry);

    bool Unlocked(int entry) => special ? SpecialGuest.Seen(entry) : catalog.IsUnlocked(kind, entry);

    void SetTab(int tab)
    {
        special = tab == 2;
        if (!special) kind = (VisitorKind)tab;
        page = 0;
        selected = -1;
        Layout();

        int first = -1;
        for (int i = 0; i < EntryTotal() && first < 0; i++)
        {
            if (Unlocked(i)) first = i;
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
        while (Valid(count)) count++;
        return count;
    }

    // Rows fill the list height; with more entries than fit, the list is split into pages.
    void Layout()
    {
        countText.text = Loc.T("gallery.found", catalog.UnlockedCount, catalog.TotalCount);
        for (int k = 0; k < 2; k++)
        {
            tabs[k].Text = OutfitCatalog.KindName((VisitorKind)k);
            tabs[k].Highlighted = !special && k == (int)kind;
        }
        tabs[2].Highlighted = special;

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
            bool shown = slot < perPage && Valid(entry);
            rows[slot].gameObject.SetActive(shown);
            if (!shown) continue;

            bool open = Unlocked(entry);
            string number = special ? "S" + (entry + 1) : catalog.EntryNumber(entry).ToString();
            string label = special ? SpecialGuest.Names[entry] : catalog.EntryLabel(kind, entry);
            rows[slot].Text = number + "  " + (open ? label : "???");
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

        if (special)
        {
            stage.ShowGuest(selected, ModelViewportX);
            nameText.text = Loc.T("gallery.special") + " " + SpecialGuest.Names[selected] + "\n<size=22>" + Loc.T(GameInput.UsingTouch ? "drag.rotateTouch" : "drag.rotate") + "</size>";
            return;
        }

        var outfit = catalog.EntryOutfit(kind, selected);
        stage.Show(new List<Outfit> { outfit }, ModelViewportX);
        nameText.text = OutfitCatalog.KindName(kind) + " " + catalog.EntryLabel(kind, selected) + "\n<size=22>" + Loc.T(GameInput.UsingTouch ? "drag.rotateTouch" : "drag.rotate") + "</size>";
    }
}
