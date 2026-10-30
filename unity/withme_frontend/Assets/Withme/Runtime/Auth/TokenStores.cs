using System;
using Newtonsoft.Json;
using UnityEngine;

namespace Withme.Auth
{
    public interface ITokenStore
    {
        AuthSession Read();
        void Save(AuthSession session);
        void Delete();
    }
    // Editor storage deliberately does not persist tokens to disk or PlayerPrefs.
    public sealed class MemoryTokenStore : ITokenStore
    {
        string snapshot;
        public AuthSession Read() => snapshot == null ? null : JsonConvert.DeserializeObject<AuthSession>(snapshot);
        public void Save(AuthSession session) => snapshot = JsonConvert.SerializeObject(session);
        public void Delete() => snapshot = null;
    }
    public static class TokenStores
    {
        public static ITokenStore Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidKeystoreTokenStore();
#else
            return new MemoryTokenStore();
#endif
        }
    }
#if UNITY_ANDROID && !UNITY_EDITOR
    public sealed class AndroidKeystoreTokenStore : ITokenStore
    {
        T Call<T>(string method, params object[] args)
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var context = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass("org.withme.auth.SecureSessionStore"))
                {
                    var parameters = new object[args.Length + 1];
                    parameters[0] = context;
                    Array.Copy(args, 0, parameters, 1, args.Length);
                    return bridge.CallStatic<T>(method, parameters);
                }
            }
            catch (AndroidJavaException) { throw new ApiFailure("STORAGE", 0); }
        }
        public AuthSession Read()
        {
            var text = Call<string>("read");
            if (string.IsNullOrEmpty(text)) return null;
            try { return JsonConvert.DeserializeObject<AuthSession>(text); }
            catch (JsonException) { Delete(); return null; }
        }
        public void Save(AuthSession session)
        {
            if (!Call<bool>("save", JsonConvert.SerializeObject(session))) throw new ApiFailure("STORAGE", 0);
        }
        public void Delete()
        {
            if (!Call<bool>("clear")) throw new ApiFailure("STORAGE", 0);
        }
    }
#endif
}
