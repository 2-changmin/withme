"""Bounded smoothing A/B experiment; never changes the canonical pipeline.

Run inside Blender with separate Temp/batch output and compare unchanged QA
thresholds. A numerical improvement still requires visual approval.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace

ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'blender'))
from dogrig import weights
from dogrig.joint_contract import from_contract
import source_rig


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('input');parser.add_argument('joints');parser.add_argument('baseline');parser.add_argument('directory');parser.add_argument('--iterations',type=int,choices=[8],default=8)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    directory=Path(args.directory).resolve();allowed=(ROOT/'unity/withme_frontend/Temp/batch/quality-experiments').resolve()
    assert directory.is_relative_to(allowed);directory.mkdir(parents=True,exist_ok=True)
    report_file=directory/'report.json';output=directory/'rigged.glb';intent_file=directory/'experiment.json'
    intent={'inputSha256':hashlib.sha256(Path(args.input).read_bytes()).hexdigest(),'jointsSha256':hashlib.sha256(Path(args.joints).read_bytes()).hexdigest(),'smoothingIterations':args.iterations,'thresholdsChanged':False}
    if intent_file.exists():assert json.loads(intent_file.read_text())==intent
    else:intent_file.write_text(json.dumps(intent))
    if report_file.exists() and output.exists():
        report=json.loads(report_file.read_text());assert report['status'] in ('ok','needs_review')
    else:
        original=weights._smooth_seam_weights
        def candidate(mesh,iterations=4,strength=.35):return original(mesh,iterations=args.iterations,strength=strength)
        weights._smooth_seam_weights=candidate
        with tempfile.TemporaryDirectory(dir=directory) as temporary:
            landmarks=Path(temporary)/'internal.json';landmarks.write_text(json.dumps(from_contract(json.loads(Path(args.joints).read_text()))))
            report=source_rig.run(SimpleNamespace(input=args.input,landmarks=str(landmarks),output=str(output),report=str(report_file),blend=None,landmarks_out=None))
            report['jointsFormat']='git-normalized-v1';report['pipelineExperiment']=intent
            report_file.write_text(json.dumps(report,indent=2))
    baseline=json.loads(Path(args.baseline).read_text())
    comparison={'beforeIssues':baseline['quality']['issue_count'],'afterIssues':report['quality']['issue_count'],'clips':{name:{'before':clip['edge_stretch_p99'],'after':report['clips'][name]['edge_stretch_p99']} for name,clip in baseline['clips'].items()},'canonicalPipelineChanged':False,'qualityApproved':False}
    (directory/'comparison.json').write_text(json.dumps(comparison,indent=2));print(json.dumps(comparison),flush=True)
