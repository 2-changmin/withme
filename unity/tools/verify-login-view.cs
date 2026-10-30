using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Withme.Auth;
using Withme.Networking;
using Withme.UI;

public static class VerifyLoginView
{
    sealed class Fixture : IHttpTransport
    {
        public bool FailPets;
        public readonly DemoHttpTransport Demo = new DemoHttpTransport();
        public TaskCompletionSource<HttpReply> PendingOtp;
        public Task<HttpReply> SendAsync(HttpCall call, CancellationToken token)
        {
            if (PendingOtp != null && call.Url.EndsWith("/otp")) return PendingOtp.Task;
            if (FailPets && call.Url.EndsWith("/pets")) return Task.FromResult(new HttpReply
                { Status = 503, Body = "{\"error\":{\"code\":\"INTERNAL\"}}" });
            return Demo.SendAsync(call, token);
        }
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static async Task Until(Func<bool> condition)
    {
        var deadline = Time.realtimeSinceStartupAsDouble + 10;
        while (!condition())
        {
            if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("UI did not reach expected state.");
            await Task.Yield();
        }
    }
    static async Task Frames(int count)
    { var frame = Time.frameCount + count; await Until(() => Time.frameCount >= frame); }
    static async Task Click(Mouse mouse, UnityEngine.UI.Button button)
    {
        Require(button.interactable, "Button is disabled.");
        var rect = button.GetComponent<RectTransform>();
        var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        await Frames(2);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
        await Frames(2);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        await Frames(3);
    }
    public static async Task<object> Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Play the Start scene first.");
        var flow = UnityEngine.Object.FindFirstObjectByType<LoginFlow>();
        Require(flow != null, "LoginFlow missing.");
        await Until(() => !flow.Busy);
        var original = InputSystem.settings;
        var temporary = UnityEngine.Object.Instantiate(original);
        temporary.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        temporary.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = temporary;
        var mouse = InputSystem.AddDevice<Mouse>("WithmeAuthTestMouse");
        var checks = 0;
        var registrationEvents = 0;
        UnityEngine.Events.UnityAction registration = () => registrationEvents++;
        flow.RegistrationRequested.AddListener(registration);
        var generation = UnityEngine.Object.FindFirstObjectByType<GenerationFlow>();
        var generationEnabled = generation != null && generation.enabled;
        if (generation != null) { generation.Later(); generation.enabled = false; }
        try
        {
            var fixture = new Fixture(); long clock = 1000;
            flow.ConfigureForVerification(fixture, new MemoryTokenStore(), () => clock);
            flow.emailInput.text = "bad@";
            Require(!flow.sendButton.interactable, "Invalid email enabled sending.");
            flow.emailInput.text = "test@example.com";
            await Click(mouse, flow.sendButton);
            await Until(() => !flow.Busy && flow.ScreenState == LoginScreen.Code);
            Require(!flow.resendButton.interactable && flow.recipientLabel.text.Contains("test@example.com"), "Code page or cooldown missing.");
            checks++;
            flow.codeInput.text = "111111";
            await Click(mouse, flow.verifyButton);
            await Until(() => !flow.Busy);
            Require(flow.ScreenState == LoginScreen.Code && !string.IsNullOrEmpty(flow.codeError.text), "Wrong code not handled.");
            checks++;
            flow.ChangeEmail();
            Require(flow.ScreenState == LoginScreen.Email && !flow.sendButton.interactable, "Cooldown reset by navigation.");
            flow.emailInput.text = "other@example.com";
            await Click(mouse, flow.sendButton);
            await Until(() => !flow.Busy && flow.ScreenState == LoginScreen.Code);
            flow.codeInput.text = "123456";
            Require(flow.codeDigits[0].text == "1" && flow.codeDigits[5].text == "6", "Six-digit paste display failed.");
            await Click(mouse, flow.verifyButton);
            await Until(() => !flow.Busy);
            Require(flow.ScreenState == LoginScreen.NeedsRegistration && registrationEvents == 1 && flow.Session.IsAuthenticated,
                "Unregistered user handoff failed.");
            checks++;
            var registrationFlow = UnityEngine.Object.FindFirstObjectByType<RegistrationFlow>();
            if (registrationFlow != null && registrationFlow.Visible) registrationFlow.Back();
            await Click(mouse, flow.logoutButton);
            await Until(() => !flow.Busy);
            Require(flow.ScreenState == LoginScreen.Email && !flow.Session.IsAuthenticated && flow.codeInput.text == "", "Logout failed.");
            checks++;

            fixture = new Fixture(); fixture.Demo.ExistingPet = true;
            flow.ConfigureForVerification(fixture, new MemoryTokenStore(), () => clock);
            flow.emailInput.text = "existing@example.com";
            flow.SendCode(); await Until(() => !flow.Busy && flow.ScreenState == LoginScreen.Code);
            flow.codeInput.text = "123456"; flow.VerifyCode(); await Until(() => !flow.Busy);
            Require(flow.ScreenState == LoginScreen.NeedsStateCheck && flow.LastPets.Length == 1,
                "Existing pet bypassed missing confirmation state.");
            checks++;

            fixture = new Fixture { FailPets = true };
            flow.ConfigureForVerification(fixture, new MemoryTokenStore(), () => clock);
            flow.emailInput.text = "retry@example.com";
            flow.SendCode(); await Until(() => !flow.Busy && flow.ScreenState == LoginScreen.Code);
            flow.codeInput.text = "123456"; flow.VerifyCode(); await Until(() => !flow.Busy);
            Require(flow.ScreenState == LoginScreen.Retry && flow.Session.IsAuthenticated, "Pet lookup failure discarded session.");
            fixture.FailPets = false; flow.Retry(); await Until(() => !flow.Busy);
            Require(flow.ScreenState == LoginScreen.NeedsRegistration, "Pet lookup retry failed.");
            checks++;

            fixture = new Fixture { PendingOtp = new TaskCompletionSource<HttpReply>() };
            flow.ConfigureForVerification(fixture, new MemoryTokenStore(), () => clock);
            flow.emailInput.text = "cancel@example.com";
            flow.SendCode(); Require(flow.Busy, "OTP request not started.");
            flow.ChangeEmail();
            fixture.PendingOtp.SetResult(new HttpReply { Status = 200, Body = "{}" });
            await Frames(5);
            Require(flow.ScreenState == LoginScreen.Email && !flow.Busy, "Late OTP response changed the new screen.");
            checks++;
            flow.ConfigureForVerification(new Fixture(), new MemoryTokenStore());
            flow.emailInput.text = "";
            return new { success = true, checks, actualPointerClick = true, registrationEvents,
                frame = Time.frameCount, externalEmailSent = false };
        }
        finally
        {
            if (generation != null) generation.enabled = generationEnabled;
            flow.RegistrationRequested.RemoveListener(registration);
            InputSystem.RemoveDevice(mouse);
            InputSystem.settings = original;
            UnityEngine.Object.Destroy(temporary);
        }
    }
}
