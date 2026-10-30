#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Withme.Auth;

namespace Withme.Registration
{
    // Local fixture only: does not create pets, send photos or call generation services.
    public sealed class DemoRegistration : IRegistrationApi, IFileUploader
    {
        Pet pet;
        readonly Dictionary<string, MediaRecord> media = new Dictionary<string, MediaRecord>();
        public DemoRegistration(Pet existing = null) => pet = existing;
        public Task<Pet[]> ListPetsAsync(CancellationToken cancellation) => Task.FromResult(pet == null ? Array.Empty<Pet>() : new[] { pet });
        public Task<Pet> CreatePetAsync(string name, CancellationToken cancellation)
        { pet = new Pet { Id = Guid.NewGuid().ToString(), Name = name }; return Task.FromResult(pet); }
        public Task<UploadTicket> TicketAsync(string petId, PhotoDraft photo, CancellationToken cancellation)
        {
            var id = Guid.NewGuid().ToString();
            media[id] = new MediaRecord { Id = id, PetId = petId, Status = "pending" };
            return Task.FromResult(new UploadTicket { MediaId = id, UploadUrl = "https://fixture.invalid/upload",
                Headers = new Dictionary<string, string> { ["Content-Type"] = photo.MimeType } });
        }
        public Task<UploadJob> FinalizeAsync(string mediaId, CancellationToken cancellation)
        { media[mediaId].Status = "uploaded"; return Task.FromResult(new UploadJob { JobId = "demo-" + mediaId }); }
        public Task<MediaRecord> MediaAsync(string mediaId, CancellationToken cancellation) => Task.FromResult(media[mediaId]);
        public async Task UploadAsync(UploadTicket ticket, PhotoDraft photo, IProgress<float> progress, CancellationToken cancellation)
        {
            for (var i = 1; i <= 8; i++) { await Task.Delay(60, cancellation); progress?.Report(i / 8f); }
        }
    }
}
#endif
