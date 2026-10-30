using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using Withme.Auth;

namespace Withme.Registration
{
    public sealed class UploadTicket
    {
        [JsonProperty("mediaId")] public string MediaId;
        [JsonProperty("uploadUrl")] public string UploadUrl;
        [JsonProperty("method")] public string Method = "PUT";
        [JsonProperty("headers")] public Dictionary<string, string> Headers;
    }
    public sealed class MediaRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("petId")] public string PetId;
        [JsonProperty("status")] public string Status;
        [JsonProperty("errorCode")] public string ErrorCode;
    }
    public sealed class UploadJob
    {
        [JsonProperty("jobId")] public string JobId;
    }
    public enum PhotoStage { Selected, Preparing, Uploading, Finalizing, Complete, Failed, Cancelled }

    public sealed class PhotoDraft : IDisposable
    {
        public string LocalPath, MimeType;
        public long SizeBytes;
        public Texture2D Preview;
        public string MediaId, JobId, ErrorCode, RemoteStatus;
        public UploadTicket Ticket;
        public DateTimeOffset TicketReceivedAt;
        public bool BytesSent, FinalizeUncertain;
        public float Progress;
        public PhotoStage Stage;
        public string OwnerDirectory;
        public bool Complete => Stage == PhotoStage.Complete && (RemoteStatus == "uploaded" || RemoteStatus == "ready");
        public void Dispose()
        {
            if (Preview != null) UnityEngine.Object.Destroy(Preview);
            Preview = null;
            // Only copies owned by this draft can be removed; never the selected source photo.
            if (!string.IsNullOrEmpty(LocalPath) && !string.IsNullOrEmpty(OwnerDirectory))
            {
                var root = Path.GetFullPath(OwnerDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(LocalPath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(LocalPath); } catch (IOException) { }
            }
        }
    }
    public static class RegistrationRules
    {
        public const long MaxPhotoBytes = 10L * 1024 * 1024;
        public const int MaxPhotos = 4;
        public static string Name(string value)
        {
            value = (value ?? "").Trim();
            var length = 0;
            for (var i = 0; i < value.Length; i++, length++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[++i])) throw new ApiFailure("NAME_INVALID", 0);
                }
                else if (char.IsLowSurrogate(value[i]) || char.IsControl(value[i])) throw new ApiFailure("NAME_INVALID", 0);
            }
            if (length < 1 || length > 30) throw new ApiFailure("NAME_INVALID", 0);
            return value;
        }
        public static void Photo(PhotoDraft photo)
        {
            if (photo == null || string.IsNullOrEmpty(photo.LocalPath) || !File.Exists(photo.LocalPath))
                throw new ApiFailure("PHOTO_MISSING", 0);
            var actual = new FileInfo(photo.LocalPath).Length;
            if (actual <= 0 || actual > MaxPhotoBytes || actual != photo.SizeBytes) throw new ApiFailure("PHOTO_SIZE", 0);
            if (photo.MimeType != "image/jpeg" && photo.MimeType != "image/png"
                && photo.MimeType != "image/webp" && photo.MimeType != "image/heic") throw new ApiFailure("PHOTO_FORMAT", 0);
        }
        public static string Message(Exception exception)
        {
            switch ((exception as ApiFailure)?.Code)
            {
                case "NAME_INVALID": return "강아지 이름을 1~30자로 입력해 주세요.";
                case "PHOTO_MISSING": return "사진을 다시 선택해 주세요.";
                case "PHOTO_SIZE": return "사진은 한 장에 10MB까지 올릴 수 있어요.";
                case "PHOTO_FORMAT": return "이 사진을 읽지 못했어요. JPG나 PNG 사진을 선택해 주세요.";
                case "PHOTO_LARGE": return "사진 크기를 줄여 다시 선택해 주세요.";
                case "PHOTO_LIMIT": return "이번에는 사진을 1~4장 선택해 주세요.";
                case "PHOTO_PERMISSION": return "사진 접근을 허용하거나 다른 사진을 선택해 주세요.";
                case "PHOTO_PICKER": return "사진 선택기를 열지 못했어요. 다시 시도해 주세요.";
                case "PET_EXISTS": return "이미 등록된 이름이 있어요. 등록 정보를 확인해 주세요.";
                case "UPLOAD_EXPIRED": return "사진 전송 시간이 지났어요. 사진을 다시 선택해 주세요.";
                case "UPLOAD_FAILED": return "사진을 보내지 못했어요. 다시 시도해 주세요.";
                case "MEDIA_REJECTED": return "이 사진을 사용할 수 없어요. 다른 사진을 선택해 주세요.";
                case "FINALIZE_PENDING": return "전송 결과를 확인 중이에요. 잠시 후 다시 확인해 주세요.";
                case "AUTH_RETRY": return "로그인 정보를 확인했어요. 다시 시도해 주세요.";
            }
            return AuthMessages.For(exception);
        }
    }
}
