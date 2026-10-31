using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Withme.Character;
using Withme.Generation;
using Withme.Networking;

namespace Withme.UI
{
    public sealed class AppearanceFlow : MonoBehaviour
    {
        public RigFlow rig;
        public CharacterAppearance appearance;
        public GameObject panel;
        public RawImage originalPhoto,preview;
        public TMP_Text hint;
        public Slider[] sliders;
        public Button useButton,cancelButton,resetButton;
        public bool Busy{get;private set;}
        AppearanceParameters before;
        CancellationTokenSource cancellation;
        Texture2D photo;
        void Start(){panel.SetActive(false);preview.texture=rig.preview.texture;}
        public async void Open()
        {
            if(Busy||rig.Busy||rig.Page!="walk")return;
            before=appearance.Parameters.Copy();SetSliders(before);rig.ShowAppearance();panel.SetActive(true);preview.texture=rig.preview.texture;hint.text="원본 사진과 비교하며 조금씩 맞춰 주세요.";
            Busy=true;Controls();var current=new CancellationTokenSource();cancellation=current;
            try
            {
                var g=rig.generation;var login=g.login;var auth=login.Session;
                var api=new GenerationApi(new JsonHttpClient(g.Fixture?g.mockBaseUrl:login.settings.apiBaseUrl,new UnityHttpTransport(),20,allowEditorLoopback:g.Fixture),auth);
                var url=await api.PhotoUrlAsync(g.Coordinator.Session.MediaIds[0],current.Token);
                var image=await GenerationAssets.Image(url,g.Fixture,current.Token);
                if(!this||current.IsCancellationRequested||login.Session!=auth||!auth.IsAuthenticated){Destroy(image);throw new OperationCanceledException();}
                photo=image;originalPhoto.texture=photo;
            }
            catch(OperationCanceledException){}
            catch(Exception){if(this&&!current.IsCancellationRequested)hint.text="사진을 불러오지 못했어요. 조절을 취소하고 나중에 다시 확인할 수 있어요.";}
            finally{current.Dispose();if(this){cancellation=null;Busy=false;Controls();}}
        }
        public void Change(float ignored)
        {
            if(Busy||rig.Page!="appearance")return;
            appearance.Set(new AppearanceParameters{EarScale=sliders[0].value,EarAngle=sliders[1].value,TailScale=sliders[2].value,LegLength=sliders[3].value,BodyLength=sliders[4].value,HeadScale=sliders[5].value,HueShift=sliders[6].value,Brightness=sliders[7].value});
        }
        void SetSliders(AppearanceParameters p)
        {var values=new[]{p.EarScale,p.EarAngle,p.TailScale,p.LegLength,p.BodyLength,p.HeadScale,p.HueShift,p.Brightness};for(var i=0;i<values.Length;i++)sliders[i].SetValueWithoutNotify(values[i]);}
        public void ResetAppearance(){if(Busy)return;var p=new AppearanceParameters();appearance.Set(p);SetSliders(p);}
        public void Cancel(){if(Busy)return;appearance.Set(before);Close();}
        public void Use(){if(Busy)return;Close();}
        void Close(){panel.SetActive(false);ReleasePhoto();rig.ReturnFromAppearance();}
        void Controls(){useButton.interactable=cancelButton.interactable=resetButton.interactable=!Busy;foreach(var s in sliders)s.interactable=!Busy;}
        void ReleasePhoto(){if(originalPhoto!=null)originalPhoto.texture=null;if(photo!=null)Destroy(photo);photo=null;}
        public void ClearUser(){cancellation?.Cancel();panel.SetActive(false);ReleasePhoto();appearance.Set(new AppearanceParameters());appearance.Release();before=null;}
        void Update()
        {
            if(panel.activeSelf && (!rig.canvas.activeSelf||rig.generation.login.Session==null||!rig.generation.login.Session.IsAuthenticated))
            {cancellation?.Cancel();if(before!=null)appearance.Set(before);panel.SetActive(false);ReleasePhoto();}
        }
        void OnDestroy(){cancellation?.Cancel();ReleasePhoto();}
    }
}
