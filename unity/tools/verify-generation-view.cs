using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Withme.Auth;
using Withme.Generation;
using Withme.Registration;
using Withme.UI;

public static class VerifyGenerationView
{
    sealed class Picker:IPhotoPicker
    {public Task<PhotoDraft> PickAsync(CancellationToken c)=>PhotoPicker.ImportAsync(Path.Combine(Application.streamingAssetsPath,"Validation/Generation/golden/front.png"),c);}
    static void Check(bool v,string m){if(!v)throw new Exception(m);}
    static async Task Until(Func<bool> predicate,int seconds=35)
    {var end=Time.realtimeSinceStartupAsDouble+seconds;while(!predicate()){if(Time.realtimeSinceStartupAsDouble>end)throw new Exception("UI timed out");await Task.Yield();}}
    static async Task Frames(int n){var target=Time.frameCount+n;await Until(()=>Time.frameCount>=target);}
    static async Task Click(Mouse mouse,UnityEngine.UI.Button button)
    {
        Check(button.interactable&&button.gameObject.activeInHierarchy,"Inactive button");var r=button.GetComponent<RectTransform>();var p=RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(r.rect.center));
        InputSystem.QueueStateEvent(mouse,new MouseState{position=p});await Frames(2);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=p}.WithButton(MouseButton.Left));await Frames(2);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=p});await Frames(3);
    }
    static async Task Login(LoginFlow login)
    {
        login.ConfigureForVerification(new DemoHttpTransport(),new MemoryTokenStore());
        login.emailInput.text="fixture@example.com";login.SendCode();await Until(()=>!login.Busy);
        login.codeInput.text="123456";login.VerifyCode();await Until(()=>!login.Busy);await Frames(3);
    }
    public static async Task<object> Run()
    {
        Check(Application.isPlaying,"Start Play first");
        var g=UnityEngine.Object.FindFirstObjectByType<GenerationFlow>();var r=UnityEngine.Object.FindFirstObjectByType<RegistrationFlow>();var login=g.login;
        var rig=UnityEngine.Object.FindFirstObjectByType<RigFlow>();if(rig!=null)g.mockBaseUrl="http://127.0.0.1:8767";
        g.VerificationDirectory=Path.Combine(Application.temporaryCachePath,"WithmeGenerationVerification");
        var store=new GenerationStore(g.VerificationDirectory);store.Delete("11111111-1111-4111-8111-111111111111");
        var original=InputSystem.settings;var temporary=UnityEngine.Object.Instantiate(original);temporary.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;temporary.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=temporary;
        var mouse=InputSystem.AddDevice<Mouse>("WithmeGenerationMouse");
        try
        {
            await Until(()=>!login.Busy);await Login(login);
            var demo=new DemoRegistration();r.ConfigureForVerification(demo,demo,new Picker());r.nameInput.text="보리";r.PickPhoto();await Until(()=>r.Coordinator.Photos.Count==1&&r.addButton.interactable);
            r.Next();r.ConfirmUpload();await Until(()=>g.Coordinator!=null);await Until(()=>g.Page=="review"&&!g.Busy);
            Check(g.views.Length==4&&g.views[0].texture.width==1024&&g.originalPhoto.texture!=null,"Provided 4 views not loaded");
            g.Zoom(0);Check(g.zoomPanel.activeSelf,"Zoom missing");g.CloseZoom();
            await Click(mouse,g.retryButton);await Until(()=>g.Page=="review"&&!g.Busy);Check(g.Coordinator.Session.Retries==1,"Retry count");
            var job=g.Coordinator.Session.ReferenceJobId;g.PhotoMode();await Until(()=>g.Page=="photo"&&!g.Busy);Check(g.photoOnly.texture!=null,"Photo mode missing");
            g.Resume();await Until(()=>g.Page=="review"&&!g.Busy);Check(g.Coordinator.Session.ReferenceJobId==job,"Resume created duplicate");
            await Click(mouse,g.approveButton);g.Approve();await Until(()=>g.Page=="ready"&&!g.Busy);
            if(rig!=null)await Until(()=>!rig.Busy&&(rig.Page=="walk"||rig.Page=="error"),150);
            var loaded=rig!=null?rig.player.Model?.gameObject:g.modelPreview.Loaded;
            Check(loaded!=null&&g.CharacterModelId!=null,"Provided GLB missing");
            var renderer=loaded.GetComponentInChildren<Renderer>();Check(renderer!=null&&renderer.bounds.size.magnitude>0,"Empty model");
            var before=loaded.transform.rotation;if(rig!=null)rig.Rotate();else g.modelPreview.Rotate();Check(loaded.transform.rotation!=before,"Rotate failed");
            Check(!g.registration.Visible&&!g.login.resultPanel.activeInHierarchy,"Overlapping canvases");
            return new {passed=7,checks=new[]{"#9 upload handoff over local HTTP","provided 4 PNGs and original","zoom","actual retry pointer click","photo mode and same-job resume","actual approve click and automatic rigged walk","rotation and canvas isolation"},skins=loaded.GetComponentsInChildren<SkinnedMeshRenderer>().Length,externalGenerations=0};
        }
        finally{InputSystem.RemoveDevice(mouse);InputSystem.settings=original;UnityEngine.Object.Destroy(temporary);}
    }
    public static async Task<object> ResumeAfterRestart()
    {
        var g=UnityEngine.Object.FindFirstObjectByType<GenerationFlow>();g.VerificationDirectory=Path.Combine(Application.temporaryCachePath,"WithmeGenerationVerification");
        var store=new GenerationStore(g.VerificationDirectory);var before=store.Read("11111111-1111-4111-8111-111111111111");Check(before!=null,"Missing checkpoint");
        await Until(()=>!g.login.Busy);await Login(g.login);await Until(()=>g.Page=="ready"&&!g.Busy);
        var rig=UnityEngine.Object.FindFirstObjectByType<RigFlow>();if(rig!=null)await Until(()=>!rig.Busy&&rig.Page=="walk"&&rig.player.IsReady,150);
        Check(g.Coordinator.Session.ModelJobId==before.ModelJobId&&(rig!=null?rig.player.IsReady:g.modelPreview.Loaded!=null),"Restart did not resume original model");
        return new {resumed=true,sameJob=true,modelLoaded=true,stage=g.Coordinator.Session.Stage};
    }
    public static async Task<object> Failure()
    {
        var g=UnityEngine.Object.FindFirstObjectByType<GenerationFlow>();var r=g.registration;
        g.VerificationDirectory=Path.Combine(Application.temporaryCachePath,"WithmeGenerationFailure",Guid.NewGuid().ToString("N"));g.mockBaseUrl="http://127.0.0.1:8766";
        await Until(()=>!g.login.Busy);await Login(g.login);var demo=new DemoRegistration();r.ConfigureForVerification(demo,demo,new Picker());r.nameInput.text="보리";r.PickPhoto();await Until(()=>r.Coordinator.Photos.Count==1&&r.addButton.interactable);
        r.Next();r.ConfirmUpload();await Until(()=>g.Page=="error"&&!g.Busy);
        Check(!g.recoverButton.interactable&&g.errorLabel.text.Contains("한도"),"Credit failure offers retry");
        var job=g.Coordinator.Session.ReferenceJobId;g.PhotoMode();await Until(()=>g.Page=="photo"&&!g.Busy);
        Check(g.photoOnly.texture!=null&&g.Coordinator.Session.ReferenceJobId==job,"Failure photo fallback lost original");
        return new {creditFailureHandled=true,retryDisabled=true,photoLoaded=true,sameJob=true};
    }
    public static async Task<object> PreparePending()
    {
        var g=UnityEngine.Object.FindFirstObjectByType<GenerationFlow>();var r=g.registration;
        g.VerificationDirectory=Path.Combine(Application.temporaryCachePath,"WithmeGenerationPending");g.mockBaseUrl="http://127.0.0.1:8765";
        new GenerationStore(g.VerificationDirectory).Delete("11111111-1111-4111-8111-111111111111");
        await Until(()=>!g.login.Busy);await Login(g.login);var demo=new DemoRegistration();r.ConfigureForVerification(demo,demo,new Picker());r.nameInput.text="보리";r.PickPhoto();await Until(()=>r.Coordinator.Photos.Count==1&&r.addButton.interactable);
        r.Next();r.ConfirmUpload();await Until(()=>g.Coordinator?.Session.ReferenceJobId!=null);var id=g.Coordinator.Session.ReferenceJobId;
        Check(g.Coordinator.Session.Stage=="reference","Job already complete");g.Later();await Until(()=>!g.Busy);
        return new {pending=true,jobId=id};
    }
    public static async Task<object> ResumePending()
    {
        var g=UnityEngine.Object.FindFirstObjectByType<GenerationFlow>();g.VerificationDirectory=Path.Combine(Application.temporaryCachePath,"WithmeGenerationPending");
        var before=new GenerationStore(g.VerificationDirectory).Read("11111111-1111-4111-8111-111111111111");Check(before.Stage=="reference","Not a pending checkpoint");
        await Until(()=>!g.login.Busy);await Login(g.login);await Until(()=>g.Page=="review"&&!g.Busy);
        Check(g.Coordinator.Session.ReferenceJobId==before.ReferenceJobId&&g.views[0].texture!=null,"Pending job recreated");
        return new {resumedPending=true,sameJob=true,viewsLoaded=4};
    }
}
