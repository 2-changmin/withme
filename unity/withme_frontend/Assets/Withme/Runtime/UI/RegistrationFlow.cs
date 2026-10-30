using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Networking;
using Withme.Registration;

namespace Withme.UI
{
    public enum RegistrationPage { Profile, Notice, Uploading, PhotoOnly, Uploaded }
    public sealed class RegistrationFlow : MonoBehaviour
    {
        public LoginFlow login;
        public GameObject loginCanvas, registrationCanvas, profilePanel, noticePanel, photoPanel, uploadedPanel;
        public TMP_InputField nameInput;
        public TMP_Text message, photoCount, modeLabel, uploadedDescription;
        public RawImage[] thumbnails;
        public TMP_Text[] photoStatuses;
        public Button[] removeButtons, retryButtons, slotButtons;
        public Button nextButton, addButton, confirmButton;
        public RawImage localPhoto, noticePhoto;
        public UnityEngine.UI.ScrollRect scroll;
        public RectTransform safeArea;
        public readonly UnityEvent UploadsReady = new UnityEvent();
        public bool Visible { get; private set; }
        public bool Busy { get; private set; }
        public RegistrationPage Page { get; private set; }
        public UploadCoordinator Coordinator { get; private set; }
        public string[] CompletedMediaIds => Coordinator?.MediaIds ?? Array.Empty<string>();
        public string PetId => Coordinator?.Pet?.Id;

        IPhotoPicker picker = new PhotoPicker();
        AuthService boundSession;
        string owner;
        CancellationTokenSource operationCancellation;
        Task running;
        bool selecting;
        Rect lastSafe;
        int lastWidth, lastHeight;
        string lastMessage;

        void Start()
        {
            login.RegistrationRequested.AddListener(Open);
            registrationCanvas.SetActive(false);
            nameInput.onValueChanged.AddListener(_ => Render());
        }
        public void Open()
        {
            if (Busy || selecting) return;
            if (login.Session == null || !login.Session.IsAuthenticated) return;
            if (Coordinator == null || boundSession != login.Session || owner != login.Session.UserId)
            {
                ClearDraft();
                boundSession = login.Session; owner = boundSession.UserId;
                IRegistrationApi api;
                IFileUploader uploader;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (login.UsingDemo)
                {
                    var demo = new DemoRegistration(login.LastPets?.FirstOrDefault()); api = demo; uploader = demo;
                }
                else
#endif
                {
                    api = new RegistrationApi(new JsonHttpClient(login.settings.apiBaseUrl, new UnityHttpTransport(), login.settings.timeoutSeconds), boundSession);
                    uploader = new UnityFileUploader();
                }
                Coordinator = new UploadCoordinator(api, uploader);
                Coordinator.Changed = Render;
                nameInput.text = login.LastPets?.FirstOrDefault()?.Name ?? "";
            }
            Visible = true; loginCanvas.SetActive(false); registrationCanvas.SetActive(true);
            modeLabel.text = login.UsingDemo ? "화면 확인용 · 서버 등록·사진 전송 없음" : "";
            message.text = "";
            SetPage(RegistrationPage.Profile);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void ConfigureForVerification(IRegistrationApi api, IFileUploader uploader, IPhotoPicker testPicker)
        {
            if (Busy) throw new InvalidOperationException("Wait for the current operation.");
            ClearDraft(); boundSession = login.Session; owner = boundSession.UserId;
            Coordinator = new UploadCoordinator(api, uploader); Coordinator.Changed = Render; picker = testPicker;
            Visible = true; loginCanvas.SetActive(false); registrationCanvas.SetActive(true);
            modeLabel.text = "화면 확인용 · 서버 등록·사진 전송 없음";
            nameInput.text = ""; message.text = ""; SetPage(RegistrationPage.Profile);
        }
#endif
        public async void PickPhoto()
        {
            if (Busy || selecting || Coordinator == null) return;
            if (Coordinator.Photos.Count >= RegistrationRules.MaxPhotos) { message.text = RegistrationRules.Message(new ApiFailure("PHOTO_LIMIT", 0)); return; }
            selecting = true;
            var session = boundSession;
            operationCancellation = new CancellationTokenSource();
            var current = operationCancellation;
            Render();
            try
            {
                var photo = await picker.PickAsync(current.Token);
                if (photo != null)
                {
                    if (!this || current.IsCancellationRequested || session != login.Session || !login.Session.IsAuthenticated)
                    { photo.Dispose(); return; }
                    Coordinator.Photos.Add(photo); message.text = "";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (this) message.text = RegistrationRules.Message(exception); }
            finally
            {
                current.Dispose();
                if (this) { selecting = false; operationCancellation = null; if (!Visible) ClearDraft(); else Render(); }
            }
        }
        public void RemovePhoto(int index)
        {
            if (Busy || selecting || Coordinator == null || index < 0 || index >= Coordinator.Photos.Count) return;
            var photo = Coordinator.Photos[index]; Coordinator.Photos.RemoveAt(index); photo.Dispose(); Render();
        }
        public void Next()
        {
            if (Busy || selecting || Coordinator == null) return;
            try
            {
                RegistrationRules.Name(nameInput.text);
                if (Coordinator.Photos.Count < 1) throw new ApiFailure("PHOTO_LIMIT", 0);
                foreach (var photo in Coordinator.Photos) RegistrationRules.Photo(photo);
                message.text = ""; SetPage(RegistrationPage.Notice);
            }
            catch (Exception exception) { message.text = RegistrationRules.Message(exception); }
        }
        public void ConfirmUpload()
        {
            if (Busy || selecting || Page != RegistrationPage.Notice || Coordinator == null) return;
            Busy = true; operationCancellation = new CancellationTokenSource();
            SetPage(RegistrationPage.Uploading);
            running = UploadAsync(operationCancellation);
        }
        async Task UploadAsync(CancellationTokenSource cancellation)
        {
            try
            {
                await Coordinator.RunAsync(nameInput.text, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (Coordinator.Ready)
                {
                    uploadedDescription.text = $"사진 {Coordinator.Photos.Count}장을 올렸어요.\n사진 검사가 이어질 수 있어요.";
                    SetPage(RegistrationPage.Uploaded);
                    UploadsReady.Invoke(); // #10 can subscribe; this issue never starts generation.
                }
                else { SetPage(RegistrationPage.Profile); message.text = "일부 사진을 보내지 못했어요. 확인 후 다시 시도해 주세요."; }
            }
            catch (OperationCanceledException)
            { if (this) { SetPage(RegistrationPage.Profile); message.text = "사진 전송을 멈췄어요. 완료된 사진은 그대로 있어요."; } }
            catch (Exception exception)
            {
                if (this)
                {
                    SetPage(RegistrationPage.Profile); message.text = RegistrationRules.Message(exception);
                    if (boundSession == null || !boundSession.IsAuthenticated) Hide();
                }
            }
            finally
            {
                cancellation.Dispose();
                if (this) { Busy = false; operationCancellation = null; if (!Visible) ClearDraft(); else Render(); }
            }
        }
        public void RetryPhoto(int index)
        {
            if (Busy || Coordinator == null || index < 0 || index >= Coordinator.Photos.Count) return;
            // Confirm again; the coordinator resumes failed stages and preserves completed photos.
            Next();
        }
        public void ShowPhotoOnly()
        {
            if (Busy || selecting || Coordinator == null || Coordinator.Photos.Count == 0) return;
            localPhoto.texture = Coordinator.Photos[0].Preview;
            Fit(localPhoto); SetPage(RegistrationPage.PhotoOnly);
        }
        public void Back()
        {
            if (Busy || selecting) { operationCancellation?.Cancel(); return; }
            if (Page != RegistrationPage.Profile) { SetPage(RegistrationPage.Profile); return; }
            Hide();
        }
        void Hide()
        {
            Visible = false; registrationCanvas.SetActive(false); loginCanvas.SetActive(true);
            if (login.Session == null || !login.Session.IsAuthenticated) login.ChangeEmail();
        }
        void SetPage(RegistrationPage page)
        {
            Page = page;
            profilePanel.SetActive(page == RegistrationPage.Profile || page == RegistrationPage.Uploading);
            noticePanel.SetActive(page == RegistrationPage.Notice);
            photoPanel.SetActive(page == RegistrationPage.PhotoOnly);
            uploadedPanel.SetActive(page == RegistrationPage.Uploaded);
            scroll.verticalNormalizedPosition = 1;
            if (Coordinator != null && Coordinator.Photos.Count > 0) { noticePhoto.texture = Coordinator.Photos[0].Preview; Fit(noticePhoto); }
            Render();
        }
        void Render()
        {
            if (this == null || Coordinator == null) return;
            var count = Coordinator.Photos.Count;
            photoCount.text = $"우리 아이 사진 · {count} / 4";
            var validName = true;
            try { RegistrationRules.Name(nameInput.text); } catch (ApiFailure) { validName = false; }
            var locked = Busy || selecting;
            nameInput.interactable = !locked && Coordinator.Pet == null;
            if (Coordinator.Pet != null) nameInput.SetTextWithoutNotify(Coordinator.Pet.Name);
            nextButton.interactable = !locked && validName && count > 0;
            addButton.interactable = !locked && count < 4;
            confirmButton.interactable = !locked;
            for (var i = 0; i < thumbnails.Length; i++)
            {
                var exists = i < count;
                thumbnails[i].gameObject.SetActive(exists);
                slotButtons[i].interactable = !locked && !exists && count < 4;
                removeButtons[i].gameObject.SetActive(exists); removeButtons[i].interactable = !locked;
                retryButtons[i].gameObject.SetActive(exists && (Coordinator.Photos[i].Stage == PhotoStage.Failed || Coordinator.Photos[i].Stage == PhotoStage.Cancelled));
                retryButtons[i].interactable = !locked;
                if (!exists) { photoStatuses[i].text = "사진 추가"; continue; }
                var photo = Coordinator.Photos[i]; thumbnails[i].texture = photo.Preview; Fit(thumbnails[i]);
                photoStatuses[i].text = Status(photo);
            }
        }
        static string Status(PhotoDraft photo)
        {
            switch (photo.Stage)
            {
                case PhotoStage.Preparing: return "전송을 준비하고 있어요";
                case PhotoStage.Uploading: return $"사진 전송 중 · {photo.Progress * 100:F0}%";
                case PhotoStage.Finalizing: return "전송 결과 확인 중";
                case PhotoStage.Complete: return photo.RemoteStatus == "ready" ? "사진 준비 완료" : "전송 완료 · 검사 중";
                case PhotoStage.Failed: return RegistrationRules.Message(new ApiFailure(photo.ErrorCode ?? "UPLOAD_FAILED", 0));
                case PhotoStage.Cancelled: return "전송을 멈췄어요";
                default: return "선택한 사진";
            }
        }
        static void Fit(RawImage image)
        {
            var aspect = image.GetComponent<AspectRatioFitter>();
            if (aspect != null && image.texture != null) aspect.aspectRatio = image.texture.width / (float)image.texture.height;
        }
        void Update()
        {
            if (!Visible) return;
            if (boundSession != login.Session || !boundSession.IsAuthenticated || owner != boundSession.UserId)
            { operationCancellation?.Cancel(); Hide(); if (!Busy && !selecting) ClearDraft(); return; }
            if (lastSafe != Screen.safeArea || lastWidth != Screen.width || lastHeight != Screen.height)
            {
                lastSafe = Screen.safeArea; lastWidth = Screen.width; lastHeight = Screen.height;
                safeArea.anchorMin = new Vector2(lastSafe.xMin / Screen.width, lastSafe.yMin / Screen.height);
                safeArea.anchorMax = new Vector2(lastSafe.xMax / Screen.width, lastSafe.yMax / Screen.height);
                safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            }
            if (TouchScreenKeyboard.visible && nameInput.isFocused) scroll.verticalNormalizedPosition = 0.6f;
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            { if (nameInput.isFocused) nameInput.DeactivateInputField(); else Back(); }
        }
        void LateUpdate()
        {
            if (!Visible || message.text == lastMessage) return;
            lastMessage = message.text;
            // Bring the inline error into view on shorter phone screens as well.
            if (!string.IsNullOrEmpty(lastMessage)) scroll.verticalNormalizedPosition = 0;
        }
        void ClearDraft()
        {
            if (Coordinator == null) return;
            Coordinator.Changed = null;
            foreach (var photo in Coordinator.Photos) photo.Dispose();
            Coordinator = null;
        }
        async void OnDestroy()
        {
            if (login != null) login.RegistrationRequested.RemoveListener(Open);
            operationCancellation?.Cancel();
            try { if (running != null) await running; } catch { }
            ClearDraft();
        }
    }
}
