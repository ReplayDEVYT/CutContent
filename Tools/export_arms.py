import json
import math
import os
import re
import struct
import sys

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

GAME = r"C:\spt41\EscapeFromTarkov_Data\StreamingAssets\Windows"
DATABASE = r"C:\spt41\SPT_Runtime\SPT_Data\database"
AK74 = "5bf3e03b0db834001d2c4a9c"
DEFAULT_SKIN = os.path.join(GAME, "assets", "content", "hands", "bear", "bear_hands_skin.bundle")
LOD_SKIP = re.compile(r"_LOD[1-9]|_lod[1-9]|shadow|collider|MuzzleJet|muzzleflash|HeatHaze", re.IGNORECASE)


def identity():
    return [[1.0 if i == j else 0.0 for j in range(4)] for i in range(4)]


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def mat_inv(m):
    a = [row[:] + [1.0 if i == j else 0.0 for j in range(4)] for i, row in enumerate(m)]
    for c in range(4):
        p = max(range(c, 4), key=lambda r: abs(a[r][c]))
        a[c], a[p] = a[p], a[c]
        d = a[c][c]
        a[c] = [v / d for v in a[c]]
        for r in range(4):
            if r != c:
                f = a[r][c]
                a[r] = [x - f * y for x, y in zip(a[r], a[c])]
    return [row[4:] for row in a]


def column_major(m):
    return [m[r][c] for c in range(4) for r in range(4)]


def trs_matrix(t, r, s):
    x, y, z, w = r
    m = [
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), t[0]],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), t[1]],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), t[2]],
        [0.0, 0.0, 0.0, 1.0],
    ]
    for i in range(3):
        for j in range(3):
            m[i][j] *= s[j]
    return m


def flip(m):
    s = [-1.0, 1.0, 1.0, 1.0]
    return [[m[i][j] * s[i] * s[j] for j in range(4)] for i in range(4)]


def transform_point(m, v):
    return [m[r][0] * v[0] + m[r][1] * v[1] + m[r][2] * v[2] + m[r][3] for r in range(3)]


def transform_vector(m, v):
    return [m[r][0] * v[0] + m[r][1] * v[1] + m[r][2] * v[2] for r in range(3)]


def normalize(v):
    length = math.sqrt(sum(c * c for c in v)) or 1.0
    return [c / length for c in v]


def bundle_path(prefab_path):
    return os.path.join(GAME, *prefab_path.split("/"))


class Prefab:
    def __init__(self, path):
        sources = [path]
        shared = os.path.join(os.path.dirname(path), "client_assets.bundle")
        if os.path.exists(shared) and os.path.abspath(shared) != os.path.abspath(path):
            sources.append(shared)
        self.env = UnityPy.load(*sources)
        self.transforms, self.names, self.meshes, self.active = {}, {}, {}, {}
        filters = {}
        renderers = set()
        mesh_objects = {}
        for obj in self.env.objects:
            kind = obj.type.name
            if kind == "Transform":
                self.transforms[obj.path_id] = obj.read_typetree()
            elif kind == "GameObject":
                data = obj.read_typetree()
                self.names[obj.path_id] = data["m_Name"]
                self.active[obj.path_id] = data.get("m_IsActive", True)
            elif kind == "MeshFilter":
                data = obj.read_typetree()
                filters[data["m_GameObject"]["m_PathID"]] = data["m_Mesh"]["m_PathID"]
            elif kind == "MeshRenderer":
                data = obj.read_typetree()
                if data.get("m_Enabled", 1):
                    renderers.add(data["m_GameObject"]["m_PathID"])
            elif kind == "Mesh":
                mesh_objects[obj.path_id] = obj
        for go, mesh_id in filters.items():
            if go in renderers and mesh_id in mesh_objects:
                self.meshes[go] = mesh_objects[mesh_id]

    def name(self, pid):
        return self.names[self.transforms[pid]["m_GameObject"]["m_PathID"]]

    def game_object(self, pid):
        return self.transforms[pid]["m_GameObject"]["m_PathID"]

    def children(self, pid):
        return [c["m_PathID"] for c in self.transforms[pid]["m_Children"] if c["m_PathID"] in self.transforms]

    def roots(self):
        return [p for p, t in self.transforms.items() if t["m_Father"]["m_PathID"] == 0]

    def find(self, name, under=None):
        stack = [under] if under else self.roots()
        while stack:
            pid = stack.pop()
            if self.name(pid) == name:
                return pid
            stack.extend(self.children(pid))
        return None

    def local(self, pid):
        t = self.transforms[pid]
        p, q, s = t["m_LocalPosition"], t["m_LocalRotation"], t["m_LocalScale"]
        return [-p["x"], p["y"], p["z"]], [q["x"], -q["y"], -q["z"], q["w"]], [s["x"], s["y"], s["z"]]


class Builder:
    def __init__(self):
        self.data = bytearray()
        self.views = []
        self.accessors = []
        self.nodes = []
        self.meshes = []
        self.materials = []
        self.joints = []
        self.world = {}

    def add(self, fmt, values, component, kind, count, target=None, minmax=False):
        while len(self.data) % 4:
            self.data.append(0)
        offset = len(self.data)
        self.data += struct.pack("<" + fmt * len(values), *values)
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(self.data) - offset}
        if target:
            view["target"] = target
        self.views.append(view)
        accessor = {"bufferView": len(self.views) - 1, "componentType": component, "count": count, "type": kind}
        if minmax:
            accessor["min"] = [min(values[i::3]) for i in range(3)]
            accessor["max"] = [max(values[i::3]) for i in range(3)]
        self.accessors.append(accessor)
        return len(self.accessors) - 1

    def node(self, name, parent, t=None, r=None, s=None, matrix=None):
        node = {"name": name}
        if matrix is not None:
            node["matrix"] = column_major(matrix)
            local = matrix
        else:
            node["translation"], node["rotation"], node["scale"] = t, r, s
            local = trs_matrix(t, r, s)
        index = len(self.nodes)
        self.nodes.append(node)
        if parent is not None:
            self.nodes[parent].setdefault("children", []).append(index)
            self.world[index] = mat_mul(self.world[parent], local)
        else:
            self.world[index] = local
        return index

    def material(self, name, color):
        self.materials.append({"name": name, "pbrMetallicRoughness": {"baseColorFactor": color, "metallicFactor": 0.0, "roughnessFactor": 0.85}})
        return len(self.materials) - 1

    def static_mesh(self, mesh_obj, material):
        mesh = mesh_obj.read()
        h = MeshHandler(mesh)
        h.process()
        count = h.m_VertexCount
        attributes = {"POSITION": self.add("f", [c for v in h.m_Vertices for c in (-v[0], v[1], v[2])], 5126, "VEC3", count, 34962, True)}
        if h.m_Normals:
            attributes["NORMAL"] = self.add("f", [c for v in h.m_Normals for c in normalize([-v[0], v[1], v[2]])], 5126, "VEC3", count, 34962)
        return self.mesh_with_indices(mesh, h, attributes, material)

    def mesh_with_indices(self, mesh, h, attributes, material):
        size = 2 if mesh.m_Use16BitIndices else 4
        primitives = []
        for sub in mesh.m_SubMeshes:
            if sub.indexCount == 0 or sub.topology != 0:
                continue
            first = sub.firstByte // size
            base = getattr(sub, "baseVertex", 0) or 0
            tris = [t + base for t in h.m_IndexBuffer[first:first + sub.indexCount]]
            for k in range(0, len(tris) - 2, 3):
                tris[k + 1], tris[k + 2] = tris[k + 2], tris[k + 1]
            primitives.append({"attributes": attributes, "indices": self.add("I", tris, 5125, "SCALAR", len(tris), 34963), "material": material})
        self.meshes.append({"name": mesh.m_Name, "primitives": primitives})
        return len(self.meshes) - 1

    def write(self, path, scene_nodes, skins):
        doc = {
            "asset": {"version": "2.0", "generator": "Re-Classic rig exporter"},
            "scene": 0,
            "scenes": [{"nodes": scene_nodes}],
            "nodes": self.nodes,
            "meshes": self.meshes,
            "materials": self.materials,
            "skins": skins,
            "accessors": self.accessors,
            "bufferViews": self.views,
            "buffers": [{"byteLength": len(self.data)}],
        }
        payload = json.dumps(doc, separators=(",", ":")).encode("utf-8")
        payload += b" " * (-len(payload) % 4)
        while len(self.data) % 4:
            self.data.append(0)
        total = 12 + 8 + len(payload) + 8 + len(self.data)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, total))
            f.write(struct.pack("<II", len(payload), 0x4E4F534A))
            f.write(payload)
            f.write(struct.pack("<II", len(self.data), 0x004E4942))
            f.write(self.data)


def weapon_preset(weapon_id):
    with open(os.path.join(DATABASE, "templates", "items.json"), encoding="utf-8") as f:
        items = json.load(f)
    with open(os.path.join(DATABASE, "globals.json"), encoding="utf-8") as f:
        presets = json.load(f)["ItemPresets"]
    preset = next(p for p in presets.values() if p.get("_encyclopedia") == weapon_id)
    parts = preset["_items"]
    root = parts[0]
    prefab = lambda tpl: (items[tpl]["_props"].get("Prefab") or {}).get("path", "")
    mods = [{"id": p["_id"], "parent": p.get("parentId"), "slot": p.get("slotId"), "prefab": prefab(p["_tpl"]), "name": items[p["_tpl"]]["_name"]} for p in parts[1:]]
    return prefab(root["_tpl"]), root["_id"], mods


def export(skin_bundle, out_path, weapon_id=AK74):
    container_path, root_id, mods = weapon_preset(weapon_id)
    rig = Prefab(bundle_path(container_path))
    rig_root = next(p for p in rig.roots() if rig.name(p).endswith("_model.generated") or rig.name(p) == "weapon_empty_hands.generated")
    b = Builder()
    gun_material = b.material("Weapon", [0.18, 0.18, 0.17, 1.0])
    arms_material = b.material("Arms", [0.42, 0.40, 0.36, 1.0])
    rig_nodes = {}

    def walk_rig(pid, parent):
        name = rig.name(pid)
        go = rig.game_object(pid)
        if LOD_SKIP.search(name):
            return
        t, r, s = rig.local(pid)
        if go in rig.meshes and not rig.children(pid):
            index = b.node(name, parent, t, r, s)
            b.nodes[index]["mesh"] = b.static_mesh(rig.meshes[go], gun_material)
            return
        index = b.node(name, parent, t, r, s)
        rig_nodes[name] = rig_nodes.get(name, index)
        b.joints.append(index)
        if go in rig.meshes:
            b.node(name + "_mesh", index, [0, 0, 0], [0, 0, 0, 1], [1, 1, 1])
            b.nodes[-1]["mesh"] = b.static_mesh(rig.meshes[go], gun_material)
        for child in rig.children(pid):
            walk_rig(child, index)

    walk_rig(rig_root, None)

    slots = {root_id: (rig, None, rig_nodes)}
    pending = list(mods)
    attached = []
    while pending:
        progress = False
        for mod in list(pending):
            if mod["parent"] not in slots:
                continue
            pending.remove(mod)
            progress = True
            owner, owner_root, owner_nodes = slots[mod["parent"]]
            slot_node = owner_nodes.get(mod["slot"])
            if slot_node is None:
                print(f"  skipped {mod['name']}: no {mod['slot']} slot")
                continue
            try:
                prefab = Prefab(bundle_path(mod["prefab"]))
            except Exception as e:
                print(f"  skipped {mod['name']}: {e}")
                continue
            mod_root = prefab.roots()[0]
            mod_nodes = {}

            def walk_mod(pid, parent, is_root):
                name = prefab.name(pid)
                go = prefab.game_object(pid)
                if LOD_SKIP.search(name) or not prefab.active.get(go, True):
                    return
                if is_root:
                    half = math.sqrt(0.5)
                    index = b.node(name, parent, [0, 0, 0], [half, 0, 0, half], [1, 1, 1])
                else:
                    t, r, s = prefab.local(pid)
                    index = b.node(name, parent, t, r, s)
                mod_nodes[name] = index
                if go in prefab.meshes:
                    b.nodes[index]["mesh"] = b.static_mesh(prefab.meshes[go], gun_material)
                for child in prefab.children(pid):
                    walk_mod(child, index, False)

            walk_mod(mod_root, slot_node, True)
            slots[mod["id"]] = (prefab, mod_root, mod_nodes)
            attached.append(mod["name"])
        if not progress:
            break

    mesh, h, bone_names = load_arms(skin_bundle)
    missing = [n for n in bone_names if n not in rig_nodes]
    if missing:
        raise SystemExit("Bones missing from the rig: " + ", ".join(missing))
    joint_index = {node: i for i, node in enumerate(b.joints)}
    skin_joints = [joint_index[rig_nodes[n]] for n in bone_names]
    deform = [mat_mul(b.world[rig_nodes[n]], flip(bind_matrix(bp))) for n, bp in zip(bone_names, mesh.m_BindPose)]

    positions, normals, weights, indices = [], [], [], []
    for i in range(h.m_VertexCount):
        v = h.m_Vertices[i]
        n = h.m_Normals[i] if h.m_Normals else (0, 1, 0)
        w = h.m_BoneWeights[i]
        bi = h.m_BoneIndices[i]
        total = sum(w) or 1.0
        p = [0.0, 0.0, 0.0]
        nn = [0.0, 0.0, 0.0]
        src = [-v[0], v[1], v[2]]
        nsrc = [-n[0], n[1], n[2]]
        for j, x in zip(bi, w):
            if x <= 0:
                continue
            m = deform[j]
            q = transform_point(m, src)
            r = transform_vector(m, nsrc)
            for k in range(3):
                p[k] += q[k] * x / total
                nn[k] += r[k] * x / total
        positions += p
        normals += normalize(nn)
        weights += [x / total for x in w]
        indices += [skin_joints[j] if x > 0 else 0 for j, x in zip(bi, w)]
    count = h.m_VertexCount
    attributes = {
        "POSITION": b.add("f", positions, 5126, "VEC3", count, 34962, True),
        "NORMAL": b.add("f", normals, 5126, "VEC3", count, 34962),
        "TEXCOORD_0": b.add("f", [c for v in h.m_UV0 for c in (v[0], 1.0 - v[1])], 5126, "VEC2", count, 34962),
        "JOINTS_0": b.add("H", indices, 5123, "VEC4", count, 34962),
        "WEIGHTS_0": b.add("f", weights, 5126, "VEC4", count, 34962),
    }
    arms_mesh = b.mesh_with_indices(mesh, h, attributes, arms_material)
    inverse = []
    for node in b.joints:
        inverse += column_major(mat_inv(b.world[node]))
    ibm = b.add("f", inverse, 5126, "MAT4", len(b.joints))
    arms_node = len(b.nodes)
    b.nodes.append({"name": "Arms", "mesh": arms_mesh, "skin": 0})
    b.write(out_path, [0, arms_node], [{"name": "Rig", "skeleton": 0, "joints": b.joints, "inverseBindMatrices": ibm}])

    with open(os.path.splitext(out_path)[0] + "_bones.txt", "w", encoding="utf-8") as f:
        names = {i: b.nodes[i]["name"] for i in b.joints}
        parents = {c: p for p in b.joints for c in b.nodes[p].get("children", [])}

        def path(i):
            return names[i] if i not in parents or parents[i] == 0 else path(parents[i]) + "/" + names[i]

        for i in b.joints[1:]:
            f.write(path(i) + "\n")
    with open(os.path.splitext(out_path)[0] + "_rest.json", "w", encoding="utf-8") as f:
        json.dump({b.nodes[i]["name"]: column_major(b.world[i]) for i in b.joints}, f)
    print(f"Wrote {out_path}: {len(b.joints)} bones, {count} arm vertices, attachments: {', '.join(attached)}")


def bind_matrix(bp):
    return [[bp.e00, bp.e01, bp.e02, bp.e03], [bp.e10, bp.e11, bp.e12, bp.e13], [bp.e20, bp.e21, bp.e22, bp.e23], [bp.e30, bp.e31, bp.e32, bp.e33]]


def load_arms(bundle):
    env = UnityPy.load(bundle)
    mesh, bone_paths = None, None
    for obj in env.objects:
        if obj.type.name == "Mesh":
            mesh = obj.read()
        elif obj.type.name == "MonoBehaviour":
            data = obj.read_typetree()
            if "_bonePaths" in data:
                bone_paths = data["_bonePaths"]
    if mesh is None or bone_paths is None:
        raise SystemExit("No skinned arms mesh found in " + bundle)
    handler = MeshHandler(mesh)
    handler.process()
    return mesh, handler, [p.split("/")[-1] for p in bone_paths]


if __name__ == "__main__":
    skin = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SKIN
    out = sys.argv[2] if len(sys.argv) > 2 else r"C:\spt41\CutContent\RigExport\rig_ak74_bear.glb"
    export(skin, out)
