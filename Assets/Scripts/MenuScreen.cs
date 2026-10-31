using System;
using UnityEngine;
using UnityEngine.UI;

// Title screen: start, gallery and dress-up buttons over the live queue, plus the language switch.
public class MenuScreen
{
    public event Action StartClicked;
    public event Action GalleryClicked;
    public event Action SettingsClicked;
    public event Action RecordsClicked;
    public event Action DressUpClicked;

    // Height of the title/button column in canvas units (see MenuFit).
    const float ColumnHeight = 616f;

    readonly GameObject panel;
    GameObject guide;
    GameObject guideCaption;
    Text touchHint;
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
        var bottom = new Vector2(0.5f, 0f);

        // Title and buttons stack from the top inside a column that shrinks on short (wide) screens,
        // so nothing overlaps whatever the aspect ratio. The control guide stays at the screen bottom.
        var column = UiKit.Rect("Column", root, top, top, Vector2.zero, new Vector2(1280f, ColumnHeight));
        column.gameObject.AddComponent<MenuFit>().Init(root, column);

        UiKit.Bind(UiKit.Label(column, "Title", "", 120, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -12f), new Vector2(1100f, 130f), UiKit.Accent, true),
            "menu.title", true);
        UiKit.Bind(UiKit.Label(column, "Subtitle", "", 33, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -148f), new Vector2(1280f, 44f), UiKit.Dim),
            "menu.subtitle");
        UiKit.Bind(UiKit.Label(column, "Best", "", 33, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -204f), new Vector2(600f, 44f), Color.white),
            () => Loc.T("menu.best", best));
        var start = UiKit.Button(column, "Start", "", 55, top, top, new Vector2(0f, -258f), new Vector2(380f, 84f), () => StartClicked?.Invoke());
        UiKit.Bind(start.Label, "menu.start");
        var gallery = UiKit.Button(column, "Gallery", "", 33, top, top, new Vector2(0f, -354f), new Vector2(380f, 56f), () => GalleryClicked?.Invoke());
        UiKit.Bind(gallery.Label, () => Loc.T("menu.gallery", found, total));
        var records = UiKit.Button(column, "Records", "", 33, top, top, new Vector2(0f, -420f), new Vector2(380f, 56f), () => RecordsClicked?.Invoke());
        UiKit.Bind(records.Label, "menu.records");
        var dressUp = UiKit.Button(column, "DressUp", "", 33, top, top, new Vector2(0f, -486f), new Vector2(380f, 56f), () => DressUpClicked?.Invoke());
        UiKit.Bind(dressUp.Label, "menu.dressup");
        var settings = UiKit.Button(column, "Settings", "", 33, top, top, new Vector2(0f, -552f), new Vector2(380f, 56f), () => SettingsClicked?.Invoke());
        UiKit.Bind(settings.Label, "menu.settings");

        var topRight = new Vector2(1f, 1f);
        var language = UiKit.Button(root, "Language", "", 22, topRight, topRight, new Vector2(-UiKit.MarginX, -UiKit.MarginY), new Vector2(230f, 44f),
            () => Loc.Current = Loc.Current == Lang.Ko ? Lang.En : Lang.Ko);
        UiKit.Bind(language.Label, "menu.language");

        // Control guide: a mouse icon with the pressed button lit, and what it does.
        // A centered row at the bottom: the layout group sizes each entry from its text, so the row stays
        // centered whatever the language or aspect ratio.
        var strip = UiKit.Rect("Controls", root, bottom, bottom, new Vector2(0f, 54f), new Vector2(1200f, 48f));
        var row = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.spacing = 56f;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        ControlHint(strip, UiKit.MouseButtonIcon.Left, () => Loc.T("menu.give"));
        ControlHint(strip, UiKit.MouseButtonIcon.Right, () => Loc.T("menu.away"));
        ControlHint(strip, UiKit.MouseButtonIcon.Middle, () => Loc.T("menu.blast"));
        guide = strip.gameObject;
        touchHint = UiKit.Label(root, "TouchHint", "", 22, TextAnchor.LowerCenter, bottom, bottom, new Vector2(0f, 40f), new Vector2(1200f, 32f), UiKit.Dim);
        UiKit.Bind(touchHint, "touch.hint");
        UiKit.Bind(UiKit.Label(root, "BlastCaption", "", 22, TextAnchor.LowerCenter, bottom, bottom, new Vector2(0f, 18f), new Vector2(1200f, 28f), UiKit.Dim),
            "menu.blastCaption");
        guideCaption = root.Find("BlastCaption").gameObject;

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
        // Touch devices see the swipe guide, others the mouse icons.
        bool touch = GameInput.UsingTouch;
        guide.SetActive(!touch);
        guideCaption.SetActive(!touch);
        touchHint.gameObject.SetActive(touch);
        panel.SetActive(true);
        foreach (var bound in panel.GetComponentsInChildren<LocText>(true)) bound.Refresh();
    }

    public void Hide() => panel.SetActive(false);
}

// Shrinks the menu column so it fits above the control guide on short screens (21:9 and the like).
public class MenuFit : MonoBehaviour
{
    const float GuideHeight = 104f; // room kept free at the bottom for the control guide

    RectTransform root;
    RectTransform column;

    public void Init(RectTransform screen, RectTransform content)
    {
        root = screen;
        column = content;
    }

    void Update()
    {
        if (root == null) return;
        float scale = Mathf.Clamp((root.rect.height - GuideHeight) / column.sizeDelta.y, 0.55f, 1f);
        column.localScale = new Vector3(scale, scale, 1f);
    }
}
