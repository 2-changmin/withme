using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Character;
using Withme.Generation;
using Withme.Networking;

namespace Withme.UI
{
    public sealed class RigFlow : MonoBehaviour
    {
        public GenerationFlow generation;
        public GameObject canvas, loadingPanel, walkPanel, errorPanel, editorPanel;
        public RigJointEditor jointEditor;
        public AppearanceFlow appearanceFlow;
        public CharacterSetupFlow setupFlow;
        public RectTransform safeArea;
        public TMP_Text heading, status, error, mode, quality;
        public Slider progress;
        public Button recover, retryRig, motionCorrection, appearanceCorrection, confirm;
        public Camera previewCamera;
        public RawImage preview;
        public StandardRigPlayback player;
        public readonly UnityEvent WalkReady = new UnityEvent();
        public RigCoordinator Coordinator { get; private set; }
        public bool Busy { get; private set; }
        public string Page { get; private set; }
        public bool QualityApproved { get; private set; }
        AuthService boundAuth;
        CancellationTokenSource cancellation;
        RenderTexture texture;
        bool paused, resumeAfterPause, definitiveFailure;
#if UNITY_EDITOR
        public string VerificationDirectory;
#endif
        void Start()
        {
            generation.ModelReady.AddListener(FromModel); canvas.SetActive(false); previewCamera.enabled = false;
            texture = new RenderTexture(900, 900, 24) { name = "WithmeRigPreview" };
            texture.Create(); previewCamera.targetTexture = texture; preview.texture = texture;
            if (jointEditor != null) jointEditor.view.texture = texture;
        }
        void FromModel()
        {
            if (Busy) return;
            try
            {
                boundAuth = generation.login.Session;
                var model = RigSession.RouteId(generation.CharacterModelId);
                var directory = Path.Combine(Application.persistentDataPath, "Rig", generation.Fixture ? "fixture-v1" : "live-v1");
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(VerificationDirectory)) directory = VerificationDirectory;
#endif
                var store = new RigStore(directory);
                var session = store.Read(boundAuth.UserId, model) ?? new RigSession { UserId = boundAuth.UserId, ModelId = model };
                var api = new RigApi(new JsonHttpClient(generation.Fixture ? generation.mockBaseUrl : generation.login.settings.apiBaseUrl,
                    new UnityHttpTransport(), 20, allowEditorLoopback: generation.Fixture), boundAuth);
                Coordinator = new RigCoordinator(api, store, session, generation.Fixture);
                generation.LeaveForRig(); canvas.SetActive(true);
                mode.text = generation.Fixture ? "로컬 확인용 · 실제 AI 생성 없음" : "";
                Resume();
            }
            catch (Exception e) { canvas.SetActive(true); ShowError(e); }
        }
        public void Resume()
        {
            if (Busy || Coordinator == null || boundAuth == null || !boundAuth.IsAuthenticated) return;
            cancellation = new CancellationTokenSource(); Busy = true; Controls();
            _ = Execute(cancellation);
        }
        async Task Execute(CancellationTokenSource current)
        {
            try
            {
                definitiveFailure = false; PageTo("loading"); heading.text = "우리 아이의 걸음을 준비하고 있어요"; previewCamera.enabled = false;
                if (Coordinator.Session.Joints == null)
                {
                    while (true)
                    {
                        try { await Coordinator.FetchAsync(current.Token); break; }
                        catch (ApiFailure e) when (e.Status == 409)
                        { status.text = "사진 속 모습을 살펴보고 있어요"; await Task.Delay(3000, current.Token); }
                    }
                }
                if (Coordinator.Session.PendingJoints == null && Coordinator.Session.JobId == null) Coordinator.Prepare(Coordinator.Session.Joints);
                await Coordinator.SubmitPendingAsync(current.Token);
                while (true)
                {
                    var job = await Coordinator.RefreshAsync(current.Token);
                    progress.value = job.Progress / 100f; status.text = job.Status == "queued" ? "준비 순서를 기다리고 있어요" : $"걸음을 준비하고 있어요 · {job.Progress}%";
                    if (job.Status == "failed") { definitiveFailure = true; throw new ApiFailure(job.ErrorCode ?? "RIG_FAILED", 0); }
                    if (job.Status == "succeeded")
                    {
                        QualityApproved = job.Result["quality"]?.Value<bool?>("accepted") == true && job.Result["quality"]?.Value<bool?>("requires_visual_review") == false;
                        status.text = "움직임을 불러오고 있어요";
                        await player.LoadUrlAsync(job.Result.Value<string>("riggedGlbUrl"), generation.Fixture, current.Token);
                        current.Token.ThrowIfCancellationRequested();
                        if (!player.IsReady) throw new ApiFailure("MODEL_LOAD", 0);
                        foreach (var t in player.Model.GetComponentsInChildren<Transform>()) t.gameObject.layer = 30;
                        player.Paused = false; player.Play("Walk", 0); previewCamera.enabled = true;
                        quality.text = QualityApproved ? "걸음과 모습을 천천히 살펴봐 주세요." : "아직 움직임 확인이 필요한 모습이에요. 어색한 곳이 없는지 살펴봐 주세요.";
                        heading.text = "당신의 강아지가 맞나요?"; PageTo("walk"); WalkReady.Invoke(); return;
                    }
                    await Task.Delay(3000, current.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this && !current.IsCancellationRequested) ShowError(e); }
            finally { current.Dispose(); if (this) { cancellation = null; Busy = false; Controls(); } }
        }
        public void RetryFailedRig()
        {
            if (Busy || Coordinator == null || Page != "error") return;
            // This button is offered only after a definitive failed job result.
            if (!retryRig.gameObject.activeSelf) return;
            try { Coordinator.Prepare(Coordinator.Session.Joints); Resume(); } catch(Exception e) { ShowError(e); }
        }
        public async void EditMotion()
        {
            if(Busy || Page!="walk" || jointEditor==null)return;
            // The live API currently doesn't identify the prepared coordinate
            // space/mesh URL. Don't place normalized points on an arbitrary raw GLB.
            if(!generation.Fixture){quality.text="점 위치를 맞출 준비가 아직 끝나지 않았어요.";return;}
            Busy=true;Controls();cancellation=new CancellationTokenSource();var current=cancellation;
            try
            {
                player.Paused=true;player.Model.gameObject.SetActive(false);
                var api=new GenerationApi(new JsonHttpClient(generation.mockBaseUrl,new UnityHttpTransport(),20,allowEditorLoopback:true),boundAuth);
                var model=await api.JobAsync(generation.Coordinator.Session.ModelJobId,current.Token);
                if(model.Status!="succeeded" || model.Result?.Value<string>("characterModelId")!=Coordinator.Session.ModelId)throw new ApiFailure("INVALID_RESPONSE",0);
                await jointEditor.OpenAsync(model.Result.Value<string>("glbUrl"),true,Coordinator.Session.Joints,current.Token);
                current.Token.ThrowIfCancellationRequested();heading.text="어색한 곳을 함께 맞춰 볼게요";jointEditor.hint.text="점을 누르거나 끌어서 옮겨 주세요. 반대쪽은 함께 맞춰져요.";PageTo("edit");
            }
            catch(OperationCanceledException){}
            catch(Exception e){jointEditor.Close();if(player.Model!=null)player.Model.gameObject.SetActive(true);ShowError(e);}
            finally{current.Dispose();if(this){cancellation=null;Busy=false;Controls();}}
        }
        public void CancelCorrection()
        {
            if(Busy || Page!="edit")return;jointEditor.Close();player.Model.gameObject.SetActive(true);player.Paused=false;player.Play("Walk",0);
            heading.text="당신의 강아지가 맞나요?";PageTo("walk");
        }
        public void SubmitCorrection(RigJoints joints)
        {
            if(Busy || Page!="edit")return;
            try{Coordinator.Prepare(joints);jointEditor.Close();player.Model.gameObject.SetActive(true);Resume();}catch(Exception e){ShowError(e);}
        }
        public void ShowAppearance(){heading.text="우리 아이 모습을 맞춰 볼게요";PageTo("appearance");}
        public void ReturnFromAppearance(){heading.text="당신의 강아지가 맞나요?";PageTo("walk");}
        public void ShowPreferences(){heading.text="익숙한 모습을 알려 주세요";previewCamera.enabled=false;player.Paused=true;PageTo("preferences");}
        public void PhotoMode() { Leave(); generation.PhotoMode(); }
        public void Leave()
        {
            cancellation?.Cancel(); canvas.SetActive(false); previewCamera.enabled = false; player.Paused = true;
            if(jointEditor!=null)jointEditor.Close();if(player.Model!=null)player.Model.gameObject.SetActive(true);
            generation.OfferResumeFromLogin();
            generation.login.safeArea.GetComponentInParent<Canvas>(true).gameObject.SetActive(true);
        }
        public void Rotate() { if (!Busy && Page == "walk") player.transform.Rotate(Vector3.up, 45, Space.World); }
        public void ZoomIn() { previewCamera.orthographicSize = Mathf.Max(.45f, previewCamera.orthographicSize - .1f); }
        public void ZoomOut() { previewCamera.orthographicSize = Mathf.Min(1.4f, previewCamera.orthographicSize + .1f); }
        void PageTo(string page)
        {
            Page = page; loadingPanel.SetActive(page == "loading"); walkPanel.SetActive(page == "walk"); errorPanel.SetActive(page == "error"); Controls();
            if(editorPanel!=null)editorPanel.SetActive(page=="edit");
        }
        void ShowError(Exception exception)
        {
            var code = (exception as ApiFailure)?.Code; previewCamera.enabled = false;
            heading.text = "걸음을 확인하지 못했어요";
            error.text = code == "REQUEST_UNCERTAIN" ? "요청을 보낸 뒤 연결이 끊겼어요. 같은 준비를 다시 시작하지 않도록 상태 확인이 필요해요. 사진으로 함께할 수 있어요."
                : code == "JOINTS_INVALID" || code == "INVALID_RESPONSE" ? "준비된 정보를 읽지 못했어요. 원본 사진은 그대로 있어요."
                : code == "RIG_FAILED" || code == "BLENDER_FAILED" || code == "TIMEOUT" ? "움직임 준비를 마치지 못했어요. 다시 확인하거나 사진으로 함께해 주세요."
                : "연결이나 준비 상태를 확인하지 못했어요. 다시 확인하면 기존 작업부터 살펴볼게요.";
            retryRig.gameObject.SetActive(definitiveFailure);
            PageTo("error");
        }
        void Controls()
        {
            recover.interactable = !Busy && Coordinator != null;
            retryRig.interactable = !Busy;
            // Correction/confirmation are enabled when their own flows are bound.
            if (motionCorrection != null) motionCorrection.interactable = !Busy && Page=="walk" && player.IsReady;
            if (appearanceCorrection != null) appearanceCorrection.interactable = !Busy && Page=="walk" && appearanceFlow!=null && player.IsReady;
            if (confirm != null) confirm.interactable = !Busy && Page=="walk" && QualityApproved && setupFlow!=null;
        }
        void Update()
        {
            if (Coordinator != null && (generation.login.Session != boundAuth || !boundAuth.IsAuthenticated))
            {
                cancellation?.Cancel(); canvas.SetActive(false); previewCamera.enabled = false; if(jointEditor!=null)jointEditor.Close();if(appearanceFlow!=null)appearanceFlow.ClearUser();player.Clear(); Coordinator = null; QualityApproved = false;
                if(generation.login.Session==null||!generation.login.Session.IsAuthenticated)
                {generation.login.safeArea.GetComponentInParent<Canvas>(true).gameObject.SetActive(true);if(!generation.login.Busy)generation.login.ChangeEmail();}
            }
            if (canvas.activeSelf)
            {
                var area = Screen.safeArea; safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
                safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height); safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            }
            if (resumeAfterPause && !paused && !Busy && canvas.activeSelf) { resumeAfterPause = false; Resume(); }
        }
        void OnApplicationPause(bool value)
        {
            paused = value; player.Paused = value;
            if (value) cancellation?.Cancel(); else if (canvas.activeSelf) resumeAfterPause = true;
        }
        void OnDestroy()
        {
            generation.ModelReady.RemoveListener(FromModel); cancellation?.Cancel();
            if (previewCamera != null) previewCamera.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
