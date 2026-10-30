using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Withme.Auth;
using Withme.Networking;

public static class VerifyAuth
{
    static readonly CancellationToken None = CancellationToken.None;
    const string User = "11111111-1111-4111-8111-111111111111";
    static AuthSession Session(string access = "test-access", string refresh = "test-refresh") => new AuthSession
        { AccessToken = access, RefreshToken = refresh, ExpiresAt = 10000, User = new AuthUser { Id = User } };

    sealed class Transport : IHttpTransport
    {
        public readonly List<HttpCall> Calls = new List<HttpCall>();
        public Func<HttpCall, CancellationToken, Task<HttpReply>> Reply;
        public Task<HttpReply> SendAsync(HttpCall call, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls.Add(call); return Reply(call, token); }
    }
    sealed class Store : ITokenStore
    {
        readonly MemoryTokenStore inner = new MemoryTokenStore();
        public bool FailSave;
        public AuthSession Read() => inner.Read();
        public void Save(AuthSession value) { if (FailSave) throw new Exception("simulated"); inner.Save(value); }
        public void Delete() => inner.Delete();
    }
    sealed class Api : IAuthApi
    {
        public int RefreshCalls, SendCalls;
        public Func<Task<AuthSession>> Refresh;
        public Task SendCodeAsync(string email, CancellationToken token) { SendCalls++; return Task.CompletedTask; }
        public Task<AuthSession> VerifyAsync(string email, string code, CancellationToken token) => Task.FromResult(Session());
        public Task<AuthSession> RefreshAsync(string token, CancellationToken cancellation)
        { RefreshCalls++; return Refresh == null ? Task.FromResult(Session("new-access", "new-refresh")) : Refresh(); }
        public Task LogoutAsync(string token, CancellationToken cancellation) => Task.CompletedTask;
    }
    static HttpReply Reply(int status, string body) => new HttpReply { Status = status, Body = body };
    static JsonHttpClient Client(Transport transport, bool auth = false) => new JsonHttpClient(
        auth ? "https://auth.example/auth/v1" : "https://api.example", transport, 20, "sb_publishable_test", auth);
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static async Task Failure(Func<Task> action, string code)
    {
        try { await action(); }
        catch (ApiFailure ex) { Require(ex.Code == code, "Wrong error category."); return; }
        throw new InvalidOperationException("Expected failure was not raised.");
    }
    static async Task Cancelled(Task task)
    {
        try { await task; } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected cancellation was not raised.");
    }

    public static async Task<object> Run()
    {
        var passed = new List<string>();
        var transport = new Transport { Reply = (call, token) => Task.FromResult(Reply(200, "{}")) };
        var authHttp = Client(transport, true);
        await new SupabaseAuthApi(authHttp).SendCodeAsync("test@example.com", None);
        var otp = transport.Calls.Single();
        Require((bool)JObject.Parse(otp.Json)["create_user"] == false && otp.Headers["apikey"] == "sb_publishable_test"
            && !otp.Headers.ContainsKey("Authorization") && otp.Url.EndsWith("/auth/v1/otp"), "OTP contract mismatch.");
        passed.Add("OTP route / create_user:false / header separation");

        var calls = transport.Calls.Count;
        await Failure(() => authHttp.SendAsync<Pet[]>("GET", "https://other.example/pets", null, "sensitive", None), "CONFIGURATION");
        await Failure(() => authHttp.SendAsync<Pet[]>("GET", "../pets", null, "sensitive", None), "CONFIGURATION");
        Require(transport.Calls.Count == calls, "Token could leave configured origin.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Cancelled(Client(transport).SendAsync<Pet[]>("GET", "pets", null, null, cancelled.Token));
        }
        Require(transport.Calls.Count == calls, "Cancelled call was sent.");
        passed.Add("external host / base path escape / pre-cancel rejection");

        foreach (var body in new[] { "not-json", "", "{\"error\":17}", "{\"error\":{\"code\":{}}}" })
        {
            transport.Reply = (call, token) => Task.FromResult(Reply(502, body));
            await Failure(() => Client(transport).SendAsync<Pet[]>("GET", "pets", null, null, None), "HTTP_ERROR");
        }
        transport.Reply = (call, token) => Task.FromResult(Reply(200, ""));
        await Failure(() => Client(transport).SendAsync<Pet[]>("GET", "pets", null, null, None), "INVALID_RESPONSE");
        transport.Reply = (call, token) => Task.FromResult(Reply(204, ""));
        await Client(transport).SendAsync<NoContent>("POST", "logout", null, null, None);
        transport.Reply = (call, token) => Task.FromResult(new HttpReply { NetworkError = true });
        await Failure(() => Client(transport).SendAsync<Pet[]>("GET", "pets", null, null, None), "NETWORK");
        transport.Reply = (call, token) => Task.FromException<HttpReply>(new ApiFailure("TIMEOUT", 0));
        await Failure(() => Client(transport).SendAsync<Pet[]>("GET", "pets", null, null, None), "TIMEOUT");
        passed.Add("malformed / empty / no-content / network / timeout responses");

        var api = new Api(); var store = new Store(); long now = 1000;
        using (var auth = new AuthService(api, store, () => now))
        {
            await auth.SendCodeAsync("test@example.com", 60, None);
            await Failure(() => auth.SendCodeAsync("test@example.com", 60, None), "RESEND_WAIT");
            Require(api.SendCalls == 1 && auth.ResendWait("test@example.com") == 60, "Duplicate email request.");
            now += 60; await auth.SendCodeAsync("test@example.com", 60, None);
            await Failure(() => auth.VerifyAsync("test@example.com", "12345", None), "CODE_INVALID");
            await auth.VerifyAsync("test@example.com", "123456", None);
            Require(auth.UserId == User && store.Read().RefreshToken == "test-refresh", "Login not stored.");
            var gate = new TaskCompletionSource<AuthSession>(); api.Refresh = () => gate.Task;
            using (var cancellation = new CancellationTokenSource())
            {
                var a = auth.RefreshAsync("test-access", cancellation.Token);
                var b = auth.RefreshAsync("test-access", None);
                Require(api.RefreshCalls == 1, "Concurrent refresh was duplicated.");
                cancellation.Cancel(); await Cancelled(a);
                gate.SetResult(Session("new-access", "new-refresh"));
                await b;
            }
            await auth.RefreshAsync("test-access", None);
            Require(api.RefreshCalls == 1 && store.Read().RefreshToken == "new-refresh", "Stale 401 or token rotation failed.");
            passed.Add("resend cooldown / code validation / single-flight refresh / isolated cancellation / token rotation");

            gate = new TaskCompletionSource<AuthSession>(); api.Refresh = () => gate.Task;
            var late = auth.RefreshAsync("new-access", None);
            await auth.LogoutAsync(None);
            gate.SetResult(Session("late-access", "late-refresh"));
            await Cancelled(late);
            Require(!auth.IsAuthenticated && store.Read() == null, "Late refresh resurrected a logged-out session.");
            passed.Add("logout versus late refresh race");
        }

        foreach (var error in new[] { new ApiFailure("NETWORK", 0), new ApiFailure("validation_failed", 400) })
        {
            api = new Api { Refresh = () => Task.FromException<AuthSession>(error) }; store = new Store(); store.Save(Session());
            using (var auth = new AuthService(api, store, () => 1000))
            {
                await Failure(() => auth.RestoreAsync(None), error.Code);
                Require(error.Transient ? store.Read() != null && auth.IsAuthenticated : store.Read() == null && !auth.IsAuthenticated,
                    "Transient and invalid token handling were not separated.");
            }
        }
        passed.Add("restore: network retains credentials / invalid refresh clears credentials");
        store = new Store { FailSave = true };
        using (var auth = new AuthService(new Api(), store, () => 1000))
        {
            await Failure(() => auth.VerifyAsync("test@example.com", "123456", None), "STORAGE");
            Require(!auth.IsAuthenticated, "Storage failure reported login success.");
        }
        passed.Add("save failure cannot report authenticated");

        api = new Api(); store = new Store();
        using (var auth = new AuthService(api, store, () => 1000))
        {
            await auth.VerifyAsync("test@example.com", "123456", None);
            var attempt = 0;
            transport = new Transport { Reply = (call, token) =>
            {
                attempt++;
                if (attempt == 1) Require(call.Headers["Authorization"] == "Bearer test-access", "Missing bearer.");
                else Require(call.Headers["Authorization"] == "Bearer new-access", "Replayed old bearer.");
                return Task.FromResult(Reply(401, "{\"error\":{\"code\":\"UNAUTHORIZED\"}}"));
            } };
            await Failure(() => new PetClient(Client(transport), auth).ListAsync(None), "SESSION_EXPIRED");
            Require(attempt == 2 && api.RefreshCalls == 1, "401 retry loop.");
            Require(!auth.IsAuthenticated && store.Read() == null, "Repeated 401 retained a rejected session.");
        }
        passed.Add("pets Bearer / exactly one refresh and one replay");
        Require(!AuthService.ValidCode("１２３４５６") && !AuthService.ValidEmail("bad@") && AuthService.ValidCode("123456"), "Input validation failed.");
        Require(!AuthMessages.For(new ApiFailure("otp_expired", 403)).Contains("Withme"), "Raw error displayed.");
        return new { success = true, groups = passed.Count, passed, externalEmailSent = false };
    }
}
