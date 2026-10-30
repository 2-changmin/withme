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

public static class ExtendGenerationScene
{
    static Color Background = Hex("FCFDF9"), Ink = Hex("273C35"), Primary = Hex("496A59"), Muted = Hex("718078"), Soft = Hex("F0F3ED");
    public static object Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play first");
        var scene = SceneManager.GetSceneByPath("Assets/Withme/Scenes/Start.unity");
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/Withme/Scenes/Start.unity", OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("Save scene first");
        if (scene.GetRootGameObjects().Any(g => g.GetComponent<GenerationFlow>())) throw new InvalidOperationException("Already added; edit in place");
        SceneManager.SetActiveScene(scene);
        var login = UnityEngine.Object.FindFirstObjectByType<LoginFlow>();
        var registration = UnityEngine.Object.FindFirstObjectByType<RegistrationFlow>();
        var f = new GameObject("GenerationFlow").AddComponent<GenerationFlow>(); f.login = login; f.registration = registration;
        var canvas = new GameObject("GenerationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; canvas.GetComponent<Canvas>().sortingOrder = 2;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(390,844); scaler.matchWidthOrHeight = .5f;
        f.canvas = canvas;
        var background = Stretch("Background", canvas.transform).gameObject.AddComponent<UnityEngine.UI.Image>(); background.color = Background; background.raycastTarget = false;
        f.safeArea = Stretch("SafeArea", canvas.transform);
        var scrolling = Stretch("Scroll", f.safeArea); var scroll = scrolling.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Stretch("Viewport", scrolling); viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport, 0, 0, 390, 844); content.anchorMax = Vector2.one; content.sizeDelta = new Vector2(0,844); scroll.viewport = viewport; scroll.content = content;
        f.loadingPanel = Stretch("Loading", content).gameObject; f.reviewPanel = Stretch("Review", content).gameObject;
        f.readyPanel = Stretch("Ready", content).gameObject; f.failurePanel = Stretch("Failure", content).gameObject; f.photoPanel = Stretch("Photo", content).gameObject;
        var loading = f.loadingPanel.transform;
        Text(loading,"Description","앱을 닫아도 준비는 계속돼요.\n돌아오면 준비하던 곳부터 이어갈 수 있어요.",24,220,342,70,15,Muted);
        Image(loading,"ProgressTrack",24,405,342,12,Soft);
        var slider = Rect("Progress",loading,24,405,342,12).gameObject.AddComponent<Slider>(); slider.interactable = false; slider.transition = UnityEngine.UI.Selectable.Transition.None;
        var fillArea = Stretch("FillArea", slider.transform); var fill = Stretch("Fill",fillArea).gameObject.AddComponent<UnityEngine.UI.Image>(); fill.color=Primary; fill.raycastTarget=false; slider.fillRect=fill.rectTransform; f.progress=slider;
        f.progressLabel=Text(loading,"ProgressLabel","준비 중",24,438,342,35,15,Primary);
        var review=f.reviewPanel.transform;
        f.photoCaption=Text(review,"Caption","사진을 눌러 얼굴과 무늬를 살펴봐 주세요.",24,194,342,48,13,Muted);
        f.originalPhoto=Photo(review,"Original",24,247,92,92); Button(review,"OriginalZoom","원본",126,270,90,38,null,true);
        var originalZoom = review.Find("OriginalZoom").GetComponent<Button>(); UnityEventTools.AddIntPersistentListener(originalZoom.onClick,f.Zoom,4);
        f.views=new RawImage[4];
        // API order is side/front/back/other; generic labels avoid inventing left/right orientation.
        for(var i=0;i<4;i++)
        {
            var x=24+i%2*176; var y=365+i/2*143;
            var button=Button(review,"View"+i,"",x,y,166,114,null,true); button.GetComponent<UnityEngine.UI.Image>().color=Soft;
            f.views[i]=Photo(button.transform,"Picture",0,0,166,114);
            UnityEventTools.AddIntPersistentListener(button.onClick,f.Zoom,i);
            Text(review,"ViewLabel"+i,"사진 "+(i+1),x,y+117,166,23,11,Muted);
        }
        f.approveButton=Button(review,"Approve","네, 우리 아이가 맞아요",24,669,342,48,f.Approve);
        f.retryButton=Button(review,"RetryViews","다시 찾아보기",24,729,342,42,f.RetryViews,true); f.retryLabel=f.retryButton.GetComponentInChildren<TMP_Text>();
        var ready=f.readyPanel.transform;
        var modelImage=Rect("Model",ready,24,206,342,380).gameObject.AddComponent<RawImage>(); modelImage.raycastTarget=false;
        f.modelPreview=new GameObject("GenerationModelPreview").AddComponent<GenerationModelPreview>();
        var anchor=new GameObject("ModelAnchor").transform; anchor.position=new Vector3(1000,0,0); f.modelPreview.modelAnchor=anchor;
        var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>(); camera.orthographic=true; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Soft; camera.cullingMask=1<<30;
        var rt=new RenderTexture(684,760,24); rt.name="GenerationPreview"; AssetDatabase.CreateAsset(rt,"Assets/Withme/Settings/GenerationPreview.renderTexture"); camera.targetTexture=rt; modelImage.texture=rt; f.modelPreview.previewCamera=camera;
        var light=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.cullingMask=1<<30; light.transform.rotation=Quaternion.Euler(40,-35,0);
        Text(ready,"Description","AI가 사진을 바탕으로 표현한 모습이에요.\n움직임은 다음 단계에서 준비해요.",24,608,342,60,14,Muted);
        Button(ready,"Rotate","다른 쪽에서 보기",24,687,342,44,f.modelPreview.Rotate,true);
        Text(ready,"Handoff","준비한 모습을 저장했어요. 다음 단계에서 이어갈 수 있어요.",24,742,342,50,12,Muted);
        var error=f.failurePanel.transform;
        f.errorLabel=Text(error,"Error","",24,218,342,210,16,Muted);
        f.recoverButton=Button(error,"Recover","준비 상태 확인하기",24,578,342,50,f.Resume);
        var photo=f.photoPanel.transform; f.photoOnly=Photo(photo,"Photo",24,211,342,380);
        Button(photo,"Continue","준비 이어하기",24,665,342,48,f.Resume);
        Text(photo,"Description","사진을 보는 동안에도 서버의 준비는 계속돼요.",24,608,342,45,14,Muted);
        Button(content,"PhotoMode","사진 모드로 이용",24,785,165,40,f.PhotoMode,true);
        Button(content,"Later","나중에 이어하기",201,785,165,40,f.Later,true);
        Text(content,"Title","우리 아이 만나기",65,43,280,28,14,Ink);
        f.modeLabel=Text(content,"Mode","",24,83,342,28,10,Muted);
        f.heading=Text(content,"Heading","사진 속 모습을 찾고 있어요",24,124,342,65,25,Ink,FontStyles.Bold);
        f.zoomPanel=Stretch("Zoom",f.safeArea).gameObject; f.zoomPanel.AddComponent<UnityEngine.UI.Image>().color=Background;
        f.zoomImage=Photo(f.zoomPanel.transform,"Image",20,120,350,560); Button(f.zoomPanel.transform,"Close","사진 닫기",24,735,342,48,f.CloseZoom); f.zoomPanel.SetActive(false);
        f.resumeButton=Button(login.resultPanel.transform,"ResumeGeneration","준비 이어하기",24,522,342,50,f.Resume); f.resumeButton.gameObject.SetActive(false);
        canvas.SetActive(false);
        // Explicit requested fixture; visibly labelled and only operates against the loopback mock.
        login.useDemoResponses=true;
        EditorUtility.SetDirty(login); EditorUtility.SetDirty(f); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        return new {scene=scene.path,fixture=true,views=4};
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
