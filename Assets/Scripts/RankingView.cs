using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Captions under the three models of a finished game: what it was, and its look number.
public static class LookCaptions
{
    // actions: what the player did to each ("You gave it candy"), or null to leave that line out.
    public static void Build(Transform parent, ModelStage stage, OutfitCatalog catalog, IList<Outfit> looks, IList<string> actions, List<GameObject> into)
    {
        float width = 430f;
        if (looks.Count > 1) width = Mathf.Clamp(Mathf.Abs(stage.SlotViewportX(1) - stage.SlotViewportX(0)) * 1280f - 12f, 160f, width);

        for (int i = 0; i < looks.Count; i++)
        {
            string was = Loc.T(looks[i].kind == VisitorKind.Kunmungchi ? "review.wasKun" : "review.wasOma");
            string look = Loc.T("review.look", catalog.EntryNumber(looks[i]));
            if (looks[i].sheet) look += Loc.T("review.sheet");
            string text = (actions != null ? "<color=#ffb52e>" + actions[i] + "</color>\n" : "")
                + was + "\n<size=22><color=#c8b8d9>" + look + "</color></size>";

            var anchor = new Vector2(Mathf.Clamp01(stage.SlotViewportX(i)), 0f);
            var label = UiKit.Label(parent, "Caption" + i, text, 33, TextAnchor.UpperCenter, anchor, new Vector2(0.5f, 1f),
                new Vector2(0f, 270f), new Vector2(width, 190f));
            into.Add(label.gameObject);
        }
    }

    public static void Clear(List<GameObject> captions)
    {
        foreach (var caption in captions) UnityEngine.Object.Destroy(caption);
        captions.Clear();
    }
}

// The local top 20 as a list (10 per page) on the left; picking a row shows that game's three looks on the
// model stage. Used by the Records screen and by the game-over screen. Pages turn with the arrow buttons,
// the left/right keys, or a horizontal swipe on the list.
public class RankingView
{
    public const float PanelWidth = 360f;
    const float StageViewportX = 0.64f;
    const float RowTop = 110f;
    const float RowStep = 42f;
    const float RowHeight = 38f;
    const float DesignHeight = 600f;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly Transform parent;
    readonly RectTransform root;
    readonly SimpleButton[] rows = new SimpleButton[LocalRanking.PageSize];
    readonly Text[] rankTexts = new Text[LocalRanking.PageSize];
    readonly Text[] initialsTexts = new Text[LocalRanking.PageSize];
    readonly Text[] scoreTexts = new Text[LocalRanking.PageSize];
    readonly Text[] dateTexts = new Text[LocalRanking.PageSize];
    readonly SimpleButton prevButton;
    readonly SimpleButton nextButton;
    readonly Text pageText;
    readonly Text emptyText;
    readonly List<GameObject> captions = new List<GameObject>();

    int page;
    int selected; // 1-based rank, 0 = none

    // headerKey: the Loc key of the panel title.
    public RankingView(Transform parentTransform, OutfitCatalog catalog, ModelStage stage, string headerKey)
    {
        this.catalog = catalog;
        this.stage = stage;
        parent = parentTransform;

        root = UiKit.Fill("Ranking", parentTransform);
        UiKit.SidePanel("List", root, PanelWidth);

        // The list scales down on short screens instead of running off the bottom.
        var topLeft = new Vector2(0f, 1f);
        var content = UiKit.Rect("Content", root, topLeft, topLeft, Vector2.zero, new Vector2(PanelWidth, DesignHeight));
        content.gameObject.AddComponent<ScreenFit>().Init(root, content, DesignHeight, 0f);

        UiKit.Bind(UiKit.Label(content, "Header", "", 55, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -12f), new Vector2(320f, 60f), UiKit.Accent, true), headerKey, true);

        for (int i = 0; i < rows.Length; i++)
        {
            int slot = i;
            var row = UiKit.Button(content, "Row" + i, "", 22, topLeft, topLeft, new Vector2(24f, -RowTop - i * RowStep), new Vector2(PanelWidth - 48f, RowHeight), () => Select(page * LocalRanking.PageSize + slot + 1));
            var left = new Vector2(0f, 0.5f);
            rankTexts[i] = UiKit.Label(row.transform, "Rank", "", 22, TextAnchor.MiddleLeft, left, left, new Vector2(12f, 0f), new Vector2(40f, RowHeight), UiKit.Dim);
            initialsTexts[i] = UiKit.Label(row.transform, "Initials", "", 22, TextAnchor.MiddleLeft, left, left, new Vector2(54f, 0f), new Vector2(60f, RowHeight), Color.white);
            scoreTexts[i] = UiKit.Label(row.transform, "Score", "", 22, TextAnchor.MiddleRight, left, left, new Vector2(112f, 0f), new Vector2(82f, RowHeight), UiKit.Accent);
            dateTexts[i] = UiKit.Label(row.transform, "Date", "", 22, TextAnchor.MiddleRight, left, left, new Vector2(200f, 0f), new Vector2(92f, RowHeight), UiKit.Dim);
            rows[i] = row;
        }

        emptyText = UiKit.Label(content, "Empty", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -RowTop), new Vector2(PanelWidth - 48f, 120f), UiKit.Dim);
        UiKit.Bind(emptyText, "records.empty");

        var bottomLeft = new Vector2(0f, 1f);
        float navY = -RowTop - LocalRanking.PageSize * RowStep - 10f;
        prevButton = UiKit.Button(content, "Prev", "←", 22, bottomLeft, bottomLeft, new Vector2(24f, navY), new Vector2(72f, 38f), () => ChangePage(-1));
        pageText = UiKit.Label(content, "Page", "", 22, TextAnchor.MiddleCenter, bottomLeft, bottomLeft, new Vector2(104f, navY), new Vector2(100f, 38f), UiKit.Dim);
        nextButton = UiKit.Button(content, "Next", "→", 22, bottomLeft, bottomLeft, new Vector2(212f, navY), new Vector2(72f, 38f), () => ChangePage(1));

        root.gameObject.AddComponent<Ticker>().OnUpdate = ReadInput;
        root.gameObject.SetActive(false);
    }

    public bool IsActive => root.gameObject.activeSelf;

    // Shows the list. highlightRank > 0 opens its page and selects it; otherwise the best game is selected.
    public void Show(int highlightRank)
    {
        root.gameObject.SetActive(true);
        root.SetAsFirstSibling();
        selected = highlightRank > 0 ? highlightRank : (LocalRanking.Entries.Count > 0 ? 1 : 0);
        page = selected > 0 ? (selected - 1) / LocalRanking.PageSize : 0;
        Layout();
        ShowSelected();
    }

    public void Hide()
    {
        root.gameObject.SetActive(false);
        LookCaptions.Clear(captions);
    }

    void Layout()
    {
        var entries = LocalRanking.Page(page);
        int pages = Mathf.Max(1, Mathf.CeilToInt(LocalRanking.Entries.Count / (float)LocalRanking.PageSize));
        page = Mathf.Clamp(page, 0, pages - 1);

        emptyText.gameObject.SetActive(LocalRanking.Entries.Count == 0);
        for (int i = 0; i < rows.Length; i++)
        {
            bool shown = i < entries.Count;
            rows[i].gameObject.SetActive(shown);
            if (!shown) continue;

            int rank = page * LocalRanking.PageSize + i + 1;
            rankTexts[i].text = rank.ToString();
            initialsTexts[i].text = entries[i].initials;
            scoreTexts[i].text = entries[i].score.ToString();
            dateTexts[i].text = entries[i].Date.ToString("MM-dd");
            rows[i].Highlighted = rank == selected;
        }

        bool paged = LocalRanking.Entries.Count > LocalRanking.PageSize;
        prevButton.gameObject.SetActive(paged);
        nextButton.gameObject.SetActive(paged);
        pageText.gameObject.SetActive(paged);
        prevButton.Interactable = page > 0;
        nextButton.Interactable = page < pages - 1;
        pageText.text = Loc.T("records.page", page + 1, pages);
    }

    void ChangePage(int delta)
    {
        int pages = Mathf.Max(1, Mathf.CeilToInt(LocalRanking.Entries.Count / (float)LocalRanking.PageSize));
        int next = Mathf.Clamp(page + delta, 0, pages - 1);
        if (next == page) return;
        page = next;
        Layout();
    }

    void Select(int rank)
    {
        selected = rank;
        Layout();
        ShowSelected();
    }

    void ShowSelected()
    {
        LookCaptions.Clear(captions);
        var entries = LocalRanking.Entries;
        if (selected < 1 || selected > entries.Count)
        {
            stage.Show(new List<Outfit>(), StageViewportX);
            return;
        }

        if (!entries[selected - 1].TryGetLooks(out var looks) || looks.Length == 0)
        {
            stage.Show(new List<Outfit>(), StageViewportX);
            return;
        }

        // A model hidden under a ghost sheet is shown without the sheet, as on the game-over screen.
        var shown = new List<Outfit>();
        foreach (var look in looks)
        {
            var outfit = look;
            outfit.sheet = false;
            shown.Add(outfit);
        }
        stage.Show(shown, StageViewportX);
        LookCaptions.Build(root, stage, catalog, looks, null, captions);
    }

    // Left/right keys, or a horizontal swipe that starts on the list.
    bool swiping;
    Vector2 swipeStart;

    void ReadInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame) ChangePage(-1);
            if (keyboard.rightArrowKey.wasPressedThisFrame) ChangePage(1);
        }

        var touch = Touchscreen.current;
        if (touch == null) return;
        var primary = touch.primaryTouch;
        Vector2 position = primary.position.ReadValue();
        if (primary.press.wasPressedThisFrame)
        {
            swipeStart = position;
            swiping = position.x < Screen.width * PanelWidth / 1280f && UiKit.ModalRoot == null;
        }
        else if (swiping && primary.press.wasReleasedThisFrame)
        {
            swiping = false;
            Vector2 move = position - swipeStart;
            if (Mathf.Abs(move.x) > Screen.width * 0.06f && Mathf.Abs(move.x) > Mathf.Abs(move.y) * 1.3f) ChangePage(move.x < 0f ? 1 : -1);
        }
    }
}
