using System.Collections.Generic;
using UnityEngine;

// The longer the combo, the more Halloween the yard gets: the jack-o'-lanterns start dim and glow brighter,
// cobwebs and bats appear one by one, ambient motes drift up and the music speeds up a little.
// It reads the objects the environment builder made (under "Environment") by name, so no scene change is needed;
// anything it does not find is left out.
public class ComboAtmosphere : MonoBehaviour
{
    // The streak at which everything is at full strength (the same scale as the score color).
    const int FullStreak = UiKit.ComboColorMaxStreak;

    const float RiseSpeed = 2f;      // how fast the level follows a growing combo (1/s)
    const float FallSpeed = 5f;      // and a broken one
    const float PopSpeed = 2.5f;     // scale-in speed of a bat or web (1/s)

    const float LanternDim = 0.25f;  // lantern glow without a combo, as a fraction of its normal brightness
    const float LanternFull = 1.3f;  // and at full level
    const float FlickerFull = 1.8f;  // flicker grows with the level too

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
    CandySparkle motes;
    float level;
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
            FindGlows(environment.transform.Find("JackOLanterns"));
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

    // The lantern materials are shared with the prefab asset, so they are copied (once, for all lanterns)
    // before their brightness is changed; the asset itself stays as it is.
    void FindGlows(Transform lanterns)
    {
        if (lanterns == null) return;
        var copies = new Dictionary<Material, Glow>();
        foreach (var renderer in lanterns.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var original = materials[i];
                if (original == null || !(original.name.StartsWith("EnvGlow") || original.name.StartsWith("EnvHalo"))) continue;
                if (!copies.TryGetValue(original, out var glow))
                {
                    var copy = new Material(original);
                    glow = new Glow
                    {
                        material = copy,
                        intensity = original.HasProperty("_Intensity") ? original.GetFloat("_Intensity") : 1f,
                        flicker = original.HasProperty("_FlickerAmount") ? original.GetFloat("_FlickerAmount") : 0f,
                    };
                    copies[original] = glow;
                    glows.Add(glow);
                }
                materials[i] = glow.material;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }

    void Update()
    {
        float target = Mathf.Clamp01(streak / (float)FullStreak);
        level = Mathf.MoveTowards(level, target, (target > level ? RiseSpeed : FallSpeed) * Time.unscaledDeltaTime * Mathf.Max(0.2f, Mathf.Abs(target - level) * 4f));
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

        float glowScale = Mathf.Lerp(LanternDim, LanternFull, level);
        float flickerScale = Mathf.Lerp(1f, FlickerFull, level);
        foreach (var glow in glows)
        {
            if (glow.material.HasProperty("_Intensity")) glow.material.SetFloat("_Intensity", glow.intensity * glowScale);
            if (glow.material.HasProperty("_FlickerAmount")) glow.material.SetFloat("_FlickerAmount", Mathf.Clamp01(glow.flicker * flickerScale));
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
