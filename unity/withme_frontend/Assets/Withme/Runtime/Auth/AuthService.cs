using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Withme.Networking;

namespace Withme.Auth
{
    public interface IAuthApi
    {
        Task SendCodeAsync(string email, CancellationToken cancellation);
        Task<AuthSession> VerifyAsync(string email, string code, CancellationToken cancellation);
        Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellation);
        Task LogoutAsync(string accessToken, CancellationToken cancellation);
    }
    public sealed class SupabaseAuthApi : IAuthApi
    {
        readonly JsonHttpClient http;
        public SupabaseAuthApi(JsonHttpClient http) => this.http = http;
        public async Task SendCodeAsync(string email, CancellationToken cancellation) =>
            await http.SendAsync<NoContent>("POST", "otp", new { email, create_user = false }, null, cancellation);
        public Task<AuthSession> VerifyAsync(string email, string code, CancellationToken cancellation) =>
            http.SendAsync<AuthSession>("POST", "verify", new { type = "email", email, token = code }, null, cancellation);
        public Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellation) =>
            http.SendAsync<AuthSession>("POST", "token?grant_type=refresh_token", new { refresh_token = refreshToken }, null, cancellation);
        public async Task LogoutAsync(string accessToken, CancellationToken cancellation) =>
            await http.SendAsync<NoContent>("POST", "logout", null, accessToken, cancellation);
    }

    public sealed class AuthService : IDisposable
    {
        readonly IAuthApi api;
        readonly ITokenStore store;
        readonly Func<long> now;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly object gate = new object();
        readonly Dictionary<string, long> resendAt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        AuthSession session;
        Task<AuthSession> refresh;
        bool sendingCode;
        int epoch;
        public bool IsAuthenticated { get { lock (gate) return session != null; } }
        public string UserId { get { lock (gate) return session?.User?.Id; } }

        public AuthService(IAuthApi api, ITokenStore store, Func<long> clock = null)
        {
            this.api = api; this.store = store;
            now = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        public int ResendWait(string email)
        {
            lock (gate) return resendAt.TryGetValue(email.Trim(), out var next) ? (int)Math.Max(0, next - now()) : 0;
        }
        public async Task SendCodeAsync(string email, int resendSeconds, CancellationToken cancellation)
        {
            email = email.Trim();
            if (!ValidEmail(email)) throw new ApiFailure("EMAIL_INVALID", 0);
            lock (gate)
            {
                if (sendingCode || ResendWait(email) > 0) throw new ApiFailure("RESEND_WAIT", 429);
                sendingCode = true;
                // A cancelled/lost response can still have sent an email: keep a cooldown.
                resendAt[email] = now() + Math.Max(60, resendSeconds);
            }
            try { await api.SendCodeAsync(email, cancellation); }
            finally { lock (gate) sendingCode = false; }
        }
        public static bool ValidEmail(string email) => !string.IsNullOrWhiteSpace(email) && email.Length <= 254
            && System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
        public static bool ValidCode(string code) => code != null && System.Text.RegularExpressions.Regex.IsMatch(code, @"^[0-9]{6}$");

        public async Task VerifyAsync(string email, string code, CancellationToken cancellation)
        {
            if (!ValidEmail(email)) throw new ApiFailure("EMAIL_INVALID", 0);
            if (!ValidCode(code)) throw new ApiFailure("CODE_INVALID", 0);
            int expected;
            lock (gate) expected = epoch;
            var value = await api.VerifyAsync(email.Trim(), code, cancellation);
            cancellation.ThrowIfCancellationRequested();
            Commit(value, expected);
        }
        public async Task<bool> RestoreAsync(CancellationToken cancellation)
        {
            lock (gate)
            {
                if (session == null)
                {
                    var loaded = store.Read();
                    if (loaded == null) return false;
                    if (string.IsNullOrEmpty(loaded.RefreshToken)) { store.Delete(); return false; }
                    session = loaded;
                }
            }
            await RefreshAsync(null, cancellation);
            return true;
        }
        public async Task<string> AccessTokenAsync(CancellationToken cancellation)
        {
            AuthSession value;
            lock (gate) value = session;
            if (value == null) throw new ApiFailure("SESSION_EXPIRED", 401);
            if (value.ExpiresAt <= now() + 120) value = await RefreshAsync(null, cancellation);
            return value.AccessToken;
        }

        public Task<AuthSession> RefreshAsync(string rejectedAccessToken, CancellationToken cancellation)
        {
            Task<AuthSession> task;
            lock (gate)
            {
                if (session == null) throw new ApiFailure("SESSION_EXPIRED", 401);
                // Another request already refreshed this rejected token.
                if (rejectedAccessToken != null && session.AccessToken != rejectedAccessToken) return Task.FromResult(session);
                if (refresh == null)
                {
                    var completion = new TaskCompletionSource<AuthSession>();
                    refresh = completion.Task;
                    task = refresh;
                    _ = RefreshCoreAsync(session.RefreshToken, epoch, completion);
                }
                else task = refresh;
            }
            return WaitAsync(task, cancellation);
        }
        async Task RefreshCoreAsync(string token, int expected, TaskCompletionSource<AuthSession> completion)
        {
            try
            {
                var value = await api.RefreshAsync(token, lifetime.Token);
                Commit(value, expected);
                completion.TrySetResult(value);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception exception)
            {
                if (exception is ApiFailure failure && !failure.Transient && failure.Status == 400
                    && (failure.Code == "validation_failed" || failure.Code == "refresh_token_not_found"
                        || failure.Code == "refresh_token_already_used" || failure.Code == "invalid_grant"))
                {
                    lock (gate)
                    {
                        if (epoch == expected)
                        {
                            epoch++; session = null;
                            try { store.Delete(); } catch { completion.TrySetException(new ApiFailure("STORAGE", 0)); }
                        }
                    }
                }
                completion.TrySetException(exception);
            }
            finally
            {
                lock (gate) if (ReferenceEquals(refresh, completion.Task)) refresh = null;
            }
        }
        void Commit(AuthSession value, int expected)
        {
            if (value == null) throw new ApiFailure("INVALID_RESPONSE", 0);
            value.Validate(now());
            lock (gate)
            {
                if (epoch != expected || lifetime.IsCancellationRequested) throw new OperationCanceledException();
                try { store.Save(value); }
                catch { throw new ApiFailure("STORAGE", 0); }
                session = value;
            }
        }
        public async Task LogoutAsync(CancellationToken cancellation)
        {
            string access;
            bool storageFailed = false;
            lock (gate)
            {
                access = session?.AccessToken;
                epoch++; session = null; refresh = null;
                try { store.Delete(); } catch { storageFailed = true; }
            }
            if (access != null)
            {
                try { await api.LogoutAsync(access, cancellation); }
                catch (ApiFailure) { } // Local logout is complete even when the network is unavailable.
            }
            if (storageFailed) throw new ApiFailure("STORAGE", 0);
        }
        static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var cancelled = new TaskCompletionSource<bool>();
            using (cancellation.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task) throw new OperationCanceledException(cancellation);
                return await task;
            }
        }
        public void Dispose()
        {
            lock (gate) { epoch++; session = null; lifetime.Cancel(); }
            lifetime.Dispose();
        }
    }

    public sealed class PetClient
    {
        readonly JsonHttpClient http;
        readonly AuthService auth;
        public PetClient(JsonHttpClient http, AuthService auth) { this.http = http; this.auth = auth; }
        public async Task<Pet[]> ListAsync(CancellationToken cancellation)
        {
            var token = await auth.AccessTokenAsync(cancellation);
            try { return await http.SendAsync<Pet[]>("GET", "pets", null, token, cancellation); }
            catch (ApiFailure failure) when (failure.Status == 401)
            {
                var renewed = await auth.RefreshAsync(token, cancellation);
                // Exactly one replay for this read; a second 401 is propagated.
                return await http.SendAsync<Pet[]>("GET", "pets", null, renewed.AccessToken, cancellation);
            }
        }
    }
}
