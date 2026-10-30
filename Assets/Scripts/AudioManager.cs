using System;
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
public class AudioManager : MonoBehaviour
{
    [SerializeField] SfxEntry[] entries;
    [SerializeField] int voices = 12;

    public static AudioManager Instance { get; private set; }

    readonly Dictionary<Sfx, SfxEntry> lookup = new Dictionary<Sfx, SfxEntry>();
    AudioSource[] pool;
    int next;

    void Awake()
    {
        Instance = this;

        foreach (var entry in entries)
        {
            if (entry != null && entry.clips != null && entry.clips.Length > 0) lookup[entry.id] = entry;
        }

        pool = new AudioSource[voices];
        for (int i = 0; i < voices; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            pool[i] = source;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static void Play(Sfx id, float pitch = 1f)
    {
        if (Instance != null) Instance.PlayEntry(id, pitch);
    }

    void PlayEntry(Sfx id, float pitch)
    {
        if (!lookup.TryGetValue(id, out SfxEntry entry)) return;

        AudioSource source = pool[next];
        next = (next + 1) % pool.Length;

        source.clip = entry.clips[UnityEngine.Random.Range(0, entry.clips.Length)];
        source.volume = entry.volume;
        source.pitch = pitch * (1f + UnityEngine.Random.Range(-entry.pitchJitter, entry.pitchJitter));
        source.Play();
    }
}
