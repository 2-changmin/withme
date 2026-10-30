using System;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;
using Withme.Auth;
using Withme.Generation;

namespace Withme.UI
{
    public sealed class GenerationModelPreview : MonoBehaviour
    {
        public Camera previewCamera;
        public Transform modelAnchor;
        public GameObject Loaded { get; private set; }
        GltfImport imported;
        public async Task LoadAsync(string url, bool fixture, CancellationToken cancellation)
        {
            Clear();
            var bytes = await GenerationAssets.Bytes(url, fixture, 64 * 1024 * 1024, cancellation);
            var candidate = new GltfImport(logger: new CollectingLogger());
            var root = new GameObject("GenerationDog"); root.transform.SetParent(modelAnchor, false);
            try
            {
                if (!await candidate.Load(bytes, new Uri(url), cancellationToken: cancellation)
                    || !await candidate.InstantiateMainSceneAsync(root.transform, cancellationToken: cancellation)) throw new ApiFailure("MODEL_LOAD", 0);
                cancellation.ThrowIfCancellationRequested();
                var renderers = root.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new ApiFailure("MODEL_LOAD", 0);
                var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                if (!float.IsFinite(bounds.size.magnitude) || bounds.size.magnitude <= 0) throw new ApiFailure("MODEL_LOAD", 0);
                root.transform.localScale = Vector3.one / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                root.transform.position += modelAnchor.position - bounds.center;
                foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = 30;
                previewCamera.transform.position = modelAnchor.position + new Vector3(1.1f, .45f, 1.6f);
                previewCamera.transform.LookAt(modelAnchor.position); previewCamera.orthographicSize = .66f;
                Loaded = root; imported = candidate;
            }
            catch { Destroy(root); candidate.Dispose(); throw; }
        }
        public void Rotate() { if (Loaded != null) Loaded.transform.RotateAround(modelAnchor.position, Vector3.up, 45); }
        public void Clear() { if (Loaded != null) Destroy(Loaded); Loaded = null; imported?.Dispose(); imported = null; }
        void OnDestroy() => Clear();
    }
}
