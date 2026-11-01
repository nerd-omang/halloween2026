using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Modal settings with two tabs. General: volumes, a look preset and the language. Advanced look: every
// dither option by hand. Opened from the title menu or, during play, with Esc or the gear icon; the in-game
// version pauses the game (Time.timeScale) until it is closed.
public class SettingsScreen
{
    const float BoxWidth = 860f;
    const float BoxHeight = 760f;
    const float LabelX = 36f;
    const float ControlX = 270f;
    const float OptionWidth = 130f;
    const float OptionStep = 138f;

    public event Action Closed;
    public event Action GiveUpClicked;

    readonly GameObject panel;
    readonly GameObject gear;
    readonly GameObject[] tabPages = new GameObject[2];
    readonly SimpleButton[] tabButtons = new SimpleButton[2];

    // General tab
    readonly SimpleSlider[] sliders = new SimpleSlider[3];
    readonly Text[] sliderValues = new Text[3];
    readonly SimpleButton[] presetButtons = new SimpleButton[Settings.PresetCount];
    Text presetHint;
    readonly SimpleButton[] languageButtons = new SimpleButton[2];
    readonly SimpleButton[] shareButtons = new SimpleButton[2];
    SimpleButton shareLocalButton;
    bool shareLocalBusy;
    string shareLocalNote;
    readonly SimpleButton[] skipIntroButtons = new SimpleButton[2];
    GameObject shareRow;
    GameObject confirmReset;
    GameObject confirmGiveUp;
    GameObject giveUpButton;
    Text resetDone;

    // Advanced look tab
    readonly SimpleButton[] ditherButtons = new SimpleButton[2];
    readonly SimpleButton[] patternButtons = new SimpleButton[Settings.OptionCount];
    SimpleSlider levelSlider;
    Text levelValue;
    SimpleSlider darkSlider;
    Text darkValue;
    readonly SimpleButton[] flatButtons = new SimpleButton[2];
    readonly SimpleButton[] qualityButtons = new SimpleButton[Settings.OptionCount];

    bool paused;

    public bool IsOpen => panel.activeSelf;

    public SettingsScreen(Transform parent)
    {
        var root = UiKit.Fill("SettingsScreen", parent);
        panel = root.gameObject;
        var shade = panel.AddComponent<Image>();
        shade.color = new Color(0.03f, 0.01f, 0.06f, 0.75f);

        var center = new Vector2(0.5f, 0.5f);
        var box = UiKit.Rect("Box", root, center, center, Vector2.zero, new Vector2(BoxWidth, BoxHeight));
        var boxImage = box.gameObject.AddComponent<Image>();
        UiKit.StyleBox(boxImage, UiKit.PixelBox(UiKit.PanelColor, UiKit.PanelEdge, UiKit.Ink));
        boxImage.raycastTarget = false;
        box.gameObject.AddComponent<ScreenFit>().Init(root, box, BoxHeight, 24f);

        var topLeft = new Vector2(0f, 1f);
        var topCenter = new Vector2(0.5f, 1f);
        UiKit.Bind(UiKit.Label(box, "Header", "", 66, TextAnchor.MiddleCenter, topCenter, topCenter, new Vector2(0f, -10f), new Vector2(700f, 76f), UiKit.Accent, true),
            "settings.header", true);

        string[] tabKeys = { "settings.tabGeneral", "settings.tabLook" };
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            tabButtons[i] = UiKit.Button(box, "Tab" + i, "", 22, topLeft, topLeft, new Vector2(LabelX + i * 218f, -92f), new Vector2(210f, 40f), () => SetTab(index));
            UiKit.Bind(tabButtons[i].Label, tabKeys[i]);
            tabPages[i] = UiKit.Fill("Page" + i, box).gameObject;
        }

        BuildGeneral(tabPages[0].transform);
        BuildAdvanced(tabPages[1].transform);

        var bottomRight = new Vector2(1f, 0f);
        var close = UiKit.Button(box, "Close", "", 33, bottomRight, bottomRight, new Vector2(-LabelX, 22f), new Vector2(220f, 48f), Close);
        UiKit.Bind(close.Label, "settings.close");

        // In-game gear, under the hearts.
        var topRight = new Vector2(1f, 1f);
        var gearButton = UiKit.IconButton(parent, "SettingsGear", UiKit.GearIcon(), topRight, topRight, new Vector2(-UiKit.MarginX, -UiKit.MarginY - 62f), new Vector2(48f, 48f),
            new Vector2(33f, 33f), () => Open(true));
        gear = gearButton.gameObject;
        gear.SetActive(false);

        // In-game only: leave the run and go back to the title. Top right, in the danger color.
        var topRightCorner = new Vector2(1f, 1f);
        var giveUp = UiKit.Button(box, "GiveUp", "", 22, topRightCorner, topRightCorner, new Vector2(-LabelX, -20f), new Vector2(280f, 40f), OpenGiveUpConfirm);
        UiKit.Bind(giveUp.Label, "settings.giveUp");
        giveUp.Label.color = UiKit.Danger;
        giveUpButton = giveUp.gameObject;

        confirmReset = BuildResetConfirm(root);
        confirmGiveUp = BuildGiveUpConfirm(root);

        Loc.Changed += Refresh;
        panel.SetActive(false);
    }

    GameObject BuildResetConfirm(RectTransform root)
    {
        var center = new Vector2(0.5f, 0.5f);
        var modal = UiKit.Fill("ConfirmReset", root);
        modal.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.01f, 0.06f, 0.6f);
        var card = UiKit.Card("Card", modal, new Vector2(660f, 280f));
        card.gameObject.AddComponent<ScreenFit>().Init(modal, card, 280f, 24f);
        UiKit.Bind(UiKit.Label(card, "Text", "", 33, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 45f), new Vector2(600f, 130f), Color.white), "settings.resetAsk");
        var yes = UiKit.Button(card, "Yes", "", 33, center, center, new Vector2(-140f, -90f), new Vector2(250f, 52f), () =>
        {
            CloseResetConfirm();
            Progress.ResetAll(); // settings are kept; a running game keeps its own score
            resetDone.gameObject.SetActive(true);
        });
        UiKit.Bind(yes.Label, "settings.resetYes");
        yes.Label.color = UiKit.Danger;
        var no = UiKit.Button(card, "No", "", 33, center, center, new Vector2(140f, -90f), new Vector2(250f, 52f), CloseResetConfirm);
        UiKit.Bind(no.Label, "settings.resetNo");
        modal.gameObject.SetActive(false);
        return modal.gameObject;
    }

    GameObject BuildGiveUpConfirm(RectTransform root)
    {
        var center = new Vector2(0.5f, 0.5f);
        var modal = UiKit.Fill("ConfirmGiveUp", root);
        modal.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.01f, 0.06f, 0.6f);
        var card = UiKit.Card("Card", modal, new Vector2(700f, 280f));
        card.gameObject.AddComponent<ScreenFit>().Init(modal, card, 280f, 24f);
        UiKit.Bind(UiKit.Label(card, "Text", "", 33, TextAnchor.MiddleCenter, center, center, new Vector2(0f, 45f), new Vector2(640f, 130f), Color.white), "settings.giveUpAsk");
        var yes = UiKit.Button(card, "Yes", "", 33, center, center, new Vector2(-140f, -90f), new Vector2(250f, 52f), () =>
        {
            CloseGiveUpConfirm();
            GiveUpClicked?.Invoke();
        });
        UiKit.Bind(yes.Label, "settings.giveUpYes");
        yes.Label.color = UiKit.Danger;
        var no = UiKit.Button(card, "No", "", 33, center, center, new Vector2(140f, -90f), new Vector2(250f, 52f), CloseGiveUpConfirm);
        UiKit.Bind(no.Label, "settings.resetNo");
        modal.gameObject.SetActive(false);
        return modal.gameObject;
    }

    void OpenGiveUpConfirm()
    {
        confirmGiveUp.SetActive(true);
        confirmGiveUp.transform.SetAsLastSibling();
        UiKit.ModalRoot = confirmGiveUp.transform;
    }

    void CloseGiveUpConfirm()
    {
        confirmGiveUp.SetActive(false);
        if (UiKit.ModalRoot == confirmGiveUp.transform) UiKit.ModalRoot = panel.transform;
    }

    void OpenResetConfirm()
    {
        confirmReset.SetActive(true);
        confirmReset.transform.SetAsLastSibling();
        UiKit.ModalRoot = confirmReset.transform;
    }

    void CloseResetConfirm()
    {
        confirmReset.SetActive(false);
        if (UiKit.ModalRoot == confirmReset.transform) UiKit.ModalRoot = panel.transform;
    }

    void BuildGeneral(Transform page)
    {
        var topLeft = new Vector2(0f, 1f);
        Section(page, "Audio", "settings.audio", -144f);
        string[] sliderKeys = { "settings.master", "settings.sfx", "settings.music" };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            float y = -180f - i * 42f;
            RowLabel(page, "SliderLabel" + i, sliderKeys[i], y);
            var slider = UiKit.Slider(page, "Slider" + i, topLeft, topLeft, new Vector2(ControlX, y - 2f), new Vector2(420f, 40f));
            slider.Changed += value => OnSlider(index, value);
            sliders[i] = slider;
            sliderValues[i] = UiKit.Label(page, "SliderValue" + i, "", 22, TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(ControlX + 440f, y), new Vector2(110f, 40f), Color.white);
        }

        Section(page, "Graphics", "settings.graphics", -314f);
        RowLabel(page, "PresetLabel", "settings.preset", -348f);
        string[] presetKeys = { "settings.presetRough", "settings.presetOriginal", "settings.presetGentle", "settings.presetSoft", "settings.presetSharp" };
        for (int i = 0; i < Settings.PresetCount; i++)
        {
            int preset = i;
            presetButtons[i] = Option(page, "Preset" + i, -348f, i, () => Settings.ApplyPreset(preset), presetKeys[i], 106f, 112f);
        }
        presetHint = UiKit.Label(page, "PresetHint", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(LabelX, -394f), new Vector2(BoxWidth - 2f * LabelX, 56f), UiKit.Dim);

        RowLabel(page, "LanguageLabel", "settings.language", -466f);
        languageButtons[0] = Option(page, "LangKo", -466f, 0, () => Loc.Current = Lang.Ko, null);
        languageButtons[0].Text = "한국어";
        languageButtons[1] = Option(page, "LangEn", -466f, 1, () => Loc.Current = Lang.En, null);
        languageButtons[1].Text = "English";

        RowLabel(page, "SkipIntroLabel", "settings.skipIntro", -512f);
        skipIntroButtons[0] = Option(page, "SkipIntroOn", -512f, 0, () => Settings.SkipIntro = true, "settings.on");
        skipIntroButtons[1] = Option(page, "SkipIntroOff", -512f, 1, () => Settings.SkipIntro = false, "settings.off");

        // Only offered when a ranking server is configured.
        shareRow = UiKit.Fill("ShareRow", page).gameObject;
        var shareLabel = UiKit.Label(shareRow.transform, "ShareLabel", "", 22, TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(LabelX, -558f), new Vector2(ControlX - LabelX - 8f, 40f), UiKit.Dim);
        UiKit.Bind(shareLabel, "settings.share");
        shareButtons[0] = Option(shareRow.transform, "ShareOn", -558f, 0, () => Settings.ShareOnline = true, "settings.on");
        shareButtons[1] = Option(shareRow.transform, "ShareOff", -558f, 1, () => Settings.ShareOnline = false, "settings.off");
        UiKit.Bind(UiKit.Label(shareRow.transform, "ShareHint", "", 11, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(LabelX, -604f), new Vector2(BoxWidth - 2f * LabelX - 240f, 56f), UiKit.Dim),
            "settings.shareHint");

        shareLocalButton = UiKit.Button(shareRow.transform, "ShareLocal", "", 20, topLeft, topLeft, new Vector2(BoxWidth - LabelX - 250f, -558f), new Vector2(250f, 40f), ShareLocalRecords);

        // Apart from the other settings, at the very bottom.
        var reset = UiKit.Button(page, "ResetAll", "", 22, topLeft, topLeft, new Vector2(LabelX, -696f), new Vector2(300f, 40f), OpenResetConfirm);
        UiKit.Bind(reset.Label, "settings.resetAll");
        reset.Label.color = UiKit.Danger;
        resetDone = UiKit.Label(page, "ResetDone", "", 22, TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(LabelX + 312f, -696f), new Vector2(240f, 40f), UiKit.Accent);
        UiKit.Bind(resetDone, "settings.resetDone");
        resetDone.gameObject.SetActive(false);
    }

    void BuildAdvanced(Transform page)
    {
        var topLeft = new Vector2(0f, 1f);

        RowLabel(page, "DitherLabel", "settings.dither", -150f);
        ditherButtons[0] = Option(page, "DitherOn", -150f, 0, () => Settings.DitherOn = true, "settings.on");
        ditherButtons[1] = Option(page, "DitherOff", -150f, 1, () => Settings.DitherOn = false, "settings.off");

        RowLabel(page, "PatternLabel", "settings.pattern", -194f);
        string[] patternKeys = { "settings.patNoise", "settings.patFine", "settings.patMedium", "settings.patCoarse" };
        for (int i = 0; i < Settings.OptionCount; i++)
        {
            int index = i;
            patternButtons[i] = Option(page, "Pattern" + i, -194f, i, () => Settings.Pattern = index, patternKeys[i]);
        }

        RowLabel(page, "LevelsLabel", "settings.levels", -238f);
        levelSlider = UiKit.Slider(page, "LevelSlider", topLeft, topLeft, new Vector2(ControlX, -240f), new Vector2(420f, 40f));
        levelSlider.Steps = Settings.LevelSteps.Length;
        levelSlider.Changed += value =>
        {
            Settings.LevelIndex = Mathf.RoundToInt(value * (Settings.LevelSteps.Length - 1));
            levelValue.text = Settings.LevelCount.ToString();
            RefreshPresets();
        };
        levelValue = UiKit.Label(page, "LevelValue", "", 22, TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(ControlX + 440f, -238f), new Vector2(110f, 40f), Color.white);

        RowLabel(page, "DarkLabel", "settings.dark", -282f);
        darkSlider = UiKit.Slider(page, "DarkSlider", topLeft, topLeft, new Vector2(ControlX, -284f), new Vector2(420f, 40f));
        darkSlider.Steps = Settings.LevelSteps.Length;
        darkSlider.Changed += value =>
        {
            Settings.DarkIndex = Mathf.RoundToInt(value * (Settings.LevelSteps.Length - 1));
            darkValue.text = Settings.DarkCount.ToString();
            RefreshPresets();
        };
        darkValue = UiKit.Label(page, "DarkValue", "", 22, TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(ControlX + 440f, -282f), new Vector2(110f, 40f), Color.white);

        RowLabel(page, "FlatLabel", "settings.flat", -326f);
        flatButtons[0] = Option(page, "FlatOn", -326f, 0, () => Settings.FlatOn = true, "settings.on");
        flatButtons[1] = Option(page, "FlatOff", -326f, 1, () => Settings.FlatOn = false, "settings.off");

        RowLabel(page, "QualityLabel", "settings.resolution", -370f);
        for (int i = 0; i < Settings.OptionCount; i++)
        {
            int index = i;
            qualityButtons[i] = Option(page, "Quality" + i, -370f, i, () => Settings.Quality = index, null);
        }

        UiKit.Bind(UiKit.Label(page, "Hint", "", 22, TextAnchor.UpperLeft, topLeft, topLeft, new Vector2(LabelX, -420f), new Vector2(BoxWidth - 2f * LabelX, 56f), UiKit.Dim),
            "settings.resolutionHint");
    }

    void Section(Transform page, string name, string key, float y)
    {
        var topLeft = new Vector2(0f, 1f);
        UiKit.Bind(UiKit.Label(page, name, "", 22, TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(LabelX, y), new Vector2(400f, 28f), UiKit.Accent), key);
    }

    void RowLabel(Transform page, string name, string key, float y)
    {
        var topLeft = new Vector2(0f, 1f);
        UiKit.Bind(UiKit.Label(page, name, "", 22, TextAnchor.MiddleLeft, topLeft, topLeft, new Vector2(LabelX, y), new Vector2(ControlX - LabelX - 8f, 40f), UiKit.Dim), key);
    }

    SimpleButton Option(Transform page, string name, float y, int slot, Action onClick, string textKey, float width = OptionWidth, float step = OptionStep)
    {
        var topLeft = new Vector2(0f, 1f);
        var button = UiKit.Button(page, name, "", 22, topLeft, topLeft, new Vector2(ControlX + slot * step, y), new Vector2(width, 40f), () =>
        {
            onClick();
            Refresh();
        });
        if (textKey != null) UiKit.Bind(button.Label, textKey);
        return button;
    }

    void SetTab(int index)
    {
        for (int i = 0; i < 2; i++)
        {
            tabPages[i].SetActive(i == index);
            tabButtons[i].Highlighted = i == index;
        }
    }

    void OnSlider(int index, float value)
    {
        if (index == 0) Settings.Master = value;
        else if (index == 1) Settings.Sfx = value;
        else Settings.Music = value;
        sliderValues[index].text = Mathf.RoundToInt(value * 100f) + "%";
    }

    void RefreshPresets()
    {
        int preset = Settings.CurrentPreset();
        for (int i = 0; i < presetButtons.Length; i++) presetButtons[i].Highlighted = i == preset;
        presetHint.text = Loc.T(preset < 0 ? "settings.presetCustom" : "settings.presetHint");
        presetHint.color = preset < 0 ? UiKit.Accent : UiKit.Dim;
    }

    // Syncs every control with the current settings.
    const int ShareLocalBatch = 5; // the server takes 5 results per minute from one address

    // Sends the next few saved local records to the world ranking; press again for the rest.
    void ShareLocalRecords()
    {
        if (shareLocalBusy) return;
        if (WorldBoard.Status != WorldBoard.LoadStatus.Ready)
        {
            WorldBoard.Refresh(true);
            Refresh();
            return;
        }
        // Records below the world ranking would only add to the statistics: they are skipped and counted as done.
        var skipped = new List<string>();
        var pending = LocalRanking.PendingShare(WorldBoard.Reaches, skipped);
        foreach (string text in skipped) LocalRanking.MarkShared(text);
        if (pending.Count == 0)
        {
            Refresh();
            return;
        }
        if (pending.Count > ShareLocalBatch) pending.RemoveRange(ShareLocalBatch, pending.Count - ShareLocalBatch);
        shareLocalBusy = true;
        shareLocalNote = null;
        Refresh();
        WorldBoard.SubmitMany(pending, LocalRanking.MarkShared, (ok, failed) =>
        {
            shareLocalBusy = false;
            shareLocalNote = Loc.T("settings.shareLocalDone", ok, failed);
            WorldBoard.Refresh(true);
            Refresh();
        });
    }

    void Refresh()
    {
        Settings.EnsureAssets();
        sliders[0].Set(Settings.Master);
        sliders[1].Set(Settings.Sfx);
        sliders[2].Set(Settings.Music);
        for (int i = 0; i < 3; i++) sliderValues[i].text = Mathf.RoundToInt(sliders[i].Value * 100f) + "%";
        RefreshPresets();
        languageButtons[0].Highlighted = Loc.Current == Lang.Ko;
        languageButtons[1].Highlighted = Loc.Current == Lang.En;
        skipIntroButtons[0].Highlighted = Settings.SkipIntro;
        skipIntroButtons[1].Highlighted = !Settings.SkipIntro;
        shareRow.SetActive(WorldBoard.Configured);
        shareButtons[0].Highlighted = Settings.ShareOnline;
        shareButtons[1].Highlighted = !Settings.ShareOnline;
        var boardStatus = WorldBoard.Status;
        bool boardReady = boardStatus == WorldBoard.LoadStatus.Ready;
        int pendingShare = boardReady ? LocalRanking.PendingShare(WorldBoard.Reaches).Count : 0;
        shareLocalButton.Interactable = !shareLocalBusy && (pendingShare > 0 || boardStatus == WorldBoard.LoadStatus.Failed);
        if (shareLocalBusy) shareLocalButton.Text = Loc.T("settings.shareLocalBusy");
        else if (boardStatus == WorldBoard.LoadStatus.Failed) shareLocalButton.Text = Loc.T("settings.shareLocalRetry");
        else if (!boardReady) shareLocalButton.Text = Loc.T("settings.shareLocalWait");
        else shareLocalButton.Text = shareLocalNote ?? (pendingShare > 0 ? Loc.T("settings.shareLocal", pendingShare) : Loc.T("settings.shareLocalNone"));

        bool dither = Settings.DitherOn;
        ditherButtons[0].Highlighted = dither;
        ditherButtons[1].Highlighted = !dither;
        for (int i = 0; i < Settings.OptionCount; i++)
        {
            patternButtons[i].Highlighted = i == Settings.Pattern;
            patternButtons[i].Interactable = dither;
            qualityButtons[i].Highlighted = i == Settings.Quality;
            qualityButtons[i].Text = Settings.QualityLabel(i);
        }
        levelSlider.Set((float)Settings.LevelIndex / (Settings.LevelSteps.Length - 1));
        levelSlider.Interactable = dither;
        levelValue.text = dither ? Settings.LevelCount.ToString() : "-";
        levelValue.color = dither ? Color.white : UiKit.Dim * 0.6f;
        darkSlider.Set((float)Settings.DarkIndex / (Settings.LevelSteps.Length - 1));
        darkSlider.Interactable = dither;
        darkValue.text = dither ? Settings.DarkCount.ToString() : "-";
        darkValue.color = dither ? Color.white : UiKit.Dim * 0.6f;
        flatButtons[0].Highlighted = Settings.FlatOn;
        flatButtons[1].Highlighted = !Settings.FlatOn;
        flatButtons[0].Interactable = flatButtons[1].Interactable = dither;
    }

    // pause: stop the game while the screen is up (used when opened during play).
    public void Open(bool pause)
    {
        shareLocalNote = null;
        if (IsOpen) return;
        paused = pause;
        giveUpButton.SetActive(pause);
        if (paused) GamePause.Hold(this);
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        UiKit.ModalRoot = panel.transform;
        SetTab(0);
        resetDone.gameObject.SetActive(false);
        WorldBoard.Changed -= Refresh;
        WorldBoard.Changed += Refresh; // the share button waits for the ranking
        WorldBoard.Refresh();
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        WorldBoard.Changed -= Refresh;
        confirmReset.SetActive(false);
        confirmGiveUp.SetActive(false);
        panel.SetActive(false);
        if (UiKit.ModalRoot == panel.transform || UiKit.ModalRoot == confirmReset.transform || UiKit.ModalRoot == confirmGiveUp.transform) UiKit.ModalRoot = null;
        if (paused) GamePause.Release(this);
        paused = false;
        Settings.Save();
        Closed?.Invoke();
    }

    public void SetGearVisible(bool visible) => gear.SetActive(visible);
}
