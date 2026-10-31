using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Arcade-style entry of three initials. Every letter has up/down buttons (phones have no keyboard); on a
// keyboard, A-Z types, left/right move, up/down change the letter, Backspace steps back, Enter confirms and
// Escape cancels. Reusable: Open() it with the starting letters and the two callbacks.
public class InitialsEntry
{
    public const int Letters = 3;

    readonly GameObject panel;
    readonly Text header;
    readonly SimpleButton[] letterButtons = new SimpleButton[Letters];
    readonly char[] letters = new char[Letters];

    Action<string> confirmed;
    Action cancelled;
    int cursor;

    public bool IsOpen => panel.activeSelf;

    public InitialsEntry(Transform parent)
    {
        // The ticker lives on an always-active holder; the panel itself is hidden while closed.
        var holder = UiKit.Fill("InitialsEntry", parent);
        holder.gameObject.AddComponent<Ticker>().OnUpdate = ReadKeys;
        var panelRoot = UiKit.Fill("Panel", holder);
        panel = panelRoot.gameObject;

        var center = new Vector2(0.5f, 0.5f);
        var card = UiKit.Card("Card", panelRoot, new Vector2(660f, 500f));
        card.gameObject.AddComponent<ScreenFit>().Init(holder, card, 500f, 24f);

        header = UiKit.Label(card, "Header", "", 66, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 190f), new Vector2(620f, 76f), UiKit.Accent, true);

        for (int i = 0; i < Letters; i++)
        {
            int column = i;
            float x = (i - 1) * 140f;
            UiKit.Button(card, "Up" + i, "↑", 33, center, center, new Vector2(x, 100f), new Vector2(96f, 50f), () => Step(column, 1));
            var box = UiKit.Button(card, "Letter" + i, "A", 88, center, center, new Vector2(x, 15f), new Vector2(96f, 110f), () => cursor = column);
            box.Label.font = UiKit.TitleFont;
            letterButtons[i] = box;
            UiKit.Button(card, "Down" + i, "↓", 33, center, center, new Vector2(x, -70f), new Vector2(96f, 50f), () => Step(column, -1));
        }

        var hint = UiKit.Label(card, "Hint", "", 22, TextAnchor.MiddleCenter, center, center, new Vector2(0f, -135f), new Vector2(620f, 32f), UiKit.Dim);
        UiKit.Bind(hint, "initials.hint");

        var ok = UiKit.Button(card, "Ok", "", 33, center, center, new Vector2(-120f, -200f), new Vector2(210f, 52f), Confirm);
        UiKit.Bind(ok.Label, "initials.ok");
        var back = UiKit.Button(card, "Back", "", 33, center, center, new Vector2(120f, -200f), new Vector2(210f, 52f), Cancel);
        UiKit.Bind(back.Label, "back");

        panel.SetActive(false);
    }

    // rank: shown in the title, "NEW RECORD #3".
    public void Open(string initials, int rank, Action<string> onConfirm, Action onCancel)
    {
        confirmed = onConfirm;
        cancelled = onCancel;
        string clean = LocalRanking.CleanInitials(initials);
        for (int i = 0; i < Letters; i++) letters[i] = i < clean.Length ? clean[i] : 'A';
        cursor = 0;
        UiKit.Bind(header, () => Loc.T("initials.header", rank), true);
        panel.SetActive(true);
        Refresh();
    }

    public void Close() => panel.SetActive(false);

    void Step(int column, int delta)
    {
        cursor = column;
        letters[column] = (char)('A' + Mathf.RoundToInt(Mathf.Repeat(letters[column] - 'A' + delta, 26)));
        Refresh();
    }

    void Confirm()
    {
        var done = confirmed;
        Close();
        done?.Invoke(new string(letters));
    }

    void Cancel()
    {
        var done = cancelled;
        Close();
        done?.Invoke();
    }

    void Refresh()
    {
        for (int i = 0; i < Letters; i++)
        {
            letterButtons[i].Text = letters[i].ToString();
            letterButtons[i].Highlighted = i == cursor;
        }
    }

    void ReadKeys()
    {
        if (!IsOpen) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        for (int k = 0; k < 26; k++)
        {
            if (!keyboard[Key.A + k].wasPressedThisFrame) continue;
            letters[cursor] = (char)('A' + k);
            cursor = Mathf.Min(cursor + 1, Letters - 1);
            Refresh();
        }
        if (keyboard.leftArrowKey.wasPressedThisFrame) { cursor = Mathf.Max(cursor - 1, 0); Refresh(); }
        if (keyboard.rightArrowKey.wasPressedThisFrame) { cursor = Mathf.Min(cursor + 1, Letters - 1); Refresh(); }
        if (keyboard.upArrowKey.wasPressedThisFrame) Step(cursor, 1);
        if (keyboard.downArrowKey.wasPressedThisFrame) Step(cursor, -1);
        if (keyboard.backspaceKey.wasPressedThisFrame) { cursor = Mathf.Max(cursor - 1, 0); Refresh(); }
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Confirm();
        else if (keyboard.escapeKey.wasPressedThisFrame) Cancel();
    }
}
