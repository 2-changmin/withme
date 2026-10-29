using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Withme.Validation;

public static class CreateCharacterProbe
{
    const string Root = "Assets/Validation/Character";
    const string ModelPath = "Assets/StreamingAssets/Validation/DogWalk.glb";
    const string SourcePath = Root + "/Source/DogWalk.glb";
    const string ScenePath = "Assets/Scenes/CharacterPlaybackValidation.unity";

    public static object Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
        if (File.Exists(ScenePath))
            throw new InvalidOperationException("Validation scene already exists; edit it in place.");
        EnsureFolder("Assets/StreamingAssets/Validation");
        EnsureFolder(Root + "/Animation");
        EnsureFolder("Assets/Resources/CharacterProbe");
        if (!File.Exists(SourcePath))
        {
            var error = AssetDatabase.MoveAsset(ModelPath, SourcePath);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }
        // StreamingAssets intentionally bypasses import. Keep the editable source separately.
        File.Copy(SourcePath, ModelPath, true);
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(SourcePath);
        var serializedImporter = new SerializedObject(importer);
        serializedImporter.FindProperty("importSettings.animationMethod").intValue = (int)GLTFast.AnimationMethod.Mecanim;
        serializedImporter.FindProperty("importSettings.nodeNameMethod").intValue = (int)GLTFast.NameImportMethod.OriginalUnique;
        serializedImporter.ApplyModifiedPropertiesWithoutUndo();
        importer.SaveAndReimport();
        var assets = AssetDatabase.LoadAllAssetsAtPath(SourcePath);
        var original = assets.OfType<AnimationClip>().Single(a => a.name == "Walk");
        if (original.legacy) throw new InvalidOperationException("Editor import must produce a Mecanim clip.");
        var clip = UnityEngine.Object.Instantiate(original);
        clip.name = "SampleSharedWalk";
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        var clipPath = Root + "/Animation/SampleSharedWalk.anim";
        if (File.Exists(clipPath)) UnityEngine.Object.DestroyImmediate(clip);
        else AssetDatabase.CreateAsset(clip, clipPath);
        clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        var paths = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct().OrderBy(p => p).ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("Walk has no transform bindings.");
        var controllerPath = Root + "/Animation/SampleWalk.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Walk");
            state.motion = clip;
            state.writeDefaultValues = true;
            controller.layers[0].stateMachine.defaultState = state;
        }
        // Keep the known sample's material feature combination reachable in player builds.
        var index = 0;
        foreach (var material in assets.OfType<Material>())
        {
            var path = "Assets/Resources/CharacterProbe/Material" + index++ + ".mat";
            if (!File.Exists(path)) AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(material), path);
        }

        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var cameraObject = new GameObject("ProbeCamera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.backgroundColor = new Color(0.88f, 0.91f, 0.87f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 38;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100;
            camera.transform.position = new Vector3(3, 2, 3);
            camera.transform.LookAt(Vector3.up * 0.6f);
            var light = new GameObject("KeyLight", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 2;
            var floorPath = Root + "/Floor.mat";
            var floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(floorPath);
            if (floorMaterial == null)
            {
                floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                floorMaterial.color = new Color(0.73f, 0.78f, 0.7f);
                AssetDatabase.CreateAsset(floorMaterial, floorPath);
            }
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var probe = new GameObject("CharacterProbe").AddComponent<CharacterPlaybackProbe>();
            probe.modelAnchor = new GameObject("ModelAnchor").transform;
            probe.viewCamera = camera;
            probe.walkController = controller;
            probe.requiredPaths = paths;
            var canvasObject = new GameObject("ProbeCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844);
            scaler.matchWidthOrHeight = 0.5f;
            var safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(canvasObject.transform, false);
            safe.anchorMin = Vector2.zero;
            safe.anchorMax = Vector2.one;
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            probe.safeArea = safe;
            Label(safe, "Title", "강아지 움직임 확인 · 개발 검증", 20, true, 25, 64);
            probe.statusLabel = Label(safe, "Status", "강아지 불러오는 중…", 16, true, 84, 70);
            probe.fpsLabel = Label(safe, "Measurements", "기기 측정 대기", 12, false, 145, 85);
            probe.playbackButtons = new[]
            {
                Button(safe, "SharedWalk", "공용 걷기", -87, 110, probe.PlaySharedWalk),
                Button(safe, "EmbeddedWalk", "내장 걷기", 87, 110, probe.PlayEmbeddedWalk),
                Button(safe, "Pose", "기본 자세", -87, 52, probe.ShowPose)
            };
            Button(safe, "Reload", "다시 불러오기", 87, 52, probe.ReloadClicked);
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return new { scene = ScenePath, clip = clipPath, clip.length, clip.legacy, paths, model = ModelPath };
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
    }
    static TMP_Text Label(Transform parent, string name, string text, int size, bool top, float offset, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0, top ? 1 : 0);
        rect.anchorMax = new Vector2(1, top ? 1 : 0);
        rect.pivot = new Vector2(0.5f, top ? 1 : 0);
        rect.sizeDelta = new Vector2(-36, height);
        rect.anchoredPosition = new Vector2(0, top ? -offset : offset);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = size;
        label.color = new Color(0.08f, 0.16f, 0.1f);
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }
    static UnityEngine.UI.Button Button(Transform parent, string name, string text, float x, float y, UnityAction action)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
        rect.sizeDelta = new Vector2(164, 48);
        rect.anchoredPosition = new Vector2(x, y);
        go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.96f, 0.98f, 0.94f);
        var button = go.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = go.GetComponent<UnityEngine.UI.Image>();
        UnityEventTools.AddPersistentListener(button.onClick, action);
        var label = Label(go.transform, "Label", text, 16, true, 0, 48);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
