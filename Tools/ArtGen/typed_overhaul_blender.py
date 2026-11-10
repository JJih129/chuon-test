"""Iteration 16 Type-D boss overhaul props -- Blender 5.x headless.

  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b -P Tools/ArtGen/typed_overhaul_blender.py -- [out_dir] [preview_dir]

Writes Assets/Art/ChuonPolish/TypeD/Overhaul/Meshes/SM_OH_*.fbx (front = -Y in Blender = +Z in Unity).
Style: graphite / off-white ceramic / thin TSUKASA red light lines, hazard amber only on the lockdown shutter edge. Not neon.
Material slots reuse the infiltration kit names (INF_*), remapped to M_INF_*.mat by TypeDOverhaulBuilder.

Pieces
  PowerNode        wall power node (1.0 x 1.3 x 0.6 m), origin = centre, back plate on the wall: graphite housing,
                   ceramic bezel, glowing red core column behind a slatted glass window, cable stubs into the wall
  PowerNodeBroken  same housing with the window blown out, core dark (graphite), bent slats
  Shutter          lockdown blast shutter 5.0 x 6.5 m, origin = bottom centre: ribbed steel slats, hazard band at
                   the bottom lip, red status light line, ceramic side rails
  Cover            desperation cover block 2.4 x 2.6 x 0.9 m, origin = bottom centre: ceramic shield face, graphite frame
  VisorShard       dark glass visor fragment (~10 cm) with a red edge sliver
  ArmBlade         Type-D forearm blade (0.95 m), origin = mount; ceramic spine, graphite edge, red inlay line
  BladeShard       broken blade fragment
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import tutorial_city_blender as K  # noqa: E402

EXPORTED = []


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    root = os.path.abspath(os.path.join(HERE, "..", ".."))
    out = os.path.abspath(argv[0]) if len(argv) > 0 and argv[0] not in ("", "-") else os.path.join(root, "Assets", "Art", "ChuonPolish", "TypeD", "Overhaul")
    prev = os.path.abspath(argv[1]) if len(argv) > 1 and argv[1] not in ("", "-") else None
    os.makedirs(os.path.join(out, "Meshes"), exist_ok=True)
    return out, prev


def export(objs, name, out):
    path = os.path.join(out, "Meshes", "SM_OH_" + name + ".fbx")
    for o in objs:
        if o.type == 'MESH':
            K.metre_uv(o)
    for o in list(bpy.context.view_layer.objects):
        if o is not None:
            o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                             use_tspace=False, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
    tris = 0
    for o in objs:
        if o.type == 'MESH':
            o.data.calc_loop_triangles(); tris += len(o.data.loop_triangles)
    print("EXPORTED %-18s tris=%d" % (name, tris))
    EXPORTED.append((name, tris))


def fresh():
    K.reset()


# ------------------------------------------------------------------ pieces
def node_housing(broken):
    parts = []
    # wall back plate (y = +0.25 .. +0.3 is the wall side; front faces -Y)
    plate = K.box("Plate", (1.0, 0.06, 1.3), (0, 0.27, 0), "Graphite", chamfer=0.02)
    parts.append(plate)
    body = K.box("Body", (0.82, 0.42, 1.12), (0, 0.04, 0), "Graphite", chamfer=0.05)
    K.bevel(body, 0.012, 1, 35)
    parts.append(body)
    # ceramic bezel frame around the window
    for (sx, sz, lx, lz) in ((0.7, 0.07, 0, 0.42), (0.7, 0.07, 0, -0.42), (0.07, 0.84, 0.35, 0), (0.07, 0.84, -0.35, 0)):
        parts.append(K.box("Bezel", (sx, 0.05, sz), (lx, -0.19, lz), "Ceramic", chamfer=0.01))
    if not broken:
        core = K.cyl("Core", 0.13, 0.78, (0, -0.06, 0), "Red", verts=20)
        parts.append(core)
        win = K.box("Window", (0.62, 0.02, 0.78), (0, -0.2, 0), "Glass")
        parts.append(win)
        for i in range(5):
            z = -0.32 + i * 0.16
            parts.append(K.box("Slat", (0.62, 0.025, 0.03), (0, -0.215, z), "Graphite"))
    else:
        core = K.cyl("CoreDead", 0.11, 0.6, (0, -0.02, -0.08), "Graphite", verts=12)
        core.rotation_euler = (0.25, 0.4, 0)
        parts.append(core)
        for i in range(3):
            s = K.box("BentSlat", (0.5, 0.025, 0.03), (0.05 * i, -0.24 - 0.04 * i, -0.2 + i * 0.2), "Graphite")
            s.rotation_euler = (0.3 * i, 0.2, 0.5 - 0.4 * i)
            parts.append(s)
        for i in range(4):
            g = K.box("GlassShard", (0.12, 0.012, 0.09), (-0.2 + 0.13 * i, -0.21, -0.36 + 0.05 * i), "Glass")
            g.rotation_euler = (0.4, 0.7 * i, 0.2)
            parts.append(g)
    # red light lines on the top cap + status pips
    parts.append(K.box("RedLine", (0.6, 0.015, 0.02), (0, -0.215, 0.5), "Red" if not broken else "Graphite"))
    for i in range(3):
        parts.append(K.box("Pip", (0.04, 0.02, 0.04), (-0.28 + i * 0.07, -0.215, -0.5), "Red" if (not broken and i == 0) else "White"))
    # cable stubs into the wall
    for x in (-0.3, 0.3):
        c = K.cyl("Cable", 0.035, 0.3, (x, 0.18, -0.62), "Rubber", verts=8, axis='Y')
        parts.append(c)
    return K.join(parts, "PowerNodeBroken" if broken else "PowerNode")


def shutter():
    parts = []
    w, h = 5.0, 6.5
    slats = 18
    for i in range(slats):
        z = 0.32 + i * ((h - 0.5) / slats)
        s = K.box("Slat", (w - 0.3, 0.16, (h - 0.5) / slats - 0.03), (0, 0, z + (h - 0.5) / slats / 2), "Shutter", chamfer=0.01)
        parts.append(s)
    parts.append(K.boxz("Lip", (w - 0.3, 0.22, 0.3), (0, 0, 0), "Steel", chamfer=0.02))
    # hazard band at the lip (amber = interaction/hazard affordance in the level colour language)
    parts.append(K.boxz("Hazard", (w - 0.5, 0.02, 0.14), (0, -0.115, 0.08), "Hazard"))
    for x in (-w / 2 + 0.08, w / 2 - 0.08):
        parts.append(K.boxz("Rail", (0.16, 0.3, h), (x, 0, 0), "Ceramic", chamfer=0.02))
    parts.append(K.boxz("RedStatus", (w - 0.9, 0.02, 0.03), (0, -0.11, 0.36), "Red"))
    ob = K.join(parts, "Shutter")
    return ob


def cover():
    parts = []
    parts.append(K.boxz("Frame", (2.4, 0.9, 0.18), (0, 0, 0), "Graphite", chamfer=0.03))
    face = K.boxz("Face", (2.25, 0.7, 2.3), (0, 0, 0.18), "Ceramic", chamfer=0.06)
    K.bevel(face, 0.02, 1, 35)
    parts.append(face)
    for x in (-1.12, 1.12):
        parts.append(K.boxz("Post", (0.16, 0.86, 2.6), (x, 0, 0), "Graphite", chamfer=0.02))
    parts.append(K.boxz("Cap", (2.4, 0.86, 0.1), (0, 0, 2.5), "Graphite", chamfer=0.02))
    parts.append(K.boxz("RedLine", (1.9, 0.02, 0.025), (0, -0.36, 2.2), "Red"))
    return K.join(parts, "Cover")


def visor_shard():
    bm = bmesh.new()
    pts = [(-0.05, 0, -0.03), (0.06, 0, -0.04), (0.04, 0, 0.05), (-0.03, 0, 0.04)]
    vs = [bm.verts.new(p) for p in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, 0.012, 0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = K._link("VisorShard", bm, "Glass")
    edge = K.box("Edge", (0.1, 0.014, 0.008), (0.005, 0.006, -0.036), "Red")
    return K.join([ob, edge], "VisorShard")


def arm_blade():
    parts = []
    # mount collar at origin, blade runs along -Y (Unity +Z) from the wrist toward the elbow
    parts.append(K.box("Mount", (0.1, 0.16, 0.08), (0, -0.04, 0), "Graphite", chamfer=0.015))
    bm = bmesh.new()
    prof = [(0.0, 0.0), (0.035, -0.08), (0.045, -0.6), (0.02, -0.92), (0.0, -0.96), (-0.012, -0.6), (-0.012, -0.08)]
    vs = [bm.verts.new((x, y, -0.008)) for x, y in prof]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, 0, 0.016))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    blade = K._link("Blade", bm, "Ceramic")
    blade.location = (0.04, -0.1, 0.03)
    parts.append(blade)
    parts.append(K.box("Edge", (0.008, 0.8, 0.02), (0.088, -0.52, 0.03), "Steel"))
    parts.append(K.box("Inlay", (0.006, 0.6, 0.006), (0.055, -0.5, 0.042), "Red"))
    return K.join(parts, "ArmBlade")


def blade_shard():
    bm = bmesh.new()
    prof = [(0.0, 0.0), (0.04, -0.03), (0.035, -0.16), (0.0, -0.2), (-0.01, -0.05)]
    vs = [bm.verts.new((x, y, -0.008)) for x, y in prof]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, 0, 0.016))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K._link("BladeShard", bm, "Ceramic")


def main():
    out, prev = args()
    builders = [("PowerNode", lambda: node_housing(False)), ("PowerNodeBroken", lambda: node_housing(True)),
                ("Shutter", shutter), ("Cover", cover), ("VisorShard", visor_shard), ("ArmBlade", arm_blade),
                ("BladeShard", blade_shard)]
    for name, fn in builders:
        fresh()
        ob = fn()
        export([ob], name, out)
    if prev:
        os.makedirs(prev, exist_ok=True)
        fresh()
        x = 0.0
        for name, _ in builders:
            bpy.ops.import_scene.fbx(filepath=os.path.join(out, "Meshes", "SM_OH_" + name + ".fbx"))
            for o in bpy.context.selected_objects:
                o.location.x += x
            x += 3.5 if name in ("Shutter",) else 2.0
        cam = bpy.data.cameras.new("Cam")
        co = bpy.data.objects.new("Cam", cam)
        bpy.context.scene.collection.objects.link(co)
        co.location = (x * 0.5, 14, 4)
        co.rotation_euler = (math.radians(80), 0, math.radians(180))
        bpy.context.scene.camera = co
        sun = bpy.data.lights.new("Sun", 'SUN'); sun.energy = 4
        so = bpy.data.objects.new("Sun", sun); bpy.context.scene.collection.objects.link(so)
        so.rotation_euler = (0.8, 0.2, 0.6)
        sc = bpy.context.scene
        sc.render.engine = 'BLENDER_WORKBENCH'
        sc.render.resolution_x, sc.render.resolution_y = 1600, 600
        sc.render.filepath = os.path.join(prev, "typed_overhaul_props.png")
        bpy.ops.render.render(write_still=True)
    print("DONE", EXPORTED)


main()
