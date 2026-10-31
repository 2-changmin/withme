using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme.UI;

public static class AddCharacterSetup
{
    static TMP_FontAsset font;
    static readonly Color Ink=new Color(.153f,.235f,.208f),Soft=new Color(.941f,.953f,.929f),Primary=new Color(.286f,.416f,.349f);
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");var rig=UnityEngine.Object.FindFirstObjectByType<RigFlow>();if(rig==null)throw new Exception("Open Start first");if(rig.setupFlow!=null)return new{existed=true};
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR/NotoSansKR-Dynamic.asset");var flow=rig.gameObject.AddComponent<CharacterSetupFlow>();flow.rig=rig;rig.setupFlow=flow;
        var panel=Rect("Preferences",rig.walkPanel.transform.parent,0,0,0,0);panel.anchorMin=Vector2.zero;panel.anchorMax=Vector2.one;panel.offsetMin=panel.offsetMax=Vector2.zero;flow.preferencesPanel=panel.gameObject;
        flow.stepLabel=Text(panel,"Step","익숙한 모습 · 1 / 4",24,185,342,28,14);flow.question=Text(panel,"Question","어떻게 반겼나요?",24,235,342,76,23);flow.description=Text(panel,"Description","기억에 가까운 모습을 하나 골라 주세요.",24,320,342,50,14);
        flow.options=new Button[2];flow.optionLabels=new TMP_Text[2];
        flow.options[0]=Button(panel,"First","",24,390,342,78,flow.ChooseFirst,false);flow.options[1]=Button(panel,"Second","",24,486,342,78,flow.ChooseSecond,false);
        for(var i=0;i<2;i++)flow.optionLabels[i]=flow.options[i].GetComponentInChildren<TMP_Text>();
        Text(panel,"Hint","나중에도 메뉴에서 바꿀 수 있어요.",24,591,342,28,13);flow.error=Text(panel,"Error","",24,635,342,54,14);
        flow.previous=Button(panel,"Previous","이전 질문",24,706,342,36,flow.Previous,false);flow.next=Button(panel,"Next","다음 질문",24,757,342,48,flow.Next,true);flow.nextLabel=flow.next.GetComponentInChildren<TMP_Text>();
        UnityEventTools.AddPersistentListener(rig.confirm.onClick,flow.Confirm);panel.gameObject.SetActive(false);EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);AssetDatabase.SaveAssets();return new{questions=4,optionsPerPage=2,scene=rig.gameObject.scene.path};
    }
    static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
    {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
    static TMP_Text Text(Transform parent,string name,string value,float x,float y,float w,float h,int size)
    {var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=Ink;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;}
    static Button Button(Transform parent,string name,string label,float x,float y,float w,float h,UnityAction action,bool primary)
    {var r=Rect(name,parent,x,y,w,h);var image=r.gameObject.AddComponent<Image>();image.color=primary?Primary:Soft;var b=r.gameObject.AddComponent<Button>();b.targetGraphic=image;UnityEventTools.AddPersistentListener(b.onClick,action);var t=Text(r,"Label",label,16,0,w-32,h,16);t.alignment=TextAlignmentOptions.Center;t.color=primary?Color.white:Ink;return b;}
}
