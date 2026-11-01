using System;
using PrimeTween;
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
    const float ColumnHeight = 502f;
    const float ColumnShift = 28f;   // the whole title block sits this far below the screen top
    const int ItemCount = 5; // start, gallery, records, dress-up, settings

    const float SlotSpacing = 360f; // canvas units between neighbouring entries
    const float ItemY = -352f;      // vertical center of the carousel inside the column
    const float SlideSeconds = 0.2f;

    // The chosen entry is kept while the game runs, so coming back from a screen starts on the same one.
    static int selectedIndex;

    readonly GameObject panel;
    readonly SimpleButton[] items = new SimpleButton[ItemCount];
    readonly CanvasGroup[] groups = new CanvasGroup[ItemCount];
    readonly Image[] dots = new Image[ItemCount];
    readonly Text carouselHint;
    Tween slide;
    float pos;       // continuous carousel position in entries (not wrapped)
    int targetPos;   // the entry the carousel is heading for (not wrapped)
    int armedItem = -1;
    bool pressing;
    bool dragged;
    bool cancelClick;
    float dragSign;
    float dragBase;
    Vector2 pressStart;
    GameObject guide;
    GameObject guideCaption;
    Text touchHint;
    GameObject updates;
    Text updatesHeader;
    Text updatesBody;
    SimpleButton updatesNewer;
    SimpleButton updatesOlder;
    int updatePage;
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
        var column = UiKit.Rect("Column", root, top, top, new Vector2(0f, -ColumnShift), new Vector2(1280f, ColumnHeight));
        column.gameObject.AddComponent<MenuFit>().Init(root, column);

        UiKit.Bind(UiKit.Label(column, "Title", "", 120, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -12f), new Vector2(1100f, 130f), UiKit.Accent, true),
            "menu.title", true);
        UiKit.Bind(UiKit.Label(column, "Subtitle", "", 33, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -148f), new Vector2(1280f, 44f), UiKit.Dim),
            "menu.subtitle");
        UiKit.Bind(UiKit.Label(column, "Best", "", 33, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -204f), new Vector2(600f, 44f), Color.white),
            () => Loc.T("menu.best", best));
        // Carousel: every entry is a button that slides, shrinks and fades with its distance from the chosen
        // one. A press only arms an entry; Tick() decides on release whether it was a click or a drag.
        for (int i = 0; i < ItemCount; i++)
        {
            int index = i;
            items[i] = UiKit.Button(column, "Item" + i, "", 44, top, top, Vector2.zero, new Vector2(400f, 92f), () => armedItem = index);
            ((RectTransform)items[i].transform).pivot = new Vector2(0.5f, 0.5f);
            groups[i] = items[i].gameObject.AddComponent<CanvasGroup>();
        }
        for (int i = 0; i < ItemCount; i++)
        {
            var dot = UiKit.Rect("Dot" + i, column, top, top, new Vector2((i - (ItemCount - 1) * 0.5f) * 22f, -418f), new Vector2(10f, 10f));
            dots[i] = dot.gameObject.AddComponent<Image>();
            dots[i].raycastTarget = false;
        }
        carouselHint = UiKit.Label(column, "CarouselHint", "", 22, TextAnchor.MiddleCenter, top, top, new Vector2(0f, -446f), new Vector2(1200f, 30f), UiKit.Dim * 0.8f);
        panel.AddComponent<Ticker>().OnUpdate = Tick;
        Loc.Changed += Refresh;

        var topRight = new Vector2(1f, 1f);
        var language = UiKit.Button(root, "Language", "", 22, topRight, topRight, new Vector2(-UiKit.MarginX, -UiKit.MarginY), new Vector2(230f, 44f),
            () => Loc.Current = Loc.Current == Lang.Ko ? Lang.En : Lang.Ko);
        UiKit.Bind(language.Label, "menu.language");

        // Version stamp in the bottom right corner (not translated; the smallest size the pixel font reads at),
        // with an info button that opens the update history (a click, so it works on touch as well).
        var bottomRight = new Vector2(1f, 0f);
        UiKit.Label(root, "Version", "halloween 2026-10-31\nv" + PatchNotes.Current, 11, TextAnchor.LowerRight, bottomRight, bottomRight,
            new Vector2(-UiKit.MarginX - 52f, UiKit.MarginY), new Vector2(300f, 32f), UiKit.Dim * 0.8f);
        UiKit.Button(root, "Updates", "i", 22, bottomRight, bottomRight, new Vector2(-UiKit.MarginX, UiKit.MarginY), new Vector2(44f, 44f), OpenUpdates);
        updates = BuildUpdates(root);

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

    // Update history: one build per page with its one-line highlights, newest first.
    GameObject BuildUpdates(RectTransform root)
    {
        var center = new Vector2(0.5f, 0.5f);
        var modal = UiKit.Fill("Updates", root);
        modal.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.01f, 0.06f, 0.75f);
        var card = UiKit.Card("Card", modal, new Vector2(760f, 600f));
        card.gameObject.AddComponent<ScreenFit>().Init(modal, card, 600f, 24f);

        UiKit.Bind(UiKit.Label(card, "Title", "", 44, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 255f), new Vector2(680f, 56f), UiKit.Accent), "updates.title");
        updatesHeader = UiKit.Label(card, "Header", "", 33, TextAnchor.MiddleLeft, center, center, new Vector2(0f, 192f), new Vector2(680f, 44f), Color.white);
        updatesBody = UiKit.Label(card, "Body", "", 22, TextAnchor.UpperLeft, center, center, new Vector2(0f, -10f), new Vector2(680f, 340f), Color.white);
        updatesBody.lineSpacing = 1.15f;

        updatesNewer = UiKit.Button(card, "Newer", "", 22, center, center, new Vector2(-250f, -250f), new Vector2(150f, 44f), () => ShowUpdatePage(updatePage - 1));
        UiKit.Bind(updatesNewer.Label, "updates.newer");
        updatesOlder = UiKit.Button(card, "Older", "", 22, center, center, new Vector2(-90f, -250f), new Vector2(150f, 44f), () => ShowUpdatePage(updatePage + 1));
        UiKit.Bind(updatesOlder.Label, "updates.older");
        var close = UiKit.Button(card, "Close", "", 22, center, center, new Vector2(250f, -250f), new Vector2(200f, 44f), CloseUpdates);
        UiKit.Bind(close.Label, "back");
        modal.gameObject.SetActive(false);
        return modal.gameObject;
    }

    void OpenUpdates()
    {
        updates.SetActive(true);
        updates.transform.SetAsLastSibling();
        UiKit.ModalRoot = updates.transform;
        ShowUpdatePage(0);
    }

    void CloseUpdates()
    {
        updates.SetActive(false);
        if (UiKit.ModalRoot == updates.transform) UiKit.ModalRoot = null;
    }

    void ShowUpdatePage(int page)
    {
        updatePage = Mathf.Clamp(page, 0, PatchNotes.Entries.Length - 1);
        var entry = PatchNotes.Entries[updatePage];
        bool ko = Loc.Current == Lang.Ko;
        var body = new System.Text.StringBuilder();
        foreach (var line in entry.lines)
        {
            if (body.Length > 0) body.Append('\n');
            body.Append("- ").Append(ko ? line.ko : line.en);
        }
        updatesHeader.text = "v" + entry.version + "   " + entry.date;
        updatesBody.text = body.ToString();
        updatesNewer.gameObject.SetActive(updatePage > 0);
        updatesOlder.gameObject.SetActive(updatePage < PatchNotes.Entries.Length - 1);
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
        armedItem = -1;
        if (slide.isAlive) slide.Stop();
        CloseUpdates();
        panel.SetActive(false);
    }

    static string[] ItemKeys = { "menu.start", "menu.gallery", "menu.records", "menu.dressup", "menu.settings" };

    static int Wrap(int index) => ((index % ItemCount) + ItemCount) % ItemCount;

    string ItemLabel(int index) => index == 1 ? Loc.T("menu.gallery", found, total) : Loc.T(ItemKeys[index]);

    void Refresh()
    {
        for (int i = 0; i < ItemCount; i++) items[i].Text = ItemLabel(i);
        carouselHint.text = Loc.T(GameInput.UsingTouch ? "menu.carouselHintTouch" : "menu.carouselHint");
        if (slide.isAlive) slide.Stop();
        targetPos = selectedIndex;
        SetPos(selectedIndex);
    }

    // Lays the entries out for a (fractional) position: they slide, shrink and fade with their distance.
    void SetPos(float p)
    {
        pos = p;
        int nearest = Wrap(Mathf.RoundToInt(p));
        for (int i = 0; i < ItemCount; i++)
        {
            float d = i - p;
            d -= ItemCount * Mathf.Round(d / ItemCount);
            float distance = Mathf.Abs(d);
            bool visible = distance < 1.95f;
            if (items[i].gameObject.activeSelf != visible) items[i].gameObject.SetActive(visible);
            dots[i].color = i == nearest ? UiKit.Accent : UiKit.Dim * 0.5f;
            if (!visible) continue;

            float near = Mathf.Min(distance, 1f);
            var rect = (RectTransform)items[i].transform;
            rect.anchoredPosition = new Vector2(d * SlotSpacing, ItemY);
            float scale = Mathf.Lerp(1f, 0.5f, near);
            rect.localScale = new Vector3(scale, scale, 1f);
            groups[i].alpha = Mathf.Lerp(1f, 0.6f, near) * (1f - Mathf.Clamp01((distance - 1f) / 0.95f));
            items[i].Highlighted = i == nearest;
        }
    }

    // Slides to an entry (counted without wrapping, so the shortest way round is taken). A new input stops
    // the running slide and carries on from where it is.
    void SlideTo(int target)
    {
        if (slide.isAlive) slide.Stop();
        targetPos = target;
        selectedIndex = Wrap(target);
        slide = Tween.Custom(this, pos, target, SlideSeconds, (menu, value) => menu.SetPos(value), Ease.OutCubic, useUnscaledTime: true);
    }

    void Move(int delta) => SlideTo(targetPos + delta);

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

    void Tick()
    {
        if (UiKit.ModalRoot != null)
        {
            pressing = false;
            armedItem = -1;
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
            bool mine = false;
            for (int i = 0; i < ItemCount && !mine; i++)
            {
                mine = items[i].gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)items[i].transform, position);
            }
            pressing = mine || !UiKit.OverControl(position);
            dragged = false;
            cancelClick = false;
            pressStart = position;
            if (!mine) armedItem = -1;
        }
        else if (pressing && pointer.press.isPressed)
        {
            // The entries follow the finger or cursor.
            float dx = position.x - pressStart.x;
            if (!dragged && Mathf.Abs(dx) > threshold)
            {
                dragged = true;
                dragSign = Mathf.Sign(dx);
                dragBase = pos;
                if (slide.isAlive) slide.Stop();
            }
            else if (!dragged && Mathf.Abs(position.y - pressStart.y) > threshold * 1.5f)
            {
                cancelClick = true;
            }
            if (dragged)
            {
                float entriesPerPixel = 1f / (SlotSpacing * Screen.width / 1280f);
                SetPos(dragBase - (dx - dragSign * threshold) * entriesPerPixel);
                targetPos = Mathf.RoundToInt(pos);
                selectedIndex = Wrap(targetPos);
            }
        }
        else if (pressing)
        {
            // Released: snap to the nearest entry after a drag, otherwise it was a click on the armed entry.
            if (dragged)
            {
                SlideTo(Mathf.RoundToInt(pos));
            }
            else if (!cancelClick && armedItem >= 0 && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)items[armedItem].transform, position))
            {
                int d = armedItem - selectedIndex;
                d -= ItemCount * Mathf.RoundToInt((float)d / ItemCount);
                if (d == 0) Activate();
                else Move(d);
            }
            pressing = false;
            armedItem = -1;
        }
    }
}

// Shrinks the menu column so it fits above the control guide on short screens (21:9 and the like).
public class MenuFit : MonoBehaviour
{
    const float GuideHeight = 132f; // room kept free at the bottom for the control guide, plus the column's shift down

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
