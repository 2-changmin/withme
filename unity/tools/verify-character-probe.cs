using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Withme.Validation;

public static class VerifyCharacterProbe
{
    public static async Task<object> Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play Mode.");
        var probe = UnityEngine.Object.FindFirstObjectByType<CharacterPlaybackProbe>();
        Require(probe != null && probe.IsReady, "Sample is not ready.");
        probe.playbackButtons[0].onClick.Invoke();
        await Frames(4);
        var bone = probe.AnimationRoot.Find("Dog_Rig/root/spine_01/thigh.L");
        var skin = probe.AnimationRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        Require(bone != null && skin != null, "Required skin or bone missing.");
        var initialRotation = bone.localRotation;
        var baked = new Mesh();
        float boneChange = 0, meshChange = 0;
        int vertexCount;
        try
        {
            skin.BakeMesh(baked);
            var initialVertices = baked.vertices;
            vertexCount = initialVertices.Length;
            for (var sample = 0; sample < 12; sample++)
            {
                await Frames(8);
                boneChange = Mathf.Max(boneChange, Quaternion.Angle(initialRotation, bone.localRotation));
                skin.BakeMesh(baked);
                var vertices = baked.vertices;
                for (var i = 0; i < vertices.Length; i += 71)
                    meshChange = Mathf.Max(meshChange, Vector3.Distance(initialVertices[i], vertices[i]));
            }
        }
        finally { UnityEngine.Object.Destroy(baked); }
        Require(boneChange > 1 && meshChange > 0.001f, "Animator did not deform the mesh.");
        Require(probe.BoundAnimator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Wrong Animator state.");

        probe.playbackButtons[2].onClick.Invoke();
        var pose = bone.localRotation;
        await Frames(12);
        Require(Quaternion.Angle(pose, bone.localRotation) < 0.01f, "Pose is not stable.");
        probe.playbackButtons[1].onClick.Invoke();
        var embeddedRotation = bone.localRotation;
        float embeddedChange = 0;
        for (var i = 0; i < 8; i++)
        {
            await Frames(8);
            embeddedChange = Mathf.Max(embeddedChange, Quaternion.Angle(embeddedRotation, bone.localRotation));
        }
        Require(embeddedChange > 1 && !probe.BoundAnimator.enabled, "Embedded playback did not move independently.");

        // Capture a numeric comparison at the same time in both animation systems.
        probe.PlaySharedWalk();
        probe.BoundAnimator.Play("Walk", 0, 0.4f);
        probe.BoundAnimator.Update(0);
        var sharedRotation = bone.localRotation;
        probe.PlayEmbeddedWalk();
        var animation = probe.AnimationRoot.GetComponent<Animation>();
        animation["Walk"].time = animation["Walk"].length * 0.4f;
        animation.Sample();
        var sharedVsEmbedded = Quaternion.Angle(sharedRotation, bone.localRotation);
        Require(sharedVsEmbedded < 0.1f, "Shared and embedded curves differ.");

        await probe.LoadAsync(new Uri(System.IO.Path.Combine(Application.temporaryCachePath, "missing-withme-probe.glb")).AbsoluteUri);
        Require(!probe.IsReady && !string.IsNullOrEmpty(probe.LastError), "Missing file not rejected.");
        var failureMessage = probe.LastError;
        var paths = probe.requiredPaths;
        probe.requiredPaths = paths.Concat(new[] { "MissingBone" }).ToArray();
        try
        {
            await probe.LoadAsync();
            Require(!probe.IsReady && probe.LastError.Contains("경로"), "Incompatible skeleton not rejected.");
        }
        finally { probe.requiredPaths = paths; }
        var previousLoads = probe.CompletedLoads;
        var load = probe.LoadAsync();
        await probe.LoadAsync(); // Must ignore a duplicate request while the first is active.
        await load;
        await Frames(4);
        Require(probe.IsReady && probe.CompletedLoads == previousLoads + 1, "Retry or duplicate-load guard failed.");
        Require(probe.modelAnchor.childCount == 1, "Reload leaked model instances.");
        var persistent = probe.playbackButtons.Select(b => b.onClick.GetPersistentEventCount()).ToArray();
        Require(persistent.All(n => n == 1), "Button wiring missing.");
        return new { success = true, boneChangeDegrees = boneChange, meshDisplacement = meshChange,
            vertexCount, embeddedChangeDegrees = embeddedChange, sharedVsEmbeddedDegrees = sharedVsEmbedded,
            failureMessage, probe.CompletedLoads, children = probe.modelAnchor.childCount, persistent,
            frame = Time.frameCount, probe.Mode };
    }

    static async Task Frames(int count)
    {
        var target = Time.frameCount + count;
        var deadline = Time.realtimeSinceStartupAsDouble + 15;
        while (Time.frameCount < target)
        {
            if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Player loop stopped.");
            await Task.Yield();
        }
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
