using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Withme.Auth;
using Withme.Networking;
using Withme.Registration;

public static class VerifyRegistration
{
    const string PetId = "22222222-2222-4222-8222-222222222222";
    static readonly CancellationToken None = CancellationToken.None;
    static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    static async Task Error(Func<Task> action, string code)
    {
        try { await action(); } catch (ApiFailure e) { Check(e.Code == code, "Unexpected error: " + e.Code); return; }
        throw new InvalidOperationException("Expected error: " + code);
    }
    sealed class Fixture : IRegistrationApi, IFileUploader
    {
        public Pet Pet;
        public int Creates, Tickets, Puts, Finalizes;
        public bool Conflict, LoseCreate, Reject, FailSecond, FailFinalize, LoseFinalize, CancelPut;
        public readonly Dictionary<string, string> States = new Dictionary<string, string>();
        public Task<Pet[]> ListPetsAsync(CancellationToken c) => Task.FromResult(Pet == null ? new Pet[0] : new[] { Pet });
        public Task<Pet> CreatePetAsync(string name, CancellationToken c)
        {
            Creates++; Pet = new Pet { Id = PetId, Name = name };
            if (Conflict) throw new ApiFailure("CONFLICT", 409);
            if (LoseCreate) throw new ApiFailure("TIMEOUT", 0);
            return Task.FromResult(Pet);
        }
        public Task<UploadTicket> TicketAsync(string id, PhotoDraft p, CancellationToken c)
        {
            Tickets++; var media = Guid.NewGuid().ToString(); States[media] = "pending";
            return Task.FromResult(new UploadTicket { MediaId = media, UploadUrl = "https://storage.invalid/file", Headers = new Dictionary<string, string> { ["Content-Type"] = p.MimeType } });
        }
        public Task UploadAsync(UploadTicket t, PhotoDraft p, IProgress<float> progress, CancellationToken c)
        {
            Puts++; if (CancelPut) throw new OperationCanceledException(c);
            if (FailSecond && Puts == 2) throw new ApiFailure("UPLOAD_FAILED", 503);
            progress.Report(1); return Task.CompletedTask;
        }
        public Task<UploadJob> FinalizeAsync(string id, CancellationToken c)
        {
            Finalizes++;
            if (FailFinalize) { FailFinalize = false; throw new ApiFailure("LIMIT_EXCEEDED", 429); }
            States[id] = Reject ? "rejected" : "uploaded";
            if (LoseFinalize) { LoseFinalize = false; throw new ApiFailure("TIMEOUT", 0); }
            return Task.FromResult(new UploadJob { JobId = Guid.NewGuid().ToString() });
        }
        public Task<MediaRecord> MediaAsync(string id, CancellationToken c) => Task.FromResult(new MediaRecord { Id = id, PetId = PetId, Status = States[id] });
    }
    sealed class Transport : IHttpTransport
    {
        public readonly List<HttpCall> Calls = new List<HttpCall>();
        public Func<HttpCall, HttpReply> Reply;
        public Task<HttpReply> SendAsync(HttpCall call, CancellationToken c) { Calls.Add(call); return Task.FromResult(Reply(call)); }
    }
    sealed class Auth : IAuthApi
    {
        public int Refreshes;
        public Task SendCodeAsync(string e, CancellationToken c) => Task.CompletedTask;
        public Task LogoutAsync(string t, CancellationToken c) => Task.CompletedTask;
        public Task<AuthSession> VerifyAsync(string e, string code, CancellationToken c) => Task.FromResult(Session("initial"));
        public Task<AuthSession> RefreshAsync(string t, CancellationToken c) { Refreshes++; return Task.FromResult(Session("renewed")); }
        static AuthSession Session(string t) => new AuthSession { AccessToken = t, RefreshToken = "refresh", ExpiresAt = 10000, User = new AuthUser { Id = PetId } };
    }
    static UploadCoordinator Coordinator(Fixture fixture, params PhotoDraft[] photos)
    { var c = new UploadCoordinator(fixture, fixture); c.Photos.AddRange(photos); return c; }
    public static async Task<object> Run()
    {
        var passed = new List<string>();
        var source = Path.Combine(Application.temporaryCachePath, "withme-registration-fixture.png");
        var texture = new Texture2D(16, 12); texture.SetPixels(Enumerable.Repeat(new Color(.4f, .6f, .3f), 16 * 12).ToArray()); texture.Apply();
        File.WriteAllBytes(source, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        var drafts = new List<PhotoDraft>();
        Func<Task<PhotoDraft>> photo = async () => { var p = await PhotoPicker.ImportAsync(source, None); drafts.Add(p); return p; };
        try
        {
            Check(RegistrationRules.Name("  보리  ") == "보리", "Name trim");
            Check(RegistrationRules.Name(string.Concat(Enumerable.Repeat("🐕", 30))).Length == 60, "Unicode count");
            foreach (var invalid in new[] { " ", new string('가', 31), "a\nb", "\ud800" })
                await Error(() => { RegistrationRules.Name(invalid); return Task.CompletedTask; }, "NAME_INVALID");
            var first = await photo(); Check(first.Preview.width == 16 && first.LocalPath != source, "Private preview copy");
            var copy = first.LocalPath; first.Dispose(); Check(File.Exists(source) && !File.Exists(copy), "Only owned copy removed");
            using (var bad = new MemoryStream(new byte[] { 0, 1, 2 })) await Error(() => { PhotoPicker.Mime(bad); return Task.CompletedTask; }, "PHOTO_FORMAT");
            passed.Add("name / Unicode / photo content / preview / source preservation");

            foreach (var recover in new[] { 0, 1, 2 })
            {
                var f = new Fixture { Conflict = recover == 1, LoseCreate = recover == 2 }; var c = Coordinator(f, await photo());
                await c.RunAsync("보리", None); Check(c.Ready && f.Creates == 1 && c.MediaIds.Length == 1, "Create recovery");
            }
            var existing = new Fixture { Pet = new Pet { Id = PetId, Name = "보리" } };
            var existingCoordinator = Coordinator(existing, await photo());
            await Error(() => existingCoordinator.RunAsync("다른 이름", None), "PET_EXISTS");
            Check(existing.Creates == 0 && existing.Tickets == 0 && existing.Pet.Name == "보리", "Existing pet overwritten");
            await existingCoordinator.RunAsync("보리", None); Check(existingCoordinator.Ready, "Resume existing pet");
            passed.Add("new pet / conflict / lost create response / existing pet preservation");

            var partial = new Fixture { FailSecond = true }; var partialCoordinator = Coordinator(partial, await photo(), await photo());
            await partialCoordinator.RunAsync("보리", None);
            Check(!partialCoordinator.Ready && partialCoordinator.Photos[0].Complete && partialCoordinator.Photos[1].Stage == PhotoStage.Failed, "Partial failure");
            await partialCoordinator.RunAsync("보리", None);
            Check(partialCoordinator.Ready && partial.Puts == 3 && partial.Tickets == 2 && partial.Finalizes == 2, "Completed photo resent");
            passed.Add("partial failure / per-photo retry / completed uploads retained");

            var final = new Fixture { FailFinalize = true }; var finalCoordinator = Coordinator(final, await photo());
            await finalCoordinator.RunAsync("보리", None); Check(!finalCoordinator.Ready && final.Puts == 1, "Finalize failure");
            await finalCoordinator.RunAsync("보리", None);
            Check(finalCoordinator.Ready && final.Puts == 1 && final.Finalizes == 2, "File repeated after finalize failure");
            var lost = new Fixture { LoseFinalize = true }; var lostCoordinator = Coordinator(lost, await photo());
            await lostCoordinator.RunAsync("보리", None); await lostCoordinator.RunAsync("보리", None);
            Check(lostCoordinator.Ready && lost.Finalizes == 1 && lost.Puts == 1, "Ambiguous finalize repeated");
            passed.Add("finalize rejection resumes without PUT / lost response reconciles with GET");

            var cancelled = new Fixture { CancelPut = true }; var cancelCoordinator = Coordinator(cancelled, await photo());
            try { await cancelCoordinator.RunAsync("보리", None); throw new Exception("Missing cancellation"); } catch (OperationCanceledException) { }
            Check(!cancelCoordinator.Busy && cancelCoordinator.Photos[0].Stage == PhotoStage.Cancelled, "Cancellation state");
            cancelCoordinator.Photos[0].TicketReceivedAt = DateTimeOffset.UtcNow.AddHours(-3);
            cancelled.CancelPut = false; await cancelCoordinator.RunAsync("보리", None);
            Check(cancelled.Puts == 1 && cancelled.Tickets == 1 && cancelCoordinator.Photos[0].ErrorCode == "UPLOAD_EXPIRED", "Expired reservation retried");
            var rejected = new Fixture { Reject = true }; var rejectedCoordinator = Coordinator(rejected, await photo());
            await rejectedCoordinator.RunAsync("보리", None);
            Check(!rejectedCoordinator.Ready && rejectedCoordinator.Photos[0].ErrorCode == "MEDIA_REJECTED", "Rejected media accepted");
            passed.Add("cancel / expiry without new reservation / media rejection");

            var ticket = new UploadTicket { MediaId = Guid.NewGuid().ToString(), UploadUrl = "https://storage.invalid/object", Headers = new Dictionary<string, string>() };
            foreach (var header in new[] { "cookie", "Proxy-Authorization" })
            { ticket.Headers.Clear(); ticket.Headers[header] = "sensitive"; await Error(() => { UnityFileUploader.ValidateTicket(ticket); return Task.CompletedTask; }, "INVALID_RESPONSE"); }
            ticket.Headers.Clear(); ticket.Headers["Authorization"] = "Bearer storage-ticket-only"; UnityFileUploader.ValidateTicket(ticket);
            ticket.Headers.Clear(); ticket.UploadUrl = "http://storage.invalid/object";
            await Error(() => { UnityFileUploader.ValidateTicket(ticket); return Task.CompletedTask; }, "INVALID_RESPONSE");
            passed.Add("signed ticket headers only / cookie-proxy rejection / HTTPS required");

            var authApi = new Auth(); var store = new MemoryTokenStore();
            using (var auth = new AuthService(authApi, store, () => 1000))
            {
                await auth.VerifyAsync("test@example.com", "123456", None);
                var transport = new Transport { Reply = call => new HttpReply { Status = call.Headers["Authorization"] == "Bearer initial" ? 401 : 200, Body = "[]" } };
                var api = new RegistrationApi(new JsonHttpClient("https://api.invalid", transport), auth);
                await api.ListPetsAsync(None);
                Check(transport.Calls.Count == 2 && authApi.Refreshes == 1, "GET refresh bound");
                transport.Calls.Clear(); transport.Reply = call => new HttpReply { Status = 401, Body = "{}" };
                await Error(() => api.CreatePetAsync("보리", None), "AUTH_RETRY");
                Check(transport.Calls.Count == 1, "Write replayed on 401");
                transport.Calls.Clear(); await Error(() => api.ListPetsAsync(None), "SESSION_EXPIRED");
                Check(transport.Calls.Count == 2 && !auth.IsAuthenticated && store.Read() == null, "Persistent 401 session retained");
            }
            passed.Add("GET refresh once / write not replayed / repeated 401 clears session");

            authApi = new Auth();
            using (var auth = new AuthService(authApi, new MemoryTokenStore(), () => 1000))
            {
                await auth.VerifyAsync("test@example.com", "123456", None);
                var transport = new Transport { Reply = call => new HttpReply { Status = 201, Body = "{\"mediaId\":\"" + Guid.NewGuid() + "\",\"uploadUrl\":\"https://storage.invalid/file\",\"headers\":{}}" } };
                await new RegistrationApi(new JsonHttpClient("https://api.invalid", transport), auth).TicketAsync(PetId, await photo(), None);
                var call = transport.Calls.Single(); var json = JObject.Parse(call.Json);
                Check(call.Method == "POST" && call.Url.EndsWith("/pets/" + PetId + "/uploads") && (string)json["mimeType"] == "image/png" && (long)json["sizeBytes"] > 0 && json.Count == 2, "Ticket request contract");
            }
            passed.Add("upload API route / MIME and byte size DTO contract");
            return new { passed = passed.Count, checks = passed, productionWrites = 0 };
        }
        finally { foreach (var p in drafts) p.Dispose(); File.Delete(source); }
    }
}
