using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Withme.Auth;
using Withme.Character;
using Withme.Generation;

namespace Withme.UI
{
    public sealed class RigJointEditor : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public RigFlow flow;
        public Camera previewCamera;
        public Transform anchor;
        public RawImage view;
        public TMP_Text guide, count, hint;
        public RectTransform[] markers;
        public RigJoints Working { get; private set; }
        public int Selected { get; private set; }
        public bool Ready => root != null && Working != null;
        static readonly string[] Guides = { "코 끝을 눌러 주세요", "목이 시작하는 곳을 눌러 주세요", "꼬리가 시작하는 곳을 눌러 주세요", "꼬리 끝을 눌러 주세요", "왼쪽 앞발을 눌러 주세요", "왼쪽 앞다리가 몸과 만나는 곳을 눌러 주세요", "왼쪽 뒷발을 눌러 주세요", "왼쪽 뒷다리가 몸과 만나는 곳을 눌러 주세요", "왼쪽 귀 끝을 눌러 주세요", "왼쪽 귀가 시작하는 곳을 눌러 주세요" };
        readonly List<Surface> surfaces = new List<Surface>();
        RigJoints initial;
        GltfImport imported;
        GameObject root;
        sealed class Surface { public Transform transform; public Vector3[] vertices; public int[] triangles; }
        public async Task OpenAsync(string url, bool fixture, RigJoints joints, CancellationToken cancellation)
        {
            Close(); initial = joints.Copy(); Working = joints.Copy(); Selected = 0;
            var bytes = await GenerationAssets.Bytes(url,fixture,64*1024*1024,cancellation);
            StandardRigPlayback.ValidateEmbeddedGlb(bytes);
            var candidate = new GltfImport(logger:new CollectingLogger()); var loaded = new GameObject("JointPreparedDog"); loaded.transform.SetParent(anchor,false);
            try
            {
                if(!await candidate.Load(bytes,new Uri(url),cancellationToken:cancellation) || !await candidate.InstantiateMainSceneAsync(loaded.transform,cancellationToken:cancellation)) throw new ApiFailure("MODEL_LOAD",0);
                cancellation.ThrowIfCancellationRequested();
                foreach(var filter in loaded.GetComponentsInChildren<MeshFilter>())
                {
                    if(filter.sharedMesh == null || !filter.sharedMesh.isReadable) throw new ApiFailure("MODEL_LOAD",0);
                    surfaces.Add(new Surface{transform=filter.transform,vertices=filter.sharedMesh.vertices,triangles=filter.sharedMesh.triangles});
                }
                if(surfaces.Count == 0) throw new ApiFailure("MODEL_LOAD",0);
                foreach(var t in loaded.GetComponentsInChildren<Transform>())t.gameObject.layer=30;
                root=loaded;imported=candidate; Refresh();
            }
            catch { surfaces.Clear(); Destroy(loaded);candidate.Dispose();Working=null;throw; }
        }
        public void Previous() { if(Ready){Selected=Mathf.Max(0,Selected-1);Refresh();} }
        public void Next() { if(Ready){Selected=Mathf.Min(9,Selected+1);Refresh();} }
        public void ResetPoints() { if(Ready){Working=initial.Copy();hint.text="처음 위치로 되돌렸어요.";Refresh();} }
        public void Cancel() => flow.CancelCorrection();
        public void Submit() { if(Ready)flow.SubmitCorrection(Working.Copy()); }
        public void Rotate() { if(Ready)anchor.Rotate(Vector3.up,45,Space.World); }
        public void OnPointerDown(PointerEventData e)
        {
            if(!Ready || e.button != PointerEventData.InputButton.Left)return;
            var nearest=-1;var distance=24f;
            for(var i=0;i<10;i++)
            {
                var p=RectTransformUtility.WorldToScreenPoint(e.pressEventCamera,markers[i].position);var d=Vector2.Distance(p,e.position);
                if(d<distance){distance=d;nearest=i;}
            }
            if(nearest>=0){Selected=nearest;Refresh();return;}
            MoveSelected(e.position,e.pressEventCamera);
        }
        public void OnDrag(PointerEventData e) { if(Ready && e.button == PointerEventData.InputButton.Left)MoveSelected(e.position,e.pressEventCamera); }
        public bool MoveSelected(Vector2 screen,Camera eventCamera)
        {
            if(!Ready)return false;
            var rect=view.rectTransform;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,screen,eventCamera,out var local) || !rect.rect.Contains(local))return false;
            var uv=new Vector2((local.x-rect.rect.xMin)/rect.rect.width,(local.y-rect.rect.yMin)/rect.rect.height);
            var ray=previewCamera.ViewportPointToRay(uv);var hits=new List<float>();
            foreach(var surface in surfaces)
            {
                var direction=surface.transform.InverseTransformVector(ray.direction);var scale=direction.magnitude;
                var localRay=new Ray(surface.transform.InverseTransformPoint(ray.origin),direction);var localHits=new List<float>();
                JointDepth.Intersections(localRay,surface.vertices,surface.triangles,localHits);
                if(scale>1e-8f)foreach(var distance in localHits)hits.Add(distance/scale);
            }
            if(!JointDepth.Midpoint(ray,hits,out var point)) { hint.text="몸의 깊이를 확인하지 못했어요. 조금 돌려서 다시 눌러 주세요.";return false; }
            var canonical=RigJoints.FromUnity(anchor.InverseTransformPoint(point));
            if(Selected<4)canonical[0]=0;
            else if(canonical[0]<0){hint.text="왼쪽의 점을 맞춰 주세요. 반대쪽은 함께 맞춰져요.";return false;}
            Working.Points[RigJoints.Names[Selected]]=canonical;Working.Validate();hint.text="점을 옮겼어요. 반대쪽도 함께 맞춰져요.";return true;
        }
        void Refresh() { count.text=$"{Selected+1} / 10";guide.text=Guides[Selected]; }
        void LateUpdate()
        {
            if(!Ready)return;var extra=10;
            for(var i=0;i<10;i++)
            {
                var p=RigJoints.ToUnity(Working.Points[RigJoints.Names[i]]);Place(markers[i],p,i==Selected,false);
                if(i>=4){p.x=-p.x;Place(markers[extra++],p,false,true);}
            }
        }
        void Place(RectTransform marker,Vector3 point,bool selected,bool mirror)
        {
            var uv=previewCamera.WorldToViewportPoint(anchor.TransformPoint(point));marker.gameObject.SetActive(uv.z>0 && uv.x>=0 && uv.x<=1 && uv.y>=0 && uv.y<=1);
            marker.anchoredPosition=new Vector2(uv.x*view.rectTransform.rect.width,uv.y*view.rectTransform.rect.height);
            marker.GetComponent<Image>().color=mirror?new Color(.4f,.5f,.45f,.45f):selected?new Color(.95f,.5f,.2f):new Color(.2f,.5f,.36f);
            marker.localScale=Vector3.one*(selected?1.35f:1);
        }
        public void Close()
        {
            if(root!=null)Destroy(root);root=null;imported?.Dispose();imported=null;surfaces.Clear();Working=null;initial=null;
            if(markers!=null)foreach(var marker in markers)if(marker!=null)marker.gameObject.SetActive(false);
        }
        void OnDestroy()=>Close();
    }
}
