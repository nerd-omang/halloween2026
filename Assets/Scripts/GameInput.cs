using UnityEngine;
using UnityEngine.InputSystem;

public enum GameAction { Give, Reject, Blast }

// The one place the game reads player input from. Desktop: left click gives, right click sends away,
// middle click / wheel / Space blasts. Touch: on-screen buttons call Press. Pointer helpers cover mouse,
// touch and pen alike, so screens and the model viewer work on a phone as well.
public static class GameInput
{
    static readonly int[] pressedFrame = { -1, -1, -1 };
    static bool touchSeen;

    // True on a phone/tablet browser, or once a touch has been seen. Touch UI (game buttons) is shown while this holds.
    public static bool UsingTouch => Application.isMobilePlatform || touchSeen;

    // Call once per frame (GameManager does) so a first touch switches the touch UI on.
    public static void Poll()
    {
        var touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) touchSeen = true;
    }

    // An on-screen button was pressed. The press stays valid for this frame and the next, so it does not
    // matter whether the button or the hand reads first; it is not buffered any longer than that.
    public static void Press(GameAction action)
    {
        pressedFrame[(int)action] = Time.frameCount;
    }

    // True once per press: from an on-screen button or from the desktop controls.
    public static bool Consume(GameAction action)
    {
        int i = (int)action;
        if (pressedFrame[i] >= 0 && pressedFrame[i] >= Time.frameCount - 1)
        {
            pressedFrame[i] = -1;
            return true;
        }
        return DevicePressed(action);
    }

    static bool DevicePressed(GameAction action)
    {
        var mouse = Mouse.current;
        switch (action)
        {
            case GameAction.Give:
                // A click on a UI control (settings gear...) is not a game action.
                return mouse != null && mouse.leftButton.wasPressedThisFrame && !UiKit.OverControl(mouse.position.ReadValue());
            case GameAction.Reject:
                return mouse != null && mouse.rightButton.wasPressedThisFrame && !UiKit.OverControl(mouse.position.ReadValue());
            default:
                var keyboard = Keyboard.current;
                return (mouse != null && (mouse.middleButton.wasPressedThisFrame || Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f))
                    || (keyboard != null && keyboard.spaceKey.wasPressedThisFrame);
        }
    }

    // ---- Pointer (mouse, touch or pen) ----

    public static bool PointerPressedThisFrame => Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
    public static bool PointerHeld => Pointer.current != null && Pointer.current.press.isPressed;
    public static Vector2 PointerPosition => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
    public static Vector2 PointerDelta => Pointer.current != null ? Pointer.current.delta.ReadValue() : Vector2.zero;

    // Any first click, tap or key: browsers only allow audio after a user gesture.
    public static bool AnyPressThisFrame
    {
        get
        {
            var keyboard = Keyboard.current;
            return PointerPressedThisFrame || (keyboard != null && keyboard.anyKey.wasPressedThisFrame);
        }
    }
}
