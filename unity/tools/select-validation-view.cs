using System;
using System.Reflection;
using UnityEditor;

public static class SelectValidationView
{
    public static object Run()
    {
        var assembly = typeof(Editor).Assembly;
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var sizes = sizesType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
        var group = sizesType.GetProperty("currentGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sizes);
        var customCount = (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null);
        var builtinCount = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null);
        var index = -1;
        for (var i = 0; i < builtinCount + customCount; i++)
        {
            var existing = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
            var type = existing.GetType();
            if ((int)type.GetProperty("width").GetValue(existing) == 390 && (int)type.GetProperty("height").GetValue(existing) == 844)
            { index = i; break; }
        }
        if (index < 0)
        {
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kindType = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, new[] { Enum.Parse(kindType, "FixedResolution"), (object)390, 844, "Withme Portrait" });
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            index = builtinCount + customCount;
        }
        var view = EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
        view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, index);
        view.Repaint();
        return new { width = 390, height = 844, index };
    }
}
