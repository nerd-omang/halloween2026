using UnityEngine;
using UnityEngine.UI;

// Full-screen "rotate your phone" notice while a touch device is held upright. The game is paused meanwhile.
public class RotateOverlay : MonoBehaviour
{
    GameObject panel;
    bool shown;
    Transform previousModal;

    public static RotateOverlay Create(Transform canvas)
    {
        // The component lives on an always-active holder so Update keeps running while the panel is hidden.
        var holder = UiKit.Fill("RotateOverlayHolder", canvas);
        var overlay = holder.gameObject.AddComponent<RotateOverlay>();
        var root = UiKit.Fill("RotateOverlay", holder);
        overlay.panel = root.gameObject;

        var shade = root.gameObject.AddComponent<Image>();
        shade.color = new Color(0.04f, 0.02f, 0.08f, 0.97f);

        var center = new Vector2(0.5f, 0.5f);
        UiKit.Bind(UiKit.Label(root, "Title", "", 88, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 60f), new Vector2(1100f, 110f), UiKit.Accent, true),
            "rotate.title", true);
        UiKit.Bind(UiKit.Label(root, "Hint", "", 33, TextAnchor.MiddleCenter, center, center, new Vector2(0f, -40f), new Vector2(1100f, 90f), UiKit.Dim),
            "rotate.hint");

        // A pixel phone, turned on its side by the arrow glyph beside it.
        var phone = UiKit.Icon(root, "Phone", UiKit.PixelBox(new Color(0.2f, 0.1f, 0.3f), UiKit.Accent, UiKit.Ink), center, center, new Vector2(0f, -150f), new Vector2(60f, 96f));
        phone.type = Image.Type.Sliced;
        phone.pixelsPerUnitMultiplier = 1f / 3f;

        overlay.panel.SetActive(false);
        return overlay;
    }

    void Update()
    {
        bool portrait = GameInput.UsingTouch && Screen.height > Screen.width;
        if (portrait == shown) return;
        shown = portrait;
        panel.SetActive(portrait);
        if (portrait)
        {
            transform.SetAsLastSibling();
            previousModal = UiKit.ModalRoot;
            UiKit.ModalRoot = panel.transform; // nothing underneath reacts
            GamePause.Hold(this);
        }
        else
        {
            UiKit.ModalRoot = previousModal;
            GamePause.Release(this);
        }
    }

    void OnDestroy()
    {
        GamePause.Release(this);
    }
}
