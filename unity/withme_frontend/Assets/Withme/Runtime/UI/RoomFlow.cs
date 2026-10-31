using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Character;
using Withme.Generation;
using Withme.Networking;

namespace Withme.UI
{
    public sealed class RoomFlow:MonoBehaviour
    {
        public CharacterSetupFlow setup;
        public RoomDogController dog;
        public GameObject canvas,mainPanel,menuPanel,photoPanel,restPanel,menuButton,ball,plush;
        public GameObject floor;
        public RectTransform safeArea;
        public RectTransform interactions;
        public RawImage view,photoView;
        public TMP_Text loading,menuTitle,notice,muteLabel,motionLabel,photoLabel,restLabel,photoMessage;
        public Button greetButton,toyButton,restButton,sleepButton;
        public CharacterProfile CurrentProfile{get;private set;}
        public bool Busy{get;private set;}
        public bool VerificationOnly{get;private set;}
        RigFlow Rig=>setup.rig;
        AuthService boundAuth;
        CancellationTokenSource cancellation;
        RenderTexture roomTexture,previousTexture;
        Texture2D photo;
        bool showingPhoto,resumeAfterPause;
        void Start(){canvas.SetActive(false);floor.SetActive(false);setup.RoomRequested.AddListener(FromSetup);}
        void FromSetup(){if(setup.Active!=null)Enter(setup.Active,false);}
        public void Enter(CharacterProfile profile,bool verificationOnly=false)
        {
            if(Busy)return;
#if !UNITY_EDITOR
            if(verificationOnly)throw new InvalidOperationException();
#endif
            if(verificationOnly&&!Rig.generation.Fixture)throw new InvalidOperationException();
            profile.Validate(profile.UserId,profile.PetId);
            if(!profile.Confirmed||!profile.QualityAccepted||!profile.PreferencesFinished)throw new ApiFailure("CONFIRMATION_REQUIRED",0);
            boundAuth=Rig.generation.login.Session;
            if(boundAuth==null||!boundAuth.IsAuthenticated||profile.UserId!=boundAuth.UserId||profile.PetId!=Rig.generation.Coordinator.Session.PetId)throw new ApiFailure("SESSION_EXPIRED",401);
            CurrentProfile=profile.Copy();VerificationOnly=verificationOnly;Busy=true;cancellation=new CancellationTokenSource();_=Load(cancellation);
        }
        async Task Load(CancellationTokenSource current)
        {
            try
            {
                dog.Exit();Rig.canvas.SetActive(false);canvas.SetActive(true);mainPanel.SetActive(true);menuPanel.SetActive(false);photoPanel.SetActive(false);restPanel.SetActive(false);menuButton.SetActive(true);showingPhoto=false;ReleasePhoto();
                loading.text="우리 아이를 만나고 있어요";view.texture=null;Rig.previewCamera.enabled=false;
                var api=new RigApi(Http(),boundAuth);var job=await api.JobAsync(CurrentProfile.RigJobId,current.Token);
                if(job.Type!="rig"||job.Status!="succeeded"||job.Result?.Value<string>("characterModelId")!=CurrentProfile.ModelId)throw new ApiFailure("INVALID_RESPONSE",0);
                var accepted=job.Result["quality"]?.Value<bool?>("accepted")==true&&job.Result["quality"]?.Value<bool?>("requires_visual_review")==false;
                if(!accepted&&!VerificationOnly)throw new ApiFailure("QUALITY_REVIEW_REQUIRED",0);
                await Rig.player.LoadUrlAsync(job.Result.Value<string>("riggedGlbUrl"),Rig.generation.Fixture,current.Token);current.Token.ThrowIfCancellationRequested();
                if(!Rig.player.IsReady)throw new ApiFailure("MODEL_LOAD",0);
                foreach(var t in Rig.player.Model.GetComponentsInChildren<Transform>())t.gameObject.layer=30;
                Rig.appearanceFlow.appearance.Set(CurrentProfile.Appearance);
                ConfigureTexture();Rig.previewCamera.orthographicSize=1.65f;Rig.previewCamera.enabled=true;floor.SetActive(true);
                dog.ApproachGreeting=CurrentProfile.Preferences[0]==0;dog.LiePreferred=CurrentProfile.Preferences[1]==1;dog.ActivePreferred=CurrentProfile.Preferences[3]==1;
                ball.SetActive(CurrentProfile.Preferences[2]==0);plush.SetActive(CurrentProfile.Preferences[2]==1);
                dog.sounds.Muted=PlayerPrefs.GetInt(Key("muted"),0)==1;dog.ReducedMotion=PlayerPrefs.GetInt(Key("reduced"),0)==1;dog.Enter();
                loading.text="";menuTitle.text=CurrentProfile.PetName+"와 함께";notice.text=VerificationOnly?"동작 확인용 · 품질 미승인 모델 · 실제 저장 아님":"사진을 바탕으로 AI가 표현한 모습이에요.";Labels();
            }
            catch(OperationCanceledException){if(this)loading.text="준비를 잠시 멈췄어요. 메뉴에서 다시 확인할 수 있어요.";}
            catch(ApiFailure e){if(this)loading.text=e.Code=="QUALITY_REVIEW_REQUIRED"?"움직임 확인이 아직 필요해요. 메뉴에서 모습을 다시 살펴봐 주세요.":"모습을 불러오지 못했어요. 메뉴에서 다시 확인하거나 사진을 볼 수 있어요.";}
            catch(Exception){if(this)loading.text="모습을 불러오지 못했어요. 메뉴에서 다시 확인해 주세요.";}
            finally{current.Dispose();if(this){Busy=false;cancellation=null;}}
        }
        JsonHttpClient Http()=>new JsonHttpClient(Rig.generation.Fixture?Rig.generation.mockBaseUrl:Rig.generation.login.settings.apiBaseUrl,new UnityHttpTransport(),20,allowEditorLoopback:Rig.generation.Fixture);
        void ConfigureTexture()
        {
            if(previousTexture==null)previousTexture=Rig.previewCamera.targetTexture;
            if(roomTexture!=null){roomTexture.Release();Destroy(roomTexture);}
            var scale=Mathf.Min(1,1080f/Screen.width,1920f/Screen.height);roomTexture=new RenderTexture(Mathf.Max(1,Mathf.RoundToInt(Screen.width*scale)),Mathf.Max(1,Mathf.RoundToInt(Screen.height*scale)),24){name="WithmeRoom"};roomTexture.Create();view.texture=roomTexture;Rig.previewCamera.targetTexture=roomTexture;
        }
        string Key(string item)=>"withme."+(Rig.generation.Fixture?"fixture.":"live.")+CurrentProfile.UserId+"."+CurrentProfile.PetId+"."+item;
        void Labels(){muteLabel.text=dog.sounds.Muted?"소리 켜기":"소리 끄기";motionLabel.text=dog.ReducedMotion?"기본 움직임으로 보기":"움직임 줄이기";photoLabel.text=showingPhoto?"강아지 보기":"사진 보기";}
        public void Greet(){if(!Busy)dog.Greet();}
        public void ThrowToy(){if(!Busy)dog.ThrowToy();}
        public void Rest(){if(!Busy)dog.Rest();}
        public void Sleep(){if(!Busy)dog.Sleep();}
        public void OpenMenu(){if(!canvas.activeSelf||restPanel.activeSelf)return;dog.Suspend(true);menuPanel.SetActive(true);menuButton.SetActive(false);Labels();}
        public void CloseMenu(){menuPanel.SetActive(false);menuButton.SetActive(true);if(!showingPhoto&&!Busy&&dog.State!=RoomState.Exited)dog.Suspend(false);}
        public void ToggleMute(){dog.sounds.Muted=!dog.sounds.Muted;PlayerPrefs.SetInt(Key("muted"),dog.sounds.Muted?1:0);PlayerPrefs.Save();Labels();}
        public void ToggleMotion(){dog.Suspend(true);dog.ReducedMotion=!dog.ReducedMotion;PlayerPrefs.SetInt(Key("reduced"),dog.ReducedMotion?1:0);PlayerPrefs.Save();Labels();}
        public async void TogglePhoto()
        {
            if(Busy||CurrentProfile==null)return;
            if(showingPhoto){showingPhoto=false;photoPanel.SetActive(false);mainPanel.SetActive(true);Rig.previewCamera.enabled=Rig.player.IsReady&&dog.State!=RoomState.Exited;ReleasePhoto();Labels();CloseMenu();return;}
            Busy=true;dog.Suspend(true);menuPanel.SetActive(false);menuButton.SetActive(true);mainPanel.SetActive(false);photoPanel.SetActive(true);photoMessage.text="사진을 불러오고 있어요";Rig.previewCamera.enabled=false;showingPhoto=true;
            var current=new CancellationTokenSource();cancellation=current;
            try
            {
                var auth=boundAuth;var api=new GenerationApi(Http(),auth);var url=await api.PhotoUrlAsync(Rig.generation.Coordinator.Session.MediaIds[0],current.Token);
                var image=await GenerationAssets.Image(url,Rig.generation.Fixture,current.Token);
                if(!this||current.IsCancellationRequested||Rig.generation.login.Session!=auth||!auth.IsAuthenticated){Destroy(image);throw new OperationCanceledException();}
                photo=image;photoView.texture=photo;photoMessage.text="";
            }
            catch(Exception){if(this&&!current.IsCancellationRequested)photoMessage.text="사진을 불러오지 못했어요. 메뉴에서 다시 확인해 주세요.";}
            finally{current.Dispose();if(this){Busy=false;cancellation=null;Labels();}}
        }
        public void ImmediateExit()
        {cancellation?.Cancel();dog.Exit();floor.SetActive(false);Rig.previewCamera.enabled=false;mainPanel.SetActive(false);menuPanel.SetActive(false);photoPanel.SetActive(false);restPanel.SetActive(true);menuButton.SetActive(false);ReleasePhoto();}
        public void Return(){if(CurrentProfile!=null&&!Busy)Enter(CurrentProfile,VerificationOnly);}
        public void Review()
        {cancellation?.Cancel();dog.Exit();floor.SetActive(false);RestoreTexture();canvas.SetActive(false);Rig.canvas.SetActive(true);Rig.Resume();}
        public void Preferences(){dog.Exit();floor.SetActive(false);canvas.SetActive(false);RestoreTexture();Rig.canvas.SetActive(true);setup.ShowPreferences();}
        public void SoundInfo(){notice.text="현재는 제공된 기본 효과음을 재생해요. 보호자가 등록한 실제 소리와는 구분해요.";}
        public void AccountInfo(){notice.text="이 사진을 바탕으로 AI가 표현한 모습이에요. 계정을 바꾸면 이전 강아지의 화면과 소리가 닫혀요.";}
        public void Logout(){ImmediateExit();Rig.generation.login.safeArea.GetComponentInParent<Canvas>(true).gameObject.SetActive(true);Rig.generation.login.Logout();}
        public void PetScreen(Vector2 screen,Camera eventCamera)
        {
            if(Busy||menuPanel.activeSelf||showingPhoto||!mainPanel.activeSelf)return;
            var rect=view.rectTransform;if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,screen,eventCamera,out var p)||!rect.rect.Contains(p))return;
            dog.PetRay(Rig.previewCamera.ViewportPointToRay(new Vector2((p.x-rect.rect.xMin)/rect.rect.width,(p.y-rect.rect.yMin)/rect.rect.height)));
        }
        void ReleasePhoto(){if(photoView!=null)photoView.texture=null;if(photo!=null)Destroy(photo);photo=null;}
        void RestoreTexture(){if(Rig.previewCamera!=null&&(roomTexture!=null||previousTexture!=null)){Rig.previewCamera.targetTexture=previousTexture;Rig.previewCamera.orthographicSize=.85f;}if(roomTexture!=null){roomTexture.Release();Destroy(roomTexture);roomTexture=null;}previousTexture=null;}
        void Update()
        {
            if(CurrentProfile!=null&&(Rig.generation.login.Session!=boundAuth||!boundAuth.IsAuthenticated))
            {cancellation?.Cancel();dog.Exit();floor.SetActive(false);canvas.SetActive(false);Rig.previewCamera.enabled=false;RestoreTexture();ReleasePhoto();CurrentProfile=null;}
            if(canvas.activeSelf){var area=Screen.safeArea;safeArea.anchorMin=new Vector2(area.xMin/Screen.width,area.yMin/Screen.height);safeArea.anchorMax=new Vector2(area.xMax/Screen.width,area.yMax/Screen.height);safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;interactions.anchorMin=safeArea.anchorMin;interactions.anchorMax=safeArea.anchorMax;interactions.offsetMin=interactions.offsetMax=Vector2.zero;}
            if(canvas.activeSelf&&mainPanel.activeSelf)
            {
                var ready=!Busy&&!menuPanel.activeSelf;greetButton.interactable=toyButton.interactable=ready&&dog.State==RoomState.Idle;
                restButton.interactable=ready&&(dog.State==RoomState.Idle||dog.State==RoomState.Rest);restLabel.text=dog.State==RoomState.Rest?"일어나기":"곁에 앉기";
                sleepButton.gameObject.SetActive(dog.State==RoomState.Rest);sleepButton.interactable=ready&&(Rig.player.CurrentClip=="SitIdle"||Rig.player.CurrentClip=="LieIdle");
            }
            if(resumeAfterPause&&!Busy&&canvas.activeSelf&&!restPanel.activeSelf){resumeAfterPause=false;Return();}
        }
        void OnApplicationPause(bool value){dog.Suspend(value);if(value)cancellation?.Cancel();else if(canvas.activeSelf&&!restPanel.activeSelf)resumeAfterPause=true;}
        void OnDestroy(){if(setup!=null)setup.RoomRequested.RemoveListener(FromSetup);cancellation?.Cancel();if(dog!=null)dog.Exit();RestoreTexture();ReleasePhoto();}
    }
}
