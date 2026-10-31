using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// After game over, in steps:
//   Score    the score; a new personal best is called out; if the game made the local top 20, record it or skip.
//   Initials (only after RECORD) three letters for the record.
//   Looks    the looks behind each lost heart, shown as turnable models with what the player did and who it
//            really was. A model hidden under a ghost sheet is shown without the sheet.
//   Ranking  the same screen switched to the local ranking; picking a game shows its looks.
public class ReviewScreen
{
    enum Mode
    {
        Score,
        Initials,
        Looks,
        Ranking,
    }

    public event Action PlayAgain;
    public event Action Title;

    // Records the finished game under these initials and returns its local rank (0 = it was not recorded).
    public Func<string, int> Commit;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly GameObject panel;
    readonly Text scoreText;
    readonly GameObject looksGroup; // header, score line, hint
    readonly GameObject scoreCard;
    readonly Text bigScore;
    readonly Text newBestText;
    readonly Text bestText;
    readonly Text rankText;
    readonly Text questionText;
    readonly SimpleButton recordButton;
    readonly SimpleButton skipButton;
    readonly SimpleButton nextButton;
    readonly SimpleButton tabLooks;
    readonly SimpleButton tabRanking;
    readonly GameObject bottomButtons;
    readonly InitialsEntry initialsEntry;
    readonly RankingView ranking;
    readonly List<GameObject> captions = new List<GameObject>();

    Mode mode;
    IReadOnlyList<MistakeRecord> mistakes;
    List<Outfit> shownOutfits = new List<Outfit>();
    int score;
    int best;
    int prospectiveRank;
    int recordedRank;

    // The score and initials steps use keys of their own; the game must not treat them as shortcuts.
    public bool Busy => mode == Mode.Score || mode == Mode.Initials;

    public ReviewScreen(Transform parent, OutfitCatalog catalog, ModelStage stage)
    {
        this.catalog = catalog;
        this.stage = stage;

        var root = UiKit.Fill("ReviewScreen", parent);
        panel = root.gameObject;

        // Looks step: header, score and hint.
        var looks = UiKit.Fill("Looks", root);
        looksGroup = looks.gameObject;
        var top = new Vector2(0.5f, 1f);
        UiKit.Bind(UiKit.Label(looks, "Header", "", 88, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -16f), new Vector2(900f, 100f), UiKit.Danger, true), "review.header", true);
        scoreText = UiKit.Label(looks, "Score", "", 33, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -116f), new Vector2(900f, 44f), Color.white);
        UiKit.Bind(UiKit.Label(looks, "Hint", "", 22, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -164f), new Vector2(1000f, 30f), UiKit.Dim), "review.hint");

        // Score step: a card over the three models.
        var center = new Vector2(0.5f, 0.5f);
        var card = UiKit.Card("ScoreCard", root, new Vector2(700f, 460f));
        card.gameObject.AddComponent<ScreenFit>().Init(root, card, 460f, 24f);
        scoreCard = card.gameObject;
        UiKit.Bind(UiKit.Label(card, "Header", "", 66, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 180f), new Vector2(640f, 76f), UiKit.Danger, true), "review.header", true);
        bigScore = UiKit.Label(card, "BigScore", "", 140, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 80f), new Vector2(640f, 150f), UiKit.Accent, true);
        newBestText = UiKit.Label(card, "NewBest", "", 55, TextAnchor.MiddleCenter, center, center, new Vector2(0f, -10f), new Vector2(640f, 62f), UiKit.ComboColor(12), true);
        UiKit.Bind(newBestText, "score.newBest", true);
        bestText = UiKit.Label(card, "Best", "", 33, TextAnchor.MiddleCenter, center, center, new Vector2(0f, -56f), new Vector2(640f, 40f), UiKit.Dim);
        rankText = UiKit.Label(card, "Rank", "", 33, TextAnchor.MiddleCenter, center, center, new Vector2(0f, -100f), new Vector2(640f, 40f), Color.white);
        questionText = UiKit.Label(card, "Question", "", 22, TextAnchor.MiddleCenter, center, center, new Vector2(0f, -140f), new Vector2(640f, 32f), UiKit.Dim);
        UiKit.Bind(questionText, "score.question");

        recordButton = UiKit.Button(card, "Record", "", 33, center, center, new Vector2(-120f, -190f), new Vector2(210f, 52f), OpenInitials);
        UiKit.Bind(recordButton.Label, "score.record");
        skipButton = UiKit.Button(card, "Skip", "", 33, center, center, new Vector2(120f, -190f), new Vector2(210f, 52f), () => SetMode(Mode.Looks));
        UiKit.Bind(skipButton.Label, "score.skip");
        nextButton = UiKit.Button(card, "Next", "", 33, center, center, new Vector2(0f, -190f), new Vector2(210f, 52f), () => SetMode(Mode.Looks));
        UiKit.Bind(nextButton.Label, "score.next");

        initialsEntry = new InitialsEntry(root);

        // Looks and ranking steps: tabs at the top right and the two end buttons at the bottom.
        var topRight = new Vector2(1f, 1f);
        tabRanking = UiKit.Button(root, "TabRanking", "", 22, topRight, topRight, new Vector2(-UiKit.MarginX, -UiKit.MarginY), new Vector2(150f, 44f), () => SetMode(Mode.Ranking));
        UiKit.Bind(tabRanking.Label, "review.tabRanking");
        tabLooks = UiKit.Button(root, "TabLooks", "", 22, topRight, topRight, new Vector2(-UiKit.MarginX - 158f, -UiKit.MarginY), new Vector2(150f, 44f), () => SetMode(Mode.Looks));
        UiKit.Bind(tabLooks.Label, "review.tabLooks");

        var buttons = UiKit.Fill("Buttons", root);
        bottomButtons = buttons.gameObject;
        var bottom = new Vector2(0.5f, 0f);
        var again = UiKit.Button(buttons, "PlayAgain", "", 33, bottom, bottom, new Vector2(-140f, 22f), new Vector2(250f, 56f), () => PlayAgain?.Invoke());
        UiKit.Bind(again.Label, "review.playagain");
        var title = UiKit.Button(buttons, "Title", "", 33, bottom, bottom, new Vector2(140f, 22f), new Vector2(250f, 56f), () => Title?.Invoke());
        UiKit.Bind(title.Label, "review.title");

        ranking = new RankingView(root, catalog, stage, "ranking.header");

        panel.SetActive(false);
    }

    // newBest: the score beats the previous personal best. prospectiveRank: the local rank this game would get
    // (0 = it would not make the top 20).
    public void Show(IReadOnlyList<MistakeRecord> mistakes, int score, int best, bool newBest, int prospectiveRank)
    {
        this.mistakes = mistakes;
        this.score = score;
        this.best = best;
        this.prospectiveRank = prospectiveRank;
        recordedRank = 0;

        shownOutfits = new List<Outfit>();
        foreach (var mistake in mistakes)
        {
            var outfit = mistake.outfit;
            outfit.sheet = false;
            shownOutfits.Add(outfit);
        }

        bigScore.text = score.ToString();
        newBestText.gameObject.SetActive(newBest);
        bestText.gameObject.SetActive(!newBest);
        bestText.text = Loc.T("score.best", best);
        bool canRecord = prospectiveRank > 0;
        rankText.text = canRecord ? Loc.T("score.rank", prospectiveRank) : Loc.T("score.notRanked");
        rankText.color = canRecord ? Color.white : UiKit.Dim;
        questionText.gameObject.SetActive(canRecord);
        recordButton.gameObject.SetActive(canRecord);
        skipButton.gameObject.SetActive(canRecord);
        nextButton.gameObject.SetActive(!canRecord);

        panel.SetActive(true);
        stage.Show(shownOutfits);
        SetMode(Mode.Score);
    }

    void SetMode(Mode newMode)
    {
        mode = newMode;
        scoreCard.SetActive(mode == Mode.Score);
        looksGroup.SetActive(mode == Mode.Looks);
        bool after = mode == Mode.Looks || mode == Mode.Ranking;
        tabLooks.gameObject.SetActive(after);
        tabRanking.gameObject.SetActive(after);
        bottomButtons.SetActive(after);
        tabLooks.Highlighted = mode == Mode.Looks;
        tabRanking.Highlighted = mode == Mode.Ranking;

        if (mode == Mode.Ranking)
        {
            LookCaptions.Clear(captions);
            ranking.Show(recordedRank);
            return;
        }

        ranking.Hide();
        if (mode == Mode.Looks) ShowLooks();
        else LookCaptions.Clear(captions);
    }

    void ShowLooks()
    {
        string line = Loc.T("review.score", score, best);
        if (recordedRank > 0) line += "      " + Loc.T("review.localRank", recordedRank);
        scoreText.text = line;

        stage.Show(shownOutfits);
        LookCaptions.Clear(captions);
        var originals = new List<Outfit>();
        var actions = new List<string>();
        foreach (var mistake in mistakes)
        {
            originals.Add(mistake.outfit);
            actions.Add(Loc.T(mistake.action == HandAction.Give ? "review.gave" : "review.sent"));
        }
        LookCaptions.Build(panel.transform, stage, catalog, originals, actions, captions);
    }

    void OpenInitials()
    {
        mode = Mode.Initials;
        scoreCard.SetActive(false);
        initialsEntry.Open(LocalRanking.LastInitials, prospectiveRank, initials =>
        {
            recordedRank = Commit != null ? Commit(initials) : 0;
            SetMode(Mode.Looks);
        }, () => SetMode(Mode.Score));
    }

    public void Hide()
    {
        initialsEntry.Close();
        ranking.Hide();
        LookCaptions.Clear(captions);
        panel.SetActive(false);
    }
}
