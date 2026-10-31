using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Withme.Character
{
    public sealed class StandardRigPlayback : MonoBehaviour
    {
        public StandardMotionLibrary sharedLibrary;
        public string fixtureName = "m1_golden";
        public bool loadOnStart;
        public bool useShared = true;
        public bool Paused;
        public bool IsReady => model != null && animator != null && ownClips != null;
        public bool IsLoading { get; private set; }
        public string CurrentClip { get; private set; }
        public string LastError { get; private set; }
        public int ClipCount => ownClips?.Length ?? 0;
        public Transform Model => model != null ? model.transform : null;
        public IReadOnlyDictionary<string, Transform> Bones => bones;
        public event Action<string> Completed;
        public event Action PoseEvaluated;
        public static readonly HashSet<string> Loops = new HashSet<string> { "Idle", "Walk", "Trot", "Greet", "SitIdle", "LieIdle", "Sleep" };
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<string, BoneRest> rest = new Dictionary<string, BoneRest>();
        readonly Dictionary<string, BoneRest> blendFrom = new Dictionary<string, BoneRest>();
        CancellationTokenSource cancellation;
        GltfImport imported;
        GameObject model;
        Animator animator;
        AnimationClip[] ownClips;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        Dictionary<string, BoneRest> sourceRest;
        float ownPelvisHeight, sourcePelvisHeight, clock, blendClock, blendDuration;
        bool completed;

        async void Start() { if (loadOnStart) await LoadAsync(fixtureName); }
        public Task LoadAsync(string name, CancellationToken token = default)
        {
            if (name != "m1_golden" && name != "m2_white" && name != "m3_cream") throw new ArgumentException("Unknown validation fixture");
            var path = Path.Combine(Application.streamingAssetsPath, "Validation/StandardRig", name + ".glb").Replace('\\', '/');
            if (!path.Contains("://")) path = new Uri(path).AbsoluteUri;
            return LoadCore(name, async (gltf, c) => await gltf.Load(path, new ImportSettings { AnimationMethod = AnimationMethod.Mecanim }, c), token);
        }
        public Task LoadUrlAsync(string url, bool fixture, CancellationToken token = default)
        {
            Withme.Generation.GenerationAssets.Validate(url, fixture);
            return LoadCore(null, async (gltf, c) => {
                var bytes = await Withme.Generation.GenerationAssets.Bytes(url, fixture, 64 * 1024 * 1024, c);
                ValidateEmbeddedGlb(bytes);
                return await gltf.Load(bytes, importSettings: new ImportSettings { AnimationMethod = AnimationMethod.Mecanim }, cancellationToken: c);
            }, token);
        }
        internal static void ValidateEmbeddedGlb(byte[] bytes)
        {
            if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67 || BitConverter.ToUInt32(bytes, 4) != 2
                || BitConverter.ToUInt32(bytes, 8) != bytes.Length || BitConverter.ToUInt32(bytes, 16) != 0x4E4F534A) throw new InvalidOperationException("Invalid GLB");
            var length = BitConverter.ToUInt32(bytes, 12);
            if (length > bytes.Length - 20) throw new InvalidOperationException("Invalid GLB JSON");
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes, 20, (int)length));
            foreach (var collection in new[] { "buffers", "images" })
                foreach (var item in json[collection] ?? new Newtonsoft.Json.Linq.JArray())
                {
                    var uri = item.Value<string>("uri");
                    if (uri != null && !uri.StartsWith("data:", StringComparison.Ordinal)) throw new InvalidOperationException("External GLB resource");
                }
        }
        async Task LoadCore(string name, Func<GltfImport, CancellationToken, Task<bool>> load, CancellationToken token)
        {
            if (IsLoading) throw new InvalidOperationException("Model load in progress");
            IsLoading = true; LastError = null; cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var current = cancellation; GltfImport candidate = null; GameObject root = null;
            try
            {
                Clear();
                candidate = new GltfImport(logger: new CollectingLogger());
                if (!await load(candidate, current.Token)) throw new InvalidOperationException("GLB load failed");
                root = new GameObject("StandardDog"); root.transform.SetParent(transform, false);
                if (!await candidate.InstantiateMainSceneAsync(root.transform, cancellationToken: current.Token)) throw new InvalidOperationException("GLB instantiate failed");
                current.Token.ThrowIfCancellationRequested();
                var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
                animator = root.GetComponentInChildren<Animator>();
                var clips = candidate.GetAnimationClips();
                if (skin == null || animator == null || skin.bones.Length != 33 || clips == null || clips.Length != 17 || clips.Any(c => c.legacy)) throw new InvalidOperationException("Standard rig contract mismatch");
                foreach (var b in skin.bones)
                {
                    if (b == null || bones.ContainsKey(b.name)) throw new InvalidOperationException("Missing/duplicate bone");
                    bones.Add(b.name, b); rest.Add(b.name, Capture(b));
                }
                ownPelvisHeight = Mathf.Abs(transform.InverseTransformPoint(bones["pelvis"].position).y);
                foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                model = root; root = null; imported = candidate; candidate = null; ownClips = clips; fixtureName = name;
                Play("Idle", 0);
            }
            catch (OperationCanceledException) { Clear(); }
            catch (Exception) { LastError = "강아지 움직임을 불러오지 못했어요."; Clear(); }
            finally { if (root != null) Destroy(root); candidate?.Dispose(); current.Dispose(); cancellation = null; IsLoading = false; }
        }

        public void Play(string name) => Play(name, .15f);
        public void Play(string name, float fade)
        {
            if (!IsReady) return;
            var shared = useShared && sharedLibrary != null;
            var clips = shared ? sharedLibrary.clips : ownClips;
            var clip = clips.FirstOrDefault(c => c != null && c.name == name);
            if (clip == null) throw new ArgumentException("Unknown standard clip");
            blendFrom.Clear(); foreach (var bone in bones) blendFrom[bone.Key] = Capture(bone.Value);
            if (graph.IsValid()) graph.Destroy();
            sourceRest = shared ? sharedLibrary.bones.ToDictionary(b => b.name) : rest;
            sourcePelvisHeight = shared ? sharedLibrary.pelvisHeight : ownPelvisHeight;
            if (bones.Keys.Any(n => !sourceRest.ContainsKey(n))) throw new InvalidOperationException("Common clip skeleton mismatch");
            graph = PlayableGraph.Create("WithmeStandardMotion"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph, "Generic", animator).SetSourcePlayable(playable);
            graph.Play(); clock = 0; blendClock = 0; blendDuration = Mathf.Max(0, fade); completed = false; CurrentClip = name;
            EvaluatePose(0);
        }

        public void Sample(float seconds)
        {
            if (!graph.IsValid()) return;
            clock = Mathf.Max(0, seconds); blendClock = blendDuration; EvaluatePose(clock);
        }
        static BoneRest Capture(Transform b) => new BoneRest { name = b.name, position = b.localPosition, rotation = b.localRotation, scale = b.localScale };
        void EvaluatePose(float time)
        {
            foreach (var pair in bones)
            {
                var bind = rest[pair.Key]; pair.Value.localPosition = bind.position; pair.Value.localRotation = bind.rotation; pair.Value.localScale = bind.scale;
            }
            var length = playable.GetAnimationClip().length;
            var sample = Loops.Contains(CurrentClip) && length > 0 ? time % length : Mathf.Min(time, length);
            playable.SetTime(sample); playable.SetSpeed(0); graph.Evaluate(0);
            var blend = blendDuration <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(blendClock / blendDuration));
            foreach (var pair in bones)
            {
                var target = pair.Value; var bind = rest[pair.Key]; var original = sourceRest[pair.Key];
                var rotation = bind.rotation * Quaternion.Inverse(original.rotation) * target.localRotation;
                var position = bind.position;
                if (pair.Key == "pelvis") position += (target.localPosition - original.position) * (sourcePelvisHeight > .0001f ? ownPelvisHeight / sourcePelvisHeight : 1);
                target.localPosition = Vector3.Lerp(blendFrom[pair.Key].position, position, blend);
                target.localRotation = Quaternion.Slerp(blendFrom[pair.Key].rotation, rotation, blend);
                target.localScale = bind.scale;
            }
            PoseEvaluated?.Invoke();
        }
        void Update()
        {
            if (!IsReady || Paused || !graph.IsValid()) return;
            clock += Time.deltaTime; blendClock += Time.deltaTime; EvaluatePose(clock);
            if (!completed && !Loops.Contains(CurrentClip) && clock >= playable.GetAnimationClip().length)
            { completed = true; Completed?.Invoke(CurrentClip); }
        }
        public void RestoreBindPose()
        {
            if (graph.IsValid()) graph.Destroy();
            foreach (var pair in bones) { var b = rest[pair.Key]; pair.Value.localPosition = b.position; pair.Value.localRotation = b.rotation; pair.Value.localScale = b.scale; }
            CurrentClip = null;
        }
        public void Clear()
        {
            if (graph.IsValid()) graph.Destroy();
            if (model != null) Destroy(model); model = null;
            animator = null; ownClips = null; bones.Clear(); rest.Clear(); blendFrom.Clear(); sourceRest = null;
            imported?.Dispose(); imported = null;
        }
        void OnDestroy() { cancellation?.Cancel(); Clear(); }
    }
}
