using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using TMPro;
using UnityEngine;

namespace Withme.Validation
{
    // Development-only probe. No authentication, generation API or product navigation.
    public sealed class CharacterPlaybackProbe : MonoBehaviour
    {
        public RuntimeAnimatorController walkController;
        public string[] requiredPaths;
        public Transform modelAnchor;
        public Camera viewCamera;
        public RectTransform safeArea;
        public TMP_Text statusLabel;
        public TMP_Text fpsLabel;
        public UnityEngine.UI.Button[] playbackButtons;

        public bool IsLoading { get; private set; }
        public bool IsReady => model != null && !IsLoading && string.IsNullOrEmpty(LastError);
        public string LastError { get; private set; }
        public string Mode { get; private set; } = "Loading";
        public double LoadMilliseconds { get; private set; }
        public int CompletedLoads { get; private set; }
        public int SampleFrames { get; private set; }
        public double SampleSeconds { get; private set; }
        public double AverageFps => SampleSeconds > 0 ? SampleFrames / SampleSeconds : 0;
        public Animator BoundAnimator { get; private set; }
        public Transform AnimationRoot { get; private set; }

        GltfImport imported;
        GameObject model;
        Animation embedded;
        CancellationTokenSource lifetime;
        Transform[] poseTransforms;
        Vector3[] positions, scales;
        Quaternion[] rotations;
        Rect lastSafeArea;
        int lastWidth, lastHeight;
        double sampleStart, refreshAt;
        bool suspended;

        void Awake() => lifetime = new CancellationTokenSource();
        void Start() => ReloadClicked();

        public void ReloadClicked() => _ = LoadAsync();

        // Explicit URL override is for development checks; no signed URLs are persisted or logged.
        public async Task LoadAsync(string source = null)
        {
            if (IsLoading || lifetime == null) return;
            IsLoading = true;
            LastError = null;
            Mode = "Loading";
            ResetSamples();
            SetControls(false);
            SetStatus("강아지 불러오는 중…");
            ReleaseModel();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            GltfImport candidate = null;
            GameObject candidateModel = null;
            try
            {
                if (string.IsNullOrEmpty(source))
                {
                    source = Path.Combine(Application.streamingAssetsPath, "Validation/DogWalk.glb").Replace('\\', '/');
                    if (!source.Contains("://")) source = new Uri(source).AbsoluteUri;
                }
                var token = lifetime.Token;
                var logger = new CollectingLogger();
                candidate = new GltfImport(logger: logger);
                var loaded = await candidate.Load(source, new ImportSettings
                {
                    NodeNameMethod = NameImportMethod.OriginalUnique,
                    AnimationMethod = AnimationMethod.Legacy,
                    GenerateMipMaps = true
                }, token);
                token.ThrowIfCancellationRequested();
                if (!loaded) throw new InvalidOperationException("파일을 읽지 못했어요. 다시 불러와 주세요.");

                candidateModel = new GameObject("LoadedDog");
                candidateModel.transform.SetParent(modelAnchor, false);
                var instantiator = new GameObjectInstantiator(candidate, candidateModel.transform, logger,
                    new InstantiationSettings { Mask = ComponentType.Mesh | ComponentType.Animation,
                        SceneObjectCreation = SceneObjectCreation.Never, SkinUpdateWhenOffscreen = true });
                if (!await candidate.InstantiateMainSceneAsync(instantiator, token))
                    throw new InvalidOperationException("강아지 모습을 표시하지 못했어요.");
                token.ThrowIfCancellationRequested();
                var skin = candidateModel.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin == null || skin.bones.Length == 0 || skin.bones.Any(b => b == null))
                    throw new InvalidOperationException("움직임에 필요한 뼈대가 없거나 손상됐어요.");
                if (walkController == null || requiredPaths == null || requiredPaths.Length == 0)
                    throw new InvalidOperationException("공용 걷기 설정이 없어요.");
                var missing = requiredPaths.Where(p => !string.IsNullOrEmpty(p) && candidateModel.transform.Find(p) == null).ToArray();
                if (missing.Length > 0)
                    throw new InvalidOperationException($"공용 걷기와 뼈대 경로가 맞지 않아요: {missing.Length}개");

                model = candidateModel;
                candidateModel = null;
                imported = candidate;
                candidate = null;
                AnimationRoot = model.transform;
                embedded = model.GetComponent<Animation>();
                if (embedded == null || embedded["Walk"] == null)
                    throw new InvalidOperationException("샘플의 Walk 애니메이션이 없어요.");
                embedded.Stop();
                embedded.playAutomatically = false;
                poseTransforms = model.GetComponentsInChildren<Transform>();
                positions = poseTransforms.Select(t => t.localPosition).ToArray();
                rotations = poseTransforms.Select(t => t.localRotation).ToArray();
                scales = poseTransforms.Select(t => t.localScale).ToArray();
                BoundAnimator = model.AddComponent<Animator>();
                BoundAnimator.runtimeAnimatorController = walkController;
                BoundAnimator.applyRootMotion = false;
                BoundAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                BoundAnimator.enabled = false;
                FrameModel(skin.bounds);
                LoadMilliseconds = timer.Elapsed.TotalMilliseconds;
                CompletedLoads++;
                IsLoading = false;
                SetControls(true);
                PlaySharedWalk();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                // Do not expose local paths, signed URLs or provider diagnostics in player logs.
                if (this != null)
                {
                    LastError = exception is InvalidOperationException
                        ? exception.Message
                        : "불러오기에 실패했어요. 파일과 걷기 설정을 확인해 주세요.";
                    Mode = "Error";
                    ReleaseModel();
                    SetStatus(LastError);
                }
            }
            finally
            {
                if (candidateModel != null) Destroy(candidateModel);
                candidate?.Dispose();
                if (this != null) IsLoading = false;
            }
        }

        public void PlaySharedWalk()
        {
            if (!IsReady) return;
            RestorePose();
            embedded.enabled = false;
            BoundAnimator.enabled = true;
            BoundAnimator.Rebind();
            BoundAnimator.speed = suspended ? 0 : 1;
            BoundAnimator.Play("Walk", 0, 0);
            BoundAnimator.Update(0);
            Mode = "SharedWalk";
            ResetSamples();
            SetStatus("공용 Generic 걷기 재생 중\n대기 클립은 아직 없어요.");
        }

        public void PlayEmbeddedWalk()
        {
            if (!IsReady) return;
            RestorePose();
            embedded.enabled = true;
            embedded["Walk"].wrapMode = WrapMode.Loop;
            embedded["Walk"].speed = suspended ? 0 : 1;
            embedded.Play("Walk");
            Mode = "EmbeddedWalk";
            ResetSamples();
            SetStatus("GLB 내장 걷기 재생 중\n공용 Animator와 비교해 주세요.");
        }

        public void ShowPose()
        {
            if (!IsReady) return;
            RestorePose();
            Mode = "Pose";
            ResetSamples();
            SetStatus("기본 자세 · 대기 애니메이션 아님");
        }

        void RestorePose()
        {
            if (embedded != null) { embedded.Stop(); embedded.enabled = false; }
            if (BoundAnimator != null) BoundAnimator.enabled = false;
            if (poseTransforms == null) return;
            for (var i = 0; i < poseTransforms.Length; i++)
            {
                if (poseTransforms[i] == null) continue;
                poseTransforms[i].localPosition = positions[i];
                poseTransforms[i].localRotation = rotations[i];
                poseTransforms[i].localScale = scales[i];
            }
        }

        void FrameModel(Bounds bounds)
        {
            var center = bounds.center;
            var radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            var vertical = viewCamera.fieldOfView * Mathf.Deg2Rad;
            var horizontal = 2 * Mathf.Atan(Mathf.Tan(vertical / 2) * viewCamera.aspect);
            var distance = radius / Mathf.Sin(Mathf.Min(vertical, horizontal) / 2) * 1.25f;
            viewCamera.transform.position = center + new Vector3(1, 0.3f, 1).normalized * distance;
            viewCamera.transform.LookAt(center);
        }

        void ResetSamples()
        {
            SampleFrames = 0;
            SampleSeconds = 0;
            sampleStart = Time.realtimeSinceStartupAsDouble + 1;
            refreshAt = 0;
        }

        void Update()
        {
            if (safeArea != null && (lastSafeArea != Screen.safeArea || lastWidth != Screen.width || lastHeight != Screen.height))
            {
                lastSafeArea = Screen.safeArea;
                lastWidth = Screen.width;
                lastHeight = Screen.height;
                safeArea.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
                safeArea.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
                safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            }
            if (!IsReady || suspended || Time.realtimeSinceStartupAsDouble < sampleStart) return;
            SampleFrames++;
            SampleSeconds += Time.unscaledDeltaTime;
            if (fpsLabel != null && Time.realtimeSinceStartupAsDouble >= refreshAt)
            {
                refreshAt = Time.realtimeSinceStartupAsDouble + 0.5;
                fpsLabel.text = $"{Mode} · {AverageFps:F1} fps\n{SampleSeconds:F1}초 / {SampleFrames} frames · 로드 {LoadMilliseconds:F0}ms\n{SystemInfo.deviceModel} · {Screen.width}×{Screen.height}";
            }
        }

        void OnApplicationPause(bool paused)
        {
            suspended = paused;
            if (BoundAnimator != null) BoundAnimator.speed = paused ? 0 : 1;
            if (embedded != null && embedded["Walk"] != null) embedded["Walk"].speed = paused ? 0 : 1;
            ResetSamples();
        }

        void SetControls(bool enabled)
        {
            if (playbackButtons == null) return;
            foreach (var button in playbackButtons) if (button != null) button.interactable = enabled;
        }
        void SetStatus(string text) { if (statusLabel != null) statusLabel.text = text; }

        void ReleaseModel()
        {
            if (model != null) { model.SetActive(false); Destroy(model); }
            model = null;
            embedded = null;
            BoundAnimator = null;
            AnimationRoot = null;
            poseTransforms = null;
            imported?.Dispose();
            imported = null;
        }

        void OnDestroy()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            ReleaseModel();
        }
    }
}
