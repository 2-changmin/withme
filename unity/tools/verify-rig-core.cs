using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Withme.Auth;
using Withme.Character;
using Withme.Generation;
using Withme.Networking;

public static class VerifyRigCore
{
    const string User = "11111111-1111-4111-8111-111111111111";
    static void Check(bool value,string label) { if (!value) throw new Exception(label); }
    sealed class Store : IRigStore
    {
        public bool fail; public int saves;
        public RigSession Read(string u,string m) => null;
        public void Save(RigSession s) { if (fail) throw new ApiFailure("STORAGE",0); saves++; }
    }
    sealed class Api : IRigApi
    {
        public RigJoints joints; public int posts; public bool lose; public Store store; public GenerationJob job;
        public Task<RigJoints> JointsAsync(string m,CancellationToken c) => Task.FromResult(joints);
        public Task<AcceptedJob> SubmitAsync(string m,RigJoints j,CancellationToken c)
        { Check(store.saves>0,"POST before checkpoint"); posts++; if(lose)throw new ApiFailure("NETWORK",0);return Task.FromResult(new AcceptedJob{JobId="rig-job"}); }
        public Task<GenerationJob> JobAsync(string id,CancellationToken c) => Task.FromResult(job);
    }
    static void Reject(Action action,string label) { try { action(); } catch(ApiFailure) { return; } throw new Exception(label); }
    static async Task RejectAsync(Func<Task> action,string code)
    { try { await action(); } catch(ApiFailure e) { Check(e.Code==code,"Wrong failure "+e.Code);return; }throw new Exception("Expected "+code); }
    static RigJoints Joints() => JsonConvert.DeserializeObject<RigJoints>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Validation/StandardRig/m1_golden.joints.json")));
    static RigSession Session() => new RigSession{UserId=User,ModelId="model",Joints=Joints()};
    public static async Task<object> Run()
    {
        Check(Application.isPlaying,"Play first"); var cases=new List<string>();
        var joints=Joints(); joints.Validate();
        var raw=JObject.Parse(JsonConvert.SerializeObject(joints));raw["joints"]["nose"][0]="0";
        Reject(()=>JsonConvert.DeserializeObject<RigJoints>(raw.ToString()),"String accepted");raw["joints"]["nose"][0]=true;
        Reject(()=>JsonConvert.DeserializeObject<RigJoints>(raw.ToString()),"Bool accepted");
        joints.Points["nose"][0]=float.NaN;Reject(joints.Validate,"NaN accepted"); cases.Add("strict joint JSON");
        joints=Joints(); var copy=joints.Copy(); copy.Points["nose"][0]+=1;Check(copy.Points["nose"][0]!=joints.Points["nose"][0],"Input aliased");
        var xyz=new Vector3(.2f,.7f,-.3f);Check(Vector3.Distance(xyz,RigJoints.ToUnity(RigJoints.FromUnity(xyz)))<1e-7f,"Coordinate roundtrip");cases.Add("copy and coordinate roundtrip");
        var store=new Store{fail=true};var api=new Api{joints=Joints(),store=store};var s=Session();var coordinator=new RigCoordinator(api,store,s,true);
        Reject(()=>coordinator.Prepare(Joints()),"Storage failed open");await RejectAsync(()=>coordinator.SubmitPendingAsync(default),"STORAGE");Check(api.posts==0,"Disk failure posted");cases.Add("disk failure prevents POST");
        store=new Store();api=new Api{joints=Joints(),store=store,lose=true};s=Session();coordinator=new RigCoordinator(api,store,s,false);coordinator.Prepare(Joints());
        await RejectAsync(()=>coordinator.SubmitPendingAsync(default),"NETWORK");await RejectAsync(()=>coordinator.SubmitPendingAsync(default),"REQUEST_UNCERTAIN");Check(api.posts==1,"Live duplicate POST");cases.Add("live lost response never reposts");
        coordinator=new RigCoordinator(api,store,s,true);api.lose=false;await coordinator.SubmitPendingAsync(default);Check(s.PendingJoints==null&&s.JobId=="rig-job"&&api.posts==2,"Fixture resume");cases.Add("fixture resume stores job");
        api.job=new GenerationJob{Id=s.JobId,Type="rig",Status="succeeded",Progress=100,Result=JObject.FromObject(new{characterModelId="other",riggedGlbUrl="https://example.com/rig.glb"})};
        await RejectAsync(async()=>{await coordinator.RefreshAsync(default);},"INVALID_RESPONSE");cases.Add("cross model response rejected");
        var folder=Path.Combine(Application.temporaryCachePath,"WithmeRigCore-"+Guid.NewGuid().ToString("N"));
        try { var disk=new RigStore(folder);disk.Save(s);var restored=disk.Read(User,"model");Check(restored.JobId==s.JobId,"Disk resume");Check(disk.Read("33333333-3333-4333-8333-333333333333","model")==null,"Cross user checkpoint");Reject(()=>s.Validate("33333333-3333-4333-8333-333333333333","model"),"Cross user accepted");cases.Add("atomic checkpoint and user isolation"); }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
        using(var auth=new AuthService(new SupabaseAuthApi(new JsonHttpClient("https://fixture.invalid/auth/v1",new DemoHttpTransport(),20,"sb_publishable_test",true)),new MemoryTokenStore()))
        {
            await auth.VerifyAsync("fixture@example.com","123456",default);
            var http=new JsonHttpClient("http://127.0.0.1:8767",new UnityHttpTransport(),20,allowEditorLoopback:true);
            var generation=new GenerationApi(http,auth);
            var smoke=JObject.Parse(File.ReadAllText("Temp/batch/rig-http-smoke.json"));
            var model=await generation.JobAsync(smoke.Value<string>("modelJob"),default);var modelId=model.Result.Value<string>("characterModelId");
            var rig=new RigApi(http,auth);var points=await rig.JointsAsync(modelId,default);points.Validate();
            var accepted=await rig.SubmitAsync(modelId,points,default);Check(accepted.JobId==smoke.Value<string>("rigJob"),"HTTP fixture duplicate rig");
            var result=await rig.JobAsync(accepted.JobId,default);Check(result.Status=="succeeded"&&result.Result["quality"].Value<bool>("accepted")==false,"Quality bypass");
            var obj=new GameObject("RigHttpVerification");obj.transform.position=new Vector3(3000,0,0);var player=obj.AddComponent<StandardRigPlayback>();
            player.sharedLibrary=UnityEditor.AssetDatabase.LoadAssetAtPath<StandardMotionLibrary>("Assets/Withme/Animations/StandardV1/Library.asset");
            try
            {
                await player.LoadUrlAsync(result.Result.Value<string>("riggedGlbUrl"),true);Check(player.IsReady&&player.ClipCount==17,"HTTP rig load");player.Paused=true;player.Play("Walk",0);player.Sample(.25f);
                var poses=player.Bones.ToDictionary(p=>p.Key,p=>p.Value.localPosition);obj.transform.localScale=Vector3.one*1.7f;
                await player.LoadUrlAsync(result.Result.Value<string>("riggedGlbUrl"),true);Check(player.IsReady,"Scaled HTTP rig reload");player.Play("Walk",0);player.Sample(.25f);
                Check(poses.All(p=>Vector3.Distance(p.Value,player.Bones[p.Key].localPosition)<.00001f),"Root scale changed local pelvis motion");
                cases.Add("Unity HTTP rig, 17 clips, quality preserved, scale invariant pelvis");
                await player.LoadUrlAsync("http://127.0.0.1:8767/assets/golden/front.png",true);Check(!player.IsReady&&player.LastError!=null,"PNG accepted as GLB");
                await player.LoadUrlAsync(result.Result.Value<string>("riggedGlbUrl"),true);Check(player.IsReady,"Cannot recover after invalid GLB");cases.Add("non GLB response rejected and fresh job URL reload recovers");
            }
            finally { UnityEngine.Object.Destroy(obj); }
        }
        var report=new{passed=cases.Count,cases};Directory.CreateDirectory("Temp/batch");File.WriteAllText("Temp/batch/unity-rig-core.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
    }
}
