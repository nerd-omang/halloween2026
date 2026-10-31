using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

// Shown between START and the first visitor: the Kunmungchi model turns once on the stage and its name
// rises into view below it, so the player knows who the candy is for. Only the name, no explanation.
public class IntroScreen
{
    const float NameY = 74f;
    const float RiseDistance = 28f;
    const float RiseSeconds = 0.25f;

    readonly GameObject panel;
    readonly Text nameText;
    readonly Text skipText;
    readonly RectTransform nameRect;
    readonly CanvasGroup nameGroup;
    Sequence rise;

    public IntroScreen(Transform parent)
    {
        var root = UiKit.Fill("IntroScreen", parent);
        panel = root.gameObject;

        var bottom = new Vector2(0.5f, 0f);
        nameText = UiKit.Label(root, "Name", "", 88, TextAnchor.LowerCenter, bottom, bottom, new Vector2(0f, NameY), new Vector2(1000f, 110f), UiKit.Accent, true);
        nameRect = nameText.rectTransform;
        nameGroup = nameText.gameObject.AddComponent<CanvasGroup>();

        skipText = UiKit.Label(root, "Skip", "", 22, TextAnchor.LowerCenter, bottom, bottom, new Vector2(0f, UiKit.MarginY * 0.5f), new Vector2(600f, 30f), UiKit.Dim * 0.6f);

        panel.SetActive(false);
    }

    public void Show()
    {
        nameText.text = OutfitCatalog.KindName(VisitorKind.Kunmungchi);
        skipText.text = Loc.T(GameInput.UsingTouch ? "intro.skipTouch" : "intro.skip");
        panel.SetActive(true);

        StopRise();
        nameGroup.alpha = 0f;
        nameRect.anchoredPosition = new Vector2(0f, NameY - RiseDistance);
        rise = Sequence.Create(useUnscaledTime: true)
            .Group(Tween.Alpha(nameGroup, 0f, 1f, RiseSeconds, Ease.OutCubic, useUnscaledTime: true))
            .Group(Tween.UIAnchoredPositionY(nameRect, NameY - RiseDistance, NameY, RiseSeconds, Ease.OutCubic, useUnscaledTime: true));
    }

    public void Hide()
    {
        StopRise();
        panel.SetActive(false);
    }

    void StopRise()
    {
        if (rise.isAlive) rise.Stop();
    }
}
