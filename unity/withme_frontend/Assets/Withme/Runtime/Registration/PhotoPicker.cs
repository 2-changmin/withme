using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Withme.Auth;

namespace Withme.Registration
{
    public interface IPhotoPicker
    {
        Task<PhotoDraft> PickAsync(CancellationToken cancellation);
    }
    public sealed class PhotoPicker : IPhotoPicker
    {
        public async Task<PhotoDraft> PickAsync(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = null;
#if UNITY_EDITOR
            path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("강아지 사진 선택", "",
                new[] { "사진", "jpg,jpeg,png,webp,heic" });
#elif UNITY_ANDROID || UNITY_IOS
            if (NativeGallery.IsMediaPickerBusy()) throw new ApiFailure("PHOTO_PICKER", 0);
            var permission = await NativeGallery.RequestPermissionAsync(NativeGallery.PermissionType.Read, NativeGallery.MediaType.Image);
            cancellation.ThrowIfCancellationRequested();
            if (permission != NativeGallery.Permission.Granted) throw new ApiFailure("PHOTO_PERMISSION", 0);
            var selected = new TaskCompletionSource<string>();
            NativeGallery.GetImageFromGallery(p => selected.TrySetResult(p), "강아지 사진 선택", "image/*");
            var cancelled = new TaskCompletionSource<bool>();
            using (cancellation.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(selected.Task, cancelled.Task) != selected.Task) throw new OperationCanceledException(cancellation);
                path = await selected.Task;
            }
#else
            throw new ApiFailure("PHOTO_PICKER", 0);
#endif
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(path)) return null;
            return await ImportAsync(path, cancellation);
        }
        public static async Task<PhotoDraft> ImportAsync(string source, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            PhotoDraft photo = null;
            try
            {
                if (!File.Exists(source)) throw new ApiFailure("PHOTO_MISSING", 0);
                var size = new FileInfo(source).Length;
                if (size <= 0 || size > RegistrationRules.MaxPhotoBytes) throw new ApiFailure("PHOTO_SIZE", 0);
                string mime;
                using (var stream = File.OpenRead(source)) mime = Mime(stream);
                var extension = mime == "image/jpeg" ? ".jpg" : mime == "image/png" ? ".png" : mime == "image/webp" ? ".webp" : ".heic";
                var directory = Path.Combine(Application.temporaryCachePath, "WithmeUploads");
                Directory.CreateDirectory(directory);
                var copy = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
                photo = new PhotoDraft { LocalPath = copy, MimeType = mime, SizeBytes = size,
                    OwnerDirectory = directory, Stage = PhotoStage.Selected };
                await Task.Run(() => File.Copy(source, copy, false), cancellation);
                cancellation.ThrowIfCancellationRequested();
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                photo.Preview = NativeGallery.LoadImageAtPath(copy, 1024, true, false);
#else
                if (mime != "image/png" && mime != "image/jpeg") throw new ApiFailure("PHOTO_FORMAT", 0);
                using (var stream = File.OpenRead(copy)) CheckDimensions(stream, mime);
                photo.Preview = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!photo.Preview.LoadImage(File.ReadAllBytes(copy), false)) throw new ApiFailure("PHOTO_FORMAT", 0);
                if (Math.Max(photo.Preview.width, photo.Preview.height) > 1024)
                {
                    var scale = 1024f / Math.Max(photo.Preview.width, photo.Preview.height);
                    var resized = Resize(photo.Preview, Math.Max(1, (int)(photo.Preview.width * scale)), Math.Max(1, (int)(photo.Preview.height * scale)));
                    UnityEngine.Object.Destroy(photo.Preview); photo.Preview = resized;
                }
                photo.Preview.Apply(false, true);
#endif
                if (photo.Preview == null) throw new ApiFailure("PHOTO_FORMAT", 0);
                RegistrationRules.Photo(photo);
                cancellation.ThrowIfCancellationRequested();
                return photo;
            }
            catch (OperationCanceledException) { photo?.Dispose(); throw; }
            catch (ApiFailure) { photo?.Dispose(); throw; }
            catch (Exception) { photo?.Dispose(); throw new ApiFailure("PHOTO_MISSING", 0); }
        }
        public static string Mime(Stream stream)
        {
            var header = new byte[32]; var length = stream.Read(header, 0, header.Length); stream.Position = 0;
            if (length >= 24 && header[0] == 137 && Encoding.ASCII.GetString(header, 1, 3) == "PNG"
                && header[4] == 13 && header[5] == 10 && header[6] == 26 && header[7] == 10) return "image/png";
            if (length >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255) return "image/jpeg";
            if (length >= 12 && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP") return "image/webp";
            if (length >= 12 && Encoding.ASCII.GetString(header, 4, 4) == "ftyp")
            {
                for (var offset = 8; offset + 4 <= length; offset += 4)
                {
                    if (offset == 12) continue; // minor version, not a compatible brand
                    var brand = Encoding.ASCII.GetString(header, offset, 4);
                    if (brand == "heic" || brand == "heix" || brand == "hevc" || brand == "hevx") return "image/heic";
                }
            }
            throw new ApiFailure("PHOTO_FORMAT", 0);
        }
        static int Byte(Stream stream)
        { var value = stream.ReadByte(); if (value < 0) throw new ApiFailure("PHOTO_FORMAT", 0); return value; }
        static int Big16(Stream stream) => Byte(stream) * 256 + Byte(stream);
        static long Big32(Stream stream) => (long)Big16(stream) * 65536 + Big16(stream);
        static void CheckDimensions(Stream stream, string mime)
        {
            long width = 0, height = 0;
            if (mime == "image/png") { stream.Position = 16; width = Big32(stream); height = Big32(stream); }
            else
            {
                stream.Position = 2;
                while (stream.Position < stream.Length)
                {
                    if (Byte(stream) != 255) throw new ApiFailure("PHOTO_FORMAT", 0);
                    var marker = Byte(stream); while (marker == 255) marker = Byte(stream);
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || marker >= 208 && marker <= 215) continue;
                    var length = Big16(stream);
                    if (length < 2 || stream.Position + length - 2 > stream.Length) throw new ApiFailure("PHOTO_FORMAT", 0);
                    if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                    { Byte(stream); height = Big16(stream); width = Big16(stream); break; }
                    stream.Position += length - 2;
                }
            }
            if (width < 1 || height < 1) throw new ApiFailure("PHOTO_FORMAT", 0);
            // Editor decode safety; native decoders use their downsampled path on devices.
            if (width > 8192 || height > 8192 || width * height > 32000000) throw new ApiFailure("PHOTO_LARGE", 0);
        }
        static Texture2D Resize(Texture2D source, int width, int height)
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(width, height, 0);
            try
            {
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                var result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0); result.Apply(); return result;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
        }
    }
}
