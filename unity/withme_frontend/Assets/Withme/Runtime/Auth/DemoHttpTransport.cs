#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Withme.Networking;

namespace Withme.Auth
{
    // Opt-in development fixture. Never contacts Supabase or sends an email.
    public sealed class DemoHttpTransport : IHttpTransport
    {
        public bool ExistingPet;
        public async Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellation)
        {
            await Task.Delay(150, cancellation);
            var uri = new Uri(call.Url);
            if (uri.AbsolutePath.EndsWith("/otp")) return new HttpReply { Status = 200, Body = "{}" };
            if (uri.AbsolutePath.EndsWith("/logout")) return new HttpReply { Status = 204, Body = "" };
            if (uri.AbsolutePath.EndsWith("/verify") || uri.AbsolutePath.EndsWith("/token"))
            {
                if (uri.AbsolutePath.EndsWith("/verify") && (string)JObject.Parse(call.Json)["token"] != "123456")
                    return new HttpReply { Status = 403, Body = "{\"error_code\":\"otp_expired\"}" };
                return new HttpReply { Status = 200, Body = JsonConvert.SerializeObject(new AuthSession
                {
                    AccessToken = "demo-access", RefreshToken = "demo-refresh",
                    ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600,
                    User = new AuthUser { Id = "11111111-1111-4111-8111-111111111111" }
                }) };
            }
            if (uri.AbsolutePath.EndsWith("/pets"))
                return new HttpReply { Status = 200, Body = ExistingPet
                    ? "[{\"id\":\"22222222-2222-4222-8222-222222222222\",\"name\":\"보리\",\"createdAt\":\"2026-10-30T00:00:00Z\",\"updatedAt\":\"2026-10-30T00:00:00Z\"}]" : "[]" };
            return new HttpReply { Status = 404, Body = "{\"error\":{\"code\":\"NOT_FOUND\"}}" };
        }
    }
}
#endif
