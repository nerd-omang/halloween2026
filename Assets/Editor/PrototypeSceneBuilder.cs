using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class PrototypeSceneBuilder
{
    const float RenderScale = 1f / 3f;
    const int BlastPeakFrame = 8;
    const string MaterialDir = "Assets/Materials";
    // The model prefabs are made by MokuModelBuilder and Myeong7ModelBuilder.
    const string MokuModelPath = "Assets/Prefabs/Characters/Moku.prefab";
    const string Myeong7ModelPath = "Assets/Prefabs/Characters/Myeong7.prefab";
    const string GameVisitorDir = "Assets/Prefabs/Game";
    const string KunmungchiModelPath = "Assets/Prefabs/Visitors/Koonmungchi.prefab";
    const string OmangiModelPath = "Assets/Prefabs/Visitors/Omangi.prefab";
    const string HandArtDir = "Assets/Art/Hand";
    const string SfxDir = "Assets/Audio/Sfx";
    const string BgmDir = "Assets/Audio/Bgm";
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Fangame/Build Prototype Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(GameVisitorDir);

        var dither = CreateDitherMaterial();
        AddDitherFeature("Assets/Settings/PC_Renderer.asset", dither);
        AddDitherFeature("Assets/Settings/Mobile_Renderer.asset", dither);
        OptimizeForWebGL("Assets/Settings/Mobile_RPAsset.asset");
        OptimizeForWebGL("Assets/Settings/PC_RPAsset.asset");

        BuildGameVisitor(KunmungchiModelPath, "Visitor_Kunmungchi", VisitorKind.Kunmungchi);
        BuildGameVisitor(OmangiModelPath, "Visitor_Omangi", VisitorKind.Omangi);
        // Easter-egg guests.
        BuildSpecialVisitor(MokuModelPath, "Visitor_Moku", 0);
        BuildSpecialVisitor(Myeong7ModelPath, "Visitor_Myeong7", 1);
        AssetDatabase.SaveAssets();

        // Disguises are put together at runtime by VisitorDisguise (on the model prefabs).
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var kunPrefab = LoadGameVisitor("Visitor_Kunmungchi");
        var omangiPrefab = LoadGameVisitor("Visitor_Omangi");
        BuildWorld();
        var hand = BuildCameraAndHand();
        EnvironmentBuilder.Build(); // after the camera: the porch frame is fitted to it
        var queue = BuildQueue(kunPrefab, omangiPrefab, LoadGameVisitor("Visitor_Moku"), LoadGameVisitor("Visitor_Myeong7"));
        BuildCatalogAndStage(kunPrefab, omangiPrefab, out var catalog, out var stage);
        BuildUiAndManager(queue, hand, catalog, stage);
        ConfigureSfxImport();
        ConfigureMusicImport();
        BuildAudioManager();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("PrototypeSceneBuilder: done");
    }

    [MenuItem("Fangame/Build WebGL")]
    public static void BuildWebGL()
    {
        WebBuildSettings.Apply(false); // template, compression, memory, stripping: see Docs/MobileWeb.md
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("WebGL build result: " + report.summary.result + ", size " + report.summary.totalSize);
        WebBuildSettings.LogSizeReport(report);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode)
        {
            EditorApplication.Exit(1);
        }
    }

    static Material CreateDitherMaterial()
    {
        string path = MaterialDir + "/DitherPixel.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Fangame/DitherPixel"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetFloat("_PixelSize", 1f);
        mat.SetFloat("_Levels", 13f);
        mat.SetFloat("_DitherStrength", 1f);
        mat.SetFloat("_MatrixSize", 1f);
        mat.SetFloat("_DarkBias", 1f);
        mat.SetFloat("_DarkLevels", 13f);
        mat.SetFloat("_DarkCutoff", 0.15f);
        mat.SetFloat("_FlatThreshold", 0f);
        var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(BlueNoiseGenerator.TexturePath);
        if (noise != null) mat.SetTexture("_NoiseTex", noise);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void AddDitherFeature(string rendererPath, Material mat)
    {
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
        FullScreenPassRendererFeature feature = null;
        foreach (var f in data.rendererFeatures)
        {
            if (f is FullScreenPassRendererFeature existing) feature = existing;
        }

        bool created = false;
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "DitherPixel";
            AssetDatabase.AddObjectToAsset(feature, data);
            created = true;
        }

        feature.passMaterial = mat;
        feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        feature.fetchColorBuffer = true;
        feature.requirements = ScriptableRenderPassInput.Color;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);
        AssetDatabase.SaveAssets();

        if (created)
        {
            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
        }
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }

    static void OptimizeForWebGL(string rpAssetPath)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpAssetPath);
        var so = new SerializedObject(asset);
        so.FindProperty("m_SupportsHDR").boolValue = false;
        so.FindProperty("m_RequireDepthTexture").boolValue = false;
        so.FindProperty("m_RequireOpaqueTexture").boolValue = false;
        so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 0;
        so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
        so.FindProperty("m_SoftShadowsSupported").boolValue = false;
        so.FindProperty("m_ShadowCascadeCount").intValue = 1;
        so.FindProperty("m_MainLightShadowmapResolution").intValue = 512;
        so.FindProperty("m_ShadowDistance").floatValue = 15f;
        so.FindProperty("m_RenderScale").floatValue = RenderScale;
        so.FindProperty("m_UpscalingFilter").intValue = (int)UpscalingFilterSelection.Point;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
    }

    static Material LitMaterial(string name, Color color)
    {
        string path = MaterialDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void BuildSpecialVisitor(string modelPath, string name, int guestId)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogWarning("Special guest model not found: " + modelPath + " (skipped)");
            return;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = name;
        instance.AddComponent<Visitor>().kind = VisitorKind.Kunmungchi; // kind is unused for guests
        instance.AddComponent<SpecialGuest>().Id = guestId;
        PrefabUtility.SaveAsPrefabAsset(instance, GameVisitorDir + "/" + name + ".prefab");
        Object.DestroyImmediate(instance);
    }

    static void BuildGameVisitor(string modelPath, string name, VisitorKind kind)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) throw new System.Exception("Visitor model prefab not found: " + modelPath);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = name;
        instance.AddComponent<Visitor>().kind = kind;
        PrefabUtility.SaveAsPrefabAsset(instance, GameVisitorDir + "/" + name + ".prefab");
        Object.DestroyImmediate(instance);
    }

    static void BuildWorld()
    {
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.72f, 0.45f);
        light.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var groundMat = LitMaterial("Ground", new Color(0.18f, 0.12f, 0.2f));
        var ground = Prim(PrimitiveType.Plane, null, "Ground", new Vector3(0f, 0f, 12f), new Vector3(3f, 1f, 6f), groundMat);
        ground.isStatic = true;

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.25f, 0.18f, 0.32f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.12f, 0.06f, 0.18f);
        RenderSettings.fogStartDistance = 6f;
        RenderSettings.fogEndDistance = 22f;
    }

    static HandController BuildCameraAndHand()
    {
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.06f, 0.18f);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<UniversalAdditionalCameraData>();
        camGo.transform.position = new Vector3(0f, 1.6f, 0f);
        camGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

        var spriteMat = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default"));

        var handRoot = new GameObject("Hand");
        handRoot.transform.SetParent(camGo.transform, false);
        handRoot.transform.localPosition = new Vector3(0.3f, -0.2f, 0.6f);

        var handGo = new GameObject("HandSprite");
        handGo.transform.SetParent(handRoot.transform, false);
        handGo.transform.localScale = Vector3.one * 0.35f;
        var handSr = handGo.AddComponent<SpriteRenderer>();
        handSr.sharedMaterial = spriteMat;

        var candyGo = new GameObject("CandySprite");
        candyGo.transform.SetParent(handRoot.transform, false);
        candyGo.transform.localPosition = Vector3.zero;
        candyGo.transform.localScale = Vector3.one * 0.34f;
        var candySr = candyGo.AddComponent<SpriteRenderer>();
        candySr.sharedMaterial = spriteMat;
        candySr.sortingOrder = 1;
        candyGo.SetActive(false);

        var sparkleGo = new GameObject("CandySparkle");
        var sparkle = sparkleGo.AddComponent<CandySparkle>();
        var sparkleSo = new SerializedObject(sparkle);
        sparkleSo.FindProperty("material").objectReferenceValue = spriteMat;
        sparkleSo.ApplyModifiedProperties();

        var hand = handRoot.AddComponent<HandController>();
        var so = new SerializedObject(hand);
        so.FindProperty("sparkle").objectReferenceValue = sparkle;
        so.FindProperty("handRenderer").objectReferenceValue = handSr;
        so.FindProperty("candy").objectReferenceValue = candySr;
        var candySprites = LoadCandySprites();
        var candyList = so.FindProperty("candySprites");
        candyList.arraySize = candySprites.Length;
        for (int i = 0; i < candySprites.Length; i++) candyList.GetArrayElementAtIndex(i).objectReferenceValue = candySprites[i];
        so.FindProperty("idleSprite").objectReferenceValue = LoadHandFrames("middle")[0];
        SetClip(so.FindProperty("giveClip"), LoadHandFrames("spread"), 18);
        var middleFrames = LoadHandFrames("middle");
        SetClip(so.FindProperty("rejectClip"), middleFrames, middleFrames.Length - 1);
        SetClip(so.FindProperty("thumbsUpClip"), LoadHandFrames("thumbs"), 13);
        var blastFrames = Directory.Exists(HandArtDir + "/blast") ? LoadHandFrames("blast") : new Sprite[0];
        if (blastFrames.Length > 0) SetClip(so.FindProperty("blastClip"), blastFrames, BlastPeakFrame);
        else SetClip(so.FindProperty("blastClip"), LoadHandFrames("spread"), 18);
        so.ApplyModifiedProperties();
        return hand;
    }

    static Sprite[] LoadCandySprites()
    {
        var sprites = new System.Collections.Generic.List<Sprite>();
        foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { HandArtDir + "/Candy" }))
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
            if (sprite != null) sprites.Add(sprite);
        }
        return sprites.ToArray();
    }

    static Sprite[] LoadHandFrames(string clip)
    {
        var frames = new System.Collections.Generic.List<Sprite>();
        for (int i = 1; ; i++)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{HandArtDir}/{clip}/f{i:00}.png");
            if (sprite == null) break;
            frames.Add(sprite);
        }
        return frames.ToArray();
    }

    static void SetClip(SerializedProperty clip, Sprite[] frames, int holdStart)
    {
        var list = clip.FindPropertyRelative("frames");
        list.arraySize = frames.Length;
        for (int i = 0; i < frames.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
        clip.FindPropertyRelative("holdStart").intValue = holdStart;
    }

    static Visitor LoadGameVisitor(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Visitor>(GameVisitorDir + "/" + name + ".prefab");
    }

    static VisitorQueue BuildQueue(Visitor kun, Visitor omangi, Visitor moku, Visitor myeong7)
    {
        var go = new GameObject("VisitorQueue");
        var queue = go.AddComponent<VisitorQueue>();
        var so = new SerializedObject(queue);
        so.FindProperty("kunmungchiPrefab").objectReferenceValue = kun;
        so.FindProperty("omangiPrefab").objectReferenceValue = omangi;
        var specials = so.FindProperty("specialPrefabs");
        specials.arraySize = SpecialGuest.Count;
        specials.GetArrayElementAtIndex(0).objectReferenceValue = moku;
        specials.GetArrayElementAtIndex(1).objectReferenceValue = myeong7;
        so.FindProperty("groundY").floatValue = 0f;
        so.ApplyModifiedProperties();
        return queue;
    }

    static Text MakeText(Transform parent, string name, TextAnchor anchor, Vector2 anchorPos, Vector2 pivot, Vector2 offset, int size, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = anchorPos;
        rt.pivot = pivot;
        rt.anchoredPosition = offset;
        rt.sizeDelta = sizeDelta;
        return text;
    }

    static void BuildAudioManager()
    {
        var table = new (Sfx id, string[] files, float volume, float jitter)[]
        {
            (Sfx.CandyAppear, new[] { "candy_appear" }, 0.45f, 0.04f),
            (Sfx.ComboUp, new[] { "combo_up" }, 0.6f, 0f),
            (Sfx.CandyToss, new[] { "candy_toss" }, 0.7f, 0.05f),
            (Sfx.CandyLand, new[] { "candy_land" }, 0.4f, 0.05f),
            (Sfx.AnswerCorrect, new[] { "answer_correct" }, 0.9f, 0f),
            (Sfx.AnswerWrong, new[] { "answer_wrong" }, 0.9f, 0f),
            (Sfx.HeartLost, new[] { "heart_lost" }, 0.9f, 0f),
            (Sfx.VisitorShoo, new[] { "visitor_shoo" }, 0.9f, 0.05f),
            (Sfx.BlastCharge, new[] { "blast_charge" }, 0.45f, 0.03f),
            (Sfx.SheetBlowOff, new[] { "sheet_blowoff" }, 0.8f, 0.08f),
            (Sfx.VisitorBounce, new[] { "visitor_bounce" }, 0.8f, 0.08f),
            (Sfx.PropPop, new[] { "prop_pop" }, 0.8f, 0.08f),
            (Sfx.GameOver, new[] { "game_over" }, 1f, 0f),
            (Sfx.VisitorStep, FootstepFiles(), 0.35f, 0.1f),
        };

        var go = new GameObject("AudioManager");
        var manager = go.AddComponent<AudioManager>();
        var so = new SerializedObject(manager);
        var entries = so.FindProperty("entries");
        entries.arraySize = table.Length;
        for (int i = 0; i < table.Length; i++)
        {
            var (id, files, volume, jitter) = table[i];
            var entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("id").enumValueIndex = (int)id;
            entry.FindPropertyRelative("volume").floatValue = volume;
            entry.FindPropertyRelative("pitchJitter").floatValue = jitter;

            var clips = entry.FindPropertyRelative("clips");
            clips.arraySize = files.Length;
            for (int j = 0; j < files.Length; j++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{files[j]}.wav") ??
                           AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{files[j]}.ogg");
                if (clip == null) Debug.LogWarning($"PrototypeSceneBuilder: missing SFX {files[j]}");
                clips.GetArrayElementAtIndex(j).objectReferenceValue = clip;
            }
        }
        so.FindProperty("music").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>($"{BgmDir}/halloween_ambient.wav");
        so.ApplyModifiedProperties();
    }

    static string[] FootstepFiles()
    {
        var files = new string[10];
        for (int i = 0; i < files.Length; i++) files[i] = "visitor_step_" + i;
        return files;
    }

    // Looping music: mono, 22.05 kHz, Vorbis, kept compressed in memory (a long clip would bloat decoding if decompressed).
    public static void ConfigureMusicImport()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { BgmDir }))
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as AudioImporter;
            if (importer == null) continue;

            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.4f;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 22050;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }

    // Short effects: decompress on load, mono, 22.05 kHz, Vorbis. Keeps the WebGL download small.
    public static void ConfigureSfxImport()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SfxDir }))
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as AudioImporter;
            if (importer == null) continue;

            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.5f;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 22050;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }

    static void BuildUiAndManager(VisitorQueue queue, HandController hand, OutfitCatalog catalog, ModelStage stage)
    {
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        var score = MakeText(canvasGo.transform, "Score", TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), 32, new Vector2(400, 50));
        var heartsGo = new GameObject("Hearts", typeof(RectTransform));
        heartsGo.transform.SetParent(canvasGo.transform, false);
        var heartsRt = (RectTransform)heartsGo.transform;
        heartsRt.anchorMin = heartsRt.anchorMax = new Vector2(1, 1);
        heartsRt.pivot = new Vector2(1, 1);
        heartsRt.anchoredPosition = new Vector2(-24, -20);
        heartsRt.sizeDelta = new Vector2(240, 50);
        var hearts = heartsGo.AddComponent<HeartsDisplay>();
        var message = MakeText(canvasGo.transform, "Message", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), 40, new Vector2(800, 60));
        message.color = new Color(1f, 0.85f, 0.4f);

        // Menu, gallery, dress-up and review screens are built at runtime by GameManager (UiKit).
        var gm = new GameObject("GameManager").AddComponent<GameManager>();
        var so = new SerializedObject(gm);
        so.FindProperty("queue").objectReferenceValue = queue;
        so.FindProperty("hand").objectReferenceValue = hand;
        so.FindProperty("catalog").objectReferenceValue = catalog;
        so.FindProperty("stage").objectReferenceValue = stage;
        so.FindProperty("scoreText").objectReferenceValue = score;
        so.FindProperty("heartsDisplay").objectReferenceValue = hearts;
        so.FindProperty("messageText").objectReferenceValue = message;
        so.ApplyModifiedProperties();
    }

    // Looks library (prefabs, gallery save) and the far-away stage where models are shown up close.
    static void BuildCatalogAndStage(Visitor kun, Visitor omangi, out OutfitCatalog catalog, out ModelStage stage)
    {
        catalog = new GameObject("OutfitCatalog").AddComponent<OutfitCatalog>();
        var catalogSo = new SerializedObject(catalog);
        catalogSo.FindProperty("kunmungchiPrefab").objectReferenceValue = kun;
        catalogSo.FindProperty("omangiPrefab").objectReferenceValue = omangi;
        catalogSo.ApplyModifiedProperties();

        stage = new GameObject("ModelStage").AddComponent<ModelStage>();
        var stageSo = new SerializedObject(stage);
        stageSo.FindProperty("catalog").objectReferenceValue = catalog;
        stageSo.FindProperty("floorMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/Ground.mat");
        var guests = stageSo.FindProperty("guestPrefabs");
        guests.arraySize = SpecialGuest.Count;
        guests.GetArrayElementAtIndex(0).objectReferenceValue = LoadGameVisitor("Visitor_Moku");
        guests.GetArrayElementAtIndex(1).objectReferenceValue = LoadGameVisitor("Visitor_Myeong7");
        stageSo.ApplyModifiedProperties();
    }
}
