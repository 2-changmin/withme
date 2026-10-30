using UnityEngine;

namespace Withme.Auth
{
    [CreateAssetMenu(menuName = "Withme/Connection Settings")]
    public sealed class ConnectionSettings : ScriptableObject
    {
        public string apiBaseUrl = "https://gomagochi-jeahyun.apps.dauai.org";
        public string authBaseUrl = "https://avxkgpnjevjvdgcnnrbm.supabase.co/auth/v1";
        [Tooltip("Publishable key only. Never use secret/service_role credentials.")]
        public string publishableKey = "";
        [Range(5, 60)] public int timeoutSeconds = 20;
        public int resendSeconds = 60;
    }
}
