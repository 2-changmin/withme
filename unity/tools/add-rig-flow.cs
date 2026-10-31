using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Withme.Character;
using Withme.UI;

public static class AddRigFlow
{
    static readonly Color Ink = Hex("273C35"), Muted = Hex("718078"), Primary = Hex("496A59"), Background = Hex("FCFDF9"), Soft = Hex("F0F3ED");
    static TMP_FontAsset font;
    public static object Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play first");
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath("Assets/Withme/Scenes/Start.unity"); var opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Withme/Scenes/Start.unity", OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            foreach (var root in scene.GetRootGameObjects()) if (root.GetComponentInChildren<RigFlow>(true) != null) return new { existed = true };
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR/NotoSansKR-Dynamic.asset");
            var generation = Array.Find(scene.GetRootGameObjects(), g => g.GetComponent<GenerationFlow>() != null).GetComponent<GenerationFlow>();
            var flow = new GameObject("RigFlow").AddComponent<RigFlow>(); flow.generation = generation; generation.mockBaseUrl = "http://127.0.0.1:8767";
            var canvas = new GameObject("RigCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; canvas.GetComponent<Canvas>().sortingOrder = 30;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(390,844); scaler.matchWidthOrHeight = .5f;
            Stretch("Background", canvas.transform).gameObject.AddComponent<Image>().color = Background;
            flow.canvas = canvas; flow.safeArea = Stretch("SafeArea",canvas.transform);
            var scroll = Stretch("Scroll",flow.safeArea).gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Stretch("Viewport",scroll.transform); viewport.gameObject.AddComponent<RectMask2D>(); scroll.viewport = viewport;
            var content = Rect("Content",viewport,0,0,390,844); content.anchorMax = Vector2.one; content.sizeDelta = new Vector2(0,844); scroll.content = content;
            Text(content,"Brand","곁에",24,30,80,28,19,Ink);
            flow.mode = Text(content,"Mode","",24,68,342,28,11,Muted);
            flow.heading = Text(content,"Heading","우리 아이의 걸음을 준비하고 있어요",24,112,342,62,24,Ink);
            flow.loadingPanel = Stretch("Loading",content).gameObject;
            flow.status = Text(flow.loadingPanel.transform,"Status","준비 순서를 기다리고 있어요",24,330,342,54,18,Ink);
            var track = Rect("Progress",flow.loadingPanel.transform,24,415,342,8); track.gameObject.AddComponent<Image>().color = Soft;
            flow.progress = track.gameObject.AddComponent<Slider>(); flow.progress.interactable = false; flow.progress.transition = Selectable.Transition.None;
            var fill = Stretch("Fill",track); fill.gameObject.AddComponent<Image>().color = Primary; flow.progress.fillRect = fill;
            Text(flow.loadingPanel.transform,"Hint","화면을 나가도 준비한 작업은 그대로 두고\n다음에 이어서 확인할게요.",24,465,342,60,15,Muted);
            Button(flow.loadingPanel.transform,"Photo","사진으로 함께하기",24,676,342,48,flow.PhotoMode,false);
            Button(flow.loadingPanel.transform,"Later","나중에 확인할게요",24,738,342,48,flow.Leave,false);
            flow.walkPanel = Stretch("Walk",content).gameObject;
            var dogFrame = Rect("DogFrame",flow.walkPanel.transform,24,190,342,330);
            flow.preview = Stretch("DogView",dogFrame).gameObject.AddComponent<RawImage>(); flow.preview.color = Soft; flow.preview.raycastTarget = false;
            flow.preview.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            flow.preview.gameObject.GetComponent<AspectRatioFitter>().aspectRatio = 1;
            Button(flow.walkPanel.transform,"Rotate","돌려 보기",24,525,130,38,flow.Rotate,false);
            Button(flow.walkPanel.transform,"ZoomIn","확대",230,525,62,38,flow.ZoomIn,false);
            Button(flow.walkPanel.transform,"ZoomOut","축소",300,525,66,38,flow.ZoomOut,false);
            flow.quality = Text(flow.walkPanel.transform,"Quality","",24,577,342,48,14,Muted);
            flow.motionCorrection = Button(flow.walkPanel.transform,"MotionCorrection","움직임이 어색해요",24,636,342,40,null,false);
            flow.appearanceCorrection = Button(flow.walkPanel.transform,"AppearanceCorrection","외형이 어색해요",24,682,342,40,null,false);
            flow.confirm = Button(flow.walkPanel.transform,"Confirm","네, 이 모습으로 만날게요",24,744,342,48,null,true);
            flow.errorPanel = Stretch("Error",content).gameObject;
            flow.error = Text(flow.errorPanel.transform,"Message","",24,270,342,160,18,Ink);
            flow.recover = Button(flow.errorPanel.transform,"Recover","상태 다시 확인",24,510,342,48,flow.Resume,true);
            flow.retryRig = Button(flow.errorPanel.transform,"Retry","걸음 다시 준비",24,570,342,48,flow.RetryFailedRig,false);
            Button(flow.errorPanel.transform,"Photo","사진으로 함께하기",24,676,342,48,flow.PhotoMode,false);
            Button(flow.errorPanel.transform,"Later","나중에 확인할게요",24,738,342,48,flow.Leave,false);
            var stage = new GameObject("RigPreviewStage"); stage.transform.position = new Vector3(2000,0,0);
            flow.player = new GameObject("Dog").AddComponent<StandardRigPlayback>(); flow.player.transform.SetParent(stage.transform,false);
            flow.player.sharedLibrary = AssetDatabase.LoadAssetAtPath<StandardMotionLibrary>("Assets/Withme/Animations/StandardV1/Library.asset");
            flow.previewCamera = new GameObject("RigCamera",typeof(Camera)).GetComponent<Camera>(); flow.previewCamera.transform.SetParent(stage.transform,false);
            flow.previewCamera.transform.localPosition = new Vector3(1.5f,1.05f,2.5f); flow.previewCamera.transform.LookAt(stage.transform.position + Vector3.up*.48f);
            flow.previewCamera.orthographic = true; flow.previewCamera.orthographicSize = .85f; flow.previewCamera.nearClipPlane = .01f; flow.previewCamera.farClipPlane = 10;
            flow.previewCamera.cullingMask = 1 << 30; flow.previewCamera.clearFlags = CameraClearFlags.SolidColor; flow.previewCamera.backgroundColor = Soft; flow.previewCamera.enabled = false;
            var light = new GameObject("RigLight",typeof(Light)).GetComponent<Light>(); light.transform.SetParent(stage.transform,false); light.type = LightType.Directional; light.intensity = 1.3f; light.cullingMask = 1 << 30; light.transform.rotation = Quaternion.Euler(40,-25,0);
            flow.loadingPanel.SetActive(true); flow.walkPanel.SetActive(false); flow.errorPanel.SetActive(false); canvas.SetActive(false);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); return new { scene = scene.path, created = "RigFlow", fixturePort = 8767 };
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene,true); if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
    }
    static Color Hex(string value) { ColorUtility.TryParseHtmlString("#"+value,out var color); return color; }
    static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
    { var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r; }
    static RectTransform Stretch(string name,Transform parent)
    { var r=Rect(name,parent,0,0,0,0);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r; }
    static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,int size,Color color)
    { var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=color;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t; }
    static Button Button(Transform parent,string name,string label,float x,float y,float w,float h,UnityAction action,bool primary)
    { var r=Rect(name,parent,x,y,w,h);var image=r.gameObject.AddComponent<Image>();image.color=primary?Primary:Soft;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;
      if(action!=null)UnityEventTools.AddPersistentListener(b.onClick,action);var t=Text(r,"Label",label,12,0,w-24,h,15,primary?Color.white:Ink);t.alignment=TextAlignmentOptions.Center;return b; }
}
