using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Small runtime UI toolkit for the menu screens. Buttons and toggles poll the pointer (mouse, touch, pen) themselves
// (the project uses the new Input System only and has no EventSystem).
//
// Look: dark violet panels with a stepped pixel frame, pumpkin-amber accents, Galmuri11 body font and the
// Creepster title font (English glyphs only; Korean text falls back to the body font).
public static class UiKit
{
    // Sizes that are multiples of 11 keep Galmuri11 crispest.
    static Font body;
    static Font title;

    public static Font Font
    {
        get
        {
            if (body == null) body = Resources.Load<Font>("Fonts/Galmuri11");
            if (body == null) body = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return body;
        }
        set => body = value;
    }

    public static Font TitleFont
    {
        get
        {
            if (title == null) title = Resources.Load<Font>("Fonts/SpookyTitle");
            return title != null ? title : Font;
        }
    }

    public static readonly Color Ink = new Color(0.05f, 0.02f, 0.09f);
    public static readonly Color Accent = new Color(1f, 0.7f, 0.18f);
    public static readonly Color Danger = new Color(1f, 0.45f, 0.4f);
    public static readonly Color Dim = new Color(0.78f, 0.72f, 0.85f);
    public static readonly Color PanelColor = new Color(0.1f, 0.05f, 0.16f, 0.94f);
    public static readonly Color PanelEdge = new Color(0.42f, 0.26f, 0.58f);

    // Keeps corner UI (score, hearts, corner buttons) a little inside the screen edges.
    public const float MarginX = 64f;
    public const float MarginY = 32f;

    // One source texel is drawn this many canvas pixels wide.
    const float TexelScale = 3f;

    static readonly List<RectTransform> controls = new List<RectTransform>();
    static readonly Dictionary<(Color, Color, Color), Sprite> boxes = new Dictionary<(Color, Color, Color), Sprite>();
    static Sprite circle;

    // Frame of the last UI click, so a press is never handled by two screens in the same frame.
    public static int ClickFrame = -1;

    // While set, only controls under this transform react to the mouse (a modal screen is open).
    public static Transform ModalRoot;

    public static bool Usable(Transform control) => ModalRoot == null || control.IsChildOf(ModalRoot);

    // Extra hit area (canvas units on each side) on touch devices, where fingers are bigger than a cursor.
    const float TouchPadding = 6f;

    // Is the pointer (mouse, finger or pen) over this control? Touch gets a slightly larger hit area.
    public static bool Hits(RectTransform rt, Vector2 screenPoint)
    {
        float pad = GameInput.UsingTouch ? -TouchPadding : 0f;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null, new Vector4(pad, pad, pad, pad));
    }

    // Press test: with the larger touch hit areas neighbours overlap, so an edge press goes to the control
    // that really contains the point, and to none of the padded ones if another control contains it.
    public static bool PressHits(RectTransform rt, Vector2 screenPoint)
    {
        if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null)) return true;
        if (!GameInput.UsingTouch || !Hits(rt, screenPoint)) return false;
        foreach (var other in controls)
        {
            if (other != null && other != rt && other.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(other, screenPoint, null)) return false;
        }
        return true;
    }

    // Hover highlight: a finger only "hovers" while it is down.
    public static bool Hovering(bool over) => over && (!GameInput.UsingTouch || GameInput.PointerHeld);

    public static void Register(RectTransform rt) => controls.Add(rt);
    public static void Unregister(RectTransform rt) => controls.Remove(rt);

    public static bool OverControl(Vector2 screenPoint)
    {
        foreach (var rt in controls)
        {
            if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint)) return true;
        }
        return false;
    }

    // 7x7 stepped-corner frame: outline, edge, shade, then fill. The 3 texel border is sliced.
    public static Sprite PixelBox(Color fill, Color edge, Color outline)
    {
        var key = (fill, edge, outline);
        if (boxes.TryGetValue(key, out var cached) && cached != null) return cached;
        const int n = 7;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var shade = Color.Lerp(fill, outline, 0.35f);
        shade.a = fill.a;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int d = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y));
                bool corner = (x == 0 || x == n - 1) && (y == 0 || y == n - 1);
                Color c = corner ? Color.clear : d == 0 ? outline : d == 1 ? edge : d == 2 ? shade : fill;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
        boxes[key] = sprite;
        return sprite;
    }

    public static void StyleBox(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f / TexelScale;
        image.color = Color.white;
    }

    // Hard drop shadow so text stays readable over the 3D view.
    public static void AddShadow(Text text)
    {
        var shadow = text.gameObject.GetComponent<Shadow>();
        if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.9f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    static bool IsAscii(string s)
    {
        foreach (char c in s)
        {
            if (c > 0x7f) return false;
        }
        return true;
    }

    public static Font FontFor(string text, bool useTitleFont) => useTitleFont && IsAscii(text) ? TitleFont : Font;

    // Keeps a label's text (and font) in sync with the current language.
    public static Text Bind(Text text, Func<string> source, bool useTitleFont = false)
    {
        var bound = text.gameObject.GetComponent<LocText>();
        if (bound == null) bound = text.gameObject.AddComponent<LocText>();
        bound.Init(text, source, useTitleFont);
        return text;
    }

    public static Text Bind(Text text, string key, bool useTitleFont = false) => Bind(text, () => Loc.T(key), useTitleFont);

    static readonly Color[] ComboSteps =
    {
        new Color(0.85f, 0.85f, 0.88f), // 0-1: plain gray-white
        new Color(1f, 0.88f, 0.4f),     // candy yellow
        new Color(1f, 0.55f, 0.1f),     // pumpkin
        new Color(1f, 0.2f, 0.15f),     // blood red
        new Color(0.78f, 0.35f, 1f),    // ghostly purple, at the top of the combo
    };

    // The streak at which the score color is fully purple (the same scale as the hand speed and the time limit).
    public const int ComboColorMaxStreak = 200;

    // Score color for a streak: gray-white at the start, warming up to a Halloween purple as the combo grows.
    public static Color ComboColor(int streak)
    {
        if (streak < 2) return ComboSteps[0];
        float t = Mathf.Clamp01(streak / (float)ComboColorMaxStreak) * (ComboSteps.Length - 1);
        int i = Mathf.Min((int)t, ComboSteps.Length - 2);
        return Color.Lerp(ComboSteps[i], ComboSteps[i + 1], t - i);
    }

    public enum MouseButtonIcon
    {
        Left,
        Right,
        Middle,
    }

    static readonly Dictionary<MouseButtonIcon, Sprite> mouseIcons = new Dictionary<MouseButtonIcon, Sprite>();

    // 12x16 pixel mouse seen from above with one button (or the wheel) lit.
    public static Sprite MouseIcon(MouseButtonIcon lit)
    {
        if (mouseIcons.TryGetValue(lit, out var cached) && cached != null) return cached;
        const int w = 12;
        const int h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var body = new Color(0.82f, 0.78f, 0.88f);
        var wheel = new Color(0.3f, 0.22f, 0.4f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool edgeX = x == 0 || x == w - 1;
                bool edgeY = y == 0 || y == h - 1;
                Color c = body;
                if (edgeX && edgeY) c = Color.clear;
                else if (edgeX || edgeY) c = Ink;
                else if (y == 8) c = Ink;
                else if (y > 8)
                {
                    bool wheelCol = x == 5 || x == 6;
                    if (wheelCol) c = y >= 10 && y <= 13 ? (lit == MouseButtonIcon.Middle ? Accent : wheel) : Ink;
                    else if (x < 5 && lit == MouseButtonIcon.Left) c = Accent;
                    else if (x > 6 && lit == MouseButtonIcon.Right) c = Accent;
                }
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        mouseIcons[lit] = sprite;
        return sprite;
    }

    public static Image Icon(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rt = Rect(name, parent, anchor, pivot, position, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    // Restyles a HUD text that the scene builder created with the default font.
    public static void StyleHud(Text text, bool useTitleFont = false)
    {
        text.font = FontFor(text.text, useTitleFont);
        AddShadow(text);
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Fill(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    // A framed column along the left edge (its left, top and bottom frame sit off screen).
    public static RectTransform SidePanel(string name, Transform parent, float width)
    {
        var rt = Fill(name, parent);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.offsetMin = new Vector2(-30f, -30f);
        rt.offsetMax = new Vector2(width, 30f);
        var image = rt.gameObject.AddComponent<Image>();
        StyleBox(image, PixelBox(PanelColor, PanelEdge, Ink));
        image.raycastTarget = false;
        return rt;
    }

    // A framed box in the middle of the screen.
    public static RectTransform Card(string name, Transform parent, Vector2 size)
    {
        var center = new Vector2(0.5f, 0.5f);
        var rt = Rect(name, parent, center, center, Vector2.zero, size);
        var image = rt.gameObject.AddComponent<Image>();
        StyleBox(image, PixelBox(PanelColor, PanelEdge, Ink));
        image.raycastTarget = false;
        return rt;
    }

    // The same framed column along the right edge.
    public static RectTransform SidePanelRight(string name, Transform parent, float width)
    {
        var rt = Fill(name, parent);
        rt.anchorMin = new Vector2(1f, 0f);
        rt.offsetMin = new Vector2(-width, -30f);
        rt.offsetMax = new Vector2(30f, 30f);
        var image = rt.gameObject.AddComponent<Image>();
        StyleBox(image, PixelBox(PanelColor, PanelEdge, Ink));
        image.raycastTarget = false;
        return rt;
    }

    public static Text Label(Transform parent, string name, string text, int size, TextAnchor align,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 box, Color? color = null, bool useTitleFont = false)
    {
        var rt = Rect(name, parent, anchor, pivot, position, box);
        var label = rt.gameObject.AddComponent<Text>();
        label.font = FontFor(text, useTitleFont);
        label.fontSize = size;
        label.alignment = align;
        label.color = color ?? Color.white;
        label.text = text;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        AddShadow(label);
        return label;
    }

    public static SimpleButton Button(Transform parent, string name, string text, int fontSize,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Action onClick)
    {
        var rt = Rect(name, parent, anchor, pivot, position, size);
        var background = rt.gameObject.AddComponent<Image>();
        StyleBox(background, SimpleButton.Look(SimpleButton.Normal));
        // Small controls (list rows, tabs) get a thinner frame so text is not drawn over it.
        if (size.y < 44f) background.pixelsPerUnitMultiplier = 1f / 2f;
        var label = Label(rt, "Label", text, fontSize, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        var button = rt.gameObject.AddComponent<SimpleButton>();
        button.Init(background, label, onClick);
        return button;
    }

    public static SimpleToggle Toggle(Transform parent, string name, string text, bool radio, int fontSize,
        Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rt = Rect(name, parent, anchor, pivot, position, size);
        var box = Rect("Box", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(24f, 24f));
        var boxImage = box.gameObject.AddComponent<Image>();
        boxImage.raycastTarget = false;
        var dot = Rect("Mark", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
        var dotImage = dot.gameObject.AddComponent<Image>();
        dotImage.color = Accent;
        dotImage.raycastTarget = false;
        dotImage.enabled = false;
        var hollow = new Color(0.1f, 0.05f, 0.15f);
        if (radio)
        {
            boxImage.color = Dim;
            var hole = Rect("Hole", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            var holeImage = hole.gameObject.AddComponent<Image>();
            holeImage.color = hollow;
            holeImage.raycastTarget = false;
            boxImage.sprite = holeImage.sprite = dotImage.sprite = CircleSprite();
            // The hole is created after the dot's parent exists but must sit below the mark.
            dot.SetAsLastSibling();
        }
        else
        {
            StyleBox(boxImage, PixelBox(hollow, PanelEdge, Ink));
        }

        var label = Label(rt, "Label", text, fontSize, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(38f, 0f), new Vector2(size.x - 38f, size.y));
        // Toggle labels are one line; wrapping would drop the "%" of narrow ones like "30%" onto a second line.
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var toggle = rt.gameObject.AddComponent<SimpleToggle>();
        toggle.Init(dotImage, label, radio);
        return toggle;
    }

    // A button showing an icon instead of text.
    public static SimpleButton IconButton(Transform parent, string name, Sprite icon, Vector2 anchor, Vector2 pivot,
        Vector2 position, Vector2 size, Vector2 iconSize, Action onClick)
    {
        var button = Button(parent, name, "", 22, anchor, pivot, position, size, onClick);
        var image = Icon(button.transform, "Icon", icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, iconSize);
        image.color = Dim * 1.15f;
        button.IconImage = image;
        return button;
    }

    static Sprite gear;

    // 11x11 pixel gear.
    public static Sprite GearIcon()
    {
        if (gear != null) return gear;
        const int n = 11;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = x - 5f;
                float dy = y - 5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360f;
                float fromTooth = Mathf.Abs(Mathf.Repeat(angle + 22.5f, 45f) - 22.5f);
                bool body = r <= 4.1f && r >= 1.9f;
                bool tooth = r > 3.6f && r <= 5.4f && fromTooth <= 14f;
                tex.SetPixel(x, y, body || tooth ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        gear = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return gear;
    }

    public static SimpleSlider Slider(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rt = Rect(name, parent, anchor, pivot, position, size);
        var track = Rect("Track", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(size.x, 18f));
        track.anchorMax = new Vector2(1f, 0.5f);
        track.offsetMin = new Vector2(0f, -9f);
        track.offsetMax = new Vector2(0f, 9f);
        var trackImage = track.gameObject.AddComponent<Image>();
        StyleBox(trackImage, PixelBox(new Color(0.1f, 0.05f, 0.15f), PanelEdge, Ink));
        trackImage.raycastTarget = false;
        var fill = Rect("Fill", track, new Vector2(0f, 0f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.offsetMin = new Vector2(6f, 6f);
        fill.offsetMax = new Vector2(-6f, -6f);
        var fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = Accent;
        fillImage.raycastTarget = false;
        var handle = Rect("Handle", rt, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 44f));
        var handleImage = handle.gameObject.AddComponent<Image>();
        StyleBox(handleImage, PixelBox(new Color(0.95f, 0.88f, 0.7f), Accent, Ink));
        handleImage.raycastTarget = false;
        var slider = rt.gameObject.AddComponent<SimpleSlider>();
        slider.Init(track, fill, handle);
        return slider;
    }

    // Hard-edged 9x9 disc, so radio dots match the pixel look.
    static Sprite CircleSprite()
    {
        if (circle != null) return circle;
        const int n = 9;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                tex.SetPixel(x, y, d <= r - 0.2f ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return circle;
    }
}

// Keeps a label's text (and font) in sync with the current language.
public class LocText : MonoBehaviour
{
    Text text;
    Func<string> source;
    bool useTitleFont;

    public void Init(Text target, Func<string> producer, bool title)
    {
        text = target;
        source = producer;
        useTitleFont = title;
        if (isActiveAndEnabled) Refresh();
    }

    void OnEnable()
    {
        Loc.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => Loc.Changed -= Refresh;

    public void Refresh()
    {
        if (text == null || source == null) return;
        string value = source();
        text.font = UiKit.FontFor(value, useTitleFont);
        text.text = value;
    }
}

public class SimpleButton : MonoBehaviour
{
    // Fill and edge colors per state; the frame sprite is shared through UiKit.PixelBox.
    public static readonly (Color fill, Color edge) Normal = (new Color(0.2f, 0.1f, 0.3f), new Color(0.5f, 0.32f, 0.7f));
    static readonly (Color fill, Color edge) Hover = (new Color(0.36f, 0.17f, 0.48f), UiKit.Accent);
    static readonly (Color fill, Color edge) Selected = (new Color(0.62f, 0.32f, 0.08f), new Color(1f, 0.82f, 0.35f));
    static readonly (Color fill, Color edge) Disabled = (new Color(0.11f, 0.07f, 0.16f), new Color(0.24f, 0.17f, 0.3f));

    public static Sprite Look((Color fill, Color edge) state) => UiKit.PixelBox(state.fill, state.edge, UiKit.Ink);

    public bool Interactable = true;
    public bool Highlighted;

    Image background;
    Text label;
    Action clicked;
    int lastState = -1;
    RectTransform rt;
    int enabledFrame;

    public Text Label => label;
    public Image IconImage;

    public string Text
    {
        get => label.text;
        set => label.text = value;
    }

    public void Init(Image backgroundImage, Text labelText, Action onClick)
    {
        background = backgroundImage;
        label = labelText;
        clicked = onClick;
    }

    void OnEnable()
    {
        rt = (RectTransform)transform;
        enabledFrame = Time.frameCount;
        UiKit.Register(rt);
    }

    void OnDisable()
    {
        UiKit.Unregister(rt);
    }

    void Update()
    {
        bool over = Interactable && Pointer.current != null && UiKit.Usable(transform) && UiKit.Hits(rt, GameInput.PointerPosition);
        bool hover = UiKit.Hovering(over);
        int state = !Interactable ? 0 : Highlighted ? 1 : hover ? 2 : 3;
        if (state != lastState)
        {
            lastState = state;
            background.sprite = Look(state == 0 ? Disabled : state == 1 ? Selected : state == 2 ? Hover : Normal);
        }
        label.color = !Interactable ? UiKit.Dim * 0.7f : Highlighted || hover ? Color.white : UiKit.Dim * 1.15f;
        if (IconImage != null) IconImage.color = label.color;

        if (!over || !GameInput.PointerPressedThisFrame || !UiKit.PressHits(rt, GameInput.PointerPosition)) return;
        if (UiKit.ClickFrame == Time.frameCount || enabledFrame == Time.frameCount) return;
        UiKit.ClickFrame = Time.frameCount;
        AudioManager.Play(Sfx.CandyAppear);
        clicked?.Invoke();
    }
}

public class SimpleToggle : MonoBehaviour
{
    public event Action<bool> Changed;
    public bool Interactable = true;

    Image mark;
    Text label;
    bool radio;
    RectTransform rt;
    int enabledFrame;

    public bool Value { get; private set; }

    public string Text
    {
        get => label.text;
        set => label.text = value;
    }

    public Text Label => label;

    public void Init(Image markImage, Text labelText, bool isRadio)
    {
        mark = markImage;
        label = labelText;
        radio = isRadio;
    }

    // Sets the state without raising Changed (used when syncing from code).
    public void Set(bool value)
    {
        Value = value;
        if (mark != null) mark.enabled = value;
    }

    void OnEnable()
    {
        rt = (RectTransform)transform;
        enabledFrame = Time.frameCount;
        UiKit.Register(rt);
    }

    void OnDisable()
    {
        UiKit.Unregister(rt);
    }

    void Update()
    {
        bool over = Interactable && Pointer.current != null && UiKit.Usable(transform) && UiKit.Hits(rt, GameInput.PointerPosition);
        label.color = !Interactable ? UiKit.Dim * 0.6f : UiKit.Hovering(over) || Value ? Color.white : UiKit.Dim * 1.15f;

        if (!over || !GameInput.PointerPressedThisFrame || !UiKit.PressHits(rt, GameInput.PointerPosition)) return;
        if (UiKit.ClickFrame == Time.frameCount || enabledFrame == Time.frameCount) return;
        UiKit.ClickFrame = Time.frameCount;
        AudioManager.Play(Sfx.CandyAppear);
        if (radio && Value) return;
        Set(radio ? true : !Value);
        Changed?.Invoke(Value);
    }
}

// Horizontal slider, 0..1. Polls the mouse like the other controls; dragging works anywhere on the bar.
public class SimpleSlider : MonoBehaviour
{
    public event Action<float> Changed;
    public bool Interactable = true;
    // 0 = continuous; otherwise the value snaps to this many evenly spaced positions.
    public int Steps;

    RectTransform track;
    RectTransform fill;
    RectTransform handle;
    RectTransform rt;
    bool dragging;
    int enabledFrame;

    public float Value { get; private set; }

    public void Init(RectTransform trackRect, RectTransform fillRect, RectTransform handleRect)
    {
        track = trackRect;
        fill = fillRect;
        handle = handleRect;
        Set(Value);
    }

    // Sets the value without raising Changed (used when syncing from code).
    public void Set(float value)
    {
        Value = Mathf.Clamp01(value);
        if (fill != null)
        {
            fill.anchorMax = new Vector2(Value, 1f);
            fill.gameObject.SetActive(Value > 0.04f); // below that the inset would give the fill a negative width
        }
        if (handle != null) handle.anchorMin = handle.anchorMax = new Vector2(Value, 0.5f);
    }

    void OnEnable()
    {
        rt = (RectTransform)transform;
        enabledFrame = Time.frameCount;
        dragging = false;
        UiKit.Register(rt);
    }

    void OnDisable()
    {
        dragging = false;
        UiKit.Unregister(rt);
    }

    void Update()
    {
        if (Pointer.current == null || !Interactable) return;
        Vector2 point = GameInput.PointerPosition;

        if (!dragging)
        {
            bool over = UiKit.Usable(transform) && UiKit.Hits(rt, point);
            if (!over || !GameInput.PointerPressedThisFrame || !UiKit.PressHits(rt, point)) return;
            if (UiKit.ClickFrame == Time.frameCount || enabledFrame == Time.frameCount) return;
            UiKit.ClickFrame = Time.frameCount;
            dragging = true;
        }
        else if (!GameInput.PointerHeld)
        {
            dragging = false;
            AudioManager.Play(Sfx.CandyAppear);
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, point, null, out var local)) return;
        float t = Mathf.Clamp01((local.x - track.rect.xMin) / track.rect.width);
        if (Steps > 1) t = Mathf.Round(t * (Steps - 1)) / (Steps - 1);
        if (Mathf.Approximately(t, Value)) return;
        Set(t);
        Changed?.Invoke(Value);
    }
}

// Scales a screen box down so it fits when the canvas is short (21:9, phones in landscape).
public class ScreenFit : MonoBehaviour
{
    RectTransform root;
    RectTransform target;
    float designHeight;
    float reserve;

    public void Init(RectTransform screen, RectTransform content, float height, float margin)
    {
        root = screen;
        target = content;
        designHeight = height;
        reserve = margin;
    }

    void Update()
    {
        if (root == null) return;
        float scale = Mathf.Clamp((root.rect.height - reserve) / designHeight, 0.5f, 1f);
        target.localScale = new Vector3(scale, scale, 1f);
    }
}

// Runs a callback every frame while its GameObject is active (screens are plain classes, not MonoBehaviours).
public class Ticker : MonoBehaviour
{
    public Action OnUpdate;

    void Update() => OnUpdate?.Invoke();
}
