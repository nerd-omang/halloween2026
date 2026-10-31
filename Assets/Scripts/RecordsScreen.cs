using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Records from the title menu: the local ranking with each game's looks, the player's statistics, and a
// placeholder for the world ranking.
public class RecordsScreen
{
    const int TopFooledCount = 5;
    const float BarMaxWidth = 280f;
    const float ResetConfirmSeconds = 3f;

    public event Action Back;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly RectTransform root;
    readonly GameObject panel;
    readonly RankingView ranking;
    readonly GameObject statsPage;
    readonly GameObject worldPage;
    readonly GameObject header;
    readonly SimpleButton[] tabs = new SimpleButton[3];
    readonly Text[] statLines = new Text[5];
    readonly Text noFooledText;
    readonly Text[] fooledLabels = new Text[TopFooledCount];
    readonly Image[] fooledBars = new Image[TopFooledCount];
    readonly Text[] fooledCounts = new Text[TopFooledCount];
    readonly SimpleButton resetButton;

    float resetArmedUntil;

    public RecordsScreen(Transform parent, OutfitCatalog catalog, ModelStage stage)
    {
        this.catalog = catalog;
        this.stage = stage;

        root = UiKit.Fill("RecordsScreen", parent);
        panel = root.gameObject;
        root.gameObject.AddComponent<Ticker>().OnUpdate = Tick;

        // Tabs at the top right.
        var topRight = new Vector2(1f, 1f);
        string[] tabKeys = { "records.tabMine", "records.tabStats", "records.tabWorld" };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            tabs[i] = UiKit.Button(root, "Tab" + i, "", 22, topRight, topRight,
                new Vector2(-UiKit.MarginX - (2 - i) * 158f, -UiKit.MarginY), new Vector2(150f, 44f), () => SetTab(index));
            UiKit.Bind(tabs[i].Label, tabKeys[i]);
        }

        var topLeft = new Vector2(0f, 1f);
        header = UiKit.Bind(UiKit.Label(root, "Header", "", 55, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(24f, -12f), new Vector2(500f, 60f), UiKit.Accent, true),
            "records.header", true).gameObject;

        ranking = new RankingView(root, catalog, stage, "records.header");

        statsPage = BuildStats(out statLines, out noFooledText, out fooledLabels, out fooledBars, out fooledCounts, out resetButton);
        worldPage = BuildWorld();

        var bottomRight = new Vector2(1f, 0f);
        var back = UiKit.Button(root, "Back", "", 33, bottomRight, bottomRight, new Vector2(-UiKit.MarginX, UiKit.MarginY), new Vector2(200f, 56f), () => Back?.Invoke());
        UiKit.Bind(back.Label, "back");

        panel.SetActive(false);
    }

    GameObject BuildStats(out Text[] lines, out Text none, out Text[] labels, out Image[] bars, out Text[] counts, out SimpleButton reset)
    {
        var center = new Vector2(0.5f, 0.5f);
        var card = UiKit.Card("StatsCard", root, new Vector2(760f, 540f));
        card.gameObject.AddComponent<ScreenFit>().Init(root, card, 540f, 24f);

        lines = new Text[5];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = UiKit.Label(card, "Line" + i, "", 33, TextAnchor.MiddleLeft, center, center, new Vector2(0f, 215f - i * 40f), new Vector2(680f, 40f), Color.white);
        }

        UiKit.Bind(UiKit.Label(card, "FooledHeader", "", 22, TextAnchor.MiddleLeft, center, center, new Vector2(0f, 8f), new Vector2(680f, 30f), UiKit.Accent), "stats.fooled");
        none = UiKit.Label(card, "None", "", 22, TextAnchor.MiddleLeft, center, center, new Vector2(0f, -36f), new Vector2(680f, 30f), UiKit.Dim);
        UiKit.Bind(none, "stats.none");

        labels = new Text[TopFooledCount];
        bars = new Image[TopFooledCount];
        counts = new Text[TopFooledCount];
        var left = new Vector2(0f, 0.5f);
        for (int i = 0; i < TopFooledCount; i++)
        {
            float y = -36f - i * 32f;
            labels[i] = UiKit.Label(card, "FooledLabel" + i, "", 22, TextAnchor.MiddleLeft, center, left, new Vector2(-340f, y), new Vector2(250f, 28f), Color.white);
            var bar = UiKit.Rect("FooledBar" + i, card, center, left, new Vector2(-80f, y), new Vector2(BarMaxWidth, 18f));
            bars[i] = bar.gameObject.AddComponent<Image>();
            bars[i].color = UiKit.Accent;
            bars[i].raycastTarget = false;
            counts[i] = UiKit.Label(card, "FooledCount" + i, "", 22, TextAnchor.MiddleLeft, center, left, new Vector2(220f, y), new Vector2(120f, 28f), UiKit.Dim);
        }

        reset = UiKit.Button(card, "Reset", "", 22, center, center, new Vector2(0f, -232f), new Vector2(420f, 44f), OnResetClicked);
        UiKit.Bind(reset.Label, () => Loc.T(Time.unscaledTime < resetArmedUntil ? "stats.resetConfirm" : "stats.reset"));
        return card.gameObject;
    }

    GameObject BuildWorld()
    {
        var center = new Vector2(0.5f, 0.5f);
        var card = UiKit.Card("WorldCard", root, new Vector2(760f, 260f));
        UiKit.Bind(UiKit.Label(card, "Text", "", 33, TextAnchor.MiddleCenter, center, center, Vector2.zero, new Vector2(680f, 120f), UiKit.Dim), "records.world");
        return card.gameObject;
    }

    public void Show()
    {
        panel.SetActive(true);
        SetTab(0);
    }

    public void Hide()
    {
        ranking.Hide();
        panel.SetActive(false);
    }

    void SetTab(int index)
    {
        for (int i = 0; i < tabs.Length; i++) tabs[i].Highlighted = i == index;
        statsPage.SetActive(index == 1);
        worldPage.SetActive(index == 2);
        header.SetActive(index != 0);
        if (index == 0)
        {
            ranking.Show(0);
            return;
        }
        ranking.Hide();
        stage.Show(new List<Outfit>());
        if (index == 1) RefreshStats();
    }

    void RefreshStats()
    {
        resetArmedUntil = 0f;
        statLines[0].text = Loc.T("stats.games", LocalStats.Games);
        statLines[1].text = Loc.T("stats.total", LocalStats.TotalScore);
        statLines[2].text = Loc.T("stats.average", LocalStats.AverageScore.ToString("0.0"));
        statLines[3].text = Loc.T("stats.gave", LocalStats.GaveToOmangi);
        statLines[4].text = Loc.T("stats.sent", LocalStats.SentKunmungchi);

        var top = LocalStats.TopFooled(TopFooledCount);
        noFooledText.gameObject.SetActive(top.Count == 0);
        int most = top.Count > 0 ? top[0].Value : 1;
        for (int i = 0; i < TopFooledCount; i++)
        {
            bool shown = i < top.Count;
            fooledLabels[i].gameObject.SetActive(shown);
            fooledBars[i].gameObject.SetActive(shown);
            fooledCounts[i].gameObject.SetActive(shown);
            if (!shown) continue;

            int key = top[i].Key;
            fooledLabels[i].text = key == LocalStats.NaturalKey ? Loc.T("outfit.none") : catalog.ItemLabel(ItemTable.FromBit(key));
            fooledBars[i].rectTransform.sizeDelta = new Vector2(Mathf.Max(8f, BarMaxWidth * top[i].Value / most), 18f);
            fooledCounts[i].text = Loc.T("stats.times", top[i].Value);
        }
    }

    // First press arms the button, a second one within a few seconds really deletes.
    void OnResetClicked()
    {
        if (Time.unscaledTime < resetArmedUntil)
        {
            LocalRanking.Clear();
            LocalStats.Reset();
            RefreshStats();
            return;
        }
        resetArmedUntil = Time.unscaledTime + ResetConfirmSeconds;
        RefreshResetLabel();
    }

    void RefreshResetLabel()
    {
        resetButton.Label.text = Loc.T(Time.unscaledTime < resetArmedUntil ? "stats.resetConfirm" : "stats.reset");
    }

    bool wasArmed;

    void Tick()
    {
        bool armed = Time.unscaledTime < resetArmedUntil;
        if (armed != wasArmed)
        {
            wasArmed = armed;
            RefreshResetLabel();
        }
    }
}
