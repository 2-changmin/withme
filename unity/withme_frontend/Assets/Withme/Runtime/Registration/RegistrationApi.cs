using System;
using System.Threading;
using System.Threading.Tasks;
using Withme.Auth;
using Withme.Networking;

namespace Withme.Registration
{
    public interface IRegistrationApi
    {
        Task<Pet[]> ListPetsAsync(CancellationToken cancellation);
        Task<Pet> CreatePetAsync(string name, CancellationToken cancellation);
        Task<UploadTicket> TicketAsync(string petId, PhotoDraft photo, CancellationToken cancellation);
        Task<UploadJob> FinalizeAsync(string mediaId, CancellationToken cancellation);
        Task<MediaRecord> MediaAsync(string mediaId, CancellationToken cancellation);
    }
    public sealed class RegistrationApi : IRegistrationApi
    {
        readonly JsonHttpClient http;
        readonly AuthService auth;
        public RegistrationApi(JsonHttpClient http, AuthService auth) { this.http = http; this.auth = auth; }
        async Task<T> Send<T>(string method, string path, object body, CancellationToken cancellation)
        {
            var token = await auth.AccessTokenAsync(cancellation);
            try { return await http.SendAsync<T>(method, path, body, token, cancellation); }
            catch (ApiFailure failure) when (failure.Status == 401)
            {
                var renewed = await auth.RefreshAsync(token, cancellation);
                // Do not blindly replay reservations/registration/finalize after an ambiguous write.
                if (method != "GET") throw new ApiFailure("AUTH_RETRY", 401);
                try { return await http.SendAsync<T>(method, path, body, renewed.AccessToken, cancellation); }
                catch (ApiFailure retryFailure) when (retryFailure.Status == 401)
                {
                    auth.Invalidate(renewed.AccessToken);
                    throw new ApiFailure("SESSION_EXPIRED", 401);
                }
            }
        }
        static string Id(string value)
        { if (!Guid.TryParse(value, out _)) throw new ApiFailure("INVALID_RESPONSE", 0); return value; }
        public Task<Pet[]> ListPetsAsync(CancellationToken cancellation) => Send<Pet[]>("GET", "pets", null, cancellation);
        public Task<Pet> CreatePetAsync(string name, CancellationToken cancellation) => Send<Pet>("POST", "pets", new { name }, cancellation);
        public Task<UploadTicket> TicketAsync(string petId, PhotoDraft photo, CancellationToken cancellation) =>
            Send<UploadTicket>("POST", "pets/" + Id(petId) + "/uploads", new { mimeType = photo.MimeType, sizeBytes = photo.SizeBytes }, cancellation);
        public Task<UploadJob> FinalizeAsync(string mediaId, CancellationToken cancellation) =>
            Send<UploadJob>("POST", "media/" + Id(mediaId) + "/finalize", null, cancellation);
        public Task<MediaRecord> MediaAsync(string mediaId, CancellationToken cancellation) =>
            Send<MediaRecord>("GET", "media/" + Id(mediaId), null, cancellation);
    }
}
