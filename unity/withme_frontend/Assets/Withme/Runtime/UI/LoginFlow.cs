using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Networking;

namespace Withme.UI
{
    public enum LoginScreen { Email, Code, Checking, NeedsRegistration, NeedsStateCheck, Retry }

    public sealed class LoginFlow : MonoBehaviour
    {
        public ConnectionSettings settings;
        [Tooltip("Explicit development fixture, code 123456. No email is sent.")]
        public bool useDemoResponses;
        public GameObject emailPanel, codePanel, resultPanel;
        public TMP_InputField emailInput, codeInput;
        public TMP_Text emailError, codeError, recipientLabel, resultTitle, resultDescription, modeLabel, resendLabel;
        public TMP_Text[] codeDigits;
        public Button sendButton, verifyButton, resendButton, backButton, retryButton, logoutButton;
        public RectTransform safeArea, emailInputRect, codeInputRect;
        public ScrollRect scroll;
        public readonly UnityEvent RegistrationRequested = new UnityEvent();
        public LoginScreen ScreenState { get; private set; }
        public bool Busy { get; private set; }
        public bool UsingDemo { get; private set; }
        public Pet[] LastPets { get; private set; }
        public AuthService Session { get; private set; }

        PetClient pets;
        CancellationTokenSource request;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        string recipient = "";
        int version;
        Rect previousSafe;
        int previousWidth, previousHeight;
        float lastCooldown = -1;

        void Awake()
        {
            emailInput.onValueChanged.AddListener(_ => RefreshControls());
            codeInput.onValueChanged.AddListener(_ => RefreshControls());
            emailInput.onSubmit.AddListener(_ => SendCode());
            codeInput.onSubmit.AddListener(_ => VerifyCode());
            Initialize();
        }
        async void Start()
        {
            if (Session == null) return;
            await RunAsync(async cancellation =>
            {
                if (await Session.RestoreAsync(cancellation)) await ResolveAsync(cancellation);
            });
        }
        void Initialize()
        {
            if (settings == null) { SetResult(LoginScreen.Retry, "잠시 연결할 수 없어요", "로그인 연결을 준비하고 있어요."); return; }
            UsingDemo = false;
            IHttpTransport transport = new UnityHttpTransport();
            var key = settings.publishableKey;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (useDemoResponses)
            {
                UsingDemo = true;
                transport = new DemoHttpTransport();
                key = "sb_publishable_demo";
            }
#endif
            try
            {
                var authHttp = new JsonHttpClient(settings.authBaseUrl, transport, settings.timeoutSeconds, key, true);
                Session = new AuthService(new SupabaseAuthApi(authHttp), TokenStores.Create());
                pets = new PetClient(new JsonHttpClient(settings.apiBaseUrl, transport, settings.timeoutSeconds), Session);
                if (modeLabel != null) modeLabel.text = UsingDemo ? "화면 확인용 · 메일 발송 없음 · 코드 123456" : "";
                Show(LoginScreen.Email);
            }
            catch (ApiFailure) { SetResult(LoginScreen.Retry, "잠시 연결할 수 없어요", "로그인 연결을 준비하고 있어요."); }
        }

        // Only replaces services in the editor/development fixture; production always uses the real transport.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void ConfigureForVerification(IHttpTransport transport, ITokenStore store, Func<long> clock = null)
        {
            CancelCurrent();
            Session?.Dispose();
            LastPets = null;
            Session = new AuthService(new SupabaseAuthApi(new JsonHttpClient(settings.authBaseUrl, transport,
                settings.timeoutSeconds, "sb_publishable_test", true)), store, clock);
            pets = new PetClient(new JsonHttpClient(settings.apiBaseUrl, transport, settings.timeoutSeconds), Session);
            UsingDemo = true;
            modeLabel.text = "화면 확인용 · 메일 발송 없음 · 코드 123456";
            Show(LoginScreen.Email);
        }
#endif
        public async void SendCode()
        {
            if (Busy || ScreenState != LoginScreen.Email || Session == null) return;
            var email = emailInput.text.Trim();
            if (!AuthService.ValidEmail(email)) { emailError.text = AuthMessages.For(new ApiFailure("EMAIL_INVALID", 0)); return; }
            await RunAsync(async cancellation =>
            {
                await Session.SendCodeAsync(email, settings.resendSeconds, cancellation);
                cancellation.ThrowIfCancellationRequested();
                recipient = email;
                codeInput.text = "";
                recipientLabel.text = recipient + "으로 보낸\n6자리 인증 코드를 입력해 주세요.";
                Show(LoginScreen.Code);
                codeInput.ActivateInputField();
            });
        }
        public async void VerifyCode()
        {
            if (Busy || ScreenState != LoginScreen.Code || Session == null) return;
            if (!AuthService.ValidCode(codeInput.text)) { codeError.text = AuthMessages.For(new ApiFailure("CODE_INVALID", 0)); return; }
            var code = codeInput.text;
            await RunAsync(async cancellation =>
            {
                await Session.VerifyAsync(recipient, code, cancellation);
                codeInput.text = "";
                await ResolveAsync(cancellation);
            });
        }
        public async void ResendCode()
        {
            if (Busy || ScreenState != LoginScreen.Code || Session == null) return;
            await RunAsync(async cancellation =>
            {
                await Session.SendCodeAsync(recipient, settings.resendSeconds, cancellation);
                cancellation.ThrowIfCancellationRequested();
                codeInput.text = "";
                codeError.text = "새 코드를 보냈어요. 이메일을 확인해 주세요.";
            });
        }
        public void ChangeEmail()
        {
            CancelCurrent();
            codeInput.text = "";
            recipient = "";
            Show(LoginScreen.Email);
        }
        public async void Retry()
        {
            if (Busy || Session == null) return;
            await RunAsync(async cancellation =>
            {
                if (!Session.IsAuthenticated && !await Session.RestoreAsync(cancellation)) { Show(LoginScreen.Email); return; }
                await ResolveAsync(cancellation);
            });
        }
        public async void Logout()
        {
            if (Session == null) return;
            CancelCurrent();
            LastPets = null;
            await RunAsync(async cancellation =>
            {
                await Session.LogoutAsync(cancellation);
                codeInput.text = "";
                recipient = "";
                Show(LoginScreen.Email);
            });
        }
        async Task ResolveAsync(CancellationToken cancellation)
        {
            SetResult(LoginScreen.Checking, "로그인을 확인했어요", "등록된 정보를 확인하고 있어요.");
            LastPets = await pets.ListAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (LastPets.Length == 0)
            {
                SetResult(LoginScreen.NeedsRegistration, "로그인을 마쳤어요", "강아지의 이름과 사진을 준비해 주세요.");
                RegistrationRequested.Invoke();
            }
            else SetResult(LoginScreen.NeedsStateCheck, "등록된 정보를 확인했어요", "만날 준비 상태를 아직 확인할 수 없어요.\n잠시 후 다시 확인해 주세요.");
        }
        async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (Busy) return;
            var currentVersion = ++version;
            request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var current = request;
            Busy = true;
            emailError.text = codeError.text = "";
            RefreshControls();
            try { await action(current.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (this == null || currentVersion != version) return;
                if (ScreenState == LoginScreen.Email) emailError.text = AuthMessages.For(error);
                else if (ScreenState == LoginScreen.Code) codeError.text = AuthMessages.For(error);
                else if (Session != null && !Session.IsAuthenticated)
                { LastPets = null; Show(LoginScreen.Email); emailError.text = AuthMessages.For(error); }
                else SetResult(LoginScreen.Retry, "잠시 확인하지 못했어요", AuthMessages.For(error));
            }
            finally
            {
                current.Dispose();
                if (this != null && currentVersion == version) { request = null; Busy = false; RefreshControls(); }
            }
        }
        void CancelCurrent()
        {
            version++;
            request?.Cancel();
            request = null;
            Busy = false;
        }
        void Show(LoginScreen screen)
        {
            ScreenState = screen;
            emailPanel.SetActive(screen == LoginScreen.Email);
            codePanel.SetActive(screen == LoginScreen.Code);
            resultPanel.SetActive(screen != LoginScreen.Email && screen != LoginScreen.Code);
            emailError.text = codeError.text = "";
            scroll.verticalNormalizedPosition = 1;
            RefreshControls();
        }
        void SetResult(LoginScreen screen, string title, string description)
        {
            Show(screen);
            resultTitle.text = title;
            resultDescription.text = description;
        }
        void RefreshControls()
        {
            if (sendButton == null || Session == null) return;
            var email = ScreenState == LoginScreen.Code ? recipient : emailInput.text;
            var wait = Session.ResendWait(email);
            sendButton.interactable = !Busy && AuthService.ValidEmail(emailInput.text) && wait == 0;
            sendButton.GetComponentInChildren<TMP_Text>().text = Busy && ScreenState == LoginScreen.Email ? "코드를 요청하고 있어요…"
                : wait > 0 && ScreenState == LoginScreen.Email ? $"인증 코드 받기 · {wait}초 후" : "인증 코드 받기";
            verifyButton.interactable = !Busy && AuthService.ValidCode(codeInput.text);
            verifyButton.GetComponentInChildren<TMP_Text>().text = Busy ? "인증을 확인하고 있어요…" : "인증하고 시작하기";
            resendButton.interactable = !Busy && wait == 0;
            // Address change cancels old requests rather than accepting their late results.
            backButton.interactable = true;
            retryButton.interactable = !Busy;
            logoutButton.interactable = !Busy;
            resendLabel.text = wait > 0 ? $"코드 다시 받기 · {wait}초 후" : "코드 다시 받기";
            if (codeDigits != null)
                for (var i = 0; i < codeDigits.Length; i++) codeDigits[i].text = i < codeInput.text.Length ? codeInput.text[i].ToString() : "";
        }
        void Update()
        {
            if (Session != null && Time.unscaledTime - lastCooldown >= 0.2f) { lastCooldown = Time.unscaledTime; RefreshControls(); }
            if (safeArea != null && (Screen.safeArea != previousSafe || Screen.width != previousWidth || Screen.height != previousHeight))
            {
                previousSafe = Screen.safeArea;
                previousWidth = Screen.width; previousHeight = Screen.height;
                safeArea.anchorMin = new Vector2(previousSafe.xMin / Screen.width, previousSafe.yMin / Screen.height);
                safeArea.anchorMax = new Vector2(previousSafe.xMax / Screen.width, previousSafe.yMax / Screen.height);
                safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            }
            // Scroll the selected field above the mobile keyboard, preserving PC layout.
            if (TouchScreenKeyboard.visible && scroll != null)
            {
                var selected = codeInput.isFocused ? codeInputRect : emailInput.isFocused ? emailInputRect : null;
                if (selected != null)
                {
                    var corners = new Vector3[4]; selected.GetWorldCorners(corners);
                    var bottom = RectTransformUtility.WorldToScreenPoint(null, corners[0]).y;
                    var keyboardTop = TouchScreenKeyboard.area.yMax;
                    if (bottom < keyboardTop + 20) scroll.verticalNormalizedPosition = 0;
                }
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (emailInput.isFocused || codeInput.isFocused) { emailInput.DeactivateInputField(); codeInput.DeactivateInputField(); }
                else if (ScreenState == LoginScreen.Code) ChangeEmail();
            }
        }
        void OnDestroy()
        {
            CancelCurrent(); lifetime.Cancel(); lifetime.Dispose(); Session?.Dispose();
        }
    }
}
