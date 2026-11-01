using UnityEngine;
using UnityEngine.Rendering;

// The scene has no skybox (the sky is a model with its own shader), so metal had nothing to reflect and looked
// like a dull color. This paints a small cubemap of the night yard when the scene loads (a purple sky with a
// warm glow at the horizon, the moon, and the orange of the jack-o'-lanterns) and uses it for reflections.
public static class EnvironmentReflection
{
    const int Size = 64;

    // Same direction as the moon in Tools/Blender/build_environment.py (azimuth 18, elevation 13), in Unity axes.
    static readonly Vector3 MoonDirection = Direction(18f, 13f);
    // Roughly where the lanterns stand, seen from the camera.
    static readonly Vector3[] LanternDirections =
    {
        new Vector3(0.65f, -0.15f, 0.75f).normalized,
        new Vector3(-0.67f, -0.1f, 0.74f).normalized,
    };

    static readonly Color Top = new Color(0.16f, 0.09f, 0.28f);
    static readonly Color Horizon = new Color(0.55f, 0.28f, 0.33f);
    static readonly Color Ground = new Color(0.04f, 0.03f, 0.05f);
    static readonly Color MoonColor = new Color(1f, 0.93f, 0.72f);
    static readonly Color LanternColor = new Color(1f, 0.5f, 0.1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        var cube = new Cubemap(Size, TextureFormat.RGBA32, true) { name = "NightYardReflection", wrapMode = TextureWrapMode.Clamp };
        for (int face = 0; face < 6; face++)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.5f) / Size * 2f - 1f;
                    cube.SetPixel((CubemapFace)face, x, y, Shade(FaceDirection(face, u, v).normalized));
                }
            }
        }
        cube.Apply(true, true);

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = cube;
        RenderSettings.reflectionIntensity = 1f;
    }

    // The direction a cubemap texel looks in (OpenGL cube face layout).
    static Vector3 FaceDirection(int face, float u, float v)
    {
        switch (face)
        {
            case 0: return new Vector3(1f, -v, -u);   // +X
            case 1: return new Vector3(-1f, -v, u);   // -X
            case 2: return new Vector3(u, 1f, v);     // +Y
            case 3: return new Vector3(u, -1f, -v);   // -Y
            case 4: return new Vector3(u, -v, 1f);    // +Z
            default: return new Vector3(-u, -v, -1f); // -Z
        }
    }

    static Color Shade(Vector3 d)
    {
        Color color;
        if (d.y >= 0f) color = Color.Lerp(Horizon, Top, Mathf.Sqrt(d.y));
        else color = Color.Lerp(Horizon * 0.5f, Ground, Mathf.Clamp01(-d.y * 2.5f));

        float moon = Mathf.Max(0f, Vector3.Dot(d, MoonDirection));
        color += MoonColor * (Mathf.Pow(moon, 400f) * 1.5f + Mathf.Pow(moon, 14f) * 0.3f);

        foreach (var lantern in LanternDirections)
        {
            float glow = Mathf.Max(0f, Vector3.Dot(d, lantern));
            color += LanternColor * (Mathf.Pow(glow, 60f) * 0.9f);
        }

        color.r = Mathf.Clamp01(color.r);
        color.g = Mathf.Clamp01(color.g);
        color.b = Mathf.Clamp01(color.b);
        color.a = 1f;
        return color;
    }

    static Vector3 Direction(float azimuthDegrees, float elevationDegrees)
    {
        float az = azimuthDegrees * Mathf.Deg2Rad;
        float el = elevationDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
    }
}
