// Run with Unity CLI script run --file <this file> --entry WithmeSetup.InitializeFonts.Run.
// TMP essential resources must be imported first. This is setup tooling, not app UI.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace WithmeSetup
{
public static class InitializeFonts
{
public static object Run()
{
if (EditorApplication.isPlaying || EditorApplication.isCompiling)
    throw new InvalidOperationException("Stop Play Mode and wait for compilation before setup.");

if (TMP_Settings.instance == null)
    throw new InvalidOperationException("Import TMP essential resources before font setup.");

const string folder = "Assets/Fonts/NotoSansKR";
const string mainPath = folder + "/NotoSansKR-UI.asset";
const string fallbackPath = folder + "/NotoSansKR-Dynamic.asset";
const string scenePath = "Assets/Scenes/SetupValidation.unity";
var project = Directory.GetParent(Application.dataPath).FullName;
var corpus = File.ReadAllText(Path.Combine(project, "../tools/font-characters.txt"));
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
var source = AssetDatabase.LoadAssetAtPath<Font>(folder + "/NotoSansKR-Regular.otf");
if (source == null) throw new InvalidOperationException("Noto Sans KR source font was not imported.");

TMP_FontAsset CreateFont(string assetPath, bool dynamic)
{
    var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
    if (existing != null) return existing;
    var font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA,
        1024, 1024, AtlasPopulationMode.Dynamic, dynamic);
    if (font == null) throw new InvalidOperationException("TMP font creation failed.");
    font.name = Path.GetFileNameWithoutExtension(assetPath);
    var face = font.faceInfo;
    face.scale = 1;
    font.faceInfo = face;
    if (!dynamic)
    {
        if (!font.TryAddCharacters(corpus, out string missing))
            throw new InvalidOperationException("Primary font is missing characters: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
    }
    var serialized = new SerializedObject(font);
    var clear = serialized.FindProperty("m_ClearDynamicDataOnBuild");
    if (clear == null) throw new InvalidOperationException("TMP clear-on-build setting not found.");
    clear.boolValue = dynamic;
    serialized.ApplyModifiedPropertiesWithoutUndo();
    AssetDatabase.CreateAsset(font, assetPath);
    font.material.name = font.name + " Material";
    AssetDatabase.AddObjectToAsset(font.material, font);
    foreach (var atlas in font.atlasTextures)
    {
        atlas.name = font.name + " Atlas";
        AssetDatabase.AddObjectToAsset(atlas, font);
    }
    EditorUtility.SetDirty(font);
    return font;
}

var fallback = CreateFont(fallbackPath, true);
var primary = CreateFont(mainPath, false);
primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
TMP_Settings.defaultFontAsset = primary;
TMP_Settings.fallbackFontAssets = new List<TMP_FontAsset> { fallback };
EditorUtility.SetDirty(primary);
EditorUtility.SetDirty(TMP_Settings.instance);
AssetDatabase.SaveAssets();

// Preserve the current scene. Never overwrite an existing validation scene.
if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
{
    var previous = SceneManager.GetActiveScene();
    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
    SceneManager.SetActiveScene(scene);
    try
    {
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        camera.tag = "MainCamera";
        camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        camera.GetComponent<Camera>().backgroundColor = new Color(0.94f, 0.96f, 0.93f);
        var canvasObject = new GameObject("SetupValidationCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(390, 844);
        scaler.matchWidthOrHeight = 0.5f;
        TextMeshProUGUI Label(string name, string value, Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(canvasObject.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = primary;
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.15f, 0.24f, 0.2f);
            label.raycastTarget = false;
            label.text = value;
            return label;
        }
        Label("Title", "한글·입력 확인", new Vector2(0, 290), new Vector2(342, 60), 28);
        Label("Sample", "보리 · 똘이 · 뭉치 · 초코\n사진으로 강아지를 찾아볼게요\n당신의 강아지가 맞나요?",
            new Vector2(0, 125), new Vector2(342, 150), 19);
        var output = Label("ClickResult", "버튼을 눌러 입력을 확인하세요", new Vector2(0, -155), new Vector2(342, 65), 19);
        var buttonObject = new GameObject("InputCheckButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);
        var buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0, -60);
        buttonRect.sizeDelta = new Vector2(300, 60);
        buttonObject.GetComponent<UnityEngine.UI.Image>().color = new Color(0.28f, 0.42f, 0.35f);
        var button = buttonObject.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = buttonObject.GetComponent<UnityEngine.UI.Image>();
        var buttonLabel = Label("InputCheckLabel", "입력 확인", new Vector2(0, -60), new Vector2(300, 60), 21);
        buttonLabel.color = Color.white;
        var setter = typeof(TMP_Text).GetProperty("text").GetSetMethod();
        var setText = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), output, setter);
        UnityEventTools.AddStringPersistentListener(button.onClick, setText, "입력 확인 완료");
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        EditorSceneManager.SaveScene(scene, scenePath);
    }
    finally
    {
        EditorSceneManager.CloseScene(scene, true);
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
    }
}
AssetDatabase.SaveAssets();
return new { primary = mainPath, fallback = fallbackPath, primaryGlyphs = primary.characterTable.Count,
    primaryAtlas = primary.atlasWidth, fallbackAtlas = fallback.atlasWidth, scene = scenePath,
    serialization = EditorSettings.serializationMode.ToString(), versionControl = VersionControlSettings.mode,
    unity = Application.unityVersion };

}
}
}
