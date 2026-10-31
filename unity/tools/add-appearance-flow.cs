using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme.Character;
using Withme.UI;

public static class AddAppearanceFlow
{
    static TMP_FontAsset font;
    static readonly Color Ink=new Color(.153f,.235f,.208f),Soft=new Color(.941f,.953f,.929f),Primary=new Color(.286f,.416f,.349f);
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var rig=UnityEngine.Object.FindFirstObjectByType<RigFlow>();if(rig==null)throw new Exception("Open Start scene first");
        if(rig.appearanceFlow!=null)return new{existed=true};font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR/NotoSansKR-Dynamic.asset");
        var flow=rig.gameObject.AddComponent<AppearanceFlow>();flow.rig=rig;rig.appearanceFlow=flow;
        flow.appearance=rig.player.gameObject.AddComponent<CharacterAppearance>();flow.appearance.player=rig.player;flow.appearance.coatShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Withme/Shaders/CoatPreview.shader");
        if(flow.appearance.coatShader==null)throw new Exception("Missing coat shader");
        var panel=Rect("Appearance",rig.walkPanel.transform.parent,0,0,0,0);panel.anchorMin=Vector2.zero;panel.anchorMax=Vector2.one;panel.offsetMin=panel.offsetMax=Vector2.zero;flow.panel=panel.gameObject;
        flow.originalPhoto=Photo(panel,"Original",24,190,162,160);flow.preview=Photo(panel,"Preview",204,190,162,160);
        Text(panel,"OriginalCaption","원본 사진",24,354,162,22,12);Text(panel,"PreviewCaption","함께 만날 모습",204,354,162,22,12);
        flow.hint=Text(panel,"Hint","원본 사진과 비교하며 조금씩 맞춰 주세요.",24,382,342,26,13);
        var names=new[]{"귀 크기","귀 기울기","꼬리 길이","다리 길이","몸 길이","머리 크기","털 색감","털 밝기"};
        var mins=new[]{.5f,-45,.5f,.7f,.7f,.8f,-30,-.3f};var maxs=new[]{1.5f,45,1.5f,1.3f,1.3f,1.2f,30,.3f};var defaults=new[]{1f,0,1,1,1,1,0,0};flow.sliders=new Slider[8];
        for(var i=0;i<8;i++)
        {
            var y=416+i*37;Text(panel,"Label"+i,names[i],24,y-3,120,30,14);
            var r=Rect("Adjust"+i,panel,155,y,211,22);var slider=r.gameObject.AddComponent<Slider>();slider.minValue=mins[i];slider.maxValue=maxs[i];slider.value=defaults[i];
            var track=Rect("Track",r,0,7,211,8).gameObject.AddComponent<Image>();track.color=Soft;
            var fill=Rect("Fill",track.transform,0,0,0,0);fill.anchorMin=Vector2.zero;fill.anchorMax=Vector2.one;fill.offsetMin=fill.offsetMax=Vector2.zero;fill.gameObject.AddComponent<Image>().color=Primary;slider.fillRect=fill;
            var area=Rect("HandleArea",r,10,0,191,22);var handle=Rect("Handle",area,0,0,20,0);handle.anchorMin=new Vector2(.5f,0);handle.anchorMax=new Vector2(.5f,1);handle.pivot=Vector2.one*.5f;var image=handle.gameObject.AddComponent<Image>();image.color=Primary;image.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");slider.handleRect=handle;slider.targetGraphic=image;
            UnityEventTools.AddPersistentListener(slider.onValueChanged,flow.Change);flow.sliders[i]=slider;
        }
        flow.resetButton=Button(panel,"Reset","처음 모습으로 되돌리기",24,715,342,32,flow.ResetAppearance,false);
        flow.useButton=Button(panel,"Use","이 모습으로 움직임을 볼게요",24,757,342,44,flow.Use,true);flow.cancelButton=Button(panel,"Cancel","수정 취소",24,808,342,30,flow.Cancel,false);
        UnityEventTools.AddPersistentListener(rig.appearanceCorrection.onClick,flow.Open);panel.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);AssetDatabase.SaveAssets();
        var errors=ShaderUtil.GetShaderMessages(flow.appearance.coatShader);return new{parameters=8,scene=rig.gameObject.scene.path,shaderMessages=errors.Length};
    }
    static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
    {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
    static RawImage Photo(Transform parent,string name,float x,float y,float w,float h)
    {var frame=Rect(name+"Frame",parent,x,y,w,h);var r=Rect(name,frame,0,0,0,0);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;var image=r.gameObject.AddComponent<RawImage>();image.raycastTarget=false;image.color=Color.white;var fit=r.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=1;return image;}
    static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,int size)
    {var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=Ink;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;}
    static Button Button(Transform parent,string name,string label,float x,float y,float w,float h,UnityAction action,bool primary)
    {var r=Rect(name,parent,x,y,w,h);var image=r.gameObject.AddComponent<Image>();image.color=primary?Primary:Soft;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;UnityEventTools.AddPersistentListener(b.onClick,action);var t=Text(r,"Label",label,8,0,w-16,h,14);t.alignment=TextAlignmentOptions.Center;t.color=primary?Color.white:Ink;return b;}
}
