using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Withme.UI;

public static class ExtendRegistrationScene
{
    static Color Background = Hex("FCFDF9"), Ink = Hex("273C35"), Primary = Hex("496A59"), Muted = Hex("718078"), Soft = Hex("F0F3ED");
    public static object Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play and finish compilation first.");
        const string path = "Assets/Withme/Scenes/Start.unity";
        var scene = SceneManager.GetSceneByPath(path);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("Save your current scene changes first.");
        if (scene.GetRootGameObjects().Any(g => g.GetComponent<RegistrationFlow>())) throw new InvalidOperationException("Registration already exists; edit in place.");
        SceneManager.SetActiveScene(scene);
        var login = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LoginFlow>(true)).Single();
        var flow = new GameObject("RegistrationFlow").AddComponent<RegistrationFlow>();
        flow.login = login; flow.loginCanvas = login.safeArea.GetComponentInParent<Canvas>().gameObject;
        login.registrationButton = Button(login.resultPanel.transform, "ContinueRegistration", "사진 등록 이어가기", 24, 585, 342, 50, login.ContinueRegistration);
        login.registrationButton.gameObject.SetActive(false);
        var canvas = new GameObject("RegistrationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.GetComponent<Canvas>().sortingOrder = 1;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(390, 844); scaler.matchWidthOrHeight = 0.5f;
        flow.registrationCanvas = canvas;
        Image(canvas.transform, "Background", 0, 0, 390, 1200, Background);
        flow.safeArea = Stretch("SafeArea", canvas.transform);
        var scrolling = Stretch("Scroll", flow.safeArea);
        flow.scroll = scrolling.gameObject.AddComponent<ScrollRect>(); flow.scroll.horizontal = false;
        flow.scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Stretch("Viewport", scrolling); viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport, 0, 0, 390, 940);
        content.anchorMax = Vector2.one; content.sizeDelta = new Vector2(0, 940);
        flow.scroll.viewport = viewport; flow.scroll.content = content;
        flow.profilePanel = Stretch("ProfilePage", content).gameObject;
        flow.noticePanel = Stretch("NoticePage", content).gameObject;
        flow.photoPanel = Stretch("PhotoOnlyPage", content).gameObject;
        flow.uploadedPanel = Stretch("UploadedPage", content).gameObject;
        var profile = flow.profilePanel.transform;
        Text(profile, "Headline", "우리 아이를 알려주세요", 24, 109, 342, 42, 26, Ink, FontStyles.Bold);
        Text(profile, "Description", "사진으로 강아지를 찾아볼게요.\n얼굴과 몸이 잘 보이는 사진을 골라 주세요.", 24, 161, 342, 52, 15, Muted);
        Text(profile, "NameCaption", "이름", 24, 234, 342, 24, 13, Muted);
        flow.nameInput = Input(profile, "NameInput", "우리 아이 이름", 24, 264, 342, 50);
        // Validate Unicode code points ourselves; TMP's UTF-16 limit would split emoji.
        flow.nameInput.characterLimit = 60;
        flow.photoCount = Text(profile, "PhotoCount", "우리 아이 사진 · 0 / 4", 24, 335, 342, 26, 13, Ink);
        flow.thumbnails = new RawImage[4]; flow.photoStatuses = new TMP_Text[4];
        flow.removeButtons = new Button[4]; flow.retryButtons = new Button[4]; flow.slotButtons = new Button[4];
        for (var i = 0; i < 4; i++)
        {
            var x = 24 + i % 2 * 176; var y = 375 + i / 2 * 155;
            var slot = Button(profile, "PhotoSlot" + i, "+", x, y, 166, 100, flow.PickPhoto, true);
            slot.GetComponent<Image>().color = Soft; flow.slotButtons[i] = slot;
            flow.thumbnails[i] = Photo(slot.transform, "Preview", 0, 0, 166, 100);
            flow.photoStatuses[i] = Text(profile, "PhotoStatus" + i, "사진 추가", x, y + 105, 166, 34, 11, Muted);
            var remove = Button(slot.transform, "Remove", "삭제", 120, 4, 42, 28, null);
            UnityEventTools.AddIntPersistentListener(remove.onClick, flow.RemovePhoto, i); flow.removeButtons[i] = remove;
            var retry = Button(slot.transform, "RetryPhoto", "다시 시도", 30, 35, 106, 32, null);
            UnityEventTools.AddIntPersistentListener(retry.onClick, flow.RetryPhoto, i); flow.retryButtons[i] = retry;
        }
        flow.addButton = Button(profile, "AddPhoto", "사진 추가하기", 24, 687, 342, 42, flow.PickPhoto, true);
        Text(profile, "PhotoHint", "사진 1~4장 · 한 장당 최대 10MB\nJPEG, PNG, WebP, HEIC · 선택만으로 전송되지 않아요.", 24, 733, 342, 48, 11, Muted);
        flow.nextButton = Button(profile, "Next", "다음", 24, 790, 342, 48, flow.Next);
        Button(profile, "PhotoOnly", "사진으로만 함께하기", 24, 845, 342, 40, flow.ShowPhotoOnly, true);
        var notice = flow.noticePanel.transform;
        Text(notice, "Headline", "사진을 보내기 전에", 24, 109, 342, 42, 26, Ink, FontStyles.Bold);
        flow.noticePhoto = Photo(notice, "SelectedPhoto", 24, 175, 342, 185);
        Text(notice, "Purpose", "사진으로 강아지를 찾아볼게요", 24, 387, 342, 34, 20, Ink, FontStyles.Bold);
        Text(notice, "Disclosure", "선택한 사진을 서버에 올려요. 이후 강아지의 모습을 찾는 과정에서 Gemini(Google)와 Meshy에 전달될 수 있어요.\n\nGemini는 사진 속 모습을 정리하고, Meshy는 3D 모습을 만드는 데 사용해요.\n\nAI가 표현한 모습은 실제와 다를 수 있어요. 원하지 않으면 사진으로만 함께할 수 있어요.", 24, 434, 342, 225, 15, Muted);
        flow.confirmButton = Button(notice, "ConfirmUpload", "안내를 확인하고 사진 올리기", 24, 705, 342, 52, flow.ConfirmUpload);
        Button(notice, "PhotoOnly", "사진으로만 함께하기", 24, 772, 342, 44, flow.ShowPhotoOnly, true);
        var photo = flow.photoPanel.transform;
        Text(photo, "Headline", "사진으로 함께해요", 24, 109, 342, 42, 26, Ink, FontStyles.Bold);
        flow.localPhoto = Photo(photo, "LocalPhoto", 24, 185, 342, 400);
        Text(photo, "Description", "이 화면의 사진은 기기 안에서만 보여요.\n등록 전에는 서버로 보내지 않아요.", 24, 616, 342, 64, 15, Muted);
        Button(photo, "Return", "사진 선택으로 돌아가기", 24, 735, 342, 48, flow.Back);
        var uploaded = flow.uploadedPanel.transform;
        Text(uploaded, "Headline", "사진을 올렸어요", 24, 158, 342, 55, 28, Ink, FontStyles.Bold);
        flow.uploadedDescription = Text(uploaded, "Description", "", 24, 239, 342, 100, 16, Muted);
        Text(uploaded, "NextStep", "다음 단계에서 사진을 바탕으로\n우리 아이의 모습을 찾아볼 거예요.", 24, 377, 342, 90, 16, Ink);
        Button(uploaded, "Return", "등록한 사진 확인하기", 24, 705, 342, 50, flow.Back);
        // Shared header and messages stay outside page panels.
        Button(content, "Back", "‹", 18, 35, 38, 44, flow.Back, true);
        Text(content, "Title", "우리 아이 등록", 65, 43, 280, 30, 14, Ink);
        flow.modeLabel = Text(content, "DevelopmentMode", "", 24, 81, 342, 20, 10, Muted);
        flow.message = Text(content, "Message", "", 24, 889, 342, 45, 12, Hex("9A5949"));
        canvas.SetActive(false);
        EditorUtility.SetDirty(login); EditorUtility.SetDirty(flow);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        return new { scene = path, photoSlots = 4, consentApi = false, defaultDemo = login.useDemoResponses };
    }
    static RawImage Photo(Transform parent, string name, float x, float y, float w, float h)
    {
        var frame = Rect(name + "Frame", parent, x, y, w, h);
        frame.gameObject.AddComponent<RectMask2D>();
        var image = Stretch(name, frame).gameObject.AddComponent<RawImage>(); image.raycastTarget = false;
        var fit = image.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        return image;
    }
    static RectTransform Stretch(string name, Transform parent)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; return r;
    }
    static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r;
    }
    static Image Image(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); image.type = UnityEngine.UI.Image.Type.Sliced;
        image.color = color; image.raycastTarget = false; return image;
    }
    static TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, int size, Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset; text.text = value; text.fontSize = size; text.fontStyle = style;
        text.color = color; text.alignment = align; text.raycastTarget = false; return text;
    }
    static Button Button(Transform parent, string name, string label, float x, float y, float w, float h, UnityAction action, bool link = false)
    {
        var image = Image(parent, name, x, y, w, h, link ? Color.clear : Primary); image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        if (action != null) UnityEventTools.AddPersistentListener(button.onClick, action);
        Text(image.transform, "Label", label, 0, 0, w, h, 14, link ? Primary : Color.white, align: TextAlignmentOptions.Center); return button;
    }
    static TMP_InputField Input(Transform parent, string name, string placeholder, float x, float y, float w, float h)
    {
        var image = Image(parent, name, x, y, w, h, Color.white); image.raycastTarget = true;
        var field = image.gameObject.AddComponent<TMP_InputField>();
        var viewport = Rect("TextArea", image.transform, 14, 4, w - 28, h - 8); viewport.gameObject.AddComponent<RectMask2D>();
        var text = Text(viewport, "Text", "", 0, 0, w - 28, h - 8, 15, Ink, align: TextAlignmentOptions.MidlineLeft);
        var hint = Text(viewport, "Placeholder", placeholder, 0, 0, w - 28, h - 8, 15, Muted, align: TextAlignmentOptions.MidlineLeft);
        field.textViewport = viewport; field.textComponent = (TextMeshProUGUI)text; field.placeholder = hint;
        field.targetGraphic = image; field.lineType = TMP_InputField.LineType.SingleLine; return field;
    }
    static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
}
