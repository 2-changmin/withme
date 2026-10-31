using System;
using System.Threading;
using System.Threading.Tasks;
using Withme.Auth;
using Withme.Generation;
using Withme.Networking;

namespace Withme.Character
{
    public interface IRigApi
    {
        Task<RigJoints> JointsAsync(string model, CancellationToken c);
        Task<AcceptedJob> SubmitAsync(string model, RigJoints joints, CancellationToken c);
        Task<GenerationJob> JobAsync(string id, CancellationToken c);
    }
    public sealed class RigApi : IRigApi
    {
        readonly JsonHttpClient http; readonly AuthService auth;
        public RigApi(JsonHttpClient http, AuthService auth) { this.http = http; this.auth = auth; }
        async Task<T> Send<T>(string method, string path, object body, CancellationToken c)
        {
            var token = await auth.AccessTokenAsync(c);
            try { return await http.SendAsync<T>(method, path, body, token, c); }
            catch (ApiFailure e) when (e.Status == 401)
            {
                var renewed = await auth.RefreshAsync(token, c);
                try { return await http.SendAsync<T>(method, path, body, renewed.AccessToken, c); }
                catch (ApiFailure again) when (again.Status == 401) { auth.Invalidate(renewed.AccessToken); throw new ApiFailure("SESSION_EXPIRED", 401); }
            }
        }
        public Task<RigJoints> JointsAsync(string model, CancellationToken c) => Send<RigJoints>("GET", "character-models/" + RigSession.RouteId(model) + "/joints", null, c);
        public Task<AcceptedJob> SubmitAsync(string model, RigJoints joints, CancellationToken c) { joints.Validate(); return Send<AcceptedJob>("POST", "character-models/" + RigSession.RouteId(model) + "/rig", joints, c); }
        public Task<GenerationJob> JobAsync(string id, CancellationToken c) => Send<GenerationJob>("GET", "jobs/" + RigSession.RouteId(id), null, c);
    }
    public sealed class RigCoordinator
    {
        readonly IRigApi api; readonly IRigStore store; readonly bool fixtureDeduplication;
        public RigSession Session { get; }
        public RigCoordinator(IRigApi api, IRigStore store, RigSession session, bool fixtureDeduplication)
        { this.api = api; this.store = store; Session = session; this.fixtureDeduplication = fixtureDeduplication; }
        public async Task FetchAsync(CancellationToken c)
        {
            var joints = await api.JointsAsync(Session.ModelId, c); c.ThrowIfCancellationRequested();
            if (joints == null) throw new ApiFailure("JOINTS_INVALID", 0);
            joints.Validate(); Session.Joints = joints.Copy(); store.Save(Session);
        }
        public void Prepare(RigJoints joints)
        {
            if (Session.PendingJoints != null) throw new ApiFailure("CONFLICT", 409);
            Session.PendingJoints = joints.Copy(); Session.Attempted = false; store.Save(Session);
        }
        public async Task SubmitPendingAsync(CancellationToken c)
        {
            if (Session.PendingJoints == null) return;
            // Live POST has no documented deduplication yet. A lost response is
            // ambiguous: do not silently launch a second paid/server-side job.
            if (Session.Attempted && !fixtureDeduplication) throw new ApiFailure("REQUEST_UNCERTAIN", 0);
            Session.Attempted = true; store.Save(Session);
            AcceptedJob result;
            try { result = await api.SubmitAsync(Session.ModelId, Session.PendingJoints, c); }
            catch (ApiFailure e) when (e.Status == 409 || e.Status == 422 || e.Status == 429)
            { Session.Attempted = false; store.Save(Session); throw; }
            if (result == null) throw new ApiFailure("INVALID_RESPONSE", 0);
            RigSession.RouteId(result.JobId);
            Session.JobId = result.JobId; Session.Joints = Session.PendingJoints.Copy();
            Session.PendingJoints = null; Session.Attempted = false; store.Save(Session);
            c.ThrowIfCancellationRequested();
        }
        public async Task<GenerationJob> RefreshAsync(CancellationToken c)
        {
            var job = await api.JobAsync(RigSession.RouteId(Session.JobId), c); c.ThrowIfCancellationRequested();
            if (job == null || job.Id != Session.JobId || job.Type != "rig" || job.Progress < 0 || job.Progress > 100
                || (job.Status != "queued" && job.Status != "running" && job.Status != "succeeded" && job.Status != "failed")) throw new ApiFailure("INVALID_RESPONSE", 0);
            if (job.Status == "succeeded" && (job.Result == null || job.Result.Value<string>("characterModelId") != Session.ModelId
                || string.IsNullOrEmpty(job.Result.Value<string>("riggedGlbUrl")))) throw new ApiFailure("INVALID_RESPONSE", 0);
            return job;
        }
    }
}
