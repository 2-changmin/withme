using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using GLTFast;
using GLTFast.Logging;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Withme.Character;

public static class CreateStandardRigProbe
{
    static Color Background=Hex("FCFDF9"),Ink=Hex("273C35"),Primary=Hex("496A59"),Muted=Hex("718078"),Soft=Hex("F0F3ED");
    public static async Task<object> Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("Stop Play first");
        const string scenePath="Assets/Withme/Scenes/StandardRigValidation.unity";
        if(File.Exists(scenePath))throw new Exception("Already exists; edit in place");
        var previous=SceneManager.GetActiveScene();if(previous.isDirty)throw new Exception("Save current scene first");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        GltfImport gltf=null;GameObject imported=null;
        try
        {
            Folder("Assets/Withme/Animations/StandardV1");
            var library=ScriptableObject.CreateInstance<StandardMotionLibrary>();
            gltf=new GltfImport(deferAgent:new UninterruptedDeferAgent(),logger:new CollectingLogger());
            var file=new Uri(Path.GetFullPath("Assets/StreamingAssets/Validation/StandardRig/m1_golden.glb")).AbsoluteUri;
            if(!await gltf.Load(file,new ImportSettings{AnimationMethod=AnimationMethod.Mecanim}))throw new Exception("Fixture failed");
            imported=new GameObject("LibrarySource");if(!await gltf.InstantiateMainSceneAsync(imported.transform))throw new Exception("Instantiate failed");
            var skin=imported.GetComponentInChildren<SkinnedMeshRenderer>();
            library.bones=skin.bones.Select(b=>new BoneRest{name=b.name,position=b.localPosition,rotation=b.localRotation,scale=b.localScale}).ToArray();
            library.pelvisHeight=Mathf.Abs(skin.bones.Single(b=>b.name=="pelvis").position.y);
            library.clips=gltf.GetAnimationClips().Select(source=>{
                var clip=UnityEngine.Object.Instantiate(source);clip.name=source.name;clip.legacy=false;
                var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=StandardRigPlayback.Loops.Contains(clip.name);AnimationUtility.SetAnimationClipSettings(clip,settings);
                AssetDatabase.CreateAsset(clip,"Assets/Withme/Animations/StandardV1/"+clip.name+".anim");return clip;
            }).ToArray();
            AssetDatabase.CreateAsset(library,"Assets/Withme/Animations/StandardV1/Library.asset");
            UnityEngine.Object.DestroyImmediate(imported);imported=null;gltf.Dispose();gltf=null;
            var probe=new GameObject("StandardRigProbe").AddComponent<StandardRigProbe>();
            var names=new[]{"m1_golden","m2_white","m3_cream"};probe.players=new StandardRigPlayback[3];
            for(var i=0;i<3;i++){
                var player=new GameObject(names[i]).AddComponent<StandardRigPlayback>();player.transform.position=new Vector3((i-1)*1.15f,0,0);player.fixtureName=names[i];player.sharedLibrary=library;probe.players[i]=player;
            }
            var camera=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(0,1.25f,4);camera.transform.LookAt(new Vector3(0,.5f,0));camera.orthographic=true;camera.orthographicSize=.92f;camera.backgroundColor=Soft;camera.clearFlags=CameraClearFlags.SolidColor;camera.rect=new Rect(0,.29f,1,.65f);
            RenderSettings.ambientLight=new Color(.65f,.65f,.65f);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            var light=new GameObject("Light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.transform.rotation=Quaternion.Euler(40,-25,0);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Ground";floor.transform.localScale=new Vector3(.5f,1,.25f);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=Hex("E9EDE6");AssetDatabase.CreateAsset(material,"Assets/Withme/Animations/StandardV1/Ground.mat");floor.GetComponent<Renderer>().sharedMaterial=material;
            var canvas=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1200,900);scaler.matchWidthOrHeight=.5f;
            var header=Rect("HeaderBackground",canvas.transform,0,0,1200,130).gameObject.AddComponent<Image>();header.color=Soft;header.raycastTarget=false;
            var footer=Rect("FooterBackground",canvas.transform,0,640,1200,260).gameObject.AddComponent<Image>();footer.color=Soft;footer.raycastTarget=false;
            Text(canvas.transform,"Title","표준 뼈대·공용 동작 PC 검증",28,18,1140,36,26,Ink,FontStyles.Bold);
            probe.status=Text(canvas.transform,"Status","불러오는 중",28,62,1135,64,17,Muted);
            var clips=library.clips.Select(c=>c.name).ToArray();
            for(var i=0;i<clips.Length;i++){
                var b=Button(canvas.transform,"Clip"+clips[i],clips[i],28+i%6*194,657+i/6*58,182,48,null);UnityEventTools.AddStringPersistentListener(b.onClick,probe.Play,clips[i]);
            }
            Button(canvas.transform,"Source","공용/모델별 비교",28,837,230,44,probe.ToggleSource);
            Button(canvas.transform,"Pause","일시 정지/재개",276,837,210,44,probe.TogglePause);
            Text(canvas.transform,"Notice","품질 경고가 남은 검토용 모델입니다. 모바일 성능·사용자 품질 승인은 별도입니다.",510,843,660,42,13,Muted);
            var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)).GetComponent<InputSystemUIInputModule>();events.AssignDefaultActions();
            EditorSceneManager.SaveScene(scene,scenePath);AssetDatabase.SaveAssets();
            return new {scene=scenePath,clips=library.clips.Length,bones=library.bones.Length,pelvis=library.pelvisHeight};
        }
        finally{if(imported!=null)UnityEngine.Object.DestroyImmediate(imported);gltf?.Dispose();EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var i=path.LastIndexOf('/');Folder(path.Substring(0,i));AssetDatabase.CreateFolder(path.Substring(0,i),path.Substring(i+1));}
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
