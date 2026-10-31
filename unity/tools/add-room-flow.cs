using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme;
using Withme.Character;
using Withme.UI;

public static class AddRoomFlow
{
    static TMP_FontAsset font;
    static readonly Color Ink=new Color(.153f,.235f,.208f),Background=new Color(.988f,.992f,.976f),Soft=new Color(.941f,.953f,.929f),Primary=new Color(.286f,.416f,.349f);
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");var setup=UnityEngine.Object.FindFirstObjectByType<CharacterSetupFlow>();if(setup==null)throw new Exception("Open Start first");if(setup.GetComponent<RoomFlow>()!=null)return new{existed=true};
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR/NotoSansKR-Dynamic.asset");var rig=setup.rig;var flow=setup.gameObject.AddComponent<RoomFlow>();flow.setup=setup;
        flow.dog=rig.player.gameObject.AddComponent<RoomDogController>();flow.dog.player=rig.player;flow.dog.sounds=rig.player.gameObject.AddComponent<DogSounds>();flow.dog.sounds.pitchJitter=.05f;
        var toy=new GameObject("RoomToy");toy.transform.SetParent(rig.player.transform.parent,false);flow.dog.toy=toy.transform;
        flow.ball=GameObject.CreatePrimitive(PrimitiveType.Sphere);flow.ball.name="Ball";flow.ball.transform.SetParent(toy.transform,false);flow.ball.transform.localScale=Vector3.one*.13f;
        flow.plush=GameObject.CreatePrimitive(PrimitiveType.Capsule);flow.plush.name="Plush";flow.plush.transform.SetParent(toy.transform,false);flow.plush.transform.localScale=new Vector3(.11f,.1f,.16f);
        foreach(var shape in new[]{flow.ball,flow.plush}){shape.layer=30;UnityEngine.Object.DestroyImmediate(shape.GetComponent<Collider>());}
        toy.SetActive(false);flow.floor=GameObject.CreatePrimitive(PrimitiveType.Plane);flow.floor.name="RoomGround";flow.floor.layer=30;flow.floor.transform.SetParent(rig.player.transform.parent,false);flow.floor.transform.localScale=Vector3.one*.3f;UnityEngine.Object.DestroyImmediate(flow.floor.GetComponent<Collider>());flow.floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Withme/Animations/StandardV1/Ground.mat");flow.floor.SetActive(false);
        var canvas=new GameObject("RoomCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;canvas.GetComponent<Canvas>().sortingOrder=40;
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(390,844);scaler.matchWidthOrHeight=.5f;flow.canvas=canvas;
        Stretch("Background",canvas.transform).gameObject.AddComponent<Image>().color=Background;
        flow.mainPanel=Stretch("Main",canvas.transform).gameObject;flow.view=Stretch("DogView",flow.mainPanel.transform).gameObject.AddComponent<RawImage>();flow.view.color=Color.white;flow.view.gameObject.AddComponent<RoomPointerSurface>().flow=flow;
        flow.safeArea=Stretch("SafeArea",canvas.transform);var actions=Stretch("Interactions",flow.safeArea);flow.interactions=actions;
        flow.greetButton=BottomButton(actions,"Greet","불러보기",24,24,108,48,flow.Greet);flow.toyButton=BottomButton(actions,"Toy","장난감",141,24,108,48,flow.ThrowToy);flow.restButton=BottomButton(actions,"Rest","곁에 앉기",258,24,108,48,flow.Rest);flow.restLabel=flow.restButton.GetComponentInChildren<TMP_Text>();
        flow.sleepButton=BottomButton(actions,"Sleep","잠들기",141,86,108,40,flow.Sleep);flow.sleepButton.gameObject.SetActive(false);
        flow.loading=Text(flow.mainPanel.transform,"Loading","",24,350,342,130,19);
        flow.photoPanel=Stretch("Photo",flow.safeArea).gameObject;flow.photoView=Stretch("Image",flow.photoPanel.transform).gameObject.AddComponent<RawImage>();flow.photoView.raycastTarget=false;var fit=flow.photoView.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=1;
        flow.photoMessage=Text(flow.photoPanel.transform,"Status","",24,670,342,80,16);
        flow.menuPanel=Stretch("Menu",flow.safeArea).gameObject;flow.menuPanel.AddComponent<Image>().color=Background;
        flow.menuTitle=Text(flow.menuPanel.transform,"Title","함께",24,74,342,36,23);flow.notice=Text(flow.menuPanel.transform,"Notice","",24,118,342,70,13);
        Button(flow.menuPanel.transform,"Close","닫기",276,24,90,36,flow.CloseMenu,false);
        var menu=flow.menuPanel.transform;
        flow.muteLabel=Button(menu,"Mute","소리 끄기",24,205,342,44,flow.ToggleMute,false).GetComponentInChildren<TMP_Text>();
        flow.motionLabel=Button(menu,"Reduced","움직임 줄이기",24,258,342,44,flow.ToggleMotion,false).GetComponentInChildren<TMP_Text>();
        flow.photoLabel=Button(menu,"Photo","사진 보기",24,311,342,44,flow.TogglePhoto,false).GetComponentInChildren<TMP_Text>();
        Button(menu,"Preferences","습관 바꾸기",24,364,342,44,flow.Preferences,false);Button(menu,"Review","모습과 움직임 확인",24,417,342,44,flow.Review,false);
        Button(menu,"Sounds","소리 안내",24,470,342,44,flow.SoundInfo,false);Button(menu,"Account","계정·동의 안내",24,523,342,44,flow.AccountInfo,false);
        Button(menu,"Retry","모습 다시 불러오기",24,576,342,44,flow.Return,false);Button(menu,"Logout","로그아웃",24,629,342,44,flow.Logout,false);Button(menu,"Exit","즉시 나가기",24,744,342,48,flow.ImmediateExit,true);
        flow.restPanel=Stretch("Rest",flow.safeArea).gameObject;flow.restPanel.AddComponent<Image>().color=Background;Text(flow.restPanel.transform,"Title","잠시 쉬어가요",24,305,342,44,26);Text(flow.restPanel.transform,"Description","원할 때 다시 함께할 수 있어요.",24,370,342,60,16);Button(flow.restPanel.transform,"Return","다시 함께하기",24,711,342,48,flow.Return,true);
        var menuButton=Button(flow.safeArea,"OpenMenu","메뉴",300,24,66,44,flow.OpenMenu,false);var menuRect=menuButton.GetComponent<RectTransform>();menuRect.anchorMin=menuRect.anchorMax=Vector2.one;menuRect.pivot=Vector2.one;menuRect.anchoredPosition=new Vector2(-24,-24);flow.menuButton=menuButton.gameObject;
        // Interaction controls are hidden together with the main panel, while
        // the single menu remains available in photo mode.
        actions.SetParent(flow.mainPanel.transform,false);actions.anchorMin=Vector2.zero;actions.anchorMax=Vector2.one;actions.offsetMin=actions.offsetMax=Vector2.zero;
        flow.photoPanel.SetActive(false);flow.menuPanel.SetActive(false);flow.restPanel.SetActive(false);canvas.SetActive(false);
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);AssetDatabase.SaveAssets();return new{scene=rig.gameObject.scene.path,singleMenu=true,interactions=4};
    }
    static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
    {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
    static RectTransform Stretch(string name,Transform parent){var r=Rect(name,parent,0,0,0,0);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r;}
    static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,int size)
    {var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=Ink;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;}
    static Button Button(Transform parent,string name,string label,float x,float y,float w,float h,UnityAction action,bool primary)
    {var r=Rect(name,parent,x,y,w,h);var image=r.gameObject.AddComponent<Image>();image.color=primary?Primary:Soft;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;UnityEventTools.AddPersistentListener(b.onClick,action);var t=Text(r,"Label",label,12,0,w-24,h,15);t.alignment=TextAlignmentOptions.Center;t.color=primary?Color.white:Ink;return b;}
    static Button BottomButton(Transform parent,string name,string label,float x,float y,float w,float h,UnityAction action)
    {var b=Button(parent,name,label,x,0,w,h,action,false);var r=b.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=Vector2.zero;r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(x,y);return b;}
}
