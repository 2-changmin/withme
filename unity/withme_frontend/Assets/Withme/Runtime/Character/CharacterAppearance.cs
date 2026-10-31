using System.Collections.Generic;
using UnityEngine;

namespace Withme.Character
{
    public sealed class CharacterAppearance : MonoBehaviour
    {
        public StandardRigPlayback player;
        public Shader coatShader;
        public AppearanceParameters Parameters { get; private set; } = new AppearanceParameters();
        readonly List<Material> owned=new List<Material>();
        readonly Dictionary<Renderer,Material[]> originals=new Dictionary<Renderer,Material[]>();
        Transform model;
        public void Set(AppearanceParameters value){value.Validate();Parameters=value.Copy();}
        void OnEnable(){if(player!=null)player.PoseEvaluated+=Apply;}
        void Apply()
        {
            if(player.Model==null)return;
            if(model!=player.Model){Release();model=player.Model;}
            var p=Parameters;var bones=player.Bones;
            var colored=Mathf.Abs(p.HueShift)>.0001f||Mathf.Abs(p.Brightness)>.0001f;
            if(colored&&owned.Count==0)CloneMaterials();
            else if(!colored&&owned.Count>0){Release();model=player.Model;}
            foreach(var side in new[]{".L",".R"})
            {
                if(bones.TryGetValue("ear_01"+side,out var ear)){ear.localScale*=p.EarScale;ear.localRotation*=Quaternion.Euler(0,0,p.EarAngle*(side==".L"?1:-1));}
                foreach(var name in new[]{"forearm","front_paw","shin","hind_foot","hind_toe"})if(bones.TryGetValue(name+side,out var leg))leg.localPosition*=p.LegLength;
            }
            if(bones.TryGetValue("pelvis",out var pelvis)){var position=pelvis.localPosition;position.y*=p.LegLength;pelvis.localPosition=position;}
            foreach(var name in new[]{"spine_02","spine_03"})if(bones.TryGetValue(name,out var spine))spine.localPosition*=p.BodyLength;
            if(bones.TryGetValue("head",out var head))head.localScale*=p.HeadScale;
            if(bones.TryGetValue("tail_01",out var tail))tail.localScale*=p.TailScale;
            foreach(var material in owned){material.SetFloat("_HueShift",p.HueShift/360f);material.SetFloat("_Brightness",p.Brightness);}
        }
        void CloneMaterials()
        {
            if(coatShader==null)return;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var source=renderer.sharedMaterials;originals[renderer]=source;var copies=new Material[source.Length];
                for(var i=0;i<source.Length;i++)
                {
                    var original=source[i];if(original==null)continue;
                    var copy=new Material(coatShader){name="WithmeCoatPreview"};
                    var textureName=original.HasProperty("baseColorTexture")?"baseColorTexture":original.HasProperty("_BaseColorTexture")?"_BaseColorTexture":original.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
                    if(original.HasProperty(textureName)){copy.SetTexture("_BaseMap",original.GetTexture(textureName));copy.SetTextureScale("_BaseMap",original.GetTextureScale(textureName));copy.SetTextureOffset("_BaseMap",original.GetTextureOffset(textureName));}
                    var colorName=original.HasProperty("baseColorFactor")?"baseColorFactor":original.HasProperty("_BaseColorFactor")?"_BaseColorFactor":original.HasProperty("_BaseColor")?"_BaseColor":"_Color";
                    if(original.HasProperty(colorName))copy.SetColor("_BaseColor",original.GetColor(colorName));
                    copies[i]=copy;owned.Add(copy);
                }
                renderer.sharedMaterials=copies;
            }
        }
        public void Release()
        {
            foreach(var pair in originals)if(pair.Key!=null)pair.Key.sharedMaterials=pair.Value;originals.Clear();
            foreach(var material in owned)if(material!=null)Destroy(material);owned.Clear();model=null;
        }
        void OnDisable(){if(player!=null)player.PoseEvaluated-=Apply;Release();}
    }
}
