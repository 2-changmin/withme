using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class CharacterProbeBuildSettings
{
    const string Key = "Withme.CharacterProbe.BuildSettings";
    [Serializable]
    class Snapshot
    {
        public string identifier;
        public int orientation;
        public bool customKeystore;
    }
    public static object Prepare()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building.");
        if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")))
            throw new InvalidOperationException("Restore the prior build settings before preparing again.");
        var snapshot = new Snapshot { identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
            orientation = (int)PlayerSettings.defaultInterfaceOrientation,
            customKeystore = PlayerSettings.Android.useCustomKeystore };
        SessionState.SetString(Key, JsonUtility.ToJson(snapshot));
        // This identifier and debug signature are exclusively for the local verification APK.
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.withme.validation.characterprobe");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.Android.useCustomKeystore = false;
        AssetDatabase.SaveAssets();
        return new { snapshot.identifier, snapshot.orientation, verificationIdentifier = "com.withme.validation.characterprobe" };
    }
    public static object Restore()
    {
        var json = SessionState.GetString(Key, "");
        if (string.IsNullOrEmpty(json)) return "No temporary build settings to restore.";
        var snapshot = JsonUtility.FromJson<Snapshot>(json);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, snapshot.identifier);
        PlayerSettings.defaultInterfaceOrientation = (UIOrientation)snapshot.orientation;
        PlayerSettings.Android.useCustomKeystore = snapshot.customKeystore;
        AssetDatabase.SaveAssets();
        SessionState.EraseString(Key);
        return "Original identifier, orientation and signing settings restored.";
    }
}
