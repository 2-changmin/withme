using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Withme.Auth;
using Withme.Networking;

public static class VerifyAuthLive
{
    public static async Task<object> Run()
    {
        var settings = AssetDatabase.LoadAssetAtPath<ConnectionSettings>("Assets/Withme/Settings/ConnectionSettings.asset");
        var client = new JsonHttpClient(settings.apiBaseUrl, new UnityHttpTransport());
        var health = await client.SendAsync<JObject>("GET", "health", null, null, CancellationToken.None);
        if ((string)health["status"] != "ok") throw new InvalidOperationException("Server health check failed.");
        var denied = false;
        try { await client.SendAsync<Pet[]>("GET", "pets", null, null, CancellationToken.None); }
        catch (ApiFailure failure) { denied = failure.Status == 401 && failure.Code == "UNAUTHORIZED"; }
        if (!denied) throw new InvalidOperationException("Unauthenticated request was not rejected as documented.");
        return new { health = "ok", unauthorized = denied, sentEmails = 0, authenticatedUserDataRead = false };
    }
}
