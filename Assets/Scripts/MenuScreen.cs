using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Title screen: start, gallery and dress-up buttons over the live queue, plus the language switch.
public class MenuScreen
{
    public event Action StartClicked;
    public event Action GalleryClicked;
    public event Action SettingsClicked;
    public event Action RecordsClicked;
    public event Action DressUpClicked;

    // Height of the title/carousel column in canvas units (see MenuFit).
    const float ColumnHeight = 490f;
    const int ItemCount = 5; // start, gallery, records, dress-up, settings

    enum Armed
    {
        None,
        Left,
        Center,
        Right,
    }

    // The chosen entry is kept while the game runs, so coming back from a screen starts on the same one.
    static int selectedIndex;

    readonly GameObject panel;
    readonly SimpleButton centerButton;
    readonly SimpleButton leftButton;
    readonly SimpleButton rightButton;
    readonly Image[] dots = new Image[ItemCount];
    readonly Text description;
    readonly Text carouselHint;
    Armed armed;
    bool pressing;
    bool dragged;
    Vector2 pressStart;
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
        // Carousel: the chosen entry big in the middle, the previous and next small at the sides. A press
        // only arms an entry; Tick() decides on release whether it was a click or the start of a drag.
        centerButton = UiKit.Button(column, "Chosen", "", 44, top, top, new Vector2(0f, -292f), new Vector2(520f, 76f), () => armed = Armed.Center);
        centerButton.Highlighted = true;
        leftButton = UiKit.Button(column, "Previous", "", 22, top, top, new Vector2(-430f, -302f), new Vector2(300f, 56f), () => armed = Armed.Left);
        rightButton = UiKit.Button(column, "Next", "", 22, top, top, new Vector2(430f, -302f), new Vector2(300f, 56f), () => armed = Armed.Right);
        for (int i = 0; i < ItemCount; i++)
        {
            var dot = UiKit.Rect("Dot" + i, column, top, top, new Vector2((i - (ItemCount - 1) * 0.5f) * 22f, -386f), new Vector2(10f, 10f));
            dots[i] = dot.gameObject.AddComponent<Image>();
            dots[i].raycastTarget = false;
        }
        description = UiKit.Label(column, "Description", "", 22, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -410f), new Vector2(1100f, 30f), Color.white);
        carouselHint = UiKit.Label(column, "CarouselHint", "", 22, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -444f), new Vector2(1200f, 30f), UiKit.Dim * 0.8f);
        panel.AddComponent<Ticker>().OnUpdate = Tick;
        Loc.Changed += Refresh;

        var topRight = new Vector2(1f, 1f);
        var language = UiKit.Button(root, "Language", "", 22, topRight, topRight, new Vector2(-UiKit.MarginX, -UiKit.MarginY), new Vector2(230f, 44f),
            () => Loc.Current = Loc.Current == Lang.Ko ? Lang.En : Lang.Ko);
        UiKit.Bind(language.Label, "menu.language");

        // Version stamp in the bottom right corner (not translated; the smallest size the pixel font reads at).
        var bottomRight = new Vector2(1f, 0f);
        UiKit.Label(root, "Version", "halloween 2026-10-31", 11, TextAnchor.LowerRight, bottomRight, bottomRight,
            new Vector2(-UiKit.MarginX, UiKit.MarginY), new Vector2(300f, 16f), UiKit.Dim * 0.8f);

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
        Refresh();
    }

    public void Hide()
    {
        pressing = false;
        armed = Armed.None;
        panel.SetActive(false);
    }

    static string[] ItemKeys = { "menu.start", "menu.gallery", "menu.records", "menu.dressup", "menu.settings" };
    static string[] DescriptionKeys = { "menu.desc.start", "menu.desc.gallery", "menu.desc.records", "menu.desc.dressup", "menu.desc.settings" };

    static int Wrap(int index) => ((index % ItemCount) + ItemCount) % ItemCount;

    string ItemLabel(int index) => index == 1 ? Loc.T("menu.gallery", found, total) : Loc.T(ItemKeys[index]);

    void Refresh()
    {
        centerButton.Text = ItemLabel(selectedIndex);
        leftButton.Text = ItemLabel(Wrap(selectedIndex - 1));
        rightButton.Text = ItemLabel(Wrap(selectedIndex + 1));
        description.text = Loc.T(DescriptionKeys[selectedIndex]);
        carouselHint.text = Loc.T(GameInput.UsingTouch ? "menu.carouselHintTouch" : "menu.carouselHint");
        for (int i = 0; i < ItemCount; i++) dots[i].color = i == selectedIndex ? UiKit.Accent : UiKit.Dim * 0.5f;
    }

    // Moves the choice one entry; it wraps around at the ends.
    void Move(int delta)
    {
        selectedIndex = Wrap(selectedIndex + delta);
        Refresh();
    }

    void Activate()
    {
        switch (selectedIndex)
        {
            case 0: StartClicked?.Invoke(); break;
            case 1: GalleryClicked?.Invoke(); break;
            case 2: RecordsClicked?.Invoke(); break;
            case 3: DressUpClicked?.Invoke(); break;
            default: SettingsClicked?.Invoke(); break;
        }
    }

    RectTransform ArmedRect() => armed == Armed.Left ? (RectTransform)leftButton.transform : armed == Armed.Right ? (RectTransform)rightButton.transform : (RectTransform)centerButton.transform;

    void Tick()
    {
        if (UiKit.ModalRoot != null)
        {
            pressing = false;
            armed = Armed.None;
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) Move(-1);
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) Move(1);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
            {
                Activate();
                return;
            }
        }
        var mouse = Mouse.current;
        if (mouse != null)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel > 0.01f) Move(-1);
            else if (wheel < -0.01f) Move(1);
        }

        var pointer = Pointer.current;
        if (pointer == null) return;
        Vector2 position = GameInput.PointerPosition;
        float threshold = Mathf.Max(24f, Screen.width * 0.04f);

        if (GameInput.PointerPressedThisFrame)
        {
            // A press on another control (the language button) is that control's; anywhere else may start a drag.
            bool mine = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)leftButton.transform, position)
                || RectTransformUtility.RectangleContainsScreenPoint((RectTransform)centerButton.transform, position)
                || RectTransformUtility.RectangleContainsScreenPoint((RectTransform)rightButton.transform, position);
            pressing = mine || !UiKit.OverControl(position);
            dragged = false;
            pressStart = position;
            if (!mine) armed = Armed.None;
        }
        else if (pressing && pointer.press.isPressed)
        {
            // Dragging left brings the next entry, dragging right the previous one; keep dragging for more.
            float dx = position.x - pressStart.x;
            if (Mathf.Abs(dx) > threshold)
            {
                dragged = true;
                Move(dx < 0f ? 1 : -1);
                pressStart = position;
            }
            else if (Mathf.Abs(position.y - pressStart.y) > threshold * 1.5f)
            {
                dragged = true;
            }
        }
        else if (pressing && pointer.press.wasReleasedThisFrame)
        {
            // A release that never turned into a drag is a click on the armed entry.
            if (!dragged && armed != Armed.None && RectTransformUtility.RectangleContainsScreenPoint(ArmedRect(), position))
            {
                if (armed == Armed.Center) Activate();
                else Move(armed == Armed.Left ? -1 : 1);
            }
            pressing = false;
            armed = Armed.None;
        }
        else if (!pointer.press.isPressed)
        {
            pressing = false;
            armed = Armed.None;
        }
    }
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
