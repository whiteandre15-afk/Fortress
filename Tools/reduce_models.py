"""Cuts heavy scanned rocks down to game-ready meshes with three detail levels.

Run with Blender, from the project root:
    /Applications/Blender.app/Contents/MacOS/Blender -b -P Tools/reduce_models.py

For each scan in ROCKS it moves the downloaded original out of the Unity project into
ArtSource/ (kept for re-running, never imported), splits sets of rocks into single rocks,
and writes Assets/Fortress/Art/<Name>/<Name>_<n>.fbx per rock holding Rock_LOD0/1/2.
Unity turns the _LOD names into a LOD group by itself. Decimating keeps the UVs, so the
scan's own textures still fit.
"""
import os
import shutil
import sys

import bpy

ART = "Assets/Fortress/Art"
SOURCE = "ArtSource"
# (Poly Haven's rock_face scans were tried as outcrops: they're one-sided cliff shells, not solid
# rocks, and tear apart when reduced.)
ROCKS = ["MossRocksA", "MossRocksB", "Boulder02", "Boulder03", "Boulder04", "Boulder06"]
# Triangles per detail level.
LEVELS = [6000, 2000, 600]
# Grass atlases: only their textures are used, the 1.6M-polygon models aren't needed at all.
TEXTURE_ONLY = ["GrassClumpsA", "GrassClumpsB"]


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def decimated(obj, target, name):
    """A copy of `obj` cut down to about `target` triangles."""
    copy = obj.copy()
    copy.data = obj.data.copy()
    copy.name = name
    bpy.context.collection.objects.link(copy)
    count = triangles(copy)
    if count > target:
        modifier = copy.modifiers.new("reduce", "DECIMATE")
        modifier.ratio = target / count
        bpy.context.view_layer.objects.active = copy
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return copy


def pieces(path):
    """Imports a scan and returns its rocks, biggest first, each with its origin at its base centre."""
    clear()
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    # Scans sometimes ship extra detail levels of their own; keep only the most detailed.
    meshes = [o for o in meshes if not any(tag in o.name for tag in ("LOD1", "LOD2", "LOD3"))]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # A set of rocks in one mesh becomes one object per loose rock.
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    rocks = [o for o in bpy.context.scene.objects if o.type == "MESH"]

    def size(o):
        d = o.dimensions
        return d.x * d.y * d.z

    rocks.sort(key=size, reverse=True)
    biggest = size(rocks[0])
    # Drop crumbs: anything under 2% of the biggest rock's volume.
    rocks = [o for o in rocks if size(o) > biggest * 0.02]
    for o in rocks:
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
        # Move the origin down to the rock's underside, so placing it puts it on the ground.
        low = min((o.matrix_world @ v.co).z for v in o.data.vertices)
        bpy.context.scene.cursor.location = (o.location.x, o.location.y, low)
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        o.location = (0, 0, 0)
    return rocks


def export(objects, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             bake_anim=False, apply_unit_scale=True)


def main():
    os.makedirs(SOURCE, exist_ok=True)
    for name in TEXTURE_ONLY:
        art = f"{ART}/{name}/{name}.fbx"
        if os.path.exists(art):
            shutil.move(art, f"{SOURCE}/{name}.fbx")
            if os.path.exists(art + ".meta"):
                os.remove(art + ".meta")
    for name in ROCKS:
        original, art = f"{SOURCE}/{name}.fbx", f"{ART}/{name}/{name}.fbx"
        if os.path.exists(art) and not os.path.exists(original):
            shutil.move(art, original)
            if os.path.exists(art + ".meta"):
                os.remove(art + ".meta")
        rocks = pieces(original)
        for index, rock in enumerate(rocks[:4]):
            levels = [decimated(rock, target, f"Rock_LOD{level}") for level, target in enumerate(LEVELS)]
            out = f"{ART}/{name}/{name}_{index}.fbx"
            export(levels, out)
            print(f"REDUCED {name}_{index}: {triangles(rock)} -> {[triangles(l) for l in levels]} triangles, "
                  f"size {tuple(round(d, 2) for d in rock.dimensions)}")
            for level in levels:
                bpy.data.objects.remove(level)
        # Unity reads the ROCK list from files on disk; the plain <name>.fbx must not linger.
        if os.path.exists(art):
            os.remove(art)
    # A marker so fetch_assets.py knows these were downloaded and reduced, not missing.
    print("done")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print("FAILED", error)
        sys.exit(1)
