using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Withme.Auth;

namespace Withme.Generation
{
    public static class GenerationAssets
    {
        public static Uri Validate(string url, bool fixture)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ApiFailure("INVALID_RESPONSE", 0);
            var allowed = uri.Scheme == "https";
#if UNITY_EDITOR
            allowed |= fixture && uri.IsLoopback && uri.Scheme == "http";
#endif
            if (!allowed || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) throw new ApiFailure("INVALID_RESPONSE", 0);
            return uri;
        }
        public static async Task<byte[]> Bytes(string url, bool fixture, int limit, CancellationToken cancellation)
        {
            Validate(url, fixture); cancellation.ThrowIfCancellationRequested();
            using (var request = UnityWebRequest.Get(url))
            {
                request.redirectLimit = 0; request.timeout = 40;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellation.IsCancellationRequested) { request.Abort(); cancellation.ThrowIfCancellationRequested(); }
                    if (request.downloadedBytes > (ulong)limit) { request.Abort(); throw new ApiFailure("INVALID_RESPONSE", 0); }
                    await Task.Yield();
                }
                cancellation.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success) throw new ApiFailure("ASSET_DOWNLOAD", (int)request.responseCode);
                var bytes = request.downloadHandler.data;
                if (bytes == null || bytes.Length < 1 || bytes.Length > limit) throw new ApiFailure("INVALID_RESPONSE", 0);
                return bytes;
            }
        }
        public static async Task<Texture2D> Image(string url, bool fixture, CancellationToken cancellation)
        {
            var bytes = await Bytes(url, fixture, 12 * 1024 * 1024, cancellation);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!texture.LoadImage(bytes, true)) { UnityEngine.Object.Destroy(texture); throw new ApiFailure("INVALID_RESPONSE", 0); }
            return texture;
        }
    }
}
