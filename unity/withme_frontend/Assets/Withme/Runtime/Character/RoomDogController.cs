using System;
using System.Collections.Generic;
using UnityEngine;

namespace Withme.Character
{
    public enum RoomState { Exited, Idle, Greet, Pet, Play, Rest }
    public sealed class RoomDogController:MonoBehaviour
    {
        public StandardRigPlayback player;
        public DogSounds sounds;
        public Transform toy;
        public RoomState State{get;private set;}=RoomState.Exited;
        public bool ReducedMotion{get;set;}
        public bool LiePreferred{get;set;}
        public bool ApproachGreeting{get;set;}=true;
        public bool ActivePreferred{get;set;}
        public float reactionSoundChance=.6f;
        readonly Queue<string> sequence=new Queue<string>();
        Vector3 home,target;
        float elapsed,stepDistance;
        int playPhase;
        bool suspended;
        void OnEnable(){player.Completed+=OnComplete;}
        public void Enter()
        {
            home=Vector3.zero;player.transform.localPosition=home;player.transform.localRotation=Quaternion.identity;player.Paused=false;suspended=false;Idle();
        }
        void Idle(){sequence.Clear();State=RoomState.Idle;elapsed=0;player.Play("Idle");sounds.StartIdle();if(toy!=null)toy.gameObject.SetActive(false);}
        void StartAction(RoomState state){sequence.Clear();sounds.StopIdle();State=state;elapsed=0;stepDistance=0;}
        void Sound(string action){if(UnityEngine.Random.value<reactionSoundChance)sounds.Play(action);}
        public bool Greet()
        {
            if(State!=RoomState.Idle||suspended)return false;StartAction(RoomState.Greet);
            target=home+new Vector3(0,0,.3f);player.Play(ApproachGreeting&&!ReducedMotion?"Walk":"Greet");
            if(!ApproachGreeting||ReducedMotion)Sound("greet");return true;
        }
        public bool Pet(string part)
        {
            if(State!=RoomState.Idle||suspended)return false;
            var clip=part=="head"?"PetHead":part=="chin"?"PetChin":part=="belly"?"PetBelly":null;if(clip==null)return false;
            StartAction(RoomState.Pet);player.Play(clip);Sound("pet");return true;
        }
        public bool ThrowToy()
        {
            if(State!=RoomState.Idle||suspended)return false;StartAction(RoomState.Play);
            if(ReducedMotion){player.Play("HeadTilt");Sound("fetch");return true;}
            target=home+new Vector3(.6f,0,.4f);playPhase=0;toy.gameObject.SetActive(true);toy.localPosition=target+Vector3.up*.08f;
            player.Play(ActivePreferred?"Trot":"Walk");Sound("fetch");return true;
        }
        public bool Rest()
        {
            if(suspended)return false;
            if(State==RoomState.Rest)return Wake();
            if(State!=RoomState.Idle)return false;StartAction(RoomState.Rest);
            Begin(LiePreferred?new[]{"LieDown","LieIdle"}:new[]{"SitDown","SitIdle"});return true;
        }
        public bool Sleep()
        {
            if(suspended||State!=RoomState.Rest)return false;
            if(player.CurrentClip=="SitIdle")Begin(new[]{"StandUp","LieDown","FallAsleep","Sleep"});
            else if(player.CurrentClip=="LieIdle")Begin(new[]{"FallAsleep","Sleep"});else return false;return true;
        }
        public bool Wake()
        {
            if(suspended||State!=RoomState.Rest)return false;
            if(player.CurrentClip=="Sleep")Begin(new[]{"WakeUp","GetUp"});
            else if(player.CurrentClip=="LieIdle")Begin(new[]{"GetUp"});
            else if(player.CurrentClip=="SitIdle")Begin(new[]{"StandUp"});else return false;return true;
        }
        void Begin(string[] clips){sequence.Clear();foreach(var clip in clips)sequence.Enqueue(clip);Advance();}
        void Advance(){if(sequence.Count>0)player.Play(sequence.Dequeue());else Idle();}
        void OnComplete(string clip)
        {
            if(State==RoomState.Exited||suspended)return;
            if(State==RoomState.Rest)Advance();
            else if(State==RoomState.Pet||State==RoomState.Play&&ReducedMotion)Idle();
        }
        bool Move(Vector3 destination,float speed)
        {
            var position=player.transform.localPosition;var delta=destination-position;
            if(delta.sqrMagnitude<.0001f){player.transform.localPosition=destination;return true;}
            player.transform.localRotation=Quaternion.RotateTowards(player.transform.localRotation,Quaternion.LookRotation(delta),360*Time.deltaTime);
            var next=Vector3.MoveTowards(position,destination,speed*Time.deltaTime);stepDistance+=Vector3.Distance(position,next);player.transform.localPosition=next;
            if(stepDistance>.18f){sounds.Footstep();stepDistance=0;}return false;
        }
        void Update()
        {
            if(State==RoomState.Exited||suspended||!player.IsReady)return;elapsed+=Time.deltaTime;
            if(State==RoomState.Greet)
            {
                if(player.CurrentClip=="Walk") {if(Move(target,.4f)){player.Play("Greet");elapsed=0;Sound("greet");}}
                else if(elapsed>2.2f)Idle();
            }
            else if(State==RoomState.Play&&!ReducedMotion)
            {
                if(playPhase==0&&Move(target,ActivePreferred ? .65f : .4f)){playPhase=1;}
                if(playPhase==1)
                {
                    toy.localPosition=player.transform.localPosition+player.transform.localRotation*new Vector3(0,.35f,.3f);
                    if(Move(home,ActivePreferred ? .65f : .4f)&&elapsed>=3f){player.transform.localRotation=Quaternion.identity;Idle();}
                }
                if(elapsed>6){player.transform.localPosition=home;Idle();}
            }
        }
        public bool PetRay(Ray worldRay)
        {
            if(State!=RoomState.Idle||!player.IsReady)return false;var closest=float.PositiveInfinity;var mesh=new Mesh();
            try
            {
                foreach(var skin in player.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.BakeMesh(mesh);var d=skin.transform.InverseTransformVector(worldRay.direction);var hits=new List<float>();
                    JointDepth.Intersections(new Ray(skin.transform.InverseTransformPoint(worldRay.origin),d),mesh.vertices,mesh.triangles,hits);
                    foreach(var hit in hits)closest=Mathf.Min(closest,hit/d.magnitude);
                }
            }
            finally{Destroy(mesh);}
            if(!float.IsFinite(closest))return false;var point=worldRay.GetPoint(closest);var head=player.Bones["head"].position;
            if(Vector3.Distance(point,head)<.28f)return Pet(point.y<head.y-.04f?"chin":"head");
            var belly=Vector3.Lerp(player.Bones["pelvis"].position,player.Bones["neck_01"].position,.5f)-Vector3.up*.1f;
            return Vector3.Distance(point,belly)<.35f&&Pet("belly");
        }
        public void Exit(){State=RoomState.Exited;sequence.Clear();if(player!=null)player.Paused=true;if(sounds!=null)sounds.StopAll();if(toy!=null)toy.gameObject.SetActive(false);}
        public void Suspend(bool value)
        {
            if(State==RoomState.Exited)return;suspended=value;sounds.StopAll();player.Paused=value;
            if(!value){player.transform.localPosition=home;Idle();}
        }
        void OnDisable(){if(player!=null){player.Completed-=OnComplete;Exit();}}
    }
}
