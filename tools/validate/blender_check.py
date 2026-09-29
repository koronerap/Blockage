"""Opens what Blockage exported in Blender and checks it arrived as it left (Fullreleaseplan 8.6).

    blender -b --factory-startup --python-exit-code 1 --python tools/validate/blender_check.py -- <folder>

<folder> is what `Blockage --export-to=<folder>` writes for its sample level: a little of
everything an export carries. Each format is imported into an empty scene and looked over; every
check prints PASS or FAIL, and any FAIL makes Blender exit with 1.
"""
import sys

import bpy
from mathutils import Vector

folder = sys.argv[sys.argv.index("--") + 1]
failures = []

MESHES = ["Demo", "Crate", "Crate copy", "Table", "Cup", "Small crate"]


def check(ok, message):
    print(("PASS " if ok else "FAIL ") + message)
    if not ok:
        failures.append(message)


def near(a, b, tolerance=0.02):
    return abs(a - b) <= tolerance


def world_bounds(obj):
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    low = Vector([min(c[i] for c in corners) for i in range(3)])
    high = Vector([max(c[i] for c in corners) for i in range(3)])
    return low, high


def textured(obj):
    for slot in obj.material_slots:
        material = slot.material
        if material and material.node_tree and any(
            node.type == "TEX_IMAGE" and node.image and node.image.size[0] > 0 for node in material.node_tree.nodes
        ):
            return True
    return False


def look_over(label, scene_graph, lights, spaces):
    # OBJ writes a name's spaces as underscores; the formats with a scene keep them.
    objects = {o.name.replace(spaces, " "): o for o in bpy.data.objects}
    for name in MESHES:
        obj = objects.get(name)
        check(obj is not None and obj.type == "MESH", f"{label}: {name} is a mesh")
        if obj is not None:
            check(textured(obj), f"{label}: {name} has its texture")

    # Blockage is Y up with its front on +Z; Blender is Z up with its front on -Y.
    if "Table" in objects:
        low, high = world_bounds(objects["Table"])
        check(
            all(near(a, b) for a, b in zip(low, (20, -4, 0))) and all(near(a, b) for a, b in zip(high, (26, 0, 3))),
            f"{label}: Table stands where it stood (from {tuple(round(v, 2) for v in low)} to {tuple(round(v, 2) for v in high)})",
        )

    if "Small crate" in objects:
        low, high = world_bounds(objects["Small crate"])
        check(all(near(h - l, 2) for l, h in zip(low, high)), f"{label}: Small crate is half-size voxels, 2 a side")

    if scene_graph:
        table = objects.get("Table")
        check(table is not None and table.type == "MESH" and len(table.data.uv_layers) >= 2, f"{label}: Table has lightmap UVs as its second UV map")
        cup = objects.get("Cup")
        check(cup is not None and cup.parent is not None and cup.parent.name == "Table", f"{label}: Cup is under Table")
        crate, copy = objects.get("Crate"), objects.get("Crate copy")
        check(crate is not None and copy is not None and crate.data == copy.data, f"{label}: Crate and its copy share one mesh")
        spawn = objects.get("Spawn")
        check(spawn is not None and spawn.type == "EMPTY", f"{label}: Spawn is an empty")
        check(spawn is not None and spawn.get("team") == "red", f"{label}: Spawn keeps its properties")

    if lights:
        check(objects.get("Sun") is not None and objects["Sun"].type == "LIGHT" and objects["Sun"].data.type == "SUN", f"{label}: the sun is a sun")
        check(objects.get("Lamp") is not None and objects["Lamp"].type == "LIGHT" and objects["Lamp"].data.type == "POINT", f"{label}: the lamp is a point light")
        collider = objects.get("Table-colonly")
        check(collider is not None and collider.parent is not None and collider.parent.name == "Table", f"{label}: Table's collision is under it")


def run(label, importer, path, scene_graph, lights, spaces=" "):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        importer(filepath=path)
    except Exception as problem:  # anything the importer refuses is itself the finding
        check(False, f"{label}: imports ({problem})")
        return

    check(True, f"{label}: imports")
    look_over(label, scene_graph, lights, spaces)


run("OBJ", bpy.ops.wm.obj_import, f"{folder}/sample.obj", scene_graph=False, lights=False, spaces="_")
run("GLB", bpy.ops.import_scene.gltf, f"{folder}/sample.glb", scene_graph=True, lights=True)
run("glTF", bpy.ops.import_scene.gltf, f"{folder}/sample.gltf", scene_graph=True, lights=True)
run("FBX", bpy.ops.import_scene.fbx, f"{folder}/sample.fbx", scene_graph=True, lights=False)

print(f"{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
