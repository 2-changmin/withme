using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Withme.Character;

public static class VerifyStandardRig
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static float Moved(Vector3[] a,Vector3[] b){var max=0f;for(var i=0;i<a.Length;i++)max=Mathf.Max(max,Vector3.Distance(a[i],b[i]));return max;}
    public static async Task<object> Run()
    {
        var probe=UnityEngine.Object.FindFirstObjectByType<StandardRigProbe>();Check(Application.isPlaying&&probe!=null,"Play standard probe first");
        var end=Time.realtimeSinceStartupAsDouble+20;while(!probe.Ready){if(Time.realtimeSinceStartupAsDouble>end)throw new Exception(probe.status.text);await Task.Yield();}
        var cases=new List<object>();var maxJointError=0f;var maxPositionDrift=0f;var maxLoopAngle=0f;var failures=new List<string>();
        foreach(var player in probe.players)
        {
            player.Paused=true;player.RestoreBindPose();
            var rest=player.Bones.ToDictionary(p=>p.Key,p=>p.Value.localPosition);
            var joints=JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Validation/StandardRig",player.fixtureName+".joints.json")))["joints"];
            foreach(var pair in new[]{("hip_l","thigh.L"),("ear_base_l","ear_01.L"),("tail_base","tail_01")})
            {
                var xyz=(JArray)joints[pair.Item1];var expected=new Vector3(-(float)xyz[0],(float)xyz[1],(float)xyz[2]);
                var actual=player.transform.InverseTransformPoint(player.Bones[pair.Item2].position);
                maxJointError=Mathf.Max(maxJointError,Vector3.Distance(expected,actual));
            }
            var skin=player.Model.GetComponentInChildren<SkinnedMeshRenderer>();
            Check(skin.bones.Length==33&&player.ClipCount==17,"Rig/clip count");
            foreach(var shared in new[]{false,true})
            foreach(var clip in player.sharedLibrary.clips)
            {
                player.useShared=shared;player.Play(clip.name,0);player.Sample(0);
                var mesh=new Mesh();skin.BakeMesh(mesh);var first=mesh.vertices;
                var startRot=player.Bones.ToDictionary(p=>p.Key,p=>p.Value.localRotation);
                player.Sample(clip.length*.37f);skin.BakeMesh(mesh);var movement=Moved(first,mesh.vertices);
                if(clip.name=="Walk"||clip.name=="Trot")Check(movement>.002f,"No skin motion "+player.fixtureName+" "+clip.name);
                foreach(var pair in player.Bones)
                {
                    if(pair.Key!="pelvis")maxPositionDrift=Mathf.Max(maxPositionDrift,Vector3.Distance(rest[pair.Key],pair.Value.localPosition));
                    Check(pair.Value.localScale==Vector3.one,"Scale overwritten");
                }
                Check(mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Non-finite skin");
                if(StandardRigPlayback.Loops.Contains(clip.name))
                {
                    player.Sample(Mathf.Max(0,clip.length-.0001f));
                    foreach(var pair in player.Bones)maxLoopAngle=Mathf.Max(maxLoopAngle,Quaternion.Angle(startRot[pair.Key],pair.Value.localRotation));
                }
                else
                {
                    player.Sample(clip.length);skin.BakeMesh(mesh);var atEnd=mesh.vertices;
                    player.Sample(clip.length+1);skin.BakeMesh(mesh);Check(Moved(atEnd,mesh.vertices)<.00001f,"One-shot looped");
                }
                cases.Add(new{model=player.fixtureName,clip=clip.name,source=shared?"shared":"own",skinMovement=movement});
                UnityEngine.Object.Destroy(mesh);
            }
            player.useShared=true;player.Paused=false;player.Play("Walk",0);
        }
        probe.Play("Walk");
        Check(maxJointError<.001f,"Git/GLB/Unity joint space mismatch: "+maxJointError);
        Check(maxPositionDrift<.00001f,"Common clip overwrote body length");
        Check(maxLoopAngle<1f,"Loop seam rotation: "+maxLoopAngle);
        var report=new{passed=cases.Count,models=3,clips=17,sourceModes=2,maxJointError,maxPositionDrift,maxLoopAngle,cases,qualityApproved=false};
        Directory.CreateDirectory("Temp/batch");File.WriteAllText("Temp/batch/unity-standard-rig.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return new{passed=cases.Count,models=3,clips=17,sourceModes=2,maxJointError,maxPositionDrift,maxLoopAngle,qualityApproved=false};
    }
}
