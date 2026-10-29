"""Turns a .blend into an .fbx Unity can read on its own.

Unity does not parse .blend files. It shells out to Blender and imports whatever comes
back, so on a machine without Blender every object made from one is a "Missing Prefab"
and the level has holes in it. Eighty of them had an .fbx of the same name sitting next
to them and were repaired by the swap tool; the four ladders did not, because nobody had
ever exported them.

This does that export, once, so the result can be committed and nobody needs Blender
again. It runs against the `bpy` module (`pip install bpy`), not an installed Blender:

    python3 blend_to_fbx.py Assets/St/St.blend Assets/St/St2.blend

Two things about the export are deliberate and both were measured rather than guessed.

THE VERTICES ARE LEFT WHERE THEY WERE MODELLED. The object's own loc/rot/scale is NOT
applied into the mesh. Vlad placed these ladders in the scene against the .blend import,
and the transform he dialled in -- scale (0.0188, 0.0181, 0.1538) on St -- only makes
sense against the raw mesh coordinates: 95.246 units of length times 0.018768 is 1.79 m,
which is the exact rise from the arena's ground floor to the walkway above it. Apply the
object transform and that same scale gives a five-centimetre pancake. Keeping the mesh
as modelled is what lets his placement survive.

THE FILE DECLARES METRES. Blender's exporter writes UnitScaleFactor = 1 by default --
"these numbers are centimetres" -- and then compensates with Lcl Scaling = 100 on the
root node, which Unity drops. That is the bug that made the whole dungeon pack import at
a two-hundredth of its size. apply_scale_options='FBX_SCALE_UNITS' moves the scale into
UnitScaleFactor where Unity reads it, and leaves the node alone.

The object is turned a quarter turn about X on the way out, and that is not a third
decision -- it is what KEEPS the first one. Blender models Z-up and Unity reads Y-up, so
the exporter puts a -90 degree turn on the node instead of in the mesh, and an importer
that collapses a single root node drops it: the ladder would come in a quarter turn out
of true, its 5.3-unit face swapped with its 3.1-unit edge. Turning the object +90 first
cancels it in the file, measured and not assumed: the node comes out at -4e-06 degrees
and the vertices are untouched.
"""
import math
import os
import sys


def convert(src, dst=None, quiet=False):
    """Writes <src>.fbx beside the .blend. Returns (path, {object: local bbox size})."""
    import bpy
    from mathutils import Matrix

    dst = dst or os.path.splitext(src)[0] + ".fbx"
    bpy.ops.wm.open_mainfile(filepath=src)

    sizes = {}
    for o in list(bpy.data.objects):
        if o.type != "MESH":
            continue
        me = o.data
        xs = [v.co.x for v in me.vertices]
        ys = [v.co.y for v in me.vertices]
        zs = [v.co.z for v in me.vertices]
        sizes[o.name] = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
        # The placement in the scene was authored against these coordinates, so the mesh
        # goes out exactly as it is -- and the quarter turn cancels the one the exporter
        # would otherwise leave on the node for Unity to drop.
        o.matrix_world = Matrix.Rotation(math.radians(90.0), 4, "X")

    bpy.ops.export_scene.fbx(
        filepath=dst, check_existing=False, use_selection=False,
        object_types={"MESH"}, use_mesh_modifiers=True, mesh_smooth_type="OFF",
        apply_unit_scale=True, global_scale=1.0,
        apply_scale_options="FBX_SCALE_UNITS",   # metres, declared where Unity reads it
        axis_forward="-Z", axis_up="Y",          # the Y-up file every importer expects
        use_space_transform=True, bake_space_transform=False,
        add_leaf_bones=False, bake_anim=False,
        path_mode="COPY", embed_textures=False)

    if not quiet:
        for name, (x, y, z) in sorted(sizes.items()):
            print(f"  {os.path.basename(dst)}: {name} is "
                  f"{x:.3f} x {y:.3f} x {z:.3f} as modelled")
    return dst, sizes


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    for src in argv:
        if not os.path.exists(src):
            print(f"  no such file: {src}")
            return 1
        dst, _ = convert(src)
        print(f"  wrote {dst} ({os.path.getsize(dst)} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
