using System;
using UnityEngine;
using UnityEngine.UI;

// Title screen: start, gallery and dress-up buttons over the live queue, plus the language switch.
public class MenuScreen
{
    public event Action StartClicked;
    public event Action GalleryClicked;
    public event Action SettingsClicked;
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

        UiKit.Bind(UiKit.Label(root, "Best", "", 33, TextAnchor.MiddleCenter, mid, center, new Vector2(0f, 126f), new Vector2(600f, 44f), Color.white),
            () => Loc.T("menu.best", best));
        var start = UiKit.Button(root, "Start", "", 55, mid, center, new Vector2(0f, 50f), new Vector2(380f, 84f), () => StartClicked?.Invoke());
        UiKit.Bind(start.Label, "menu.start");
        var gallery = UiKit.Button(root, "Gallery", "", 33, mid, center, new Vector2(0f, -44f), new Vector2(380f, 56f), () => GalleryClicked?.Invoke());
        UiKit.Bind(gallery.Label, () => Loc.T("menu.gallery", found, total));
        var dressUp = UiKit.Button(root, "DressUp", "", 33, mid, center, new Vector2(0f, -108f), new Vector2(380f, 56f), () => DressUpClicked?.Invoke());
        UiKit.Bind(dressUp.Label, "menu.dressup");
        var settings = UiKit.Button(root, "Settings", "", 33, mid, center, new Vector2(0f, -172f), new Vector2(380f, 56f), () => SettingsClicked?.Invoke());
        UiKit.Bind(settings.Label, "menu.settings");

        var topRight = new Vector2(1f, 1f);
        var language = UiKit.Button(root, "Language", "", 22, topRight, topRight, new Vector2(-UiKit.MarginX, -UiKit.MarginY), new Vector2(230f, 44f),
            () => Loc.Current = Loc.Current == Lang.Ko ? Lang.En : Lang.Ko);
        UiKit.Bind(language.Label, "menu.language");

        // Control guide: a mouse icon with the pressed button lit, and what it does.
        // A centered row at the bottom: the layout group sizes each entry from its text, so the row stays
        // centered whatever the language or aspect ratio.
        var strip = UiKit.Rect("Controls", root, bottom, bottom, new Vector2(0f, 20f), new Vector2(1200f, 48f));
        var row = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.spacing = 56f;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        ControlHint(strip, UiKit.MouseButtonIcon.Left, () => Loc.T("menu.give"));
        ControlHint(strip, UiKit.MouseButtonIcon.Right, () => Loc.T("menu.away"));
        ControlHint(strip, UiKit.MouseButtonIcon.Middle, () => Loc.T("menu.blast") + "\n<color=#c8b8d9>" + Loc.T("menu.blastKeys") + "</color>");

        panel.SetActive(false);
    }

    static void ControlHint(Transform parent, UiKit.MouseButtonIcon button, Func<string> text)
    {
        var center = new Vector2(0.5f, 0.5f);
        var item = UiKit.Rect("Hint" + button, parent, center, center, Vector2.zero, Vector2.zero);
        var layout = item.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var icon = UiKit.Icon(item, "Mouse", UiKit.MouseIcon(button), center, center, Vector2.zero, Vector2.zero);
        var size = icon.gameObject.AddComponent<LayoutElement>();
        size.preferredWidth = 24f;
        size.preferredHeight = 32f;

        var label = UiKit.Label(item, "Text", "", 22, TextAnchor.MiddleLeft, center, center, Vector2.zero, Vector2.zero);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Bind(label, text);
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
