using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Character;

namespace Withme.UI
{
    public sealed class CharacterSetupFlow:MonoBehaviour
    {
        public RigFlow rig;
        public GameObject preferencesPanel;
        public TMP_Text question,stepLabel,description,error;
        public Button[] options;
        public TMP_Text[] optionLabels;
        public Button previous,next;
        public TMP_Text nextLabel;
        public readonly UnityEvent RoomRequested=new UnityEvent();
        public CharacterProfile Active{get;private set;}
        public int QuestionIndex{get;private set;}
        ICharacterProfiles profiles;
        string boundUser;
        bool saving;
        static readonly string[] Questions={"어떻게 반겼나요?","쉴 때 어떤 자세를 좋아했나요?","어떤 장난감을 좋아했나요?","평소 어떤 모습이었나요?"};
        static readonly string[][] Choices={new[]{"천천히 다가왔어요","제자리에서 반겼어요"},new[]{"앉아서 쉬었어요","엎드려 쉬었어요"},new[]{"공을 좋아했어요","인형을 좋아했어요"},new[]{"차분한 편이었어요","활발한 편이었어요"}};
#if UNITY_EDITOR
        public string VerificationDirectory;
#endif
        void Start(){preferencesPanel.SetActive(false);rig.WalkReady.AddListener(FromWalk);}
        void Bind()
        {
            boundUser=rig.generation.login.Session.UserId;
#if UNITY_EDITOR
            if(rig.generation.Fixture){var directory=string.IsNullOrEmpty(VerificationDirectory)?Path.Combine(Application.persistentDataPath,"CharacterProfiles/fixture-v1"):VerificationDirectory;profiles=new MockCharacterProfiles(directory);return;}
#endif
            profiles=new UnavailableCharacterProfiles();
        }
        void FromWalk()
        {
            try
            {
                Bind();Active=profiles.Read(boundUser,rig.generation.Coordinator.Session.PetId);
                if(Active==null)return;
                if(Active.PreferencesFinished)RoomRequested.Invoke();else ShowPreferences();
            }
            catch(ApiFailure e){if(e.Code!="NOT_IMPLEMENTED")rig.quality.text="저장한 모습을 읽지 못했어요. 원본 사진은 그대로 있어요.";}
        }
        public void Confirm()
        {
            if(saving||rig.Busy||rig.Page!="walk")return;
            if(!rig.QualityApproved){rig.quality.text="움직임 확인이 아직 필요해요. 지금은 모습과 걸음을 살펴봐 주세요.";return;}
            saving=true;
            try
            {
                Bind();var session=rig.generation.Coordinator.Session;
                var draft=new CharacterProfile{UserId=boundUser,PetId=session.PetId,PetName=session.PetName,ModelId=rig.Coordinator.Session.ModelId,RigJobId=rig.Coordinator.Session.JobId,Appearance=rig.appearanceFlow.appearance.Parameters.Copy()};
                Active=profiles.Confirm(draft,rig.QualityApproved);
                if(Active.PreferencesFinished)RoomRequested.Invoke();else ShowPreferences();
            }
            catch(Exception){rig.quality.text="지금은 함께할 모습을 저장하지 못했어요. 기존 모습은 그대로 있어요.";}
            finally{saving=false;}
        }
        public void ShowPreferences()
        {
            if(Active==null||!Active.Confirmed)return;
            var unanswered=Array.FindIndex(Active.Preferences,a=>a<0);QuestionIndex=unanswered<0?0:unanswered;
            rig.ShowPreferences();preferencesPanel.SetActive(true);Render();
        }
        public void ChooseFirst()=>Choose(0);
        public void ChooseSecond()=>Choose(1);
        void Choose(int choice)
        {
            if(saving||Active==null||!preferencesPanel.activeSelf)return;
            try{var draft=Active.Copy();draft.Preferences[QuestionIndex]=choice;Active=profiles.SavePreferences(draft);error.text="";Render();}
            catch(Exception){error.text="선택을 저장하지 못했어요. 다시 골라 주세요.";}
        }
        public void Previous(){if(saving||QuestionIndex==0)return;QuestionIndex--;Render();}
        public void Next()
        {
            if(saving||Active==null||!preferencesPanel.activeSelf||Active.Preferences[QuestionIndex]<0)return;
            if(QuestionIndex<3){QuestionIndex++;Render();return;}
            saving=true;
            try{var draft=Active.Copy();draft.PreferencesFinished=true;Active=profiles.SavePreferences(draft);preferencesPanel.SetActive(false);RoomRequested.Invoke();}
            catch(Exception){error.text="선택을 저장하지 못했어요. 다시 확인해 주세요.";}
            finally{saving=false;}
        }
        void Render()
        {
            question.text=QuestionIndex==0?Active.PetName+"는 "+Questions[0]:Questions[QuestionIndex];stepLabel.text=$"익숙한 모습 · {QuestionIndex+1} / 4";
            description.text="기억에 가까운 모습을 하나 골라 주세요.";
            for(var i=0;i<2;i++){optionLabels[i].text=(Active.Preferences[QuestionIndex]==i?"●  ":"○  ")+Choices[QuestionIndex][i];options[i].interactable=!saving;}
            previous.interactable=!saving&&QuestionIndex>0;next.interactable=!saving&&Active.Preferences[QuestionIndex]>=0;nextLabel.text=QuestionIndex==3?Active.PetName+"와 함께하기":"다음 질문";
        }
        void Update()
        {
            var auth=rig.generation.login.Session;
            if(boundUser!=null&&(auth==null||!auth.IsAuthenticated||auth.UserId!=boundUser)){Active=null;profiles=null;boundUser=null;preferencesPanel.SetActive(false);}
        }
#if UNITY_EDITOR
        // A test supplies an explicitly approved contract fixture in an isolated
        // directory. It never changes RigFlow's real quality flag or live state.
        public void ConfigureForVerification(ICharacterProfiles service,CharacterProfile profile)
        {if(!rig.generation.Fixture)throw new InvalidOperationException();profiles=service;Active=profile;boundUser=profile.UserId;}
#endif
        void OnDestroy(){rig.WalkReady.RemoveListener(FromWalk);}
    }
}
