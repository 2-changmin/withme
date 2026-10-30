using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Withme.Auth;
using Withme.UI;

public static class CreateLoginScene
{
    static Color Background = Hex("FCFDF9"), Ink = Hex("273C35"), Primary = Hex("496A59"), Muted = Hex("718078"), Soft = Hex("F0F3ED");
    const string ScenePath = "Assets/Withme/Scenes/Start.unity";
    public static object Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play and finish compilation first.");
        if (File.Exists(ScenePath)) throw new InvalidOperationException("Start scene exists. Edit it in place.");
        Folder("Assets/Withme/Scenes"); Folder("Assets/Withme/Settings");
        var settings = AssetDatabase.LoadAssetAtPath<ConnectionSettings>("Assets/Withme/Settings/ConnectionSettings.asset");
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<ConnectionSettings>();
            AssetDatabase.CreateAsset(settings, "Assets/Withme/Settings/ConnectionSettings.asset");
        }
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("UICamera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Background;
            var flow = new GameObject("LoginFlow").AddComponent<LoginFlow>();
            flow.settings = settings;
            var canvas = new GameObject("LoginCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844); scaler.matchWidthOrHeight = 0.5f;
            var safe = Stretch("SafeArea", canvas.transform);
            flow.safeArea = safe;
            var scrollObject = Stretch("Scroll", safe);
            flow.scroll = scrollObject.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            flow.scroll.horizontal = false; flow.scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            var viewport = Stretch("Viewport", scrollObject);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var content = Rect("Content", viewport, 0, 0, 390, 844);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.sizeDelta = new Vector2(0, 844);
            flow.scroll.viewport = viewport; flow.scroll.content = content;
            flow.emailPanel = Stretch("EmailPage", content).gameObject;
            flow.codePanel = Stretch("CodePage", content).gameObject;
            flow.resultPanel = Stretch("ResultPage", content).gameObject;

            var email = flow.emailPanel.transform;
            Text(email, "Brand", "곁에", 24, 42, 80, 32, 20, Ink, FontStyles.Bold);
            Text(email, "PageTitle", "시작하기", 145, 44, 100, 28, 14, Ink, align: TextAlignmentOptions.Center);
            Image(email, "PrivateBadge", 24, 123, 111, 25, Soft);
            Text(email, "Private", "PRIVATE SPACE", 24, 123, 111, 25, 11, Primary, align: TextAlignmentOptions.Center);
            Text(email, "Headline", "기억을 꺼내는\n우리만의 작은 공간", 24, 168, 342, 88, 30, Ink, FontStyles.Bold);
            Text(email, "Description", "사진 속 우리 아이를 만나고,\n원할 때 조용히 함께해요.", 24, 265, 342, 52, 15, Muted);
            Image(email, "MemoryCard", 24, 333, 342, 167, Soft);
            Text(email, "MemoryCaption", "사진과 기억", 40, 344, 150, 26, 11, Primary);
            // Wireframe illustration: a pair of photo cards, no invented personal photo.
            Image(email, "PhotoBack", 155, 383, 94, 78, Hex("D6DDD7"));
            Image(email, "PhotoFront", 124, 373, 94, 78, Background);
            Text(email, "PhotoSymbol", "♡", 144, 386, 54, 42, 30, Primary, align: TextAlignmentOptions.Center);
            Text(email, "EmailCaption", "이메일 주소", 24, 518, 342, 28, 12, Muted);
            flow.emailInput = Input(email, "EmailInput", "name@example.com", 24, 551, 342, 52);
            flow.emailInput.contentType = TMP_InputField.ContentType.EmailAddress;
            flow.emailInput.characterLimit = 254;
            flow.emailInputRect = flow.emailInput.GetComponent<RectTransform>();
            flow.emailError = Text(email, "EmailError", "", 24, 609, 342, 38, 12, Hex("9A5949"));
            flow.sendButton = Button(email, "SendCode", "인증 코드 받기", 24, 650, 342, 50, flow.SendCode);
            Text(email, "Footer", "이메일로 받은 코드를 입력하면 시작할 수 있어요.\n사진을 바탕으로 AI가 표현한 모습이에요.", 24, 718, 342, 57, 12, Muted);

            var code = flow.codePanel.transform;
            flow.backButton = Button(code, "Back", "‹", 18, 35, 38, 44, flow.ChangeEmail, true);
            Text(code, "PageTitle", "이메일 인증", 60, 42, 230, 30, 14, Ink);
            Text(code, "Headline", "이메일을 확인해 주세요", 24, 137, 342, 44, 26, Ink, FontStyles.Bold);
            flow.recipientLabel = Text(code, "Recipient", "6자리 인증 코드를 입력해 주세요.", 24, 185, 342, 65, 14, Muted);
            flow.recipientLabel.fontSizeMin = 11; flow.recipientLabel.fontSizeMax = 14;
            flow.recipientLabel.enableAutoSizing = true;
            flow.codeInput = Input(code, "CodeInput", "", 24, 278, 342, 62);
            flow.codeInput.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
            flow.codeInput.characterValidation = TMP_InputField.CharacterValidation.Digit;
            flow.codeInput.keyboardType = TouchScreenKeyboardType.NumberPad;
            flow.codeInput.characterLimit = 6;
            flow.codeInput.textComponent.color = Color.clear;
            flow.codeInput.caretColor = Primary;
            flow.codeInput.customCaretColor = true;
            flow.codeInputRect = flow.codeInput.GetComponent<RectTransform>();
            flow.codeDigits = new TMP_Text[6];
            for (var i = 0; i < 6; i++)
            {
                Image(code, "DigitBox" + i, 24 + i * 58, 278, 51, 62, Color.white);
                flow.codeDigits[i] = Text(code, "Digit" + i, "", 24 + i * 58, 278, 51, 62, 23, Ink, align: TextAlignmentOptions.Center);
            }
            // Decorations are not raycast targets: one real input handles focus, paste and editing.
            flow.codeError = Text(code, "CodeError", "", 24, 350, 342, 43, 12, Hex("9A5949"));
            flow.resendButton = Button(code, "Resend", "코드 다시 받기", 24, 401, 342, 48, flow.ResendCode, true);
            flow.resendLabel = flow.resendButton.GetComponentInChildren<TMP_Text>();
            flow.verifyButton = Button(code, "Verify", "인증하고 시작하기", 24, 688, 342, 50, flow.VerifyCode);
            Button(code, "ChangeEmail", "이메일 주소 바꾸기", 24, 745, 342, 44, flow.ChangeEmail, true);

            var result = flow.resultPanel.transform;
            Text(result, "Brand", "곁에", 24, 42, 100, 32, 20, Ink, FontStyles.Bold);
            flow.resultTitle = Text(result, "ResultTitle", "로그인을 확인하고 있어요", 24, 180, 342, 85, 26, Ink, FontStyles.Bold);
            flow.resultDescription = Text(result, "ResultDescription", "잠시만 기다려 주세요.", 24, 280, 342, 140, 15, Muted);
            flow.retryButton = Button(result, "Retry", "다시 확인하기", 24, 650, 342, 50, flow.Retry);
            flow.logoutButton = Button(result, "Logout", "로그아웃", 24, 714, 342, 44, flow.Logout, true);
            flow.modeLabel = Text(content, "DevelopmentMode", "", 18, 805, 354, 24, 10, Muted, align: TextAlignmentOptions.Center);
            flow.codePanel.SetActive(false); flow.resultPanel.SetActive(false);
            var module = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return new {scene = ScenePath, demoByDefault = flow.useDemoResponses, keyConfigured = !string.IsNullOrEmpty(settings.publishableKey)};
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var split = path.LastIndexOf('/'); var parent = path.Substring(0, split);
        Folder(parent); AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }
    static RectTransform Stretch(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
    static UnityEngine.UI.Image Image(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = UnityEngine.UI.Image.Type.Sliced; image.color = color; image.raycastTarget = false;
        return image;
    }
    static TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, int size,
        Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset; text.text = value; text.fontSize = size;
        text.fontStyle = style; text.color = color; text.alignment = align; text.raycastTarget = false;
        return text;
    }
    static UnityEngine.UI.Button Button(Transform parent, string name, string label, float x, float y, float w, float h,
        UnityAction action, bool link = false)
    {
        var image = Image(parent, name, x, y, w, h, link ? Color.clear : Primary);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        UnityEventTools.AddPersistentListener(button.onClick, action);
        Text(image.transform, "Label", label, 0, 0, w, h, 15, link ? Primary : Color.white, align: TextAlignmentOptions.Center);
        return button;
    }
    static TMP_InputField Input(Transform parent, string name, string placeholder, float x, float y, float w, float h)
    {
        var image = Image(parent, name, x, y, w, h, Color.white); image.raycastTarget = true;
        var field = image.gameObject.AddComponent<TMP_InputField>();
        var viewport = Rect("TextArea", image.transform, 14, 4, w - 28, h - 8);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var text = Text(viewport, "Text", "", 0, 0, w - 28, h - 8, 15, Ink, align: TextAlignmentOptions.MidlineLeft);
        var hint = Text(viewport, "Placeholder", placeholder, 0, 0, w - 28, h - 8, 15, Muted, align: TextAlignmentOptions.MidlineLeft);
        field.textViewport = viewport; field.textComponent = (TextMeshProUGUI)text; field.placeholder = hint;
        field.targetGraphic = image; field.lineType = TMP_InputField.LineType.SingleLine;
        field.customCaretColor = true; field.caretColor = Primary;
        return field;
    }
    static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
}
