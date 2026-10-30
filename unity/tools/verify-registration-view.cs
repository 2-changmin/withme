using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Withme.Auth;
using Withme.Networking;
using Withme.Registration;
using Withme.UI;

public static class VerifyRegistrationView
{
    sealed class Picker : IPhotoPicker
    {
        public string Source;
        public Task<PhotoDraft> PickAsync(CancellationToken c) => PhotoPicker.ImportAsync(Source, c);
    }
    sealed class Fixture : IRegistrationApi, IFileUploader
    {
        readonly DemoRegistration demo = new DemoRegistration();
        public int Creates, Tickets, Puts;
        public bool FailSecond = true;
        public Task<Pet[]> ListPetsAsync(CancellationToken c) => demo.ListPetsAsync(c);
        public Task<Pet> CreatePetAsync(string n, CancellationToken c) { Creates++; return demo.CreatePetAsync(n, c); }
        public Task<UploadTicket> TicketAsync(string id, PhotoDraft p, CancellationToken c) { Tickets++; return demo.TicketAsync(id, p, c); }
        public Task<UploadJob> FinalizeAsync(string id, CancellationToken c) => demo.FinalizeAsync(id, c);
        public Task<MediaRecord> MediaAsync(string id, CancellationToken c) => demo.MediaAsync(id, c);
        public async Task UploadAsync(UploadTicket t, PhotoDraft p, IProgress<float> progress, CancellationToken c)
        {
            Puts++; if (Puts == 2 && FailSecond) throw new ApiFailure("UPLOAD_FAILED", 503);
            await demo.UploadAsync(t, p, progress, c);
        }
    }
    static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    static async Task Until(Func<bool> predicate)
    {
        var end = Time.realtimeSinceStartupAsDouble + 15;
        while (!predicate()) { if (Time.realtimeSinceStartupAsDouble > end) throw new TimeoutException("UI state timeout"); await Task.Yield(); }
    }
    static async Task Frames(int count) { var target = Time.frameCount + count; await Until(() => Time.frameCount >= target); }
    static async Task Click(Mouse mouse, UnityEngine.UI.Button button)
    {
        Check(button.interactable && button.gameObject.activeInHierarchy, "Button not active");
        var r = button.GetComponent<RectTransform>(); var point = RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));
        Check(point.y > 0 && point.y < Screen.height, "Button outside viewport");
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); await Frames(2);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); await Frames(2);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); await Frames(3);
    }
    static async Task Login(LoginFlow login, bool existing = false)
    {
        login.ConfigureForVerification(new DemoHttpTransport { ExistingPet = existing }, new MemoryTokenStore(), () => 1000);
        login.emailInput.text = "test@example.com"; login.SendCode(); await Until(() => !login.Busy);
        login.codeInput.text = "123456"; login.VerifyCode(); await Until(() => !login.Busy);
        await Frames(2);
    }
    public static async Task<object> Run()
    {
        Check(Application.isPlaying, "Start Play first");
        var login = UnityEngine.Object.FindFirstObjectByType<LoginFlow>();
        var flow = UnityEngine.Object.FindFirstObjectByType<RegistrationFlow>();
        Check(login != null && flow != null, "Registration scene missing");
        await Until(() => !login.Busy);
        var original = InputSystem.settings; var temporary = UnityEngine.Object.Instantiate(original);
        temporary.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        temporary.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = temporary; var mouse = InputSystem.AddDevice<Mouse>("WithmeRegistrationTestMouse");
        var path = Path.Combine(Application.temporaryCachePath, "withme-ui-fixture.png");
        var texture = new Texture2D(96, 96); var colors = new Color[96*96];
        for (var i = 0; i < colors.Length; i++) colors[i] = ((i / 96 / 16 + i % 96 / 16) % 2 == 0) ? new Color(.4f,.6f,.4f) : new Color(.85f,.9f,.8f);
        texture.SetPixels(colors); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.Destroy(texture);
        var checks = new List<string>();
        var generation = UnityEngine.Object.FindFirstObjectByType<GenerationFlow>();
        var generationEnabled = generation != null && generation.enabled;
        if (generation != null) { generation.Later(); generation.enabled = false; }
        try
        {
            await Login(login); Check(flow.Visible && flow.Page == RegistrationPage.Profile, "Login handoff missing");
            checks.Add("unregistered login opens registration");
            var fixture = new Fixture(); flow.ConfigureForVerification(fixture, fixture, new Picker { Source = path });
            Check(!flow.nextButton.interactable, "Empty name/photo allowed"); flow.nameInput.text = "보리";
            Check(!flow.nextButton.interactable, "Empty photo allowed");
            await Click(mouse, flow.addButton); await Until(() => flow.Coordinator.Photos.Count == 1 && flow.addButton.interactable);
            Check(flow.nextButton.interactable && flow.thumbnails[0].texture != null, "Preview/required data missing");
            flow.ShowPhotoOnly(); Check(flow.Page == RegistrationPage.PhotoOnly && fixture.Creates == 0 && fixture.Tickets == 0 && fixture.Puts == 0, "Photo mode sent data"); flow.Back();
            checks.Add("required data / actual add click / local preview without network");
            for (var i = 1; i < 4; i++) { flow.PickPhoto(); var expected = i + 1; await Until(() => flow.Coordinator.Photos.Count == expected && (expected == 4 || flow.addButton.interactable)); await Frames(2); }
            Check(!flow.addButton.interactable && flow.Coordinator.Photos.Count == 4, "Photo limit");
            flow.PickPhoto(); Check(flow.Coordinator.Photos.Count == 4, "Photo limit bypass");
            flow.RemovePhoto(3); flow.RemovePhoto(2);
            Check(flow.Coordinator.Photos.Count == 2, "Remove failed"); checks.Add("four-photo limit / remove and cleanup");
            flow.scroll.verticalNormalizedPosition = 0; await Frames(3);
            await Click(mouse, flow.nextButton);
            Check(flow.Page == RegistrationPage.Notice && fixture.Creates == 0 && fixture.Tickets == 0 && fixture.Puts == 0, "Transfer before confirmation");
            checks.Add("actual next click / static notice / no premature transfer");
            flow.scroll.verticalNormalizedPosition = 0.7f; await Frames(2);
            await Click(mouse, flow.confirmButton);
            Check(flow.Busy && !flow.addButton.interactable, "Upload not locked");
            flow.ConfirmUpload(); flow.RemovePhoto(0); Check(flow.Coordinator.Photos.Count == 2, "Mutation during upload");
            await Until(() => !flow.Busy);
            Check(flow.Page == RegistrationPage.Profile && flow.Coordinator.Photos[0].Complete && flow.retryButtons[1].gameObject.activeSelf, "Partial failure not visible");
            flow.RetryPhoto(1); flow.ConfirmUpload(); await Until(() => !flow.Busy);
            Check(flow.Page == RegistrationPage.Uploaded && flow.CompletedMediaIds.Length == 2 && !string.IsNullOrEmpty(flow.PetId), "Handoff missing");
            Check(fixture.Creates == 1 && fixture.Tickets == 2 && fixture.Puts == 3, "Retry duplicated successful photo");
            checks.Add("actual confirm click / progress lock / partial failure / retry / #10 handoff");
            var copies = flow.Coordinator.Photos.ConvertAll(p => p.LocalPath);
            await login.Session.LogoutAsync(CancellationToken.None); await Frames(4);
            Check(!flow.Visible && flow.Coordinator == null && copies.TrueForAll(p => !File.Exists(p)), "Session cleanup failed");
            checks.Add("logout hides registration and deletes owned copies");
            await Login(login, true); Check(login.registrationButton.gameObject.activeInHierarchy && !flow.Visible, "Existing pet continue route missing");
            login.ContinueRegistration(); Check(flow.Visible && flow.nameInput.text.Length > 0, "Existing pet handoff");
            checks.Add("existing pet explicit registration resume");
            // Leave a clearly labelled fixture preview for a subsequent screen capture.
            fixture = new Fixture(); flow.ConfigureForVerification(fixture, fixture, new Picker { Source = path });
            flow.nameInput.text = "보리"; flow.PickPhoto(); await Until(() => flow.Coordinator.Photos.Count == 1 && flow.addButton.interactable);
            flow.PickPhoto(); await Until(() => flow.Coordinator.Photos.Count == 2 && flow.addButton.interactable); await Frames(3);
            return new { passed = checks.Count, checks, actualPointerClicks = true, productionWrites = 0, deviceGalleryTested = false };
        }
        finally
        {
            if (generation != null) generation.enabled = generationEnabled;
            File.Delete(path); InputSystem.RemoveDevice(mouse); InputSystem.settings = original; UnityEngine.Object.Destroy(temporary);
        }
    }
}
