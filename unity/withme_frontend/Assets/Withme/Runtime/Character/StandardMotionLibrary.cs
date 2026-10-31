using System;
using UnityEngine;

namespace Withme.Character
{
    [Serializable]
    public struct BoneRest
    {
        public string name;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }
    public sealed class StandardMotionLibrary : ScriptableObject
    {
        public int skeletonVersion = 1;
        public AnimationClip[] clips;
        public BoneRest[] bones;
        public float pelvisHeight;
    }
}
