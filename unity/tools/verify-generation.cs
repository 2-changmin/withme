using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Withme.Auth;
using Withme.Generation;

public static class VerifyGeneration
{
    const string User="11111111-1111-4111-8111-111111111111";
    static void Check(bool v,string m) {if(!v)throw new Exception(m);}
    sealed class Api:IGenerationApi
    {
        public int Posts; public bool Lost=true; public string Key; public GenerationJob Job;
        public Task<AcceptedJob> SubmitAsync(GenerationSession s,CancellationToken c)
        {
            Posts++; if(Key==null)Key=s.PendingKey;
            if(Lost){Lost=false;throw new ApiFailure("NETWORK",0);}
            return Task.FromResult(new AcceptedJob{JobId=s.PendingAction=="approve"?"mock-model":"mock-reference-"+s.Retries});
        }
        public Task<GenerationJob> JobAsync(string id,CancellationToken c) {Job.Id=id;return Task.FromResult(Job);}
        public Task<string> PhotoUrlAsync(string id,CancellationToken c)=>Task.FromResult("https://fixture.invalid/photo.png");
    }
    sealed class BrokenStore:IGenerationStore
    {
        public GenerationSession Read(string user)=>null;
        public void Save(GenerationSession s){throw new ApiFailure("STORAGE",0);}
        public void Delete(string user){}
    }
    static async Task Error(Func<Task> action,string code)
    {try{await action();}catch(ApiFailure e){Check(e.Code==code,"Wrong error "+e.Code);return;}throw new Exception("Expected "+code);}
    public static async Task<object> Run()
    {
        var directory=Path.Combine(Application.temporaryCachePath,"WithmeGenerationCore",Guid.NewGuid().ToString("N"));
        var store=new GenerationStore(directory);var api=new Api();
        var session=new GenerationSession{UserId=User,PetId=Guid.NewGuid().ToString(),PetName="보리",MediaIds=new[]{Guid.NewGuid().ToString()}};
        var coordinator=new GenerationCoordinator(api,store,session);
        var blockedApi=new Api();var blocked=new GenerationCoordinator(blockedApi,new BrokenStore(),new GenerationSession{UserId=User,PetId=session.PetId,MediaIds=session.MediaIds});
        await Error(()=>{blocked.Prepare("create");return Task.CompletedTask;},"STORAGE");
        await Error(()=>blocked.SubmitPendingAsync(CancellationToken.None),"STORAGE");Check(blockedApi.Posts==0,"Disk failure bypassed on resume");
        coordinator.Prepare("create");var key=session.PendingKey;
        Check(store.Read(User).PendingKey==key&&api.Posts==0,"Intent not persisted before request");
        await Error(()=>coordinator.SubmitPendingAsync(CancellationToken.None),"NETWORK");
        coordinator=new GenerationCoordinator(api,store,store.Read(User));
        Check(coordinator.Session.PendingKey==key,"Lost response replaced key");await coordinator.SubmitPendingAsync(CancellationToken.None);
        Check(api.Key==key&&coordinator.Session.ReferenceJobId=="mock-reference-0","Resume identity");
        api.Job=new GenerationJob{Type="reference_views",Status="running",Progress=50};
        await coordinator.RefreshAsync(CancellationToken.None);Check(coordinator.Session.Stage=="reference","Premature completion");
        api.Job.Status="succeeded";api.Job.Progress=100;api.Job.Result=JObject.Parse("{\"referenceViewId\":\"mock-view\",\"views\":[\"https://fixture.invalid/1\",\"https://fixture.invalid/2\",\"https://fixture.invalid/3\",\"https://fixture.invalid/4\"],\"problems\":[]}");
        await coordinator.RefreshAsync(CancellationToken.None);Check(coordinator.Session.Stage=="review","Review missing");
        for(var i=0;i<3;i++){coordinator.Prepare("retry");await coordinator.SubmitPendingAsync(CancellationToken.None);await coordinator.RefreshAsync(CancellationToken.None);}
        await Error(()=>{coordinator.Prepare("retry");return Task.CompletedTask;},"LIMIT_EXCEEDED");
        coordinator.Prepare("approve");await coordinator.SubmitPendingAsync(CancellationToken.None);
        api.Job=new GenerationJob{Type="meshy_generate",Status="failed",Progress=20,ErrorCode="LIMIT_EXCEEDED"};
        var failed=await coordinator.RefreshAsync(CancellationToken.None);Check(failed.Status=="failed"&&coordinator.Session.Stage=="model","Failure incorrectly completed");
        var posts=api.Posts;await coordinator.RefreshAsync(CancellationToken.None);Check(api.Posts==posts,"Failure caused new generation");
        api.Job.Status="succeeded";api.Job.Progress=100;api.Job.Result=JObject.Parse("{\"characterModelId\":\"mock-model-result\",\"glbUrl\":\"https://fixture.invalid/signed-secret\"}");
        await coordinator.RefreshAsync(CancellationToken.None);
        Check(store.Read(User).Stage=="ready","Model checkpoint missing");
        var saved=File.ReadAllText(Path.Combine(directory,User+".json"));Check(!saved.Contains("https:")&&!saved.Contains("signed-secret")&&!saved.Contains("access_token"),"Secrets persisted");
        coordinator.Session.Validate(User);
        await Error(()=>{coordinator.Session.Validate(Guid.NewGuid().ToString());return Task.CompletedTask;},"CHECKPOINT_INVALID");
        api.Job.Progress=101;await Error(()=>coordinator.RefreshAsync(CancellationToken.None),"INVALID_RESPONSE");
        await Error(()=>{GenerationAssets.Validate("http://external.invalid/model",true);return Task.CompletedTask;},"INVALID_RESPONSE");
        store.Delete(User);Directory.Delete(directory);
        return new {passed=9,checks=new[]{"disk failure blocks requests including resume","intent before POST","lost response same-key resume","job state validation","3 retry limit","approval only from review","failure never resubmits","owner scoping and URL-free disk checkpoint","asset origin validation"}};
    }
}
