using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Player settings for the mobile-browser WebGL build. Applied from Fangame > Apply WebGL Build Settings,
/// before every Fangame > Build WebGL, and once when the editor loads (idempotent, logs only on change).
/// See Docs/MobileWeb.md for the reasoning behind each number.
/// </summary>
public static class WebBuildSettings
{
    public const string Template = "PROJECT:Halloween";

    // Wasm heap. iOS Safari kills a tab around 1.2-1.5 GB on recent phones and far less on old ones, and the
    // heap shares that budget with decoded textures, the GPU and the JS side. This game is small, so a heap
    // that starts big enough to avoid reallocation copies (the real OOM risk on iOS) but caps low is the goal.
    const int InitialMemoryMB = 96;
    const int MaximumMemoryMB = 256;
    const float GeometricStep = 0.2f;
    const int GeometricCapMB = 32;

    [InitializeOnLoadMethod]
    static void ApplyOnLoad()
    {
        EditorApplication.delayCall += () => Apply(false);
    }

    [MenuItem("Fangame/Apply WebGL Build Settings")]
    static void ApplyMenu()
    {
        Apply(true);
    }

    public static void Apply(bool log)
    {
        int changes = 0;
        var web = NamedBuildTarget.WebGL;

        changes += Set(PlayerSettings.WebGL.template, Template, v => PlayerSettings.WebGL.template = v);

        // GitHub Pages cannot send "Content-Encoding: gzip/br" for Unity's .gz/.br files, so the loader decompresses
        // in JS (decompression fallback). Gzip over Brotli: the fallback gunzip is smaller and faster to run than the
        // Brotli one, and a phone pays that cost on every cold start. Hash names stop stale caches after redeploys.
        changes += Set(PlayerSettings.WebGL.compressionFormat, WebGLCompressionFormat.Gzip, v => PlayerSettings.WebGL.compressionFormat = v);
        changes += Set(PlayerSettings.WebGL.decompressionFallback, true, v => PlayerSettings.WebGL.decompressionFallback = v);
        changes += Set(PlayerSettings.WebGL.nameFilesAsHashes, true, v => PlayerSettings.WebGL.nameFilesAsHashes = v);
        changes += Set(PlayerSettings.WebGL.dataCaching, true, v => PlayerSettings.WebGL.dataCaching = v);

        changes += Set(PlayerSettings.WebGL.exceptionSupport, WebGLExceptionSupport.None, v => PlayerSettings.WebGL.exceptionSupport = v);
        changes += Set(PlayerSettings.WebGL.debugSymbolMode, WebGLDebugSymbolMode.Off, v => PlayerSettings.WebGL.debugSymbolMode = v);
        changes += Set(PlayerSettings.WebGL.showDiagnostics, false, v => PlayerSettings.WebGL.showDiagnostics = v);
        changes += Set(PlayerSettings.WebGL.linkerTarget, WebGLLinkerTarget.Wasm, v => PlayerSettings.WebGL.linkerTarget = v);
        changes += Set(PlayerSettings.WebGL.threadsSupport, false, v => PlayerSettings.WebGL.threadsSupport = v); // needs COOP/COEP headers, unavailable on Pages
        changes += SetWebGLProperty("initialMemorySize", InitialMemoryMB);
        changes += SetWebGLProperty("maximumMemorySize", MaximumMemoryMB);
        changes += SetWebGLProperty("memoryGrowthMode", WebGLMemoryGrowthMode.Geometric);
        changes += SetWebGLProperty("geometricMemoryGrowthStep", GeometricStep);
        changes += SetWebGLProperty("memoryGeometricGrowthCap", GeometricCapMB);

        changes += Set(PlayerSettings.GetManagedStrippingLevel(web), ManagedStrippingLevel.High, v => PlayerSettings.SetManagedStrippingLevel(web, v));
        changes += Set(PlayerSettings.stripEngineCode, true, v => PlayerSettings.stripEngineCode = v);

        // The built-in Unity logo splash texture alone is 2.7 MB uncompressed in the build report.
        changes += Set(PlayerSettings.SplashScreen.show, false, v => PlayerSettings.SplashScreen.show = v);
        changes += Set(PlayerSettings.GetIl2CppCodeGeneration(web), Il2CppCodeGeneration.OptimizeSize, v => PlayerSettings.SetIl2CppCodeGeneration(web, v));

        // WebGL 2 only (OpenGLES3 is the WebGL 2 device type); no WebGPU, no WebGL 1 fallback.
        if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL))
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            changes++;
        }
        var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);
        if (apis.Length != 1 || apis[0] != UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3)
        {
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            changes++;
        }

        changes += SetTextureCompression();

        if (changes > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("WebBuildSettings: updated " + changes + " player setting(s) for the mobile WebGL build");
        }
        else if (log)
        {
            Debug.Log("WebBuildSettings: already up to date");
        }
    }

    /// <summary>Logs the engine modules kept in the wasm and the largest packed assets, to decide what to cut next.</summary>
    public static void LogSizeReport(UnityEditor.Build.Reporting.BuildReport report)
    {
        try
        {
            var sb = new System.Text.StringBuilder("WebBuildSettings size report\n");
            var modules = report.strippingInfo != null ? report.strippingInfo.includedModules.ToArray() : new string[0];
            sb.AppendLine("Engine modules in build (" + modules.Length + "): " + string.Join(", ", modules));
#pragma warning disable CS0618
            var rows = report.packedAssets
                .SelectMany(p => p.contents)
                .GroupBy(c => string.IsNullOrEmpty(c.sourceAssetPath) ? "(" + c.type + ")" : c.sourceAssetPath)
                .Select(g => new { path = g.Key, size = g.Sum(c => (long)c.packedSize) })
                .OrderByDescending(r => r.size).Take(25);
#pragma warning restore CS0618
            sb.AppendLine("Largest packed assets:");
            foreach (var r in rows) sb.AppendLine(string.Format("{0,10:N0} B  {1}", r.size, r.path));
            Debug.Log(sb.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("WebBuildSettings: size report failed: " + e.Message + " (see the Build Report in Editor.log instead)");
        }
    }

    static int Set<T>(T current, T wanted, Action<T> setter)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(current, wanted)) return 0;
        setter(wanted);
        return 1;
    }

    // The memory properties differ in numeric type between editor versions, so convert to whatever the property takes.
    static int SetWebGLProperty(string name, object wanted)
    {
        var prop = typeof(PlayerSettings.WebGL).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        if (prop == null || !prop.CanWrite)
        {
            Debug.LogWarning("WebBuildSettings: PlayerSettings.WebGL." + name + " not found, set it by hand");
            return 0;
        }
        object value = prop.PropertyType.IsEnum ? wanted : Convert.ChangeType(wanted, prop.PropertyType);
        if (Equals(prop.GetValue(null), value)) return 0;
        prop.SetValue(null, value);
        return 1;
    }

    // ASTC first (every iOS device since A8 and nearly all Android GPUs), DXT for desktop browsers.
    // ETC2 is left out: it would add a third copy of every texture for hardware that ASTC already covers.
    // The setter is not in the documented API surface, so it goes through reflection and degrades to a warning.
    static int SetTextureCompression()
    {
        try
        {
            var set = typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "SetTextureCompressionFormats" && m.GetParameters().Length == 2);
            var get = typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetTextureCompressionFormats" && m.GetParameters().Length == 1);
            if (set == null || get == null) throw new MissingMethodException("PlayerSettings.Set/GetTextureCompressionFormats");

            var elementType = set.GetParameters()[1].ParameterType.GetElementType();
            var wanted = new[] { "ASTC", "DXT" }.Select(n => Enum.Parse(elementType, n)).ToArray();
            var arr = Array.CreateInstance(elementType, wanted.Length);
            for (int i = 0; i < wanted.Length; i++) arr.SetValue(wanted[i], i);

            var current = ((Array)get.Invoke(null, new object[] { BuildTarget.WebGL }))?.Cast<object>().ToArray() ?? new object[0];
            if (current.SequenceEqual(wanted)) return 0;
            set.Invoke(null, new object[] { BuildTarget.WebGL, arr });
            return 1;
        }
        catch (Exception e)
        {
            Debug.LogWarning("WebBuildSettings: could not set WebGL texture compression (" + e.Message +
                             "). Set it by hand: Player Settings > Web > Publishing > Texture Compression Format = ASTC, DXT.");
            return 0;
        }
    }
}
