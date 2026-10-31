using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme.UI;

public static class AddJointEditor
{
    static TMP_FontAsset font;
    static readonly Color Ink=new Color(.153f,.235f,.208f),Soft=new Color(.941f,.953f,.929f),Primary=new Color(.286f,.416f,.349f);
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");
        var flow=UnityEngine.Object.FindFirstObjectByType<RigFlow>();if(flow==null)throw new Exception("Open Start scene with RigFlow first");
        if(flow.jointEditor!=null)return new{existed=true};
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR/NotoSansKR-Dynamic.asset");
        var content=flow.walkPanel.transform.parent;var panel=Rect("JointEditor",content,0,0,0,0);panel.anchorMin=Vector2.zero;panel.anchorMax=Vector2.one;panel.offsetMin=panel.offsetMax=Vector2.zero;flow.editorPanel=panel.gameObject;
        var guide=Text(panel,"Guide","코 끝을 눌러 주세요",24,180,342,48,20);
        var count=Text(panel,"Count","1 / 10",24,235,342,26,13);
        var frame=Rect("DogFrame",panel,24,270,342,280);var image=Rect("DogView",frame,0,0,0,0).gameObject.AddComponent<RawImage>();
        image.rectTransform.anchorMin=Vector2.zero;image.rectTransform.anchorMax=Vector2.one;image.rectTransform.offsetMin=image.rectTransform.offsetMax=Vector2.zero;
        var fitter=image.gameObject.AddComponent<AspectRatioFitter>();fitter.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fitter.aspectRatio=1;image.color=Soft;
        var editor=image.gameObject.AddComponent<RigJointEditor>();flow.jointEditor=editor;editor.flow=flow;editor.anchor=flow.player.transform;editor.previewCamera=flow.previewCamera;editor.view=image;editor.guide=guide;editor.count=count;
        editor.markers=new RectTransform[16];
        for(var i=0;i<16;i++)
        {
            var marker=Rect("Point"+i,image.transform,0,0,15,15);marker.anchorMin=marker.anchorMax=Vector2.zero;marker.pivot=Vector2.one*.5f;
            var dot=marker.gameObject.AddComponent<Image>();dot.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");dot.raycastTarget=false;editor.markers[i]=marker;marker.gameObject.SetActive(false);
        }
        Button(panel,"Rotate","돌려 보기",24,560,130,38,editor.Rotate,false);
        Button(panel,"ZoomIn","확대",230,560,62,38,flow.ZoomIn,false);Button(panel,"ZoomOut","축소",300,560,66,38,flow.ZoomOut,false);
        editor.hint=Text(panel,"Hint","점을 끌어 위치를 맞춰 주세요.",24,611,342,48,14);
        Button(panel,"Previous","이전 부위",24,670,165,38,editor.Previous,false);Button(panel,"Next","다음 부위",201,670,165,38,editor.Next,false);
        Button(panel,"Reset","처음 위치로 되돌리기",24,715,342,32,editor.ResetPoints,false);
        Button(panel,"Submit","이 움직임으로 다시 볼게요",24,757,342,44,editor.Submit,true);Button(panel,"Cancel","수정 취소",24,808,342,30,editor.Cancel,false);
        UnityEventTools.AddPersistentListener(flow.motionCorrection.onClick,flow.EditMotion);
        panel.gameObject.SetActive(false);EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);EditorSceneManager.SaveScene(flow.gameObject.scene);AssetDatabase.SaveAssets();
        return new{points=10,mirrored=6,scene=flow.gameObject.scene.path};
    }
    static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
    {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
    static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,int size)
    {var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=Ink;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;}
    static void Button(Transform parent,string name,string label,float x,float y,float w,float h,UnityAction action,bool primary)
    {var r=Rect(name,parent,x,y,w,h);var img=r.gameObject.AddComponent<Image>();img.color=primary?Primary:Soft;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=img;UnityEventTools.AddPersistentListener(b.onClick,action);var t=Text(r,"Label",label,8,0,w-16,h,14);t.alignment=TextAlignmentOptions.Center;t.color=primary?Color.white:Ink;}
}
