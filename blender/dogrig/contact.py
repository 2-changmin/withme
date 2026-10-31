"""Bounded per-model contact baking; unresolved errors stay visible in reports."""
import bpy, math
import numpy as np
from mathutils import Vector, Euler, Matrix
from . import animations, poses, validate

FEET = {'front.L': ('forearm.L', 'front_paw.L'),
        'front.R': ('forearm.R', 'front_paw.R'),
        'hind.L': ('shin.L', 'hind_foot.L'),
        'hind.R': ('shin.R', 'hind_foot.R')}
MAX_ITERATIONS = 8
MAX_CORRECTION = 0.12
FLOOR = 0.001

def masks(mesh):
    names=[g.name for g in mesh.vertex_groups]
    dominant=[names[max(v.groups,key=lambda g:g.weight).group] if v.groups else '' for v in mesh.data.vertices]
    out={}
    for key in FEET:
        side=key[-1]
        prefixes=('forearm.', 'front_paw.') if key.startswith('front') else ('shin.','hind_foot.','hind_toe.')
        out[key]=np.array([n.endswith('.'+side) and n.startswith(prefixes) for n in dominant])
    out['body']=np.array([not n.startswith(('tail_','forearm.','front_paw.','shin.','hind_foot.','hind_toe.')) for n in dominant])
    return out

def assign(arm, fr):
    for pb in arm.pose.bones:
        pb.rotation_mode='XYZ'; pb.rotation_euler=fr[pb.name]['rot']
        pb.location=fr[pb.name].get('loc',(0,0,0)); pb.scale=(1,1,1)

def snapshot(arm):
    return {p.name:{'rot':tuple(p.rotation_euler),**({'loc':tuple(p.location)} if p.name=='pelvis' else {})} for p in arm.pose.bones}

def aim(arm, name, direction):
    pb=arm.pose.bones[name]; matrix=pb.matrix.copy(); q=matrix.to_quaternion()
    turn=(q @ Vector((0,1,0))).rotation_difference(Vector(direction))
    matrix=(turn @ q).to_matrix().to_4x4(); matrix.translation=pb.head
    pb.matrix=matrix; bpy.context.view_layer.update()

def adapt_manual_front(arm, rots, loc):
    """Retain hand-pose torso; align forearms/paws on other body types."""
    poses._set_pose(arm,rots,loc); bpy.context.view_layer.update()
    for side in ('L','R'):
        shoulder=arm.pose.bones['shoulder.'+side]
        h=shoulder.head.copy(); d=shoulder.tail-shoulder.head
        upper=arm.data.bones['upper_arm.'+side].length
        desired=0.04+upper
        joint=min((h+Matrix.Rotation(math.radians(a),3,'X')@d for a in range(-60,61)),key=lambda p:abs(p.z-desired))
        aim(arm,'shoulder.'+side,joint-h)
        drop=min(max(joint.z-0.04,0.0),upper)
        direction=Vector((0,-math.sqrt(max(upper*upper-drop*drop,0)), -drop))
        aim(arm,'upper_arm.'+side,direction if direction.length>1e-6 else Vector((0,0,-1)))
        aim(arm,'forearm.'+side,(0,-1,0))
        aim(arm,'front_paw.'+side,(0,-1,-0.05))
    return {p.name:tuple(p.rotation_euler) for p in arm.pose.bones}

def correct(arm, mesh, action):
    name=action.name; ad=arm.animation_data
    # Sit already solves straight vertical arms and contact in poses._sit.
    # A second unconstrained two-bone IK can flip the straight-arm solution.
    if name in ('SitDown','SitIdle','StandUp'):
        return action, {'method':'straight_arm_pose_solver','iterations_limit':10}
    old_mutes=[t.mute for t in ad.nla_tracks]
    animations.activate(arm,action)
    n=int(action.frame_range[1]); source=[]; positions=[]; orientations=[]
    for f in range(1,n+1):
        bpy.context.scene.frame_set(f); source.append(snapshot(arm))
        positions.append({k:arm.pose.bones[chain].tail.copy() for k,(chain,_) in FEET.items()})
        orientations.append({paw:arm.pose.bones[paw].matrix.to_quaternion() for _,paw in FEET.values()})
    ad.action=None; groups=masks(mesh); targets={}; constraints=[]
    for key,(chain,paw) in FEET.items():
        obj=bpy.data.objects.new('Contact_'+key,None); bpy.context.scene.collection.objects.link(obj); targets[key]=obj
        c=arm.pose.bones[chain].constraints.new('IK')
        c.target=obj; c.chain_count=2; c.use_stretch=False; c.iterations=64
        constraints.append((arm.pose.bones[chain],c))
        obj.rotation_mode='QUATERNION'
        c=arm.pose.bones[paw].constraints.new('COPY_ROTATION')
        c.target=obj; c.owner_space='WORLD'; c.target_space='WORLD'
        constraints.append((arm.pose.bones[paw],c))
    frames=[]; max_shift=0; exhausted=0
    try:
        for i,fr in enumerate(source):
            assign(arm,fr); s=poses._smooth(i/max(n-1,1))
            transition=name in ('LieDown','GetUp','SitDown','StandUp')
            for key in FEET:
                p=positions[0][key].lerp(positions[-1][key],s) if transition else positions[i][key].copy()
                targets[key].location=p
                targets[key].rotation_quaternion=orientations[i][FEET[key][1]]
            original_z=arm.pose.bones['pelvis'].location.z
            for iteration in range(MAX_ITERATIONS):
                bpy.context.view_layer.update(); co=validate._eval_coords(mesh); errors=[]
                for key in FEET:
                    if groups[key].any():
                        low=float(co[groups[key],2].min()); dz=float(np.clip(FLOOR-low,-0.025,0.025))
                        targets[key].location.z+=dz; errors.append(abs(FLOOR-low))
                if groups['body'].any():
                    low=float(co[groups['body'],2].min())
                    if low < -0.004:
                        p=arm.pose.bones['pelvis']
                        p.location.z=min(original_z+MAX_CORRECTION,p.location.z+min(-0.004-low,0.025))
                        errors.append(-0.004-low)
                if iteration>=3:
                    low=float(co[:,2].min())
                    if low < -0.006:
                        p=arm.pose.bones['pelvis']
                        p.location.z=min(original_z+MAX_CORRECTION,p.location.z+min(0.5*(-0.004-low),0.02))
                if max(errors,default=0)<0.003: break
            else: exhausted+=1
            bpy.context.view_layer.update(); solved={}
            for bone,m in poses._read_local(arm).items():
                prev=Euler(frames[-1][bone]['rot']) if frames else Euler(fr[bone]['rot'])
                solved[bone]={'rot':tuple(m.to_euler('XYZ',prev))}
                if bone=='pelvis': solved[bone]['loc']=tuple(m.to_translation())
            for bone,v in fr.items(): solved.setdefault(bone,v)
            max_shift=max(max_shift,abs(arm.pose.bones['pelvis'].location.z-original_z)); frames.append(solved)
    finally:
        for pb,c in constraints: pb.constraints.remove(c)
        for obj in targets.values(): bpy.data.objects.remove(obj,do_unlink=True)
        for tr,muted in zip(ad.nla_tracks,old_mutes): tr.mute=muted
    for tr in list(ad.nla_tracks):
        if any(st.action==action for st in tr.strips): ad.nla_tracks.remove(tr)
    bpy.data.actions.remove(action)
    new=animations.write_clip(arm,name,frames)
    return new,{'iterations_limit':MAX_ITERATIONS,'frames_at_limit':exhausted,'max_pelvis_correction':max_shift}


def bridge(arm, name, start, end, duration=24):
    """Explicit head/torso transition; endpoints exactly match held clips."""
    samples=[]
    for action in (start,end):
        animations.activate(arm,action)
        bpy.context.scene.frame_set(1)
        samples.append(snapshot(arm))
    a,b=samples; frames=[]
    for f in range(duration+1):
        t=poses._smooth(f/duration); fr={}
        for bone in a:
            qa=Euler(a[bone]['rot']).to_quaternion(); qb=Euler(b[bone]['rot']).to_quaternion()
            prev=Euler(frames[-1][bone]['rot']) if frames else Euler(a[bone]['rot'])
            fr[bone]={'rot':tuple(qa.slerp(qb,t).to_euler('XYZ',prev))}
            if bone=='pelvis': fr[bone]['loc']=tuple(Vector(a[bone]['loc']).lerp(Vector(b[bone]['loc']),t))
        frames.append(fr)
    arm.animation_data.action=None
    for tr in arm.animation_data.nla_tracks: tr.mute=False
    return animations.write_clip(arm,name,frames)
