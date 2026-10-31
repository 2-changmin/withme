"""Auto-rig one dog model.

blender -b --factory-startup -P blender/rig.py -- IN.glb OUT.glb
        [--landmarks IN.json]      use hand-edited landmarks instead of estimating
        [--landmarks-out OUT.json] write the landmarks that were used
        [--report OUT.json]        write checks and measurements
        [--blend OUT.blend]        save the scene (with landmark empties) for review

Exit code: 0 ok, 2 rigged with warnings, 1 failed.
"""
import argparse
import json
import os
import sys
import traceback
import hashlib
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from dogrig import SKELETON_VERSION, RigError, animations, contact, landmarks, poses, prepare, skeleton, validate, weights  # noqa: E402
from dogrig.export import export_glb  # noqa: E402


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="rig.py")
    p.add_argument("input")
    p.add_argument("output")
    p.add_argument("--landmarks")
    p.add_argument("--landmarks-out")
    p.add_argument("--report")
    p.add_argument("--blend")
    return p.parse_args(argv)


def add_landmark_empties(lm):
    col = bpy.data.collections.new("Landmarks")
    bpy.context.scene.collection.children.link(col)
    for name, p in lm["points"].items():
        e = bpy.data.objects.new(f"LM_{name}", None)
        e.empty_display_type = "SPHERE"
        e.empty_display_size = 0.02
        e.location = p
        col.objects.link(e)


def run(args):
    for dest in (args.output,args.report,args.blend,args.landmarks_out):
        if dest: Path(dest).resolve().parent.mkdir(parents=True,exist_ok=True)
    poses._CTX_CACHE.clear()
    poses._MANUAL_FIT.clear()
    report = {
        "input": os.path.abspath(args.input),
        "skeletonVersion": SKELETON_VERSION,
        "blender": bpy.app.version_string,
        "pipelineVersion": "contact-v2",
        "input_sha256": hashlib.sha256(Path(args.input).read_bytes()).hexdigest(),
    }
    prepare.reset_scene()
    mesh = prepare.import_model(args.input)
    report["normalize"] = prepare.normalize(mesh)
    report["mesh"] = prepare.mesh_health(mesh)

    if args.landmarks:
        lm = landmarks.load(args.landmarks)
    else:
        lm = landmarks.estimate(mesh)
    report["landmark_source"] = lm["source"]
    if args.landmarks_out:
        landmarks.save(lm, args.landmarks_out, meta={"model": os.path.basename(args.input)})

    arm = skeleton.build(lm)
    arm['source_model'] = os.path.splitext(os.path.basename(args.input))[0]
    report["bone_lengths"] = skeleton.bone_lengths(arm)
    report["weights"] = weights.bind(mesh, arm)
    report["static"] = validate.static_checks(mesh, arm)

    report["clips"] = {}
    report['contact_corrections'] = {}
    for name in animations.CLIPS:
        action = animations.bake(arm, name)
        report["clips"][name] = validate.clip_checks(mesh, arm, action)
        report["clips"][name]["loop"] = animations.CLIPS[name][2]
    for name in poses.IK_CLIPS:
        action = poses.bake(arm, mesh, name)
        action, report['contact_corrections'][name] = contact.correct(arm, mesh, action)
        report["clips"][name] = validate.clip_checks(mesh, arm, action)
        report["clips"][name]["loop"] = poses.IK_CLIPS[name][1]
    for name,start,end in (('FallAsleep','LieIdle','Sleep'),('WakeUp','Sleep','LieIdle')):
        action=contact.bridge(arm,name,bpy.data.actions[start],bpy.data.actions[end])
        report['clips'][name]=validate.clip_checks(mesh,arm,action)
        report['clips'][name]['loop']=False
    fit = poses._CTX_CACHE.get((arm.name, mesh.name), {})
    report["ground_fit"] = {k: round(float(fit[k]), 4) for k in ("sit_theta", "lift_lie") if k in fit}

    export_glb(mesh, arm, args.output)
    report["output"] = os.path.abspath(args.output)

    if args.blend:
        add_landmark_empties(lm)
        # Show Walk alone when the review file is opened (NLA tracks muted).
        animations.activate(arm, bpy.data.actions["Walk"])
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(args.blend))

    issues = report["static"]["issues"] + [i for c in report["clips"].values() for i in c["issues"]]
    for name,c in report['clips'].items():
        if c['loop'] and c['endpoint_max_delta']>0.001:
            c['issues'].append({'code':'LOOP_SEAM','delta':c['endpoint_max_delta']})
        if name in poses.IK_CLIPS:
            for foot,bounds in c['foot_z_ranges'].items():
                if bounds[0]<-0.015 or bounds[1]>0.025:
                    c['issues'].append({'code':'FOOT_CONTACT','foot':foot,'range':bounds})
    issues = report['static']['issues'] + [i for c in report['clips'].values() for i in c['issues']]
    report['quality'] = {'accepted': not bool(issues),'issue_count':len(issues),
                         'requires_visual_review':True,'automatic_correction_bounded':True}
    report["status"] = "needs_review" if issues else "ok"
    return report


def main():
    args = parse_args()
    try:
        report = run(args)
        code = 0 if report["status"] == "ok" else 2
    except RigError as e:
        report = {"input": args.input, "status": "failed", "error": {"code": e.code, "message": e.message}}
        code = 1
    except Exception as e:  # unexpected: keep the trace in the report
        report = {"input": args.input, "status": "failed",
                  "error": {"code": "UNEXPECTED", "message": str(e), "trace": traceback.format_exc()}}
        code = 1
    if args.report:
        with open(args.report, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
    print(f"[rig] {report['status']}: {args.input}")
    sys.stdout.flush()
    sys.exit(code)


if __name__ == "__main__":
    main()
