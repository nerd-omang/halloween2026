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
            if (looks[i].sheet) look += Loc.T(looks[i].pumpkin ? "review.pumpkin" : "review.sheet");
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
    const float DesignHeight = 700f;
    const int ShareBatch = 5; // the server takes 5 results per minute from one address

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
    readonly Text statusText;
    readonly SimpleButton localTab;
    readonly SimpleButton onlineTab;
    readonly SimpleButton retryButton;
    readonly SimpleButton clearButton;
    readonly SimpleButton shareButton; // sends the saved local records to the world ranking (records tab only)
    bool shareBusy;
    string shareNote;
    readonly GameObject confirm;
    readonly List<GameObject> captions = new List<GameObject>();

    int page;
    int onlinePage;
    int selected; // 1-based rank, 0 = none
    bool online;  // showing the world ranking
    int onlineSelected; // 1-based rank picked in the world ranking, 0 = none (separate from the local pick)
    readonly Text noLooksText;

    // headerKey: the Loc key of the panel title.
    // canClear: offer "clear ranking" (not on the game-over screen, where it could wipe the game just recorded).
    public RankingView(Transform parentTransform, OutfitCatalog catalog, ModelStage stage, string headerKey, bool canClear = false)
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
            var row = UiKit.Button(content, "Row" + i, "", 22, topLeft, topLeft, new Vector2(24f, -RowTop - i * RowStep), new Vector2(PanelWidth - 48f, RowHeight), () => Select((online ? onlinePage : page) * LocalRanking.PageSize + slot + 1));
            var left = new Vector2(0f, 0.5f);
            rankTexts[i] = UiKit.Label(row.transform, "Rank", "", 22, TextAnchor.MiddleLeft, left, left, new Vector2(12f, 0f), new Vector2(40f, RowHeight), UiKit.Dim);
            initialsTexts[i] = UiKit.Label(row.transform, "Initials", "", 22, TextAnchor.MiddleLeft, left, left, new Vector2(54f, 0f), new Vector2(60f, RowHeight), Color.white);
            scoreTexts[i] = UiKit.Label(row.transform, "Score", "", 22, TextAnchor.MiddleRight, left, left, new Vector2(112f, 0f), new Vector2(82f, RowHeight), UiKit.Accent);
            dateTexts[i] = UiKit.Label(row.transform, "Date", "", 22, TextAnchor.MiddleRight, left, left, new Vector2(200f, 0f), new Vector2(92f, RowHeight), UiKit.Dim);
            rows[i] = row;
        }

        emptyText = UiKit.Label(content, "Empty", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -RowTop), new Vector2(PanelWidth - 48f, 120f), UiKit.Dim);
        UiKit.Bind(emptyText, "records.empty");

        // LOCAL / ONLINE switch under the header.
        localTab = UiKit.Button(content, "TabLocal", "", 22, topLeft, topLeft, new Vector2(24f, -72f), new Vector2(160f, 34f), () => SetOnline(false));
        UiKit.Bind(localTab.Label, "ranking.tabLocal");
        onlineTab = UiKit.Button(content, "TabOnline", "", 22, topLeft, topLeft, new Vector2(192f, -72f), new Vector2(160f, 34f), () => SetOnline(true));
        UiKit.Bind(onlineTab.Label, "ranking.tabOnline");
        statusText = UiKit.Label(content, "Status", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -RowTop), new Vector2(PanelWidth - 48f, 120f), UiKit.Dim);
        noLooksText = UiKit.Label(root, "NoLooks", "", 33, TextAnchor.MiddleCenter, new Vector2(StageViewportX, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 60f), UiKit.Dim);
        UiKit.Bind(noLooksText, "ranking.noLooks");
        noLooksText.gameObject.SetActive(false);
        retryButton = UiKit.Button(content, "Retry", "", 22, topLeft, topLeft, new Vector2(24f, -RowTop - LocalRanking.PageSize * RowStep - 10f), new Vector2(200f, 38f), () => WorldBoard.Refresh(true));
        UiKit.Bind(retryButton.Label, "ranking.retry");

        // Below the page arrows; separate from the stats reset, and only for the local list.
        if (canClear)
        {
            clearButton = UiKit.Button(content, "Clear", "", 22, topLeft, topLeft, new Vector2(24f, -RowTop - LocalRanking.PageSize * RowStep - 56f), new Vector2(240f, 38f), OpenConfirm);
            UiKit.Bind(clearButton.Label, "ranking.clear");
            clearButton.Label.color = UiKit.Danger;
            confirm = BuildConfirm();

            if (WorldBoard.Configured)
            {
                shareButton = UiKit.Button(content, "Share", "", 20, topLeft, topLeft, new Vector2(24f, -RowTop - LocalRanking.PageSize * RowStep - 102f), new Vector2(PanelWidth - 48f, 38f), ShareRecords);
            }
        }

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
    // It always opens on the local list.
    public void Show(int highlightRank)
    {
        root.gameObject.SetActive(true);
        root.SetAsFirstSibling();
        selected = highlightRank > 0 ? highlightRank : (LocalRanking.Entries.Count > 0 ? 1 : 0);
        page = selected > 0 ? (selected - 1) / LocalRanking.PageSize : 0;
        WorldBoard.Changed -= OnWorldChanged;
        WorldBoard.Changed += OnWorldChanged;
        shareNote = null;
        WorldBoard.Refresh(); // the share button needs the ranking to know which records reach it
        SetOnline(false);
    }

    public void Hide()
    {
        WorldBoard.Changed -= OnWorldChanged;
        noLooksText.gameObject.SetActive(false);
        CloseConfirm();
        root.gameObject.SetActive(false);
        LookCaptions.Clear(captions);
    }

    GameObject BuildConfirm()
    {
        var center = new Vector2(0.5f, 0.5f);
        var modal = UiKit.Fill("ConfirmClear", root);
        modal.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.01f, 0.06f, 0.75f);
        var card = UiKit.Card("Card", modal, new Vector2(660f, 280f));
        card.gameObject.AddComponent<ScreenFit>().Init(modal, card, 280f, 24f);
        UiKit.Bind(UiKit.Label(card, "Text", "", 33, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 45f), new Vector2(600f, 130f), Color.white), "ranking.clearAsk");
        var yes = UiKit.Button(card, "Yes", "", 33, center, center, new Vector2(-140f, -90f), new Vector2(250f, 52f), () =>
        {
            CloseConfirm();
            LocalRanking.Clear();
            selected = 0;
            page = 0;
            Layout();
            RefreshShare();
            ShowSelected();
        });
        UiKit.Bind(yes.Label, "settings.resetYes");
        yes.Label.color = UiKit.Danger;
        var no = UiKit.Button(card, "No", "", 33, center, center, new Vector2(140f, -90f), new Vector2(250f, 52f), CloseConfirm);
        UiKit.Bind(no.Label, "settings.resetNo");
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
        if (confirm == null) return;
        confirm.SetActive(false);
        if (UiKit.ModalRoot == confirm.transform) UiKit.ModalRoot = null;
    }

    void SetOnline(bool value)
    {
        online = value;
        localTab.Highlighted = !online;
        onlineTab.Highlighted = online;
        if (online)
        {
            WorldBoard.Refresh();
            RefreshShare();
            LayoutOnline();
            return;
        }
        statusText.gameObject.SetActive(false);
        retryButton.gameObject.SetActive(false);
        Layout();
        RefreshShare();
        ShowSelected();
    }

    void OnWorldChanged()
    {
        RefreshShare();
        if (online) LayoutOnline();
    }

    // Sends the next few saved local records to the world ranking; press again for the rest. Records that would
    // not make the world ranking only add to the statistics: they are skipped and counted as done.
    void ShareRecords()
    {
        if (shareBusy) return;
        if (WorldBoard.Status != WorldBoard.LoadStatus.Ready)
        {
            WorldBoard.Refresh(true);
            RefreshShare();
            return;
        }
        var skipped = new List<string>();
        var pending = LocalRanking.PendingShare(WorldBoard.Reaches, skipped);
        foreach (string text in skipped) LocalRanking.MarkShared(text);
        if (pending.Count == 0)
        {
            RefreshShare();
            return;
        }
        if (pending.Count > ShareBatch) pending.RemoveRange(ShareBatch, pending.Count - ShareBatch);
        shareBusy = true;
        shareNote = null;
        RefreshShare();
        WorldBoard.SubmitMany(pending, LocalRanking.MarkShared, (ok, failed) =>
        {
            shareBusy = false;
            shareNote = Loc.T("settings.shareLocalDone", ok, failed);
            WorldBoard.Refresh(true);
            RefreshShare();
        });
    }

    void RefreshShare()
    {
        if (shareButton == null) return;
        shareButton.gameObject.SetActive(!online);
        var status = WorldBoard.Status;
        bool ready = status == WorldBoard.LoadStatus.Ready;
        int pending = ready ? LocalRanking.PendingShare(WorldBoard.Reaches).Count : 0;
        shareButton.Interactable = !shareBusy && (pending > 0 || status == WorldBoard.LoadStatus.Failed);
        if (shareBusy) shareButton.Text = Loc.T("settings.shareLocalBusy");
        else if (status == WorldBoard.LoadStatus.Failed) shareButton.Text = Loc.T("settings.shareLocalRetry");
        else if (!ready) shareButton.Text = Loc.T("settings.shareLocalWait");
        else shareButton.Text = shareNote ?? (pending > 0 ? Loc.T("settings.shareLocal", pending) : Loc.T("settings.shareLocalNone"));
    }

    void LayoutOnline()
    {
        var entries = WorldBoard.Entries;
        emptyText.gameObject.SetActive(false);
        if (clearButton != null) clearButton.gameObject.SetActive(false);
        int pages = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)LocalRanking.PageSize));
        onlinePage = Mathf.Clamp(onlinePage, 0, pages - 1);
        for (int i = 0; i < rows.Length; i++)
        {
            int index = onlinePage * LocalRanking.PageSize + i;
            bool shown = index < entries.Count;
            rows[i].gameObject.SetActive(shown);
            if (!shown) continue;

            rankTexts[i].text = (index + 1).ToString();
            initialsTexts[i].text = entries[index].initials;
            scoreTexts[i].text = entries[index].score.ToString();
            dateTexts[i].gameObject.SetActive(false);
            rows[i].Highlighted = index + 1 == onlineSelected;
        }

        bool paged = entries.Count > LocalRanking.PageSize;
        prevButton.gameObject.SetActive(paged);
        nextButton.gameObject.SetActive(paged);
        pageText.gameObject.SetActive(paged);
        prevButton.Interactable = onlinePage > 0;
        nextButton.Interactable = onlinePage < pages - 1;
        pageText.text = Loc.T("records.page", onlinePage + 1, pages);

        var status = WorldBoard.Status;
        statusText.gameObject.SetActive(entries.Count == 0);
        retryButton.gameObject.SetActive(status == WorldBoard.LoadStatus.Failed);
        string key = "online.empty";
        if (status == WorldBoard.LoadStatus.NotConfigured) key = "online.notConfigured";
        else if (status == WorldBoard.LoadStatus.Loading) key = "online.loading";
        else if (status == WorldBoard.LoadStatus.Failed) key = "online.failed";
        statusText.text = Loc.T(key);

        if (onlineSelected > entries.Count) onlineSelected = 0;
        if (onlineSelected == 0 && entries.Count > 0)
        {
            onlineSelected = 1;
            onlinePage = 0;
            rows[0].Highlighted = true;
        }
        ShowSelected();
    }

    void Layout()
    {
        var entries = LocalRanking.Page(page);
        int pages = Mathf.Max(1, Mathf.CeilToInt(LocalRanking.Entries.Count / (float)LocalRanking.PageSize));
        page = Mathf.Clamp(page, 0, pages - 1);

        emptyText.gameObject.SetActive(LocalRanking.Entries.Count == 0);
        if (clearButton != null) clearButton.gameObject.SetActive(LocalRanking.Entries.Count > 0);
        for (int i = 0; i < rows.Length; i++)
        {
            bool shown = i < entries.Count;
            rows[i].gameObject.SetActive(shown);
            if (!shown) continue;

            int rank = page * LocalRanking.PageSize + i + 1;
            rankTexts[i].text = rank.ToString();
            initialsTexts[i].text = entries[i].initials;
            scoreTexts[i].text = entries[i].score.ToString();
            dateTexts[i].gameObject.SetActive(true);
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
        if (online)
        {
            int onlinePages = Mathf.Max(1, Mathf.CeilToInt(WorldBoard.Entries.Count / (float)LocalRanking.PageSize));
            int nextOnline = Mathf.Clamp(onlinePage + delta, 0, onlinePages - 1);
            if (nextOnline == onlinePage) return;
            onlinePage = nextOnline;
            LayoutOnline();
            return;
        }
        int pages = Mathf.Max(1, Mathf.CeilToInt(LocalRanking.Entries.Count / (float)LocalRanking.PageSize));
        int next = Mathf.Clamp(page + delta, 0, pages - 1);
        if (next == page) return;
        page = next;
        Layout();
    }

    void Select(int rank)
    {
        if (online)
        {
            onlineSelected = rank;
            LayoutOnline();
            return;
        }
        selected = rank;
        Layout();
        ShowSelected();
    }

    // The three looks of the picked game, local or world alike; a game without recorded looks says so.
    void ShowSelected()
    {
        LookCaptions.Clear(captions);
        noLooksText.gameObject.SetActive(false);

        Outfit[] looks = null;
        bool picked;
        bool found = false;
        if (online)
        {
            var entries = WorldBoard.Entries;
            picked = onlineSelected >= 1 && onlineSelected <= entries.Count;
            if (picked) found = entries[onlineSelected - 1].TryGetLooks(out looks);
        }
        else
        {
            var entries = LocalRanking.Entries;
            picked = selected >= 1 && selected <= entries.Count;
            if (picked) found = entries[selected - 1].TryGetLooks(out looks);
        }

        if (!picked || !found || looks == null || looks.Length == 0)
        {
            stage.Show(new List<Outfit>(), StageViewportX);
            noLooksText.gameObject.SetActive(picked);
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
