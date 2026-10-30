using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Withme.Auth;

namespace Withme.Registration
{
    public sealed class UploadCoordinator
    {
        readonly IRegistrationApi api;
        readonly IFileUploader uploader;
        public readonly List<PhotoDraft> Photos = new List<PhotoDraft>();
        public Pet Pet { get; private set; }
        public bool Busy { get; private set; }
        public Action Changed;
        public bool Ready => !Busy && Pet != null && Photos.Count > 0 && Photos.All(p => p.Complete);
        public string[] MediaIds => Ready ? Photos.Select(p => p.MediaId).ToArray() : Array.Empty<string>();
        public UploadCoordinator(IRegistrationApi api, IFileUploader uploader) { this.api = api; this.uploader = uploader; }

        async Task EnsurePet(string name, CancellationToken cancellation)
        {
            if (Pet != null)
            {
                if (Pet.Name != name) throw new ApiFailure("PET_EXISTS", 409);
                return;
            }
            var existing = await api.ListPetsAsync(cancellation);
            if (existing.Length > 1) throw new ApiFailure("INVALID_RESPONSE", 0);
            if (existing.Length == 1) Pet = existing[0];
            else
            {
                try { Pet = await api.CreatePetAsync(name, cancellation); }
                catch (ApiFailure failure) when (failure.Status == 409 || failure.Transient || failure.Code == "AUTH_RETRY")
                {
                    var after = await api.ListPetsAsync(cancellation);
                    if (after.Length != 1) throw;
                    Pet = after[0];
                }
            }
            if (Pet == null || !Guid.TryParse(Pet.Id, out _)) throw new ApiFailure("INVALID_RESPONSE", 0);
            if (Pet.Name != name) throw new ApiFailure("PET_EXISTS", 409);
        }
        public async Task RunAsync(string name, CancellationToken cancellation)
        {
            if (Busy) return;
            name = RegistrationRules.Name(name);
            if (Photos.Count < 1 || Photos.Count > RegistrationRules.MaxPhotos) throw new ApiFailure("PHOTO_LIMIT", 0);
            foreach (var photo in Photos) if (!photo.Complete) RegistrationRules.Photo(photo);
            Busy = true; Changed?.Invoke();
            try
            {
                await EnsurePet(name, cancellation);
                foreach (var photo in Photos)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (photo.Complete) continue;
                    try { await UploadOne(photo, cancellation); }
                    catch (OperationCanceledException) { photo.Stage = PhotoStage.Cancelled; throw; }
                    catch (ApiFailure failure)
                    {
                        photo.ErrorCode = failure.Code; photo.Stage = PhotoStage.Failed;
                        if (failure.Code == "SESSION_EXPIRED" || failure.Code == "AUTH_RETRY") throw;
                    }
                    catch (System.IO.IOException) { photo.ErrorCode = "PHOTO_MISSING"; photo.Stage = PhotoStage.Failed; }
                    Changed?.Invoke();
                }
            }
            finally { Busy = false; Changed?.Invoke(); }
        }
        async Task UploadOne(PhotoDraft photo, CancellationToken cancellation)
        {
            photo.ErrorCode = null; photo.Stage = PhotoStage.Preparing; Changed?.Invoke();
            if (!string.IsNullOrEmpty(photo.MediaId))
            {
                var record = await api.MediaAsync(photo.MediaId, cancellation);
                if (AcceptRecord(photo, record)) return;
            }
            if (photo.Ticket == null && string.IsNullOrEmpty(photo.MediaId))
            {
                photo.Ticket = await api.TicketAsync(Pet.Id, photo, cancellation);
                UnityFileUploader.ValidateTicket(photo.Ticket);
                photo.MediaId = photo.Ticket.MediaId;
                photo.TicketReceivedAt = DateTimeOffset.UtcNow;
            }
            if (!photo.BytesSent)
            {
                if (photo.Ticket == null || DateTimeOffset.UtcNow - photo.TicketReceivedAt > TimeSpan.FromHours(2))
                    throw new ApiFailure("UPLOAD_EXPIRED", 0);
                photo.Stage = PhotoStage.Uploading; Changed?.Invoke();
                await uploader.UploadAsync(photo.Ticket, photo, new ProgressCallback(v => { photo.Progress = v; Changed?.Invoke(); }), cancellation);
                photo.BytesSent = true;
            }
            photo.Stage = PhotoStage.Finalizing; Changed?.Invoke();
            if (photo.FinalizeUncertain || !string.IsNullOrEmpty(photo.JobId)) throw new ApiFailure("FINALIZE_PENDING", 0);
            try
            {
                var job = await api.FinalizeAsync(photo.MediaId, cancellation);
                if (string.IsNullOrWhiteSpace(job?.JobId)) { photo.FinalizeUncertain = true; throw new ApiFailure("INVALID_RESPONSE", 0); }
                photo.JobId = job.JobId;
            }
            catch (OperationCanceledException) { photo.FinalizeUncertain = true; throw; }
            catch (ApiFailure failure) when (failure.Transient) { photo.FinalizeUncertain = true; throw; }
            if (!AcceptRecord(photo, await api.MediaAsync(photo.MediaId, cancellation))) throw new ApiFailure("UPLOAD_FAILED", 0);
        }
        bool AcceptRecord(PhotoDraft photo, MediaRecord record)
        {
            if (record == null || record.Id != photo.MediaId || record.PetId != Pet.Id) throw new ApiFailure("INVALID_RESPONSE", 0);
            photo.RemoteStatus = record.Status;
            if (record.Status == "rejected") throw new ApiFailure("MEDIA_REJECTED", 0);
            if (record.Status != "ready" && record.Status != "uploaded") return false;
            photo.Progress = 1; photo.Stage = PhotoStage.Complete; photo.Ticket = null;
            return true;
        }
        sealed class ProgressCallback : IProgress<float>
        {
            readonly Action<float> update;
            public ProgressCallback(Action<float> update) => this.update = update;
            public void Report(float value) => update(value);
        }
    }
}
