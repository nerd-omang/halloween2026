using System.Collections.Generic;
using UnityEngine;

// The longer the combo, the more Halloween the yard gets: the jack-o'-lanterns start dim and glow brighter,
// the house windows light up, the moon grows and reddens, cobwebs and bats appear one by one, ambient motes
// drift up and the music speeds up a little.
// It reads the objects the environment builder made (under "Environment") by name, so no scene change is needed;
// anything it does not find is left out.
public class ComboAtmosphere : MonoBehaviour
{
    // The streak at which everything is at full strength (the same scale as the score color).
    const int FullStreak = UiKit.ComboColorMaxStreak;

    // The lantern and window light reaches full strength sooner than the rest.
    const int FullLightStreak = 50;

    const float RiseSpeed = 2f;      // how fast the level follows a growing combo (1/s)
    const float FallSpeed = 5f;      // and a broken one
    const float PopSpeed = 2.5f;     // scale-in speed of a bat or web (1/s)

    const float LanternDim = 0.25f;  // lantern glow without a combo, as a fraction of its normal brightness
    const float LanternFull = 1.3f;  // and at full level
    const float FlickerFull = 1.8f;  // flicker grows with the level too

    const float WindowDim = 0.04f;   // window light without a combo (they look switched off), as a fraction of normal
    const float WindowFull = 1.15f;
    const float WindowFlickerFull = 2.5f;

    const float MoonRadiusFull = 1.45f;                         // the moon disc grows to this much of its size
    static readonly Color MoonFullColor = new Color(1f, 0.52f, 0.28f);  // and goes from pale yellow to harvest red
    static readonly Color MoonFullHalo = new Color(0.85f, 0.22f, 0.12f);

    const float MaxMotesPerSecond = 14f;
    const float MusicPitchFull = 1.1f;

    // Streaks at which each cobweb and each bat shows up (in the order they were built).
    static readonly int[] WebStreaks = { 8, 20, 40, 70 };
    static readonly int[] BatStreaks = { 12, 25, 45, 65, 90 };

    static readonly Color[] MoteColors =
    {
        new Color(1f, 0.55f, 0.12f), new Color(1f, 0.8f, 0.3f),
        new Color(0.75f, 0.4f, 1f), new Color(0.55f, 1f, 0.5f),
    };

    class Reveal
    {
        public Transform transform;
        public Vector3 scale;
        public int streak;
        public float shown;
    }

    class Glow
    {
        public Material material;
        public float intensity;
        public float flicker;
    }

    readonly List<Reveal> reveals = new List<Reveal>();
    readonly List<Glow> glows = new List<Glow>();
    readonly List<Glow> windows = new List<Glow>();
    Material moon;
    Color moonColor, moonHalo;
    float moonRadius;
    CandySparkle motes;
    float level;
    float lightLevel;
    float moteDebt;
    int streak;

    // The streak to show (0 outside a game).
    public int Streak { set => streak = value; }

    public void Init(CandySparkle template)
    {
        var environment = GameObject.Find("Environment");
        if (environment != null)
        {
            AddReveals(environment.transform.Find("Cobwebs"), WebStreaks);
            AddReveals(environment.transform.Find("Bats"), BatStreaks);
            var lanterns = environment.transform.Find("JackOLanterns");
            if (lanterns != null)
            {
                glows.AddRange(CopyMaterials(lanterns, "EnvGlow"));
                glows.AddRange(CopyMaterials(lanterns, "EnvHalo"));
            }
            windows.AddRange(CopyMaterials(environment.transform, "EnvWindow"));
            var moons = CopyMaterials(environment.transform, "EnvMoon");
            if (moons.Count > 0)
            {
                moon = moons[0].material;
                moonColor = moon.GetColor("_MoonColor");
                moonHalo = moon.GetColor("_HaloColor");
                moonRadius = moon.GetFloat("_DiscRadius");
            }
        }
        if (template != null && template.Material != null)
        {
            motes = CandySparkle.Create(transform, template.Material, MoteColors, 24, 0.07f, 1, 0.15f);
        }
        Apply(0f);
    }

    void AddReveals(Transform parent, int[] streaks)
    {
        if (parent == null) return;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            reveals.Add(new Reveal { transform = child, scale = child.localScale, streak = streaks[Mathf.Min(i, streaks.Length - 1)] });
            child.localScale = Vector3.zero;
            child.gameObject.SetActive(false);
        }
    }

    // Materials on the prefabs are shared with the assets, so every material whose name starts with `prefix` is
    // copied once (the copy is shared by all the renderers that used it) before it is changed at runtime.
    static List<Glow> CopyMaterials(Transform root, string prefix)
    {
        var made = new List<Glow>();
        var copies = new Dictionary<Material, Glow>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var original = materials[i];
                if (original == null || !original.name.StartsWith(prefix)) continue;
                if (!copies.TryGetValue(original, out var glow))
                {
                    glow = new Glow
                    {
                        material = new Material(original),
                        intensity = original.HasProperty("_Intensity") ? original.GetFloat("_Intensity") : 1f,
                        flicker = original.HasProperty("_FlickerAmount") ? original.GetFloat("_FlickerAmount") : 0f,
                    };
                    copies[original] = glow;
                    made.Add(glow);
                }
                materials[i] = glow.material;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
        return made;
    }

    void Update()
    {
        float target = Mathf.Clamp01(streak / (float)FullStreak);
        level = Mathf.MoveTowards(level, target, (target > level ? RiseSpeed : FallSpeed) * Time.unscaledDeltaTime * Mathf.Max(0.2f, Mathf.Abs(target - level) * 4f));
        float lightTarget = Mathf.Clamp01(streak / (float)FullLightStreak);
        lightLevel = Mathf.MoveTowards(lightLevel, lightTarget, (lightTarget > lightLevel ? RiseSpeed : FallSpeed) * Time.unscaledDeltaTime * Mathf.Max(0.2f, Mathf.Abs(lightTarget - lightLevel) * 4f));
        Apply(Time.unscaledDeltaTime);
    }

    void Apply(float dt)
    {
        foreach (var reveal in reveals)
        {
            float goal = streak >= reveal.streak ? 1f : 0f;
            reveal.shown = Mathf.MoveTowards(reveal.shown, goal, PopSpeed * dt);
            bool visible = reveal.shown > 0.001f;
            if (reveal.transform.gameObject.activeSelf != visible) reveal.transform.gameObject.SetActive(visible);
            if (visible) reveal.transform.localScale = reveal.scale * Smooth(reveal.shown);
        }

        float glowScale = Mathf.Lerp(LanternDim, LanternFull, lightLevel);
        float flickerScale = Mathf.Lerp(1f, FlickerFull, lightLevel);
        foreach (var glow in glows)
        {
            if (glow.material.HasProperty("_Intensity")) glow.material.SetFloat("_Intensity", glow.intensity * glowScale);
            if (glow.material.HasProperty("_FlickerAmount")) glow.material.SetFloat("_FlickerAmount", Mathf.Clamp01(glow.flicker * flickerScale));
        }

        float windowScale = Mathf.Lerp(WindowDim, WindowFull, lightLevel);
        float windowFlicker = Mathf.Lerp(1f, WindowFlickerFull, lightLevel);
        foreach (var window in windows)
        {
            window.material.SetFloat("_Intensity", window.intensity * windowScale);
            window.material.SetFloat("_FlickerAmount", Mathf.Clamp01(window.flicker * windowFlicker));
        }

        if (moon != null)
        {
            moon.SetColor("_MoonColor", Color.Lerp(moonColor, MoonFullColor, level));
            moon.SetColor("_HaloColor", Color.Lerp(moonHalo, MoonFullHalo, level));
            moon.SetFloat("_DiscRadius", Mathf.Min(moonRadius * Mathf.Lerp(1f, MoonRadiusFull, level), 0.6f));
        }

        AudioManager.MusicPitch = Mathf.Lerp(1f, MusicPitchFull, level);

        if (motes != null && level > 0.05f && dt > 0f)
        {
            moteDebt += MaxMotesPerSecond * level * dt;
            for (; moteDebt >= 1f; moteDebt -= 1f) EmitMote();
        }
    }

    // A mote rises slowly somewhere in the yard in front of the camera.
    void EmitMote()
    {
        var position = new Vector3(Random.Range(-9f, 9f), Random.Range(0.3f, 4.5f), Random.Range(4f, 15f));
        var velocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.25f, 0.55f), Random.Range(-0.1f, 0.1f));
        motes.Emit(position, velocity, Random.Range(3f, 5.5f), Random.Range(2.5f, 4f));
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
}
