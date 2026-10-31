"""Repeat canonical rigs into separate outputs and compare semantic GLB data."""
import argparse
import json
from pathlib import Path
import struct
import sys

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'blender'))
from worker_adapter import run_rig


def glb(path):
    data = Path(path).read_bytes()
    assert data[:4] == b'glTF' and struct.unpack_from('<I', data, 8)[0] == len(data)
    length = struct.unpack_from('<I', data, 12)[0]
    document = json.loads(data[20:20+length]); offset = 20+length
    binary = data[offset+8:]
    return document, binary


def accessor(document, binary, index):
    value = document['accessors'][index]; view = document['bufferViews'][value['bufferView']]
    types = {5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}
    width = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[value['type']]
    dtype = np.dtype(types[value['componentType']]); start = view.get('byteOffset',0)+value.get('byteOffset',0)
    return np.ndarray((value['count'],width),dtype=dtype,buffer=binary,offset=start,strides=(view.get('byteStride',width*dtype.itemsize),dtype.itemsize)).copy()


def animations(document, binary):
    result = {}
    for animation in document['animations']:
        for channel in animation['channels']:
            sampler = animation['samplers'][channel['sampler']]; target = channel['target']
            key = (animation['name'],document['nodes'][target['node']]['name'],target['path'])
            result[key] = (accessor(document,binary,sampler['input']),accessor(document,binary,sampler['output']))
    return result


def compare(before, after):
    a, ab = glb(before); b, bb = glb(after)
    assert {n['name'] for n in a['nodes']} == {n['name'] for n in b['nodes']}
    assert len(a['skins'][0]['joints']) == len(b['skins'][0]['joints']) == 33
    aj=np.array([a['nodes'][i]['name'] for i in a['skins'][0]['joints']]);bj=np.array([b['nodes'][i]['name'] for i in b['skins'][0]['joints']])
    ai=accessor(a,ab,a['skins'][0]['inverseBindMatrices']);bi=accessor(b,bb,b['skins'][0]['inverseBindMatrices'])
    for name in aj:assert np.allclose(ai[np.where(aj==name)[0][0]],bi[np.where(bj==name)[0][0]],atol=2e-5,rtol=0)
    ac, bc = animations(a,ab), animations(b,bb); assert ac.keys() == bc.keys()
    maximum = 0
    for key in ac:
        at,av = ac[key];bt,bv = bc[key];assert at.shape == bt.shape and av.shape == bv.shape
        assert np.allclose(at,bt,atol=1e-6,rtol=0)
        if key[2] == 'rotation':
            error = np.minimum(np.abs(av-bv).max(axis=1),np.abs(av+bv).max(axis=1)).max()
        else: error = np.abs(av-bv).max()
        maximum = max(maximum,float(error))
    assert maximum < 2e-5, maximum
    assert len(a['meshes']) == len(b['meshes'])
    for am,bm in zip(a['meshes'],b['meshes']):
        assert len(am['primitives']) == len(bm['primitives'])
        for ap,bp in zip(am['primitives'],bm['primitives']):
            for semantic in ('POSITION','NORMAL','TEXCOORD_0'):
                av=accessor(a,ab,ap['attributes'][semantic]);bv=accessor(b,bb,bp['attributes'][semantic])
                assert av.shape == bv.shape and np.allclose(av,bv,atol=2e-5,rtol=0),semantic
            assert np.array_equal(accessor(a,ab,ap['indices']),accessor(b,bb,bp['indices']))
            assert np.array_equal(aj[accessor(a,ab,ap['attributes']['JOINTS_0'])],bj[accessor(b,bb,bp['attributes']['JOINTS_0'])])
            assert np.allclose(accessor(a,ab,ap['attributes']['WEIGHTS_0']),accessor(b,bb,bp['attributes']['WEIGHTS_0']),atol=2e-5,rtol=0)
    return {'channels':len(ac),'maxChannelDelta':maximum,'meshUvNormalIndicesEqual':True,'skinWeightsAndBindMatricesEqual':True}


if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--blender',required=True);args=parser.parse_args()
    base=ROOT/'unity/withme_frontend/Temp/batch';results=[]
    for model in ('m1_golden','m2_white','m3_cream'):
        outcome=run_rig(args.blender,base/'prepared'/f'{model}.glb',base/'prepared'/f'{model}.joints.json',base/'repeat'/model)
        original=json.loads((base/'rigged'/f'{model}.report.json').read_text())
        assert original['bone_lengths']==outcome.report['bone_lengths']
        assert original['status']==outcome.report['status'] and original['quality']==outcome.report['quality']
        result=compare(base/'rigged'/f'{model}.glb',outcome.path)
        result.update(model=model,reused=outcome.reused,qualityApproved=False);results.append(result)
        (base/'repeat-results.json').write_text(json.dumps(results,indent=2));print(json.dumps(result),flush=True)
