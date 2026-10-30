using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Generation;
using Withme.Networking;

namespace Withme.UI
{
    public sealed class GenerationFlow : MonoBehaviour
    {
        public LoginFlow login;
        public RegistrationFlow registration;
        public GameObject canvas, loadingPanel, reviewPanel, readyPanel, failurePanel, photoPanel, zoomPanel;
        public RectTransform safeArea;
        public TMP_Text heading, progressLabel, errorLabel, modeLabel, retryLabel, photoCaption;
        public Slider progress;
        public RawImage[] views;
        public RawImage originalPhoto, photoOnly, zoomImage;
        public Button approveButton, retryButton, recoverButton, resumeButton;
        public GenerationModelPreview modelPreview;
        public string mockBaseUrl = "http://127.0.0.1:8765";
        public readonly UnityEvent ModelReady = new UnityEvent();
        public GenerationCoordinator Coordinator { get; private set; }
        public bool Busy { get; private set; }
        public bool Visible => canvas.activeSelf;
        public string Page { get; private set; }
        public string CharacterModelId => Coordinator?.Session.ModelId;
        public bool Fixture => login.UsingDemo;
        AuthService boundAuth;
        IGenerationStore store;
        IGenerationApi api;
        CancellationTokenSource cancellation;
        Task running;
        readonly List<Texture2D> textures = new List<Texture2D>();
        string lastError;
        bool paused, resumeAfterPause;
#if UNITY_EDITOR
        public string VerificationDirectory;
#endif
        void Start() { registration.UploadsReady.AddListener(FromUpload); canvas.SetActive(false); }
        void Bind()
        {
            boundAuth = login.Session;
            var directory = Path.Combine(Application.persistentDataPath, "Generation", Fixture ? "fixture-v1" : "live-v1");
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(VerificationDirectory)) directory = VerificationDirectory;
#endif
            store = new GenerationStore(directory);
            var url = login.settings.apiBaseUrl;
            if (Fixture)
            {
#if UNITY_EDITOR
                if (!Uri.TryCreate(mockBaseUrl, UriKind.Absolute, out var mockUri) || !mockUri.IsLoopback || mockUri.Scheme != "http") throw new ApiFailure("CONFIGURATION", 0);
                url = mockBaseUrl;
#else
                throw new ApiFailure("CONFIGURATION", 0); // This local server fixture is Editor-only.
#endif
            }
            api = new GenerationApi(new JsonHttpClient(url, new UnityHttpTransport(), 20, allowEditorLoopback: Fixture), boundAuth);
        }
        void FromUpload()
        {
            if (!isActiveAndEnabled || Busy || registration.CompletedMediaIds.Length == 0) return;
            var pet = registration.Coordinator.Pet;
            var ids = registration.CompletedMediaIds;
            try
            {
                Bind(); var existing = store.Read(boundAuth.UserId);
                if (existing != null) { Coordinator = new GenerationCoordinator(api, store, existing); Resume(); return; }
                var session = new GenerationSession { UserId = boundAuth.UserId, PetId = pet.Id, PetName = pet.Name, MediaIds = ids };
                Coordinator = new GenerationCoordinator(api, store, session);
                Coordinator.Prepare("create"); Run(Continue);
            }
            catch (Exception e) { Show(); Error(e); }
        }
        public void Resume()
        {
            if (Busy || Coordinator == null) return;
            Run(Continue);
        }
        public void Approve() => Submit("approve");
        public void RetryViews() => Submit("retry");
        void Submit(string action)
        {
            if (Busy || Coordinator == null || Page != "review") return;
            try { Coordinator.Prepare(action); Run(Continue); } catch (Exception e) { Error(e); }
        }
        void Run(Func<CancellationToken, Task> operation)
        {
            if (Busy || boundAuth == null || !boundAuth.IsAuthenticated) return;
            Busy = true; lastError = null; cancellation = new CancellationTokenSource(); Show(); Controls();
            running = Execute(operation, cancellation);
        }
        async Task Execute(Func<CancellationToken, Task> operation, CancellationTokenSource current)
        {
            try { await operation(current.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this && !current.IsCancellationRequested) Error(e); }
            finally { current.Dispose(); if (this) { Busy = false; cancellation = null; Controls(); } }
        }
        async Task Continue(CancellationToken token)
        {
            PageTo("loading"); ClearAssets();
            heading.text = Coordinator.Session.Stage == "model" || Coordinator.Session.Stage == "ready" ? Coordinator.Session.PetName + "를 만날 준비 중이에요" : "사진 속 모습을 찾고 있어요";
            await Coordinator.SubmitPendingAsync(token);
            while (true)
            {
                var job = await Coordinator.RefreshAsync(token);
                progress.value = job.Progress / 100f; progressLabel.text = job.Status == "queued" ? "준비 순서를 기다리고 있어요" : $"준비 중 · {job.Progress}%";
                if (job.Status == "failed") throw new ApiFailure(job.ErrorCode ?? "GENERATION_FAILED", 0);
                if (job.Status == "succeeded")
                {
                    progressLabel.text = "모습을 불러오고 있어요";
                    if (Coordinator.Session.Stage == "review")
                    {
                        var urls = (JArray)job.Result["views"];
                        for (var i = 0; i < 4; i++) { var t = await GenerationAssets.Image((string)urls[i], Fixture, token); textures.Add(t); views[i].texture = t; }
                        var photo = await GenerationAssets.Image(await api.PhotoUrlAsync(Coordinator.Session.MediaIds[0], token), Fixture, token);
                        textures.Add(photo); originalPhoto.texture = photo;
                        heading.text = "당신의 강아지가 맞나요?";
                        retryLabel.text = Coordinator.Session.Retries >= 3 ? "다시 찾아보기 횟수를 모두 사용했어요" : "다시 찾아보기";
                        var problems = job.Result["problems"] as JArray;
                        photoCaption.text = problems != null && problems.Count > 0 ? "일부 사진을 확인해 주세요. 얼굴과 무늬가 자연스러운지 살펴봐 주세요." : "사진을 눌러 얼굴과 무늬를 살펴봐 주세요.";
                        PageTo("review");
                    }
                    else
                    {
                        await modelPreview.LoadAsync(job.Result.Value<string>("glbUrl"), Fixture, token);
                        heading.text = "우리 아이의 모습을 찾았어요"; PageTo("ready"); ModelReady.Invoke();
                    }
                    return;
                }
                await Task.Delay(3000, token);
            }
        }
        public async void PhotoMode()
        {
            if (Coordinator == null) return;
            cancellation?.Cancel(); if (running != null) await running;
            if (!this || boundAuth == null || !boundAuth.IsAuthenticated) return;
            Run(async token =>
            {
                PageTo("photo"); ClearAssets();
                var texture = await GenerationAssets.Image(await api.PhotoUrlAsync(Coordinator.Session.MediaIds[0], token), Fixture, token);
                textures.Add(texture); photoOnly.texture = texture;
                heading.text = "사진으로 함께해요";
            });
        }
        public void Later()
        {
            cancellation?.Cancel(); canvas.SetActive(false); login.safeArea.GetComponentInParent<Canvas>(true).gameObject.SetActive(true);
            ClearAssets();
        }
        public void Zoom(int index)
        {
            if (Page != "review" || Busy || index < 0 || index > 4) return;
            zoomImage.texture = index == 4 ? originalPhoto.texture : views[index].texture; zoomPanel.SetActive(true);
        }
        public void CloseZoom() => zoomPanel.SetActive(false);
        void Show()
        {
            registration.LeaveForGeneration(); login.safeArea.GetComponentInParent<Canvas>(true).gameObject.SetActive(false); canvas.SetActive(true);
            modeLabel.text = Fixture ? "목 서버 확인용 · 제공된 사진·모델 · 실제 AI 생성 없음" : "";
        }
        void PageTo(string page)
        {
            Page = page; loadingPanel.SetActive(page == "loading"); reviewPanel.SetActive(page == "review"); readyPanel.SetActive(page == "ready");
            failurePanel.SetActive(page == "error"); photoPanel.SetActive(page == "photo"); zoomPanel.SetActive(false); Controls();
        }
        void Error(Exception e)
        {
            lastError = (e as ApiFailure)?.Code;
            heading.text = "잠시 확인하지 못했어요";
            errorLabel.text = lastError == "LIMIT_EXCEEDED" ? "다시 찾아볼 수 있는 횟수나 준비 비용의 한도에 도달했어요. 사진으로 함께할 수 있어요."
                : lastError == "UPSTREAM_FAILED" || lastError == "GENERATION_FAILED" ? "아직 모습을 찾지 못했어요. 원본 사진은 그대로 있어요."
                : lastError == "CHECKPOINT_INVALID" ? "이전에 준비하던 정보를 읽지 못했어요. 다시 로그인해도 계속되면 문의해 주세요."
                : lastError == "NETWORK" || lastError == "TIMEOUT" ? "연결이 불안정해 준비 상태를 확인하지 못했어요. 준비 중이던 작업은 다시 시작하지 않아요."
                : lastError == "MODEL_LOAD" || lastError == "ASSET_DOWNLOAD" ? "준비된 모습을 불러오지 못했어요. 상태를 다시 확인하거나 사진으로 함께해 주세요."
                : AuthMessages.For(e);
            PageTo("error");
        }
        void Controls()
        {
            approveButton.interactable = !Busy && Coordinator?.Session.Stage == "review" && Coordinator.Session.PendingAction == null;
            retryButton.interactable = approveButton.interactable && Coordinator.Session.Retries < 3;
            recoverButton.interactable = !Busy && Coordinator != null && lastError != "LIMIT_EXCEEDED";
            resumeButton.gameObject.SetActive(Coordinator != null && login.Session != null && login.Session.IsAuthenticated && !Visible);
            resumeButton.interactable = !Busy;
        }
        void ClearAssets()
        {
            foreach (var image in views) if (image != null) image.texture = null;
            if (originalPhoto != null) originalPhoto.texture = null; if (photoOnly != null) photoOnly.texture = null; if (zoomImage != null) zoomImage.texture = null;
            foreach (var t in textures) if (t != null) Destroy(t); textures.Clear(); if (modelPreview != null) modelPreview.Clear();
        }
        void Update()
        {
            if (login.Session != boundAuth)
            {
                cancellation?.Cancel(); canvas.SetActive(false); Coordinator = null; ClearAssets();
                if (Busy) return;
                boundAuth = login.Session; store = null; api = null;
            }
            if (boundAuth == null || !boundAuth.IsAuthenticated) { if (Visible) Later(); Coordinator = null; store = null; Controls(); return; }
            if (!login.Busy && Coordinator == null && store == null)
            {
                try { Bind(); var s = store.Read(boundAuth.UserId); if (s != null) { Coordinator = new GenerationCoordinator(api, store, s); Resume(); } }
                catch (Exception e) { Show(); Error(e); }
            }
            if (Visible)
            {
                var area = Screen.safeArea; safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height); safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height); safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
                if (UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) { if (zoomPanel.activeSelf) CloseZoom(); else Later(); }
            }
            if (resumeAfterPause && !paused && !Busy && Visible && Coordinator != null) { resumeAfterPause = false; Resume(); }
            Controls();
        }
        void OnApplicationPause(bool value)
        {
            paused = value;
            if (value) cancellation?.Cancel(); else if (Visible && Coordinator != null) resumeAfterPause = true;
        }
        async void OnDestroy()
        {
            registration.UploadsReady.RemoveListener(FromUpload); cancellation?.Cancel();
            try { if (running != null) await running; } catch { }
            ClearAssets();
        }
    }
}
