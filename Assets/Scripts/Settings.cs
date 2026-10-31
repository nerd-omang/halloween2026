using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Player settings: volumes and the dither look. Saved in PlayerPrefs and applied on startup and live.
//
// The dither options act on the shared DitherPixel material and the URP asset's render scale. Their defaults
// are read from those assets when first found, so the .mat stays the single source of truth. In the Editor the
// original values are put back when play mode ends, so playing never dirties the assets on disk.
public static class Settings
{
    const string MasterKey = "set_master";
    const string SfxKey = "set_sfx";
    const string MusicKey = "set_music";
    const string DitherKey = "set_dither";
    const string PatternKey = "set_pattern";
    const string LevelsKey = "set_levelcount";
    const string DarkKey = "set_darkcount";
    const string FlatKey = "set_flat";
    const string InitKey = "set_init";
    const string QualityKey = "set_quality";

    // _MatrixSize values: blue noise, then Bayer-style fine to coarse.
    public static readonly int[] PatternValues = { 1, 2, 4, 8 };
    // Color level choices: a hand-picked spread up to 16 (primes mixed in, 13 is the tuned default), then steps of 8.
    public static readonly int[] LevelSteps = BuildLevelSteps();
    // Render scale choices; index 1 is replaced by the URP asset's own value (1/3).
    static readonly float[] QualityScale = { 0.25f, 1f / 3f, 0.5f, 1f };
    // With dithering off, quantization is made so fine that banding is barely visible.
    const float OffLevels = 64f;

    public const int OptionCount = 4;

    static float master = 1f;
    static float sfx = 1f;
    static float music = 1f;
    static bool ditherOn = true;
    static int pattern;
    static bool flatOn = true;
    static int darkCount = -1; // -1 = the material's own value
    static int levelCount = -1; // -1 = the material's own value
    static int quality = 1;

    static Material material;
    static UniversalRenderPipelineAsset pipeline;
    static bool captured;
    static int pendingPreset = -1;
    static float defaultStrength;
    static float defaultLevels;
    static float defaultMatrix;
    static int defaultLevelCount;
    static int defaultDarkCount;
    static float defaultDarkLevels;
    static float defaultFlat;
    static float defaultScale;
    static int defaultPattern;

    public static float Master { get => master; set { master = Mathf.Clamp01(value); AudioManager.MasterVolume = master; PlayerPrefs.SetFloat(MasterKey, master); } }
    public static float Sfx { get => sfx; set { sfx = Mathf.Clamp01(value); AudioManager.SfxVolume = sfx; PlayerPrefs.SetFloat(SfxKey, sfx); } }
    public static float Music { get => music; set { music = Mathf.Clamp01(value); AudioManager.MusicVolume = music; PlayerPrefs.SetFloat(MusicKey, music); } }

    public static bool DitherOn { get => ditherOn; set { ditherOn = value; PlayerPrefs.SetInt(DitherKey, value ? 1 : 0); ApplyGraphics(); } }
    public static int Pattern { get => pattern < 0 ? defaultPattern : pattern; set { pattern = Mathf.Clamp(value, 0, OptionCount - 1); PlayerPrefs.SetInt(PatternKey, pattern); ApplyGraphics(); } }

    public static int LevelCount => levelCount < 0 ? defaultLevelCount : levelCount;

    // Position of the current level count in LevelSteps (the nearest step if it is not one).
    public static int LevelIndex
    {
        get => NearestStep(LevelCount);
        set
        {
            levelCount = LevelSteps[Mathf.Clamp(value, 0, LevelSteps.Length - 1)];
            PlayerPrefs.SetInt(LevelsKey, levelCount);
            ApplyGraphics();
        }
    }

    // Flat areas keep their color instead of turning into dither dots. On is a fixed threshold set here (not
    // read from the material, so a stray material value can't change the look); off sets it to 0.
    const float FlatOnValue = 0.000001f;

    public static bool FlatOn
    {
        get => flatOn;
        set
        {
            flatOn = value;
            PlayerPrefs.SetInt(FlatKey, flatOn ? 1 : 0);
            ApplyGraphics();
        }
    }

    public static int DarkCount => darkCount < 0 ? defaultDarkCount : darkCount;

    public static int DarkIndex
    {
        get => NearestStep(DarkCount);
        set
        {
            darkCount = LevelSteps[Mathf.Clamp(value, 0, LevelSteps.Length - 1)];
            PlayerPrefs.SetInt(DarkKey, darkCount);
            ApplyGraphics();
        }
    }

    static int NearestStep(int count)
    {
        int best = 0;
        for (int i = 0; i < LevelSteps.Length; i++)
        {
            if (Mathf.Abs(LevelSteps[i] - count) < Mathf.Abs(LevelSteps[best] - count)) best = i;
        }
        return best;
    }

    static int[] BuildLevelSteps()
    {
        var steps = new System.Collections.Generic.List<int> { 2, 3, 4, 6, 7, 11, 13, 16 };
        for (int i = 24; i <= 64; i += 8) steps.Add(i);
        return steps.ToArray();
    }
    public static int Quality { get => quality; set { quality = Mathf.Clamp(value, 0, OptionCount - 1); PlayerPrefs.SetInt(QualityKey, quality); ApplyGraphics(); } }

    public static void Save() => PlayerPrefs.Save();

    // The dither material may not be loaded yet at startup; call every frame until the saved look is applied.
    public static void Tick()
    {
        if (captured) return;
        if (EnsureAssets()) ApplyGraphics();
    }

    public const int PresetCount = 5;

    // Look presets: 0 rough, 1 original (the material's tuned look), 2 gentle, 3 soft, 4 sharp. Volumes are left alone.
    public static void ApplyPreset(int preset)
    {
        EnsureAssets();
        switch (preset)
        {
            case 0:
                SetGraphics(true, defaultPattern, 6, 6, 0);
                break;
            case 2:
                SetGraphics(true, defaultPattern, 16, 16, 2);
                break;
            case 3:
                SetGraphics(true, 1, 24, 24, 2);
                break;
            case 4:
                SetGraphics(false, defaultPattern, -1, -1, 3);
                break;
            default:
                SetGraphics(true, defaultPattern, -1, -1, 1);
                break;
        }
    }

    // The preset the current look matches, or -1 when it was fine-tuned by hand.
    public static int CurrentPreset()
    {
        EnsureAssets();
        bool flatDefault = FlatOn;
        if (flatDefault && DitherOn && Pattern == defaultPattern && LevelCount == defaultLevelCount && DarkCount == defaultDarkCount && Quality == 1) return 1;
        if (flatDefault && DitherOn && Pattern == defaultPattern && LevelCount == 6 && DarkCount == 6 && Quality == 0) return 0;
        if (flatDefault && DitherOn && Pattern == defaultPattern && LevelCount == 16 && DarkCount == 16 && Quality == 2) return 2;
        if (flatDefault && DitherOn && Pattern == 1 && LevelCount == 24 && DarkCount == 24 && Quality == 2) return 3;
        if (!DitherOn && Quality == 3) return 4;
        return -1;
    }

    // Level counts below zero mean the material's own value.
    static void SetGraphics(bool dither, int patternIndex, int levelValue, int darkValue, int qualityIndex)
    {
        ditherOn = dither;
        pattern = patternIndex;
        levelCount = levelValue;
        darkCount = darkValue;
        quality = qualityIndex;
        // Every preset keeps flat areas clean.
        flatOn = true;
        PlayerPrefs.SetInt(FlatKey, 1);
        PlayerPrefs.SetInt(DitherKey, dither ? 1 : 0);
        PlayerPrefs.SetInt(PatternKey, pattern);
        PlayerPrefs.SetInt(QualityKey, quality);
        if (levelValue < 0) PlayerPrefs.DeleteKey(LevelsKey);
        else PlayerPrefs.SetInt(LevelsKey, levelValue);
        if (darkValue < 0) PlayerPrefs.DeleteKey(DarkKey);
        else PlayerPrefs.SetInt(DarkKey, darkValue);
        ApplyGraphics();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        material = null;
        pipeline = null;
        captured = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Startup()
    {
        Application.quitting -= RestoreAssets;
        Application.quitting += RestoreAssets;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
        Load();
        // First run on a phone: start with the lightest look; the saved choice wins afterwards.
        if (!PlayerPrefs.HasKey(InitKey))
        {
            if (Application.isMobilePlatform) pendingPreset = 0; // InitKey is written once it has been applied
            else PlayerPrefs.SetInt(InitKey, 1);
        }
        EnsureAssets();
        ApplyAudio();
        ApplyGraphics();
    }

    static void Load()
    {
        master = PlayerPrefs.GetFloat(MasterKey, 1f);
        sfx = PlayerPrefs.GetFloat(SfxKey, 1f);
        music = PlayerPrefs.GetFloat(MusicKey, 1f);
        ditherOn = PlayerPrefs.GetInt(DitherKey, 1) != 0;
        pattern = -1; // resolved to the material's own pattern once it is known
        levelCount = PlayerPrefs.HasKey(LevelsKey) ? Mathf.Clamp(PlayerPrefs.GetInt(LevelsKey), 2, 255) : -1;
        flatOn = PlayerPrefs.GetInt(FlatKey, 1) != 0;
        darkCount = PlayerPrefs.HasKey(DarkKey) ? Mathf.Clamp(PlayerPrefs.GetInt(DarkKey), 2, 255) : -1;
        quality = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, 1), 0, OptionCount - 1);
        if (PlayerPrefs.HasKey(PatternKey)) pattern = Mathf.Clamp(PlayerPrefs.GetInt(PatternKey), 0, OptionCount - 1);
    }

    static void ApplyAudio()
    {
        AudioManager.MasterVolume = master;
        AudioManager.SfxVolume = sfx;
        AudioManager.MusicVolume = music;
    }

    // Finds the dither material and the active URP asset and remembers their original values.
    // Returns false while the material is not loaded yet; callers just try again later.
    public static bool EnsureAssets()
    {
        if (captured) return true;

        if (material == null)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (candidate.shader != null && candidate.shader.name == "Fangame/DitherPixel" && !candidate.name.EndsWith("(Instance)"))
                {
                    material = candidate;
                    break;
                }
            }
        }
        if (pipeline == null) pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (material == null || pipeline == null) return false;

        defaultStrength = material.GetFloat("_DitherStrength");
        defaultLevels = material.GetFloat("_Levels");
        defaultDarkLevels = material.GetFloat("_DarkLevels");
        defaultFlat = material.GetFloat("_FlatThreshold");
        defaultMatrix = material.GetFloat("_MatrixSize");
        defaultLevelCount = Mathf.Max(2, Mathf.RoundToInt(defaultLevels));
        defaultDarkCount = Mathf.Max(2, Mathf.RoundToInt(defaultDarkLevels));
        defaultScale = pipeline.renderScale;
        QualityScale[1] = defaultScale;

        defaultPattern = 0;
        for (int i = 0; i < PatternValues.Length; i++)
        {
            if (Mathf.Approximately(PatternValues[i], defaultMatrix)) defaultPattern = i;
        }
        if (pattern < 0) pattern = defaultPattern;
        captured = true;
        if (pendingPreset >= 0)
        {
            int preset = pendingPreset;
            pendingPreset = -1;
            ApplyPreset(preset);
            PlayerPrefs.SetInt(InitKey, 1);
            PlayerPrefs.Save();
        }
        return true;
    }

    public static void ApplyGraphics()
    {
        if (!EnsureAssets()) return;
        if (pattern < 0) pattern = defaultPattern;

        float count = ditherOn ? (levelCount < 0 ? defaultLevels : levelCount) : OffLevels;
        material.SetFloat("_Levels", count);
        // Shadow levels differing from the color levels turns the shadow-specific quantization on.
        material.SetFloat("_DarkLevels", ditherOn ? (darkCount < 0 ? defaultDarkLevels : darkCount) : OffLevels);
        material.SetFloat("_DitherStrength", ditherOn ? defaultStrength : 0f);
        material.SetFloat("_MatrixSize", PatternValues[pattern]);
        material.SetFloat("_FlatThreshold", FlatOn ? FlatOnValue : 0f);
        pipeline.renderScale = QualityScale[quality];
    }

    // Text for the resolution choices, e.g. "33%".
    public static string QualityLabel(int index)
    {
        EnsureAssets();
        float scale = QualityScale[Mathf.Clamp(index, 0, OptionCount - 1)];
        return Mathf.RoundToInt(scale * 100f) + "%";
    }

    static void RestoreAssets()
    {
        if (!captured) return;
#if UNITY_EDITOR
        if (material != null)
        {
            material.SetFloat("_DitherStrength", defaultStrength);
            material.SetFloat("_Levels", defaultLevels);
            material.SetFloat("_DarkLevels", defaultDarkLevels);
            material.SetFloat("_FlatThreshold", defaultFlat);
            material.SetFloat("_MatrixSize", defaultMatrix);
        }
        if (pipeline != null) pipeline.renderScale = defaultScale;
#endif
    }

#if UNITY_EDITOR
    static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode) RestoreAssets();
    }
#endif
}

// Several things can want the game paused at once (settings, the rotate notice); it runs again when none do.
public static class GamePause
{
    static readonly HashSet<object> holders = new HashSet<object>();

    public static void Hold(object owner)
    {
        holders.Add(owner);
        Apply();
    }

    public static void Release(object owner)
    {
        if (holders.Remove(owner)) Apply();
    }

    static void Apply() => Time.timeScale = holders.Count > 0 ? 0f : 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => holders.Clear();
}
