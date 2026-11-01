using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum Sfx
{
    CandyAppear,
    CandyToss,
    CandyLand,
    AnswerCorrect,
    AnswerWrong,
    HeartLost,
    VisitorShoo,
    BlastCharge,
    SheetBlowOff,
    VisitorBounce,
    PropPop,
    GameOver,
    VisitorStep,
    ComboUp,
}

[Serializable]
public class SfxEntry
{
    public Sfx id;
    public AudioClip[] clips;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 0.3f)] public float pitchJitter = 0.05f;
}

// Plays one-shot effects through a small pool of AudioSources so sounds can overlap.
// Each play picks a random clip of the entry and nudges the pitch so repeats don't sound identical.
// Master, SFX and Music volumes are static so a settings screen can set them before or after this exists;
// changes apply to sounds that are already playing.
public class AudioManager : MonoBehaviour
{
    [SerializeField] SfxEntry[] entries;
    [SerializeField] int voices = 12;

    [Header("Music")]
    [SerializeField] AudioClip music;
    [Range(0f, 1f)] [SerializeField] float musicBaseVolume = 0.35f;
    [SerializeField] float musicFadeTime = 2f;

    static float masterVolume = 1f;
    static float sfxVolume = 1f;
    static float musicVolume = 1f;

    public static AudioManager Instance { get; private set; }

    // Playback speed of the music (1 = as recorded); the combo raises it a little.
    public static float MusicPitch
    {
        get => Instance != null && Instance.musicSource != null ? Instance.musicSource.pitch : 1f;
        set
        {
            if (Instance != null && Instance.musicSource != null) Instance.musicSource.pitch = value;
        }
    }

    // 0..1. Setting clamps the value and applies it to the sounds that are playing now.
    public static float MasterVolume
    {
        get => masterVolume;
        set
        {
            masterVolume = Mathf.Clamp01(value);
            if (Instance != null) Instance.ApplyVolumes();
        }
    }

    public static float SfxVolume
    {
        get => sfxVolume;
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            if (Instance != null) Instance.ApplyVolumes();
        }
    }

    public static float MusicVolume
    {
        get => musicVolume;
        set
        {
            musicVolume = Mathf.Clamp01(value);
            if (Instance != null) Instance.ApplyVolumes();
        }
    }

    readonly Dictionary<Sfx, SfxEntry> lookup = new Dictionary<Sfx, SfxEntry>();
    AudioSource[] pool;
    float[] poolBaseVolume;
    AudioSource musicSource;
    float musicFade;
    int next;

    void Awake()
    {
        Instance = this;

        foreach (var entry in entries)
        {
            if (entry != null && entry.clips != null && entry.clips.Length > 0) lookup[entry.id] = entry;
        }

        pool = new AudioSource[voices];
        poolBaseVolume = new float[voices];
        for (int i = 0; i < voices; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            pool[i] = source;
        }

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;

        ApplyVolumes();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static void Play(Sfx id, float pitch = 1f)
    {
        if (Instance != null) Instance.PlayEntry(id, pitch);
    }

    // Call from a user input handler (first click or key). Browsers block audio before the first gesture,
    // so an early call may play silently; it never throws, and repeated calls do nothing once started.
    public static void PlayMusic()
    {
        if (Instance != null) Instance.StartMusic();
    }

    void StartMusic()
    {
        if (music == null || musicSource.isPlaying) return;

        musicSource.clip = music;
        musicFade = 0f;
        ApplyVolumes();
        musicSource.Play();
        StartCoroutine(FadeMusicIn());
    }

    IEnumerator FadeMusicIn()
    {
        for (float t = 0f; t < musicFadeTime; t += Time.unscaledDeltaTime)
        {
            musicFade = t / musicFadeTime;
            ApplyVolumes();
            yield return null;
        }
        musicFade = 1f;
        ApplyVolumes();
    }

    void PlayEntry(Sfx id, float pitch)
    {
        if (!lookup.TryGetValue(id, out SfxEntry entry)) return;

        int slot = next;
        AudioSource source = pool[slot];
        next = (next + 1) % pool.Length;

        source.clip = entry.clips[UnityEngine.Random.Range(0, entry.clips.Length)];
        poolBaseVolume[slot] = entry.volume;
        source.volume = entry.volume * sfxVolume * masterVolume;
        source.pitch = pitch * (1f + UnityEngine.Random.Range(-entry.pitchJitter, entry.pitchJitter));
        source.Play();
    }

    void ApplyVolumes()
    {
        if (pool == null || musicSource == null) return;

        for (int i = 0; i < pool.Length; i++)
        {
            pool[i].volume = poolBaseVolume[i] * sfxVolume * masterVolume;
        }
        musicSource.volume = musicBaseVolume * musicFade * musicVolume * masterVolume;
    }
}
