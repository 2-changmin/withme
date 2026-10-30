using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;
using Withme.Auth;

namespace Withme.Registration
{
    public interface IFileUploader
    {
        Task UploadAsync(UploadTicket ticket, PhotoDraft photo, IProgress<float> progress, CancellationToken cancellation);
    }
    public sealed class UnityFileUploader : IFileUploader
    {
        readonly bool allowEditorLoopback;
        public UnityFileUploader(bool allowEditorLoopback = false) => this.allowEditorLoopback = allowEditorLoopback;
        public static void ValidateTicket(UploadTicket ticket, bool allowEditorLoopback = false)
        {
            if (ticket == null || !Guid.TryParse(ticket.MediaId, out _) || ticket.Headers == null
                || !string.Equals(ticket.Method, "PUT", StringComparison.OrdinalIgnoreCase)
                || !Uri.TryCreate(ticket.UploadUrl, UriKind.Absolute, out var uri)) throw new ApiFailure("INVALID_RESPONSE", 0);
            var validScheme = uri.Scheme == "https";
#if UNITY_EDITOR
            validScheme |= allowEditorLoopback && uri.IsLoopback && uri.Scheme == "http";
#endif
            if (!validScheme || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) throw new ApiFailure("INVALID_RESPONSE", 0);
            foreach (var header in ticket.Headers)
                if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
                    throw new ApiFailure("INVALID_RESPONSE", 0);
        }
        public async Task UploadAsync(UploadTicket ticket, PhotoDraft photo, IProgress<float> progress, CancellationToken cancellation)
        {
            ValidateTicket(ticket, allowEditorLoopback);
            RegistrationRules.Photo(photo);
            cancellation.ThrowIfCancellationRequested();
            using (var request = new UnityWebRequest(ticket.UploadUrl, "PUT"))
            {
                request.uploadHandler = new UploadHandlerFile(photo.LocalPath);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.redirectLimit = 0;
                request.timeout = 120;
                // Only storage credentials explicitly returned in the ticket; no app session is available here.
                foreach (var header in ticket.Headers) request.SetRequestHeader(header.Key, header.Value);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellation.IsCancellationRequested) { request.Abort(); cancellation.ThrowIfCancellationRequested(); }
                    if (timer.Elapsed.TotalSeconds > 120) { request.Abort(); throw new ApiFailure("TIMEOUT", 0); }
                    progress?.Report(Math.Max(0, request.uploadProgress));
                    await Task.Yield();
                }
                cancellation.ThrowIfCancellationRequested();
                if (request.responseCode == 401 || request.responseCode == 403) throw new ApiFailure("UPLOAD_EXPIRED", (int)request.responseCode);
                if (request.result != UnityWebRequest.Result.Success) throw new ApiFailure("UPLOAD_FAILED", (int)request.responseCode);
                progress?.Report(1);
            }
        }
    }
}
