using System;
using UnityEngine;
using UnityEngine.UI;

// Title screen: start, gallery and dress-up buttons over the live queue, plus the language switch.
public class MenuScreen
{
    public event Action StartClicked;
    public event Action GalleryClicked;
    public event Action DressUpClicked;

    readonly GameObject panel;
    int best;
    int found;
    int total;

    public MenuScreen(Transform parent)
    {
        var root = UiKit.Fill("MenuScreen", parent);
        panel = root.gameObject;
        var shade = panel.AddComponent<Image>();
        shade.color = new Color(0.04f, 0.02f, 0.08f, 0.55f);
        shade.raycastTarget = false;

        var top = new Vector2(0.5f, 1f);
        var mid = new Vector2(0.5f, 0.5f);
        var bottom = new Vector2(0.5f, 0f);
        var center = new Vector2(0.5f, 0.5f);

        UiKit.Bind(UiKit.Label(root, "Title", "", 120, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -44f), new Vector2(1100f, 130f), UiKit.Accent, true),
            "menu.title", true);
        UiKit.Bind(UiKit.Label(root, "Subtitle", "", 33, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -185f), new Vector2(1200f, 44f), UiKit.Dim),
            "menu.subtitle");

        UiKit.Bind(UiKit.Label(root, "Best", "", 33, TextAnchor.MiddleCenter, mid, center, new Vector2(0f, 90f), new Vector2(600f, 44f), Color.white),
            () => Loc.T("menu.best", best));
        var start = UiKit.Button(root, "Start", "", 55, mid, center, new Vector2(0f, 10f), new Vector2(380f, 84f), () => StartClicked?.Invoke());
        UiKit.Bind(start.Label, "menu.start");
        var gallery = UiKit.Button(root, "Gallery", "", 33, mid, center, new Vector2(0f, -85f), new Vector2(380f, 62f), () => GalleryClicked?.Invoke());
        UiKit.Bind(gallery.Label, () => Loc.T("menu.gallery", found, total));
        var dressUp = UiKit.Button(root, "DressUp", "", 33, mid, center, new Vector2(0f, -160f), new Vector2(380f, 62f), () => DressUpClicked?.Invoke());
        UiKit.Bind(dressUp.Label, "menu.dressup");

        var topRight = new Vector2(1f, 1f);
        var language = UiKit.Button(root, "Language", "", 22, topRight, topRight, new Vector2(-24f, -24f), new Vector2(230f, 44f),
            () => Loc.Current = Loc.Current == Lang.Ko ? Lang.En : Lang.Ko);
        UiKit.Bind(language.Label, "menu.language");

        UiKit.Bind(UiKit.Label(root, "Controls", "", 22, TextAnchor.LowerCenter, bottom, bottom, new Vector2(0f, 28f), new Vector2(1200f, 70f), UiKit.Dim),
            "menu.controls");

        panel.SetActive(false);
    }

    public void Show(int best, int found, int total)
    {
        this.best = best;
        this.found = found;
        this.total = total;
        panel.SetActive(true);
        foreach (var bound in panel.GetComponentsInChildren<LocText>(true)) bound.Refresh();
    }

    public void Hide() => panel.SetActive(false);
}
