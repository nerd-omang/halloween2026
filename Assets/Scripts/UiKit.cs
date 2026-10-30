using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Small runtime UI toolkit for the menu screens. Buttons and toggles poll the mouse themselves
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
            if (body == null) body = Resources.Load<Font>("Fonts/Galmuri11") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

    // One source texel is drawn this many canvas pixels wide.
    const float TexelScale = 3f;

    static readonly List<RectTransform> controls = new List<RectTransform>();
    static readonly Dictionary<(Color, Color, Color), Sprite> boxes = new Dictionary<(Color, Color, Color), Sprite>();
    static Sprite circle;

    // Frame of the last UI click, so a press is never handled by two screens in the same frame.
    public static int ClickFrame = -1;

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
        new Color(1f, 0.88f, 0.4f),     // 3: candy yellow
        new Color(1f, 0.55f, 0.1f),     // 6: pumpkin
        new Color(1f, 0.2f, 0.15f),     // 9: blood red
        new Color(0.78f, 0.35f, 1f),    // 12+: ghostly purple
    };

    // Score color for a streak: gray-white at the start, warming up to a Halloween purple as the combo grows.
    public static Color ComboColor(int streak)
    {
        if (streak < 2) return ComboSteps[0];
        float t = Mathf.Clamp01((streak - 1) / 11f) * (ComboSteps.Length - 1);
        int i = Mathf.Min((int)t, ComboSteps.Length - 2);
        return Color.Lerp(ComboSteps[i], ComboSteps[i + 1], t - i);
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
        var toggle = rt.gameObject.AddComponent<SimpleToggle>();
        toggle.Init(dotImage, label, radio);
        return toggle;
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
    RectTransform rt;
    int enabledFrame;

    public Text Label => label;

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
        var mouse = Mouse.current;
        bool over = Interactable && mouse != null
            && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse.position.ReadValue());
        background.sprite = Look(!Interactable ? Disabled : Highlighted ? Selected : over ? Hover : Normal);
        label.color = !Interactable ? UiKit.Dim * 0.7f : Highlighted || over ? Color.white : UiKit.Dim * 1.15f;

        if (!over || !mouse.leftButton.wasPressedThisFrame) return;
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
        var mouse = Mouse.current;
        bool over = Interactable && mouse != null
            && RectTransformUtility.RectangleContainsScreenPoint(rt, mouse.position.ReadValue());
        label.color = !Interactable ? UiKit.Dim * 0.6f : over || Value ? Color.white : UiKit.Dim * 1.15f;

        if (!over || !mouse.leftButton.wasPressedThisFrame) return;
        if (UiKit.ClickFrame == Time.frameCount || enabledFrame == Time.frameCount) return;
        UiKit.ClickFrame = Time.frameCount;
        AudioManager.Play(Sfx.CandyAppear);
        if (radio && Value) return;
        Set(radio ? true : !Value);
        Changed?.Invoke(Value);
    }
}
