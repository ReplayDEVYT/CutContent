import json
import os
import sys

import bpy
from mathutils import Matrix

argv = [a for a in (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []) if not a.startswith("--")]
FOLDER = argv[0] if argv else r"C:\spt41\CutContent\RigExport"
OUT = argv[1] if len(argv) > 1 else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Resources", "looting_animations.json")
REST = os.path.join(FOLDER, "rig_ak74_bear_rest.json")
BONES_TXT = os.path.join(FOLDER, "rig_ak74_bear_bones.txt")
CLIPS = {"body": "body_anim.fbx", "loot": "looting_anim.fbx", "search": "searching_loot_anim.fbx"}

AXES = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
FLIP = Matrix(((-1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))

with open(REST, encoding="utf-8") as f:
    rest = {name: Matrix([[v[c * 4 + r] for c in range(4)] for r in range(4)]) for name, v in json.load(f).items()}
parents = {}
with open(BONES_TXT, encoding="utf-8") as f:
    for line in f:
        parts = line.strip().split("/")
        if parts and parts[0]:
            parents[parts[-1]] = parts[-2] if len(parts) > 1 else None
root_name = next(n for n in rest if n.endswith(".generated"))
for name, parent in list(parents.items()):
    if parent is None:
        parents[name] = root_name

BAKED = ["weapon"] + [n for n in rest if n.startswith("Base HumanL") and n != "Base HumanLCollarbone"]
BAKED.insert(1, "Base HumanLCollarbone")


def to_blender(m):
    return AXES @ m @ AXES.inverted()


def to_unity(blender_matrix):
    gltf = AXES.inverted() @ blender_matrix @ AXES
    return FLIP @ gltf @ FLIP


def pack(m):
    loc, rot, _ = m.decompose()
    return [round(loc.x, 6), round(loc.y, 6), round(loc.z, 6), round(rot.x, 6), round(rot.y, 6), round(rot.z, 6), round(rot.w, 6)]


def unity_local(world, name):
    parent = root_name if name == "weapon" else parents.get(name)
    return world[parent].inverted() @ world[name] if parent in world else world[name]


rest_unity = {n: FLIP @ rest[n] @ FLIP for n in rest}
result = {"fps": 24, "rest": {n: pack(unity_local(rest_unity, n)) for n in BAKED}, "clips": {}}

for key, file in CLIPS.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(FOLDER, file), automatic_bone_orientation=False)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    scene = bpy.context.scene
    result["fps"] = scene.render.fps / scene.render.fps_base
    needed = set(BAKED) | {parents[n] for n in BAKED if parents.get(n)} | {root_name}
    correction = {n: to_blender(rest[n]).inverted() @ (arm.matrix_world @ arm.data.bones[n].matrix_local) for n in needed}
    drift = max((to_blender(rest[n]).to_translation() - (arm.matrix_world @ arm.data.bones[n].matrix_local).to_translation()).length for n in needed)
    start, end = (int(x) for x in arm.animation_data.action.frame_range)
    frames = {n: [] for n in BAKED}
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        world = {n: to_unity(arm.matrix_world @ arm.pose.bones[n].matrix @ correction[n].inverted()) for n in needed}
        for n in BAKED:
            frames[n].append(pack(unity_local(world, n)))
    result["clips"][key] = {"frames": end - start + 1, "bones": frames}
    print(f"{key}: {end - start + 1} frames, rest drift {drift * 1000:.2f} mm")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(result, f, separators=(",", ":"))
print("Wrote", OUT, os.path.getsize(OUT), "bytes")
