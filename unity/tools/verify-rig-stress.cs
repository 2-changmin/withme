using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using Withme.Character;

public static class VerifyRigStress
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Finite(Mesh mesh){Check(mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Nonfinite stressed skin");}
    public static async Task<object> Run()
    {
        var probe=UnityEngine.Object.FindFirstObjectByType<StandardRigProbe>();Check(Application.isPlaying&&probe!=null,"Play standard probe first");
        var end=Time.realtimeSinceStartupAsDouble+30;while(!probe.Ready){if(Time.realtimeSinceStartupAsDouble>end)throw new Exception(probe.status.text);await Task.Yield();}
        var warnings=new List<object>();var bends=0;var appearanceCases=0;
        foreach(var player in probe.players)
        {
            player.Paused=true;player.RestoreBindPose();var skin=player.Model.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();skin.BakeMesh(mesh);
            var initial=mesh.vertices;var local=initial.Select(v=>player.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
            foreach(var pair in player.Bones.Where(p=>p.Key!="root"))
            {
                var rotation=pair.Value.localRotation;pair.Value.localRotation=rotation*Quaternion.AngleAxis(30,Vector3.right);skin.BakeMesh(mesh);Finite(mesh);bends++;
                if(pair.Key.EndsWith(".L")||pair.Key.EndsWith(".R"))
                {
                    var sign=Mathf.Sign(player.transform.InverseTransformPoint(pair.Value.position).x);var worst=0f;var vertices=mesh.vertices;
                    for(var i=0;i<local.Length;i++)if(local[i].y<.12f&&local[i].x*sign<-.03f)worst=Mathf.Max(worst,Vector3.Distance(initial[i],vertices[i]));
                    if(worst>.015f)warnings.Add(new{model=player.fixtureName,bone=pair.Key,oppositeFootDisplacement=worst});
                }
                pair.Value.localRotation=rotation;
            }
            var originalMaterials=skin.sharedMaterials;var appearance=player.gameObject.AddComponent<CharacterAppearance>();appearance.enabled=false;appearance.player=player;appearance.coatShader=UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Withme/Shaders/CoatPreview.shader");appearance.enabled=true;
            try
            {
                appearance.Set(new AppearanceParameters{EarScale=1.1f,EarAngle=6,TailScale=1.1f,LegLength=1.1f,BodyLength=.9f,HeadScale=.9f,HueShift=8,Brightness=.05f});
                foreach(var clip in player.sharedLibrary.clips)
                {player.Play(clip.name,0);player.Sample(clip.length*.37f);skin.BakeMesh(mesh);Finite(mesh);Check(Mathf.Abs(player.Bones["ear_01.L"].localScale.x-1.1f)<1e-5f,"Model base proportions lost");appearanceCases++;}
                appearance.Set(new AppearanceParameters());player.Play("Idle",0);player.Sample(0);Check(skin.sharedMaterials.SequenceEqual(originalMaterials),"Stress cancel didn't restore source materials");
            }
            finally{appearance.enabled=false;UnityEngine.Object.Destroy(appearance);UnityEngine.Object.Destroy(mesh);}
            player.Play("Walk",0);player.Paused=false;
        }
        probe.Play("Walk");var report=new{models=3,singleBoneBends=bends,appearanceClipCases=appearanceCases,warnings,qualityApproved=false};File.WriteAllText("Temp/batch/unity-rig-stress.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
    }
}
