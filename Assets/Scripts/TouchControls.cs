using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Touch play: swipe right gives candy, swipe left sends the visitor away, a tap or a vertical swipe is the
// palm blast. Gestures become GameInput.Press calls; a small hint line at the bottom explains them.
// Only active while playing on a touch device and no modal screen (settings...) is open.
public class TouchControls : MonoBehaviour
{
    const float SwipeFraction = 0.07f; // of the screen width
    const float TapTime = 0.35f;
    const float DominantAxis = 1.3f;

    Func<bool> playing;
    GameObject hint;
    bool tracking;
    bool fired;
    Vector2 start;
    float startTime;

    public static TouchControls Create(Transform canvas, Func<bool> isPlaying)
    {
        var go = new GameObject("TouchControls", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        var controls = go.AddComponent<TouchControls>();
        controls.playing = isPlaying;

        var bottom = new Vector2(0.5f, 0f);
        var label = UiKit.Label(go.transform, "Hint", "", 22, TextAnchor.LowerCenter, bottom, bottom,
            new Vector2(0f, UiKit.MarginY * 0.6f), new Vector2(1000f, 32f), UiKit.Dim * 0.9f);
        UiKit.Bind(label, "touch.hint");
        controls.hint = label.gameObject;
        controls.hint.SetActive(false);
        return controls;
    }

    void Update()
    {
        bool active = GameInput.UsingTouch && playing != null && playing() && UiKit.ModalRoot == null;
        if (hint != null && hint.activeSelf != active) hint.SetActive(active);
        if (!active)
        {
            tracking = false;
            return;
        }

        var touch = Touchscreen.current;
        if (touch == null) return;
        var primary = touch.primaryTouch;
        Vector2 position = primary.position.ReadValue();

        if (primary.press.wasPressedThisFrame)
        {
            // A finger that lands on a button (gear...) belongs to that button.
            tracking = !UiKit.OverControl(position) && UiKit.ClickFrame != Time.frameCount;
            fired = false;
            start = position;
            startTime = Time.unscaledTime;
            return;
        }
        if (!tracking) return;

        if (primary.press.isPressed)
        {
            if (fired) return;
            Vector2 move = position - start;
            if (move.magnitude < Screen.width * SwipeFraction) return;
            float x = Mathf.Abs(move.x);
            float y = Mathf.Abs(move.y);
            if (x > y * DominantAxis) Fire(move.x > 0f ? GameAction.Give : GameAction.Reject);
            else if (y > x * DominantAxis) Fire(GameAction.Blast);
            return;
        }

        if (primary.press.wasReleasedThisFrame)
        {
            if (!fired && Time.unscaledTime - startTime <= TapTime) Fire(GameAction.Blast);
            tracking = false;
        }
    }

    void Fire(GameAction action)
    {
        fired = true;
        GameInput.Press(action);
    }
}
