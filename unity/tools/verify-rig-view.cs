using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Character;
using Withme.Generation;
using Withme.UI;

public static class VerifyRigView
{
    static void Check(bool v,string m){if(!v)throw new Exception(m);}
    static async Task Until(Func<bool> predicate,int seconds=150)
    {var end=Time.realtimeSinceStartupAsDouble+seconds;while(!predicate()){if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("Rig UI timed out");await Task.Yield();}}
    static async Task Frames(int count){var target=Time.frameCount+count;await Until(()=>Time.frameCount>=target,10);}
    static async Task Click(Mouse mouse,Button button)
    {Check(button.interactable&&button.gameObject.activeInHierarchy,"Inactive button "+button.name);var rect=button.GetComponent<RectTransform>();await Pointer(mouse,RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),true);}
    static async Task Pointer(Mouse mouse,Vector2 position,bool click)
    {
        InputSystem.QueueStateEvent(mouse,new MouseState{position=position});await Frames(2);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=position}.WithButton(MouseButton.Left));await Frames(2);
        if(click){InputSystem.QueueStateEvent(mouse,new MouseState{position=position});await Frames(3);}
    }
    static async Task<RigFlow> Prepare(string origin="http://127.0.0.1:8767",string smokePath="Temp/batch/rig-http-smoke.json",bool expectFailure=false)
    {
        Check(Application.isPlaying,"Play Start first");var flow=UnityEngine.Object.FindFirstObjectByType<RigFlow>();var g=flow.generation;var login=g.login;
        var smoke=JObject.Parse(File.ReadAllText(smokePath));g.mockBaseUrl=origin;g.VerificationDirectory=Path.GetFullPath("Temp/batch/rig-view-generation");flow.VerificationDirectory=Path.GetFullPath("Temp/batch/rig-view-state");
        await Until(()=>!login.Busy);login.ConfigureForVerification(new DemoHttpTransport(),new MemoryTokenStore());await Frames(3);
        new GenerationStore(g.VerificationDirectory).Save(new GenerationSession{UserId="11111111-1111-4111-8111-111111111111",PetId="22222222-2222-4222-8222-222222222222",PetName="보리",MediaIds=new[]{"44444444-4444-4444-8444-444444444444"},ModelJobId=smoke.Value<string>("modelJob"),ReferenceJobId=smoke.Value<string>("referenceJob"),Stage="ready"});
        login.emailInput.text="fixture@example.com";login.SendCode();await Until(()=>!login.Busy);login.codeInput.text="123456";login.VerifyCode();await Until(()=>!login.Busy);
        await Until(()=>flow.canvas.activeSelf&&flow.Coordinator!=null&&!flow.Busy&&(flow.Page=="walk"&&flow.player.IsReady || flow.Page=="error"));Check(flow.Page==(expectFailure?"error":"walk"),flow.error.text);return flow;
    }
    public static async Task<object> Failure()
    {
        var flow=await Prepare("http://127.0.0.1:8768","Temp/batch/rig-failure-smoke.json",true);var cases=new List<string>();
        var id=flow.Coordinator.Session.JobId;Check(id!=null&&flow.retryRig.gameObject.activeSelf&&!flow.QualityApproved,"Failed job state");
        flow.Resume();await Until(()=>!flow.Busy);Check(flow.Page=="error"&&flow.Coordinator.Session.JobId==id,"Status check created job");cases.Add("failed rig status check keeps the same job and confirmation gate");
        flow.RetryFailedRig();flow.RetryFailedRig();await Until(()=>!flow.Busy);Check(flow.Page=="error"&&flow.Coordinator.Session.JobId!=id,"Definitive failure cannot retry");cases.Add("explicit failed-job retry creates one new attempt");
        id=flow.Coordinator.Session.JobId;flow.PhotoMode();await Until(()=>flow.generation.Page=="photo"&&!flow.generation.Busy);
        Check(flow.generation.photoOnly.texture!=null&&flow.Coordinator.Session.JobId==id&&!flow.canvas.activeSelf,"Rig failure photo fallback");cases.Add("rig failure preserves original photo and job checkpoint");
        var report=new{passed=cases.Count,cases,productionCalls=0};File.WriteAllText("Temp/batch/unity-rig-failure.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
    }
    public static async Task<object> ResumeRig()
    {
        const string user="11111111-1111-4111-8111-111111111111";
        var generation=new GenerationStore(Path.GetFullPath("Temp/batch/rig-view-generation")).Read(user);Check(generation!=null,"Generation checkpoint missing");
        var before=new RigStore(Path.GetFullPath("Temp/batch/rig-view-state")).Read(user,generation.ModelId);Check(before?.JobId!=null,"Rig checkpoint missing");
        var flow=await Prepare();Check(flow.Coordinator.Session.ModelId==before.ModelId&&flow.Coordinator.Session.JobId==before.JobId&&flow.player.IsReady,"Rig restart duplicated/replaced job");
        return new{resumed=true,sameRigJob=true,sameModel=true,qualityApproved=flow.QualityApproved};
    }
    public static async Task<object> PendingRig()
    {
        var flow=await Prepare();const string file="Temp/batch/pending-rig-check.json";
        if(File.Exists(file))
        {
            var previous=JObject.Parse(File.ReadAllText(file));
            if(previous.Value<string>("modelId")==flow.Coordinator.Session.ModelId&&previous.Value<string>("jobId")==flow.Coordinator.Session.JobId)
                return new{reused=true,jobId=flow.Coordinator.Session.JobId};
        }
        var before=flow.Coordinator.Session.JobId;var joints=flow.Coordinator.Session.Joints.Copy();joints.Points["neck"][1]+=.0005f;
        flow.Coordinator.Prepare(joints);flow.Resume();await Until(()=>flow.Coordinator.Session.PendingJoints==null&&flow.Coordinator.Session.JobId!=before);
        var report=new{modelId=flow.Coordinator.Session.ModelId,jobId=flow.Coordinator.Session.JobId,previousJob=before};File.WriteAllText(file,JsonConvert.SerializeObject(report));flow.Leave();return report;
    }
    public static async Task<object> LaterResume()
    {
        var flow=await Prepare();var id=flow.Coordinator.Session.JobId;flow.Leave();await Frames(3);
        Check(flow.generation.resumeButton.gameObject.activeInHierarchy&&flow.generation.resumeButton.interactable,"Later hides resume entry");
        flow.generation.Resume();await Until(()=>flow.canvas.activeSelf&&!flow.Busy&&flow.Page=="walk"&&flow.player.IsReady);
        Check(flow.Coordinator.Session.JobId==id,"Later resume recreated rig");return new{laterResumeVisible=true,sameRigJob=true,walkRestored=true};
    }
    public static async Task<object> Appearance()
    {
        var rig=await Prepare();var flow=rig.appearanceFlow;var appearance=flow.appearance;var player=rig.player;var cases=new List<string>();
        var before=JsonConvert.SerializeObject(appearance.Parameters);var skin=player.Model.GetComponentInChildren<SkinnedMeshRenderer>();var materials=skin.sharedMaterials;
        flow.Open();await Until(()=>!flow.Busy);Check(rig.Page=="appearance"&&flow.originalPhoto.texture!=null&&flow.preview.texture!=null,"Photo comparison");cases.Add("optional original photo and rigged preview comparison");
        var values=new[]{.9f,6,1.1f,1.05f,.95f,1.05f,8,.05f};for(var i=0;i<8;i++)flow.sliders[i].value=values[i];await Frames(4);
        Check(appearance.Parameters.EarScale==.9f&&appearance.Parameters.LegLength==1.05f,"Slider parameters");Check(skin.sharedMaterial!=materials[0],"Shared material modified");Check(skin.sharedMaterial.shader.name=="Withme/CoatPreview"&&skin.sharedMaterial.GetTexture("_BaseMap")!=null,"Coat texture lost");cases.Add("bounded parameters and copied coat texture/material");
        player.Paused=true;var mesh=new Mesh();
        foreach(var clip in player.sharedLibrary.clips)
        {
            player.Play(clip.name,0);player.Sample(clip.length*.37f);skin.BakeMesh(mesh);
            Check(mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Appearance nonfinite skin "+clip.name);
            Check(Mathf.Abs(player.Bones["ear_01.L"].localScale.x-.9f)<.0001f,"Ear ratio not applied");
        }
        UnityEngine.Object.Destroy(mesh);cases.Add("17 clips keep finite skin after length/head/ear/tail changes; visual quality unapproved");
        flow.Cancel();player.Play("Walk",0);player.Paused=false;await Frames(3);
        Check(rig.Page=="walk"&&JsonConvert.SerializeObject(appearance.Parameters)==before,"Cancel changes");Check(skin.sharedMaterials.SequenceEqual(materials),"Original material not restored");Check(flow.originalPhoto.texture==null,"Photo retained after close");cases.Add("cancel restores previous parameters and exact source materials");
        flow.Open();await Until(()=>!flow.Busy);flow.sliders[2].value=1.1f;flow.Use();await Frames(3);
        Check(rig.Page=="walk"&&appearance.Parameters.TailScale==1.1f&&!rig.confirm.interactable,"Use prematurely confirmed");cases.Add("use returns to movement review without final confirmation");
        flow.Open();await Until(()=>!flow.Busy);flow.ResetAppearance();await Frames(3);Check(JsonConvert.SerializeObject(appearance.Parameters)==JsonConvert.SerializeObject(new AppearanceParameters()),"Reset not model defaults");cases.Add("reset uses model neutral proportions");
        var report=new{passed=cases.Count,cases,qualityApproved=false};File.WriteAllText("Temp/batch/unity-appearance.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
    }
    public static async Task<object> Preferences()
    {
        var rig=await Prepare();var flow=rig.setupFlow;var cases=new List<string>();var session=rig.generation.Coordinator.Session;
        var folder=Path.GetFullPath("Temp/batch/profile-test-"+Guid.NewGuid().ToString("N"));var profiles=new MockCharacterProfiles(folder);
        var draft=new CharacterProfile{UserId=session.UserId,PetId=session.PetId,PetName=session.PetName,ModelId=rig.Coordinator.Session.ModelId,RigJobId=rig.Coordinator.Session.JobId};
        try{profiles.Confirm(draft,false);throw new Exception("Quality false confirmed");}catch(ApiFailure e){Check(e.Code=="QUALITY_REVIEW_REQUIRED","Wrong quality error");}
        Check(profiles.Read(session.UserId,session.PetId)==null,"Unapproved profile stored");flow.Confirm();Check(!flow.preferencesPanel.activeSelf&&!rig.confirm.interactable,"UI quality bypass");cases.Add("unapproved real rig cannot confirm or enter preferences");
        // Explicitly approved API-contract fixture for independent H04-H06 tests.
        // The real Blender report and RigFlow quality flag remain unchanged.
        var approved=profiles.Confirm(draft,true);Check(profiles.Confirm(draft,true).ApprovedVersion==approved.ApprovedVersion,"Duplicate confirmation version");
        var bad=draft.Copy();bad.Appearance.EarScale=2;
        try{profiles.Confirm(bad,true);throw new Exception("Invalid appearance confirmed");}catch(ApiFailure){}
        Check(profiles.Read(session.UserId,session.PetId).ApprovedVersion==approved.ApprovedVersion,"Failed save replaced old version");Check(profiles.Read("33333333-3333-4333-8333-333333333333",session.PetId)==null,"Cross-user profile");cases.Add("fixture confirmation idempotency, failed update preservation and user isolation");
        flow.ConfigureForVerification(profiles,approved);flow.ShowPreferences();Check(flow.QuestionIndex==0&&!flow.next.interactable,"First question requires answer");
        flow.ChooseFirst();flow.Next();Check(flow.QuestionIndex==1,"Not one question at a time");flow.ChooseSecond();flow.Previous();Check(flow.QuestionIndex==0&&flow.optionLabels[0].text.StartsWith("●"),"Previous answer lost");flow.Next();Check(flow.optionLabels[1].text.StartsWith("●"),"Second answer lost");cases.Add("one question, required choice, previous and next retain answers");
        var restored=profiles.Read(session.UserId,session.PetId);Check(restored.Preferences.SequenceEqual(new[]{0,1,-1,-1})&&!restored.PreferencesFinished,"Partial answer checkpoint");
        flow.ConfigureForVerification(profiles,restored);flow.ShowPreferences();Check(flow.QuestionIndex==2,"Did not resume first unanswered question");cases.Add("partial preferences resume first unanswered question");
        var roomRequests=0;UnityEngine.Events.UnityAction requested=()=>roomRequests++;flow.RoomRequested.AddListener(requested);
        try{flow.ChooseFirst();flow.Next();Check(flow.QuestionIndex==3,"Fourth question");flow.ChooseSecond();flow.Next();flow.Next();Check(roomRequests==1,"Duplicate final action");}
        finally{flow.RoomRequested.RemoveListener(requested);}
        restored=profiles.Read(session.UserId,session.PetId);Check(restored.PreferencesFinished&&restored.Preferences.SequenceEqual(new[]{0,1,0,1}),"Completed preferences not saved");Check(!rig.QualityApproved,"Test changed actual rig quality");cases.Add("final answer explicit completion and one room request, actual quality unchanged");
        var report=new{passed=cases.Count,cases,approvedContractFixtureOnly=true,actualRigQualityApproved=false};File.WriteAllText("Temp/batch/unity-preferences.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
    }
    public static async Task<object> Room()
    {
        var rig=await Prepare();var room=rig.GetComponent<RoomFlow>();var dog=room.dog;var s=rig.generation.Coordinator.Session;var cases=new List<string>();
        var profiles=new MockCharacterProfiles(Path.GetFullPath("Temp/batch/room-test-"+Guid.NewGuid().ToString("N")));
        var profile=profiles.Confirm(new CharacterProfile{UserId=s.UserId,PetId=s.PetId,PetName=s.PetName,ModelId=rig.Coordinator.Session.ModelId,RigJobId=rig.Coordinator.Session.JobId},true);
        profile.Preferences=new[]{0,0,0,1};profile.PreferencesFinished=true;profile=profiles.SavePreferences(profile);rig.setupFlow.ConfigureForVerification(profiles,profile);
        room.Enter(profile);await Until(()=>!room.Busy);Check(dog.State==RoomState.Exited&&!rig.previewCamera.enabled,"Unapproved rig entered real room");cases.Add("actual unapproved result blocks regular room even with stale approved profile");
        room.Enter(profile,true);await Until(()=>!room.Busy);Check(room.canvas.activeSelf&&room.mainPanel.activeSelf&&!room.menuPanel.activeSelf&&dog.State==RoomState.Idle,"Verification room load");Check(room.VerificationOnly&&!rig.QualityApproved,"Fixture changed actual quality");cases.Add("explicit isolated functional room fixture, quality flag preserved");
        var muteKey="withme.fixture."+s.UserId+"."+s.PetId+".muted";var reduceKey="withme.fixture."+s.UserId+"."+s.PetId+".reduced";var oldMute=PlayerPrefs.GetInt(muteKey,0);var oldReduce=PlayerPrefs.GetInt(reduceKey,0);
        var originalInputs=InputSystem.settings;var testInputs=UnityEngine.Object.Instantiate(originalInputs);testInputs.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;testInputs.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=testInputs;var mouse=InputSystem.AddDevice<Mouse>("WithmeRoomMouse");
        try
        {
            dog.sounds.Muted=false;dog.ReducedMotion=false;dog.reactionSoundChance=1;
            Check(dog.sounds.LoadedClipCount==20&&Mathf.Abs(dog.sounds.pitchJitter-.05f)<1e-7f,"Audio source/pitch contract");
            Check(dog.Greet()&&!dog.ThrowToy()&&!dog.Pet("head"),"Multiple input owners");await Until(()=>dog.State==RoomState.Idle,10);cases.Add("greet approaches, returns idle and rejects simultaneous actions");
            foreach(var part in new[]{"head","chin","belly"}){Check(dog.Pet(part),"Pet rejected "+part);await Until(()=>dog.State==RoomState.Idle,8);}cases.Add("head chin belly one-shot reactions return idle");
            var started=Time.time;Check(dog.ThrowToy(),"Toy rejected");await Until(()=>dog.State==RoomState.Idle,10);var duration=Time.time-started;Check(duration>=2.95f&&duration<=6.2f&&rig.player.transform.localPosition.sqrMagnitude<.0001f&&!dog.toy.gameObject.activeSelf,"Toy return timing/home");cases.Add("toy chase and return in 3 to 6 seconds with bounded motion");
            Check(dog.Rest(),"Rest rejected");await Until(()=>rig.player.CurrentClip=="SitIdle",10);Check(dog.Sleep(),"Sleep rejected");await Until(()=>rig.player.CurrentClip=="Sleep",20);Check(dog.Wake(),"Wake rejected");await Until(()=>dog.State==RoomState.Idle,15);cases.Add("sit stand lie fall asleep sleep wake get up ordered transitions");
            dog.ThrowToy();await Frames(5);room.OpenMenu();var held=rig.player.transform.localPosition;await Frames(5);Check(held==rig.player.transform.localPosition&&!dog.sounds.IsPlaying&&rig.player.Paused,"Menu did not suspend movement/audio");
            room.ToggleMute();Check(dog.sounds.Muted&&dog.sounds.GetComponents<AudioSource>().All(a=>a.mute),"Mute sources");room.ToggleMotion();room.CloseMenu();Check(dog.State==RoomState.Idle&&dog.ReducedMotion&&!room.menuPanel.activeSelf,"Menu close ownership");
            held=rig.player.transform.localPosition;dog.ThrowToy();await Until(()=>dog.State==RoomState.Idle,8);Check(held==rig.player.transform.localPosition,"Reduced mode moved dog");cases.Add("single menu stops actions, persists mute and reduces motion");
            room.OpenMenu();room.TogglePhoto();await Until(()=>!room.Busy);Check(room.photoPanel.activeSelf&&room.photoView.texture!=null&&!rig.previewCamera.enabled&&!dog.sounds.IsPlaying,"Photo mode");room.OpenMenu();room.TogglePhoto();Check(room.mainPanel.activeSelf&&!room.photoPanel.activeSelf,"Return from photo");cases.Add("photo and 3D share one menu and suppress photo-mode audio");
            room.ImmediateExit();Check(dog.State==RoomState.Exited&&rig.player.Paused&&!dog.sounds.IsPlaying&&room.restPanel.activeSelf&&!rig.previewCamera.enabled,"Immediate exit residue");
            room.Return();await Until(()=>!room.Busy);Check(room.mainPanel.activeSelf&&!room.menuPanel.activeSelf&&!room.photoPanel.activeSelf&&!room.restPanel.activeSelf&&dog.State==RoomState.Idle,"Reentry not 3D main");cases.Add("immediate exit stops all activity; reentry always shows 3D main");
            dog.Suspend(true);Check(!dog.sounds.IsPlaying&&rig.player.Paused,"Lifecycle suspend");dog.Suspend(false);Check(dog.State==RoomState.Idle&&!rig.player.Paused,"Lifecycle resume");cases.Add("pause clears action ownership and resumes safely idle");
            await Click(mouse,room.menuButton.GetComponent<Button>());Check(room.menuPanel.activeSelf&&!room.menuButton.activeSelf,"Menu button overlays close");await Click(mouse,room.menuPanel.transform.Find("Close").GetComponent<Button>());Check(!room.menuPanel.activeSelf&&room.menuButton.activeSelf,"Pointer close failed");
            await Click(mouse,room.greetButton);Check(dog.State==RoomState.Greet,"Pointer greeting");await Until(()=>dog.State==RoomState.Idle,10);cases.Add("actual pointer menu open close and interaction buttons");
            var uv=rig.previewCamera.WorldToViewportPoint(rig.player.Bones["head"].position);var rect=room.view.rectTransform;var point=rect.TransformPoint(new Vector3(rect.rect.xMin+uv.x*rect.rect.width,rect.rect.yMin+uv.y*rect.rect.height));
            await Pointer(mouse,RectTransformUtility.WorldToScreenPoint(null,point),true);Check(dog.State==RoomState.Pet,"Actual dog surface pointer did not pet");await Until(()=>dog.State==RoomState.Idle,8);cases.Add("actual skinned dog surface pointer selects pet reaction");
            room.Logout();await Until(()=>!rig.generation.login.Busy&&!rig.generation.login.Session.IsAuthenticated);await Frames(4);
            Check(!room.canvas.activeSelf&&room.CurrentProfile==null&&rig.setupFlow.Active==null&&rig.Coordinator==null&&!rig.player.IsReady&&!dog.sounds.IsPlaying,"Logout leaked character state/audio");
            Check(rig.generation.login.safeArea.GetComponentInParent<Canvas>(true).gameObject.activeSelf&&rig.generation.login.ScreenState==LoginScreen.Email,"Logout left blank screen");cases.Add("logout clears prior user model settings audio and reveals email screen");
            var report=new{passed=cases.Count,cases,verificationOnly=true,actualQualityApproved=false,toySeconds=duration};File.WriteAllText("Temp/batch/unity-room.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
        }
        finally{InputSystem.RemoveDevice(mouse);InputSystem.settings=originalInputs;UnityEngine.Object.Destroy(testInputs);PlayerPrefs.SetInt(muteKey,oldMute);PlayerPrefs.SetInt(reduceKey,oldReduce);PlayerPrefs.Save();dog.sounds.Muted=oldMute==1;dog.ReducedMotion=oldReduce==1;}
    }
    public static async Task<object> RoomPreview()
    {
        var rig=await Prepare();var s=rig.generation.Coordinator.Session;
        var service=new MockCharacterProfiles(Path.GetFullPath("Temp/batch/room-preview"));
        var profile=service.Confirm(new CharacterProfile{UserId=s.UserId,PetId=s.PetId,PetName=s.PetName,ModelId=rig.Coordinator.Session.ModelId,RigJobId=rig.Coordinator.Session.JobId},true);
        profile.Preferences=new[]{0,0,0,0};profile.PreferencesFinished=true;profile=service.SavePreferences(profile);rig.setupFlow.ConfigureForVerification(service,profile);
        var room=rig.GetComponent<RoomFlow>();room.Enter(profile,true);await Until(()=>!room.Busy);Check(room.dog.State==RoomState.Idle,"Preview room not ready");room.OpenMenu();
        return new{room=true,menu=true,verificationOnly=room.VerificationOnly,qualityApproved=rig.QualityApproved};
    }
    public static async Task<object> RoomPhotoCancel()
    {
        await RoomPreview();var room=UnityEngine.Object.FindFirstObjectByType<RoomFlow>();room.TogglePhoto();room.Logout();
        await Until(()=>!room.Busy&&!room.setup.rig.generation.login.Busy);await Frames(5);
        Check(room.CurrentProfile==null&&!room.canvas.activeSelf&&room.photoView.texture==null&&!room.dog.sounds.IsPlaying,"Late photo response leaked after logout");
        return new{photoRequestCancelled=true,noLateUserPhoto=true,noResidualAudio=true};
    }
    public static async Task<object> Run()
    {
        Check(Application.isPlaying,"Play Start first");var flow=UnityEngine.Object.FindFirstObjectByType<RigFlow>();var g=flow.generation;var login=g.login;
        var smoke=JObject.Parse(File.ReadAllText("Temp/batch/rig-http-smoke.json"));
        g.VerificationDirectory=Path.GetFullPath("Temp/batch/rig-view-generation");flow.VerificationDirectory=Path.GetFullPath("Temp/batch/rig-view-state");
        var original=InputSystem.settings;var temporary=UnityEngine.Object.Instantiate(original);temporary.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;temporary.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=temporary;
        var mouse=InputSystem.AddDevice<Mouse>("WithmeRigMouse");var cases=new List<string>();
        try
        {
            await Until(()=>!login.Busy);login.ConfigureForVerification(new DemoHttpTransport(),new MemoryTokenStore());await Frames(3);
            new GenerationStore(g.VerificationDirectory).Save(new GenerationSession{UserId="11111111-1111-4111-8111-111111111111",PetId="22222222-2222-4222-8222-222222222222",PetName="보리",MediaIds=new[]{"44444444-4444-4444-8444-444444444444"},ModelJobId=smoke.Value<string>("modelJob"),ReferenceJobId=smoke.Value<string>("referenceJob"),Stage="ready"});
            login.emailInput.text="fixture@example.com";login.SendCode();await Until(()=>!login.Busy);login.codeInput.text="123456";login.VerifyCode();await Until(()=>!login.Busy);
            await Until(()=>flow.canvas.activeSelf&&flow.Coordinator!=null&&!flow.Busy&&(flow.Page=="walk"&&flow.player.IsReady || flow.Page=="error"));
            Check(flow.Page=="walk",flow.error.text);Check(!g.Visible&&flow.canvas.activeSelf&&flow.player.IsReady,"Generation handoff");Check(!flow.QualityApproved&&!flow.confirm.interactable,"Quality/room gate");cases.Add("saved generation resumes into automatic walk with quality gate");
            var skin=flow.player.Model.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();skin.BakeMesh(mesh);var first=mesh.vertices;await Frames(15);skin.BakeMesh(mesh);
            Check(first.Where((v,i)=>Vector3.Distance(v,mesh.vertices[i])>.001f).Any(),"Frozen skin");UnityEngine.Object.Destroy(mesh);cases.Add("actual advancing skinned walk");
            var originalJoints=JsonConvert.SerializeObject(flow.Coordinator.Session.Joints);await Click(mouse,flow.motionCorrection);await Until(()=>!flow.Busy);Check(flow.Page=="edit"&&flow.jointEditor.Ready,"Optional editor missing");
            var editor=flow.jointEditor;Check(editor.markers.Length==16,"10 joints + 6 mirrored markers");editor.Next();Check(editor.Selected==1,"Next guide");editor.Previous();Check(editor.Selected==0,"Previous guide");cases.Add("optional 10 point guidance and mirrored markers");
            // Test the depth rule independently with a closed box and a single
            // open surface. Duplicate triangle hits must count as one surface.
            var hits=new List<float>{1,1,3,3};Check(JointDepth.Midpoint(new Ray(Vector3.zero,Vector3.forward),hits,out var middle)&&middle==new Vector3(0,0,2),"Midpoint depth");
            Check(!JointDepth.Midpoint(new Ray(Vector3.zero,Vector3.forward),new List<float>{1},out _),"Single surface accepted");Check(!JointDepth.Midpoint(new Ray(Vector3.zero,Vector3.forward),new List<float>{1,2,3,4},out _),"Multiple surfaces accepted");cases.Add("entry exit midpoint, single and multiple surface rejection");
            var rotation=editor.anchor.rotation;editor.Rotate();flow.ZoomIn();await Frames(3);
            var point=RigJoints.ToUnity(editor.Working.Points["nose"]);var uv=editor.previewCamera.WorldToViewportPoint(editor.anchor.TransformPoint(point));
            Check(Vector2.Distance(editor.markers[0].anchoredPosition,new Vector2(uv.x*editor.view.rectTransform.rect.width,uv.y*editor.view.rectTransform.rect.height))<.05f,"Markers drift after orbit/zoom");cases.Add("marker projection after orbit and zoom");
            editor.anchor.rotation=rotation;flow.ZoomOut();editor.Next();await Frames(3);
            var moved=false;var rect=editor.view.rectTransform;var dragTarget=Vector2.zero;
            foreach(var y in new[]{.45f,.5f,.55f,.6f})
            {
                foreach(var x in new[]{.45f,.5f,.55f,.6f})
                {
                    var p=rect.TransformPoint(new Vector3(rect.rect.xMin+x*rect.rect.width,rect.rect.yMin+y*rect.rect.height));
                    var screen=RectTransformUtility.WorldToScreenPoint(null,p);
                    if(editor.MoveSelected(screen,null)){moved=true;dragTarget=screen;break;}
                }
                if(moved)break;
            }
            Check(moved,"No usable prepared mesh depth");editor.ResetPoints();await Frames(3);
            var dragStart=RectTransformUtility.WorldToScreenPoint(null,editor.markers[1].position);
            await Pointer(mouse,dragStart,false);InputSystem.QueueStateEvent(mouse,new MouseState{position=dragTarget}.WithButton(MouseButton.Left));await Frames(3);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=dragTarget});await Frames(3);
            Check(JsonConvert.SerializeObject(editor.Working)!=originalJoints,"Real drag did not move selected joint");cases.Add("Input System pointer drag edits prepared mesh depth");
            editor.ResetPoints();Check(JsonConvert.SerializeObject(editor.Working)==originalJoints,"Reset differs");editor.Cancel();
            Check(flow.Page=="walk"&&JsonConvert.SerializeObject(flow.Coordinator.Session.Joints)==originalJoints,"Cancel mutated joints");cases.Add("prepared mesh hit, reset and cancel preserve accepted input");
            await Click(mouse,flow.motionCorrection);await Until(()=>!flow.Busy);Check(flow.Page=="edit","Reopen edit");var oldJob=flow.Coordinator.Session.JobId;
            editor.Submit();editor.Submit();await Until(()=>!flow.Busy);Check(flow.Page=="walk"&&flow.Coordinator.Session.JobId==oldJob,"Unchanged submit not deduplicated");cases.Add("submit returns to walk and duplicate input reuses same rig job");
            await Click(mouse,flow.motionCorrection);await Until(()=>!flow.Busy);Check(flow.Page=="edit","Changed rig reopen");
            // A bounded, reproducible correction exercises real re-rigging;
            // arbitrary pointer placement above is intentionally reset first.
            var baseline=JsonConvert.DeserializeObject<RigJoints>(File.ReadAllText("Assets/StreamingAssets/Validation/StandardRig/m1_golden.joints.json"));
            var target=baseline.Points["neck"][1]+.001f;
            if(Mathf.Abs(editor.Working.Points["neck"][1]-target)<1e-6f)target+=.001f;
            editor.Working.Points["neck"][1]=target;editor.Submit();await Until(()=>!flow.Busy);
            Check(flow.Page=="walk",flow.error.text);Check(flow.Coordinator.Session.JobId!=oldJob,"Changed input reused old job");Check(!flow.QualityApproved,"Changed rig autoapproved");cases.Add("changed canonical joint runs local Blender and returns to unapproved walk");
            var report=new{passed=cases.Count,cases,qualityApproved=false,deviceTested=false};File.WriteAllText("Temp/batch/unity-rig-view.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
        }
        finally{InputSystem.RemoveDevice(mouse);InputSystem.settings=original;UnityEngine.Object.Destroy(temporary);}
    }
}
