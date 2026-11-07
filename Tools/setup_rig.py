import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

argv = [a for a in (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []) if not a.startswith("--")]
GLB = argv[0] if argv else r"C:\spt41\CutContent\RigExport\rig_ak74_bear.glb"
BLEND = argv[1] if len(argv) > 1 else os.path.splitext(GLB)[0] + ".blend"
REST = os.path.splitext(GLB)[0] + "_rest.json"

AXES = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
SIDES = (("L", "Left"), ("R", "Right"))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
arm.name = "Rig"
arm.data.name = "Rig"

with open(REST, encoding="utf-8") as f:
    rest = {name: Matrix([[values[c * 4 + r] for c in range(4)] for r in range(4)]) for name, values in json.load(f).items()}


def game_world(name):
    return AXES @ rest[name] @ AXES.inverted()


def bone_world(name):
    return arm.matrix_world @ arm.data.bones[name].matrix_local


def correction(name):
    return game_world(name).inverted() @ bone_world(name)


bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
edit = arm.data.edit_bones
for short, long in SIDES:
    palm = f"Base Human{short}Palm"
    marker = f"weapon_{short}_IK_marker"
    target = game_world(marker) @ correction(palm)
    target = arm.matrix_world.inverted() @ target
    bone = edit.new(f"IK Hand {long}")
    bone.length = 0.06
    bone.matrix = Matrix.LocRotScale(target.to_translation(), target.to_quaternion(), None)
    bone.parent = edit[marker]
    bone.use_connect = False
    bone.use_deform = False
    upper = edit[f"Base Human{short}Upperarm"]
    fore = edit[f"Base Human{short}Forearm1"]
    collar = edit[f"Base Human{short}Collarbone"]
    ik_upper = edit.new(f"IK Upperarm {long}")
    ik_upper.head, ik_upper.tail = upper.head.copy(), fore.head.copy()
    ik_upper.roll = upper.roll
    ik_upper.parent = collar
    ik_fore = edit.new(f"IK Forearm {long}")
    ik_fore.head, ik_fore.tail = fore.head.copy(), edit[palm].head.copy()
    ik_fore.roll = fore.roll
    ik_fore.parent = ik_upper
    ik_fore.use_connect = True
    for source, holder in ((upper, ik_upper), (fore, ik_fore)):
        mch = edit.new(f"MCH {source.name}")
        mch.length = source.length
        mch.matrix = source.matrix.copy()
        mch.parent = holder
    for helper in (ik_upper, ik_fore, edit[f"MCH {upper.name}"], edit[f"MCH {fore.name}"]):
        helper.use_deform = False
bpy.ops.object.mode_set(mode="POSE")

controls = arm.data.collections.new("IK Controls")
mechanism = arm.data.collections.new("IK Mechanism")
for short, long in SIDES:
    for name in (f"IK Upperarm {long}", f"IK Forearm {long}", f"MCH Base Human{short}Upperarm", f"MCH Base Human{short}Forearm1"):
        mechanism.assign(arm.data.bones[name])
mechanism.is_visible = False
markers = arm.data.collections.new("Weapon Markers")
for short, long in SIDES:
    controls.assign(arm.data.bones[f"IK Hand {long}"])
for name in arm.data.bones.keys():
    if name.startswith("weapon_") and "marker" in name or name.startswith("Bend_Goal"):
        markers.assign(arm.data.bones[name])

for short, long in SIDES:
    arm[f"{long} Hand IK"] = 1.0
    arm.id_properties_ui(f"{long} Hand IK").update(min=0.0, max=1.0, soft_min=0.0, soft_max=1.0, description=f"1 keeps the {long.lower()} hand on the weapon, 0 lets you animate it freely")


def drive(constraint, prop):
    curve = constraint.driver_add("influence")
    driver = curve.driver
    driver.type = "AVERAGE"
    var = driver.variables.new()
    var.type = "SINGLE_PROP"
    var.targets[0].id_type = "OBJECT"
    var.targets[0].id = arm
    var.targets[0].data_path = f'["{prop}"]'


pose = arm.pose.bones
for short, long in SIDES:
    ik = pose[f"IK Forearm {long}"].constraints.new("IK")
    ik.name = "Hand IK"
    ik.target = arm
    ik.subtarget = f"IK Hand {long}"
    ik.pole_target = arm
    ik.pole_subtarget = f"Bend_Goal_{long}"
    ik.chain_count = 2
    for bone_name in (f"Base Human{short}Upperarm", f"Base Human{short}Forearm1"):
        follow = pose[bone_name].constraints.new("COPY_ROTATION")
        follow.name = "Follow IK"
        follow.target = arm
        follow.subtarget = f"MCH {bone_name}"
        drive(follow, f"{long} Hand IK")
    rot = pose[f"Base Human{short}Palm"].constraints.new("COPY_ROTATION")
    rot.name = "Hand Grip"
    rot.target = arm
    rot.subtarget = f"IK Hand {long}"
    drive(rot, f"{long} Hand IK")

bpy.context.view_layer.update()
for short, long in SIDES:
    ik = pose[f"IK Forearm {long}"].constraints["Hand IK"]
    elbow = f"Base Human{short}Forearm1"
    rest_elbow = bone_world(elbow).to_translation()
    best, best_angle = None, 0.0
    for degrees in range(-180, 180, 5):
        ik.pole_angle = math.radians(degrees)
        bpy.context.view_layer.update()
        d = ((arm.matrix_world @ pose[elbow].matrix).to_translation() - rest_elbow).length
        if best is None or d < best:
            best, best_angle = d, degrees
    ik.pole_angle = math.radians(best_angle)
    bpy.context.view_layer.update()
    wrist = (arm.matrix_world @ pose[f"Base Human{short}Palm"].matrix).to_translation()
    goal = (arm.matrix_world @ pose[f"IK Hand {long}"].matrix).to_translation()
    print(f"{long} arm: pole angle {best_angle} deg, wrist {((wrist - goal).length * 1000):.1f} mm from the grip marker")

muzzle = (arm.matrix_world @ pose["fireport"].matrix).to_translation()
breech = (arm.matrix_world @ pose["weapon"].matrix).to_translation()
sight = (arm.matrix_world @ pose["mod_sight_rear"].matrix).to_translation()
forward = (muzzle - breech).normalized()
up = sight - breech
up = (up - forward * up.dot(forward)).normalized()
camera_data = bpy.data.cameras.new("First Person")
camera_data.sensor_fit = "VERTICAL"
camera_data.angle = math.radians(55)
camera_data.clip_start = 0.01
camera = bpy.data.objects.new("First Person Camera", camera_data)
bpy.context.scene.collection.objects.link(camera)
anchor = arm.matrix_world @ pose["Camera_animated"].matrix
rotation = Matrix((forward.cross(up), up, -forward)).transposed().to_quaternion()
camera.matrix_world = Matrix.LocRotScale(anchor.to_translation(), rotation, None)
world = camera.matrix_world.copy()
camera.parent = arm
camera.parent_type = "BONE"
camera.parent_bone = "Camera_animated"
camera.matrix_world = world
bpy.context.scene.camera = camera

arm.data.display_type = "STICK"
arm.show_in_front = True
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print("Saved", BLEND)

if "--preview" in sys.argv:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    scene.render.filepath = os.path.splitext(BLEND)[0] + "_firstperson.png"
    bpy.ops.render.render(write_still=True)
    side = bpy.data.objects.new("Side", bpy.data.cameras.new("Side"))
    scene.collection.objects.link(side)
    centre = (arm.matrix_world @ pose["weapon"].matrix).to_translation()
    side.location = centre + Vector((1.1, 0.3, 0.2))
    side.rotation_euler = (centre - side.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = side
    scene.render.filepath = os.path.splitext(BLEND)[0] + "_side.png"
    bpy.ops.render.render(write_still=True)
