using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// After game over: the looks behind each lost heart, shown as turnable models with what the player did
// and who it really was. A model hidden under a ghost sheet is shown without the sheet.
public class ReviewScreen
{
    public event Action PlayAgain;
    public event Action Title;

    readonly OutfitCatalog catalog;
    readonly ModelStage stage;
    readonly GameObject panel;
    readonly Text scoreText;
    readonly List<GameObject> captions = new List<GameObject>();

    public ReviewScreen(Transform parent, OutfitCatalog catalog, ModelStage stage)
    {
        this.catalog = catalog;
        this.stage = stage;

        var root = UiKit.Fill("ReviewScreen", parent);
        panel = root.gameObject;

        var top = new Vector2(0.5f, 1f);
        UiKit.Bind(UiKit.Label(root, "Header", "", 88, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -16f), new Vector2(900f, 100f), UiKit.Danger, true), "review.header", true);
        scoreText = UiKit.Label(root, "Score", "", 33, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -116f), new Vector2(900f, 44f), Color.white);
        UiKit.Bind(UiKit.Label(root, "Hint", "", 22, TextAnchor.MiddleCenter, top, top,
            new Vector2(0f, -164f), new Vector2(1000f, 30f), UiKit.Dim), "review.hint");

        var bottom = new Vector2(0.5f, 0f);
        var again = UiKit.Button(root, "PlayAgain", "", 33, bottom, bottom, new Vector2(-140f, 22f), new Vector2(250f, 56f), () => PlayAgain?.Invoke());
        UiKit.Bind(again.Label, "review.playagain");
        var title = UiKit.Button(root, "Title", "", 33, bottom, bottom, new Vector2(140f, 22f), new Vector2(250f, 56f), () => Title?.Invoke());
        UiKit.Bind(title.Label, "review.title");

        panel.SetActive(false);
    }

    public void Show(IReadOnlyList<MistakeRecord> mistakes, int score, int best)
    {
        foreach (var caption in captions) UnityEngine.Object.Destroy(caption);
        captions.Clear();

        scoreText.text = Loc.T("review.score", score, best);

        var outfits = new List<Outfit>();
        foreach (var mistake in mistakes)
        {
            var outfit = mistake.outfit;
            outfit.sheet = false;
            outfits.Add(outfit);
        }
        stage.Show(outfits);

        for (int i = 0; i < mistakes.Count; i++)
        {
            var mistake = mistakes[i];
            string did = Loc.T(mistake.action == HandAction.Give ? "review.gave" : "review.sent");
            string was = Loc.T(mistake.outfit.kind == VisitorKind.Kunmungchi ? "review.wasKun" : "review.wasOma");
            string look = Loc.T("review.look", catalog.EntryNumber(outfits[i]));
            if (mistake.outfit.sheet) look += Loc.T("review.sheet");

            var anchor = new Vector2(Mathf.Clamp01(stage.SlotViewportX(i)), 0f);
            var label = UiKit.Label(panel.transform, "Caption" + i,
                "<color=#ffb52e>" + did + "</color>\n" + was + "\n<size=22><color=#c8b8d9>" + look + "</color></size>",
                33, TextAnchor.UpperCenter, anchor, new Vector2(0.5f, 1f), new Vector2(0f, 270f), new Vector2(430f, 190f));
            captions.Add(label.gameObject);
        }
        panel.SetActive(true);
    }

    public void Hide()
    {
        panel.SetActive(false);
    }
}
