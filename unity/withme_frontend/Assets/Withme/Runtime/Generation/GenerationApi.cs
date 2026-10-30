using System;
using System.Threading;
using System.Threading.Tasks;
using Withme.Auth;
using Withme.Networking;

namespace Withme.Generation
{
    public interface IGenerationApi
    {
        Task<AcceptedJob> SubmitAsync(GenerationSession session, CancellationToken cancellation);
        Task<GenerationJob> JobAsync(string id, CancellationToken cancellation);
        Task<string> PhotoUrlAsync(string id, CancellationToken cancellation);
    }
    public sealed class GenerationApi : IGenerationApi
    {
        readonly JsonHttpClient http;
        readonly AuthService auth;
        public GenerationApi(JsonHttpClient http, AuthService auth) { this.http = http; this.auth = auth; }
        static string Id(string id)
        {
            // Mock IDs are strings, not necessarily UUIDs. Never allow a route/path escape.
            if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9_-]+$")) throw new ApiFailure("INVALID_RESPONSE", 0);
            return id;
        }
        async Task<T> Send<T>(string method, string path, object body, string key, CancellationToken cancellation)
        {
            var token = await auth.AccessTokenAsync(cancellation);
            try { return await http.SendAsync<T>(method, path, body, token, cancellation, key); }
            catch (ApiFailure e) when (e.Status == 401)
            {
                var renewed = await auth.RefreshAsync(token, cancellation);
                // All generation submissions have documented server deduplication. Keep their original identity/key.
                try { return await http.SendAsync<T>(method, path, body, renewed.AccessToken, cancellation, key); }
                catch (ApiFailure again) when (again.Status == 401) { auth.Invalidate(renewed.AccessToken); throw new ApiFailure("SESSION_EXPIRED", 401); }
            }
        }
        public Task<AcceptedJob> SubmitAsync(GenerationSession s, CancellationToken c)
        {
            if (s.PendingAction == "create") return Send<AcceptedJob>("POST", "pets/" + Id(s.PetId) + "/reference-views", new { sourceMediaIds = s.MediaIds }, s.PendingKey, c);
            var action = s.PendingAction == "retry" ? "retry" : s.PendingAction == "approve" ? "approve" : throw new ApiFailure("CHECKPOINT_INVALID", 0);
            return Send<AcceptedJob>("POST", "reference-views/" + Id(s.PendingReferenceId) + "/" + action, null, action == "approve" ? s.PendingKey : null, c);
        }
        public Task<GenerationJob> JobAsync(string id, CancellationToken c) => Send<GenerationJob>("GET", "jobs/" + Id(id), null, null, c);
        public async Task<string> PhotoUrlAsync(string id, CancellationToken c) => (await Send<PhotoResult>("GET", "media/" + Id(id), null, null, c)).Url;
        sealed class PhotoResult { [Newtonsoft.Json.JsonProperty("url")] public string Url; }
    }
    public sealed class GenerationCoordinator
    {
        readonly IGenerationApi api;
        readonly IGenerationStore store;
        public GenerationSession Session { get; }
        public GenerationCoordinator(IGenerationApi api, IGenerationStore store, GenerationSession session) { this.api = api; this.store = store; Session = session; }
        public void Prepare(string action)
        {
            if (Session.PendingAction != null) throw new ApiFailure("CONFLICT", 409);
            if (action == "retry" && (Session.Stage != "review" || Session.Retries >= 3)) throw new ApiFailure("LIMIT_EXCEEDED", 429);
            if (action == "approve" && Session.Stage != "review") throw new ApiFailure("CONFLICT", 409);
            if (action == "create" && Session.ReferenceJobId != null) throw new ApiFailure("CONFLICT", 409);
            Session.PendingAction = action; Session.PendingReferenceId = Session.ReferenceId; Session.PendingKey = Guid.NewGuid().ToString();
            store.Save(Session); // Persist before any network write.
        }
        public async Task SubmitPendingAsync(CancellationToken cancellation)
        {
            if (Session.PendingAction == null) return;
            store.Save(Session); // A previous disk failure must never be bypassed by Resume.
            AcceptedJob result;
            try { result = await api.SubmitAsync(Session, cancellation); }
            catch (ApiFailure e) when (e.Status == 409 || e.Status == 422 || e.Status == 429)
            {
                if (e.Status == 429 && Session.PendingAction == "retry") Session.Retries = 3;
                Session.PendingAction = null; Session.PendingKey = null; Session.PendingReferenceId = null; store.Save(Session);
                throw;
            }
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(result?.JobId)) throw new ApiFailure("INVALID_RESPONSE", 0);
            if (Session.PendingAction == "approve") { Session.ModelJobId = result.JobId; Session.Stage = "model"; }
            else
            {
                Session.ReferenceJobId = result.JobId; Session.Stage = "reference";
                if (Session.PendingAction == "retry") Session.Retries++;
            }
            Session.PendingAction = null; Session.PendingKey = null; Session.PendingReferenceId = null; store.Save(Session);
        }
        public async Task<GenerationJob> RefreshAsync(CancellationToken cancellation)
        {
            var model = Session.Stage == "model" || Session.Stage == "ready";
            var id = model ? Session.ModelJobId : Session.ReferenceJobId;
            if (string.IsNullOrEmpty(id)) throw new ApiFailure("CHECKPOINT_INVALID", 0);
            var job = await api.JobAsync(id, cancellation); cancellation.ThrowIfCancellationRequested();
            if (job == null || job.Id != id || job.Type != (model ? "meshy_generate" : "reference_views")
                || job.Progress < 0 || job.Progress > 100 || (job.Status != "queued" && job.Status != "running" && job.Status != "succeeded" && job.Status != "failed")) throw new ApiFailure("INVALID_RESPONSE", 0);
            if (job.Status == "succeeded")
            {
                if (job.Result == null) throw new ApiFailure("INVALID_RESPONSE", 0);
                if (model)
                {
                    Session.ModelId = job.Result.Value<string>("characterModelId");
                    if (string.IsNullOrWhiteSpace(Session.ModelId) || string.IsNullOrWhiteSpace(job.Result.Value<string>("glbUrl"))) throw new ApiFailure("INVALID_RESPONSE", 0);
                    Session.Stage = "ready";
                }
                else
                {
                    Session.ReferenceId = job.Result.Value<string>("referenceViewId");
                    var views = job.Result["views"] as Newtonsoft.Json.Linq.JArray;
                    if (string.IsNullOrWhiteSpace(Session.ReferenceId) || views == null || views.Count != 4 || System.Linq.Enumerable.Any(views, v => v.Type != Newtonsoft.Json.Linq.JTokenType.String)) throw new ApiFailure("INVALID_RESPONSE", 0);
                    Session.Stage = "review";
                }
                store.Save(Session);
            }
            return job;
        }
    }
}
