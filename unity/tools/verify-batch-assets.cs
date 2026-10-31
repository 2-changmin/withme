using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Withme.Character;
using Withme.UI;

public static class VerifyBatchAssets
{
    public static object Run()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play first");var errors=new List<string>();var count=0;
        var required=new[]{typeof(RigFlow),typeof(RigJointEditor),typeof(AppearanceFlow),typeof(CharacterSetupFlow),typeof(RoomFlow),typeof(RoomDogController),typeof(CharacterAppearance)};
        var previous=SceneManager.GetActiveScene();
        foreach(var path in new[]{"Assets/Withme/Scenes/Start.unity","Assets/Withme/Scenes/StandardRigValidation.unity"})
        {
            var scene=SceneManager.GetSceneByPath(path);var opened=!scene.isLoaded;if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)errors.Add(path+": missing script "+t.name);
                    foreach(var script in t.GetComponents<MonoBehaviour>())
                    {
                        if(script==null||!required.Contains(script.GetType()))continue;count++;
                        foreach(var field in script.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                        {
                            if(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) && (UnityEngine.Object)field.GetValue(script)==null)errors.Add(script.GetType().Name+"."+field.Name);
                            if(field.FieldType.IsArray&&typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType.GetElementType()))
                            {var values=field.GetValue(script) as Array;if(values==null||values.Cast<UnityEngine.Object>().Any(v=>v==null))errors.Add(script.GetType().Name+"."+field.Name);}
                        }
                    }
                }
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
        var library=AssetDatabase.LoadAssetAtPath<StandardMotionLibrary>("Assets/Withme/Animations/StandardV1/Library.asset");
        if(library==null||library.bones.Length!=33||library.clips.Length!=17||library.clips.Any(c=>c==null||c.legacy))errors.Add("Motion library contract");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Withme/Shaders/CoatPreview.shader");if(shader==null||ShaderUtil.GetShaderMessages(shader).Length!=0)errors.Add("Coat shader diagnostics");
        foreach(var directory in new[]{"Assets/Withme/Runtime/Character","Assets/Withme/Shaders","Assets/Withme/Animations","Assets/StreamingAssets/Validation/StandardRig"})
            foreach(var file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories))if(!file.EndsWith(".meta")&&!File.Exists(file+".meta"))errors.Add("Missing meta "+file);
        var report=new{scenes=2,requiredComponents=count,errors};Directory.CreateDirectory("Temp/batch");File.WriteAllText("Temp/batch/unity-assets.json",JsonConvert.SerializeObject(report,Formatting.Indented));if(errors.Count>0)throw new Exception(string.Join("; ",errors));return report;
    }
}
