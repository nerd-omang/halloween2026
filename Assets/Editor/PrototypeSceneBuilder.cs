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
    const string MaterialDir = "Assets/Materials";
    const string GameVisitorDir = "Assets/Prefabs/Game";
    const string KunmungchiModelPath = "Assets/Prefabs/Visitors/Koonmungchi.prefab";
    const string OmangiModelPath = "Assets/Prefabs/Visitors/Omangi.prefab";
    const string HandArtDir = "Assets/Art/Hand";
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
        AssetDatabase.SaveAssets();

        // Disguises are put together at runtime by VisitorDisguise (on the model prefabs).
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var kunPrefab = LoadGameVisitor("Visitor_Kunmungchi");
        var omangiPrefab = LoadGameVisitor("Visitor_Omangi");
        BuildWorld();
        var hand = BuildCameraAndHand();
        var queue = BuildQueue(kunPrefab, omangiPrefab);
        BuildUiAndManager(queue, hand);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("PrototypeSceneBuilder: done");
    }

    [MenuItem("Fangame/Build WebGL")]
    public static void BuildWebGL()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("WebGL build result: " + report.summary.result + ", size " + report.summary.totalSize);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
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
        candyGo.transform.localScale = Vector3.one * 0.25f;
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

    static VisitorQueue BuildQueue(Visitor kun, Visitor omangi)
    {
        var go = new GameObject("VisitorQueue");
        var queue = go.AddComponent<VisitorQueue>();
        var so = new SerializedObject(queue);
        so.FindProperty("kunmungchiPrefab").objectReferenceValue = kun;
        so.FindProperty("omangiPrefab").objectReferenceValue = omangi;
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

    static void BuildUiAndManager(VisitorQueue queue, HandController hand)
    {
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        var score = MakeText(canvasGo.transform, "Score", TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), 32, new Vector2(400, 50));
        var hearts = MakeText(canvasGo.transform, "Hearts", TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -20), 32, new Vector2(400, 50));
        var message = MakeText(canvasGo.transform, "Message", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), 40, new Vector2(800, 60));
        message.color = new Color(1f, 0.85f, 0.4f);

        var panel = new GameObject("GameOverPanel");
        panel.transform.SetParent(canvasGo.transform, false);
        var img = panel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.75f);
        var prt = img.rectTransform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var overText = MakeText(panel.transform, "GameOverText", TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 54, new Vector2(900, 300));
        panel.SetActive(false);

        var gm = new GameObject("GameManager").AddComponent<GameManager>();
        var so = new SerializedObject(gm);
        so.FindProperty("queue").objectReferenceValue = queue;
        so.FindProperty("hand").objectReferenceValue = hand;
        so.FindProperty("scoreText").objectReferenceValue = score;
        so.FindProperty("heartsText").objectReferenceValue = hearts;
        so.FindProperty("messageText").objectReferenceValue = message;
        so.FindProperty("gameOverPanel").objectReferenceValue = panel;
        so.FindProperty("gameOverText").objectReferenceValue = overText;
        so.ApplyModifiedProperties();
    }
}
