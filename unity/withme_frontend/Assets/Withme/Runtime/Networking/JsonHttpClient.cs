using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
using Withme.Auth;

namespace Withme.Networking
{
    public sealed class HttpCall
    {
        public string Method, Url, Json;
        public int TimeoutSeconds;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
    }
    public sealed class HttpReply
    {
        public int Status;
        public string Body;
        public bool NetworkError;
    }
    public interface IHttpTransport
    {
        Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellation);
    }

    public sealed class UnityHttpTransport : IHttpTransport
    {
        public async Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            using (var request = new UnityWebRequest(call.Url, call.Method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (call.Json != null) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(call.Json));
                foreach (var header in call.Headers) request.SetRequestHeader(header.Key, header.Value);
                request.timeout = call.TimeoutSeconds;
                // Auth headers must never follow redirects to another origin.
                request.redirectLimit = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellation.IsCancellationRequested) { request.Abort(); cancellation.ThrowIfCancellationRequested(); }
                    if (clock.Elapsed.TotalSeconds > call.TimeoutSeconds)
                    { request.Abort(); throw new ApiFailure("TIMEOUT", 0); }
                    await Task.Yield();
                }
                cancellation.ThrowIfCancellationRequested();
                return new HttpReply { Status = (int)request.responseCode, Body = request.downloadHandler.text,
                    NetworkError = request.result == UnityWebRequest.Result.ConnectionError };
            }
        }
    }

    public sealed class JsonHttpClient
    {
        readonly Uri origin;
        readonly IHttpTransport transport;
        readonly int timeout;
        readonly string publicKey;
        readonly bool auth;
        public JsonHttpClient(string baseUrl, IHttpTransport transport, int timeoutSeconds = 20,
            string publishableKey = null, bool isAuth = false, bool allowEditorLoopback = false)
        {
            if (!Uri.TryCreate((baseUrl ?? "").TrimEnd('/') + "/", UriKind.Absolute, out origin))
                throw new ApiFailure("CONFIGURATION", 0);
            var schemeAllowed = origin.Scheme == "https";
#if UNITY_EDITOR
            schemeAllowed |= allowEditorLoopback && origin.IsLoopback && origin.Scheme == "http";
#endif
            if (!schemeAllowed || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0) throw new ApiFailure("CONFIGURATION", 0);
            this.transport = transport;
            timeout = Math.Max(5, timeoutSeconds);
            publicKey = publishableKey;
            auth = isAuth;
        }

        public async Task<T> SendAsync<T>(string method, string path, object body, string bearer, CancellationToken cancellation, string idempotencyKey = null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Uri.TryCreate(path, UriKind.Absolute, out _) || path.StartsWith("//"))
                throw new ApiFailure("CONFIGURATION", 0);
            var uri = new Uri(origin, path.TrimStart('/'));
            if (uri.Scheme != origin.Scheme || uri.Authority != origin.Authority
                || !uri.AbsolutePath.StartsWith(origin.AbsolutePath, StringComparison.Ordinal))
                throw new ApiFailure("CONFIGURATION", 0);
            var call = new HttpCall { Method = method, Url = uri.AbsoluteUri,
                Json = body == null ? null : JsonConvert.SerializeObject(body), TimeoutSeconds = timeout };
            call.Headers["Accept"] = "application/json";
            if (body != null) call.Headers["Content-Type"] = "application/json";
            if (auth)
            {
                if (string.IsNullOrWhiteSpace(publicKey) || publicKey.StartsWith("sb_secret_", StringComparison.Ordinal)
                    || !publicKey.StartsWith("sb_publishable_", StringComparison.Ordinal))
                    throw new ApiFailure("CONFIGURATION", 0);
                call.Headers["apikey"] = publicKey;
            }
            if (!string.IsNullOrEmpty(bearer)) call.Headers["Authorization"] = "Bearer " + bearer;
            if (idempotencyKey != null)
            {
                if (!Guid.TryParse(idempotencyKey, out _)) throw new ApiFailure("CONFIGURATION", 0);
                call.Headers["Idempotency-Key"] = idempotencyKey;
            }
            var reply = await transport.SendAsync(call, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (reply.NetworkError) throw new ApiFailure("NETWORK", 0);
            if (reply.Status < 200 || reply.Status >= 300)
            {
                string code = null;
                try
                {
                    var json = JObject.Parse(reply.Body ?? "");
                    var codeToken = auth ? json["error_code"] : (json["error"] as JObject)?["code"];
                    if (codeToken?.Type == JTokenType.String) code = codeToken.Value<string>();
                }
                catch (JsonException) { }
                if (string.IsNullOrWhiteSpace(code)) code = reply.Status == 401 ? "UNAUTHORIZED" : "HTTP_ERROR";
                throw new ApiFailure(code, reply.Status);
            }
            if (typeof(T) == typeof(NoContent)) return (T)(object)new NoContent();
            try
            {
                var value = JsonConvert.DeserializeObject<T>(reply.Body ?? "");
                if (value == null) throw new ApiFailure("INVALID_RESPONSE", reply.Status);
                return value;
            }
            catch (JsonException) { throw new ApiFailure("INVALID_RESPONSE", reply.Status); }
        }
    }
    public sealed class NoContent { }
}
