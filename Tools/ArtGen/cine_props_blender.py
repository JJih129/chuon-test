"""Iteration 14 cinematic props -- Blender 5.x headless.

  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b -P Tools/ArtGen/cine_props_blender.py -- [out_dir] [preview_dir]

Writes Assets/Art/ChuonPolish/Cinematics/Meshes/SM_CINE_*.fbx (origin = base centre, front = -Y in Blender = +Z in Unity).
Style (Docs/Story_Reference.md): graphite / off-white ceramic / thin TSUKASA red light lines, worn resistance gear in concrete
and rubber. Not neon. Material slots reuse the infiltration kit names (INF_*), remapped to M_INF_*.mat by
Assets/Scripts/Cinematics/Editor/CinematicsAssetBuilder.cs. The Tsukasa hologram figure has one slot (CINE_Holo).

Pieces
  TsukasaFigure   1.84 m scientist silhouette: long coat, hands clasped behind the back, chin raised (hologram)
  HoloProjector   1.7 m floor emitter disc: graphite ring, red light ring, ceramic lens, 4 struts
  HideoutShell    12 x 9 x 4 m concrete room (inward normals), breached doorway on +Z wall, pipes
  MapTable        overturned operations table (tilted top + legs)
  CrateStack      3 stacked supply crates
  Cot             folding cot with blanket roll
  MonitorBank     desk + 3 monitors (screen faces = Glass; TMP text is placed by the builder)
  Barricade       sandbag barricade arc
  Rubble          concrete rubble chunks
  Banner          resistance banner (cloth panel, torn corner) on a pole
  ElevatorCab     2.6 x 2.6 x 3.0 m cab interior: graphite panels, ceramic trim, light strips, rail, door leaves
  FloorIndicator  indicator housing above the cab doors (screen = Glass)
  DroneHatch      lobby floor hatch frame (2.4 x 1.6 m) behind the front desk
  DroneHatchLeaf  one hatch leaf (hinge at origin edge), used twice (mirrored)
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import tutorial_city_blender as K  # noqa: E402  (shared primitives + INF_* material slots)

EXPORTED = []


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    root = os.path.abspath(os.path.join(HERE, "..", ".."))
    out = os.path.abspath(argv[0]) if len(argv) > 0 and argv[0] not in ("", "-") else os.path.join(root, "Assets", "Art", "ChuonPolish", "Cinematics")
    prev = os.path.abspath(argv[1]) if len(argv) > 1 and argv[1] not in ("", "-") else None
    os.makedirs(os.path.join(out, "Meshes"), exist_ok=True)
    return out, prev


def export(objs, name, out, uv=True):
    path = os.path.join(out, "Meshes", "SM_CINE_" + name + ".fbx")
    for o in objs:
        if o.type == 'MESH' and uv:
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
    print("EXPORTED %-22s tris=%d" % (name, tris))
    EXPORTED.append((name, tris))


# ------------------------------------------------------------------ organic helpers
def loft(name, sections, segs=24, mat="White", cap_bottom=True, cap_top=True):
    """sections: list of (z, rx, ry, cx, cy). Elliptical rings, quads between, optional caps."""
    bm = bmesh.new()
    rings = []
    for (z, rx, ry, cx, cy) in sections:
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            ring.append(bm.verts.new((cx + math.cos(a) * rx, cy + math.sin(a) * ry, z)))
        rings.append(ring)
    for r in range(len(rings) - 1):
        a, b = rings[r], rings[r + 1]
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap_bottom:
        bm.faces.new(list(reversed(rings[0])))
    if cap_top:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K._link(name, bm, mat)


def tube(name, pts, radii, segs=12, mat="White"):
    """Tube along a polyline (pts: list of Vector), radius per point. Rings oriented by segment tangent."""
    bm = bmesh.new()
    rings = []
    up = Vector((0, 0, 1))
    for k, p in enumerate(pts):
        if k == 0:
            t = (pts[1] - pts[0]).normalized()
        elif k == len(pts) - 1:
            t = (pts[-1] - pts[-2]).normalized()
        else:
            t = ((pts[k + 1] - pts[k]).normalized() + (pts[k] - pts[k - 1]).normalized()).normalized()
        ref = up if abs(t.dot(up)) < 0.95 else Vector((1, 0, 0))
        u = t.cross(ref).normalized()
        v = t.cross(u).normalized()
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            ring.append(bm.verts.new(p + (u * math.cos(a) + v * math.sin(a)) * radii[k]))
        rings.append(ring)
    for r in range(len(rings) - 1):
        a, b = rings[r], rings[r + 1]
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return K._link(name, bm, mat)


def ellipsoid(name, r, loc, mat="White", segs=20, rings=12):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * r[0], v.co.y * r[1], v.co.z * r[2]))
    ob = K._link(name, bm, mat)
    ob.location = loc
    return ob


def smooth(ob, level=1):
    for p in ob.data.polygons:
        p.use_smooth = True
    if level > 0:
        m = ob.modifiers.new("sub", 'SUBSURF'); m.levels = level; m.render_levels = level
        K._activate(ob)
        bpy.ops.object.modifier_apply(modifier=m.name)


def single_slot(ob, slot_name):
    ob.data.materials.clear()
    m = bpy.data.materials.get(slot_name) or bpy.data.materials.new(slot_name)
    m.diffuse_color = (0.9, 0.92, 0.95, 1)
    ob.data.materials.append(m)
    for p in ob.data.polygons:
        p.material_index = 0


# ------------------------------------------------------------------ pieces
def tsukasa_figure(out):
    """Founder silhouette. Front = -Y. Long coat hem at 0.28 m, shoulders 1.50 m, head top 1.84 m.
    Hands clasped behind the back (arms swept back), chin slightly up: the 'designer watching his orbit' pose."""
    parts = []
    # shoes + trouser legs below the coat hem
    for sx in (-1, 1):
        parts.append(ellipsoid("shoe", (0.065, 0.15, 0.055), (sx * 0.11, -0.05, 0.05), segs=14, rings=8))
        parts.append(loft("leg", [(0.07, 0.066, 0.07, sx * 0.11, 0.0), (0.16, 0.074, 0.078, sx * 0.108, 0.0),
                                  (0.36, 0.085, 0.088, sx * 0.105, 0.0)], 14))
    # long coat: flared hem -> waist -> chest -> shoulders (ellipses wider in X)
    coat = [
        (0.26, 0.30, 0.24, 0.0, 0.03),
        (0.45, 0.27, 0.20, 0.0, 0.02),
        (0.75, 0.215, 0.155, 0.0, 0.0),
        (0.98, 0.185, 0.130, 0.0, 0.0),   # waist (belted)
        (1.04, 0.192, 0.135, 0.0, 0.0),
        (1.22, 0.215, 0.140, 0.0, -0.005),
        (1.40, 0.245, 0.14, 0.0, 0.0),
        (1.47, 0.235, 0.125, 0.0, 0.01),
        (1.53, 0.12, 0.085, 0.0, 0.015),
    ]
    parts.append(loft("coat", coat, 28, cap_bottom=False))
    # hem thickness (inner skirt so the open bottom never shows a hole in scanlines)
    parts.append(loft("hem", [(0.27, 0.285, 0.225, 0.0, 0.03), (0.40, 0.255, 0.19, 0.0, 0.02)], 28, cap_bottom=True, cap_top=False))
    # coat front split line (thin dark slot reads as the coat opening in scanlines) + belt
    parts.append(K.box("belt", (0.38, 0.29, 0.035), (0, 0, 1.01), "White"))
    # high collar (stand-up, open at the front)
    parts.append(loft("collar", [(1.47, 0.12, 0.10, 0, 0.02), (1.60, 0.105, 0.088, 0, 0.03)], 20, cap_bottom=False, cap_top=False))
    # neck + head (chin up ~8 deg: head pushed back slightly)
    parts.append(loft("neck", [(1.50, 0.055, 0.058, 0, 0.02), (1.64, 0.05, 0.052, 0, 0.025)], 12))
    head = ellipsoid("head", (0.088, 0.105, 0.118), (0, 0.01, 1.735))
    head.rotation_euler = (math.radians(-8), 0, 0)
    parts.append(head)
    hair = ellipsoid("hair", (0.094, 0.104, 0.07), (0, 0.035, 1.79))
    hair.rotation_euler = (math.radians(-20), 0, 0)
    parts.append(hair)
    # arms: shoulder -> elbow (back) -> wrists meeting behind the lower back
    for sx in (-1, 1):
        pts = [Vector((sx * 0.215, 0.0, 1.43)), Vector((sx * 0.25, 0.07, 1.20)), Vector((sx * 0.22, 0.16, 1.02)),
               Vector((sx * 0.10, 0.19, 0.95)), Vector((sx * 0.02, 0.19, 0.93))]
        parts.append(tube("arm", pts, [0.062, 0.056, 0.05, 0.043, 0.04], 12))
    parts.append(ellipsoid("hands", (0.07, 0.05, 0.05), (0, 0.2, 0.93)))
    for p in parts:
        p.data.materials.clear()
    ob = K.join(parts, "SM_CINE_TsukasaFigure")
    smooth(ob, 1)
    single_slot(ob, "CINE_Holo")
    export([ob], "TsukasaFigure", out, uv=False); K.clear()


def holo_projector(out):
    p = []
    p.append(K.cyl("base", 0.85, 0.12, (0, 0, 0.06), "Graphite", 40))
    p.append(K.cyl("ring", 0.78, 0.03, (0, 0, 0.135), "Red", 40))
    p.append(K.cyl("deck", 0.72, 0.05, (0, 0, 0.15), "Graphite", 40))
    p.append(K.cyl("lens", 0.32, 0.06, (0, 0, 0.19), "Ceramic", 32))
    p.append(K.cyl("lensLight", 0.24, 0.02, (0, 0, 0.225), "White", 32))
    for i in range(4):
        a = math.radians(45 + 90 * i)
        x, y = math.cos(a) * 0.66, math.sin(a) * 0.66
        s = K.boxz("strut", (0.09, 0.09, 0.5), (x, y, 0.12), "Ceramic", chamfer=0.02)
        p.append(s)
        p.append(K.boxz("strutLine", (0.02, 0.02, 0.36), (x * 1.07, y * 1.07, 0.18), "Red"))
    ob = K.join(p, "SM_CINE_HoloProjector")
    K.bevel(ob, 0.008)
    export([ob], "HoloProjector", out); K.clear()


def hideout_shell(out):
    """Concrete room seen from inside: walls are boxes facing in. Doorway breach on the +Z (Unity) / -Y? -> the
    camera-facing breach is on the far wall (+Y in Blender = -Z Unity is the back). Breach on the back wall (+Y)."""
    W, D, H, T = 12.0, 9.0, 4.0, 0.4
    p = []
    p.append(K.boxz("floor", (W + 2 * T, D + 2 * T, T), (0, 0, -T), "Concrete"))
    p.append(K.boxz("ceil", (W + 2 * T, D + 2 * T, T), (0, 0, H), "Concrete"))
    # side walls
    p.append(K.boxz("wallL", (T, D, H), (-W / 2 - T / 2, 0, 0), "Concrete"))
    p.append(K.boxz("wallR", (T, D, H), (W / 2 + T / 2, 0, 0), "Concrete"))
    # front wall (-Y, behind the default camera) solid
    p.append(K.boxz("wallF", (W, T, H), (0, -D / 2 - T / 2, 0), "Concrete"))
    # back wall (+Y) with a breached doorway (x 1.2 .. 3.4) and a blast hole
    p.append(K.boxz("wallB1", (W / 2 + 1.2, T, H), (-(W / 2 + 1.2) / 2 + 1.2, D / 2 + T / 2, 0), "Concrete"))
    p.append(K.boxz("wallB2", (W / 2 - 3.4, T, H), (3.4 + (W / 2 - 3.4) / 2, D / 2 + T / 2, 0), "Concrete"))
    p.append(K.boxz("lintel", (2.2, T, 1.2), (2.3, D / 2 + T / 2, 2.8), "Concrete"))
    p.append(K.boxz("doorFrame", (0.12, 0.5, 2.8), (1.26, D / 2 + T / 2, 0), "Steel"))
    p.append(K.boxz("doorFrame2", (0.12, 0.5, 2.8), (3.34, D / 2 + T / 2, 0), "Steel"))
    # pipes along the ceiling + a cable tray
    for k, y in enumerate((-2.6, -2.2, 2.9)):
        c = K.cyl("pipe", 0.09 if k < 2 else 0.14, W, (0, y, H - 0.3 - 0.05 * k), "Steel", 12, 'X')
        p.append(c)
    p.append(K.boxz("tray", (W, 0.4, 0.06), (0, 1.0, H - 0.45), "Graphite"))
    # pillars (structural, worn)
    for x in (-3.0, 3.0):
        p.append(K.boxz("pillar", (0.6, 0.6, H), (x, 0.6, 0), "Concrete", chamfer=0.05))
    # floor hazard strip (old parking garage) + a scorch is a texture job; keep geometry
    p.append(K.boxz("strip", (W - 1.0, 0.12, 0.01), (0, -3.6, 0), "Hazard"))
    ob = K.join(p, "SM_CINE_HideoutShell")
    K.flat(ob)
    export([ob], "HideoutShell", out); K.clear()


def map_table(out):
    p = []
    top = K.box("top", (2.2, 1.2, 0.06), (0, 0, 0.0), "Graphite", chamfer=0.02)
    p.append(top)
    p.append(K.box("map", (2.0, 1.0, 0.01), (0, 0, 0.035), "Ceramic"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(K.box("leg", (0.06, 0.06, 0.8), (sx * 1.0, sy * 0.52, -0.43), "Steel"))
    ob = K.join(p, "table")
    # overturn: tipped onto its side edge, 72 deg, resting on the floor
    ob.rotation_euler = (math.radians(72), 0, math.radians(8))
    ob.location = (0, 0, 0.62)
    K.apply_transform(ob)
    ob.name = "SM_CINE_MapTable"
    K.flat(ob)
    export([ob], "MapTable", out); K.clear()


def crate_stack(out):
    p = []
    for i, (x, y, z, yaw, s) in enumerate([(0, 0, 0, 0, 1.0), (1.05, 0.05, 0, 6, 1.0), (0.45, 0.02, 0.62, -9, 0.92)]):
        c = K.boxz("crate", (1.0 * s, 0.7 * s, 0.6 * s), (0, 0, 0), "Container", chamfer=0.03)
        band = K.boxz("band", (1.02 * s, 0.72 * s, 0.06), (0, 0, 0.27 * s), "Graphite")
        lab = K.boxz("label", (0.3, 0.01, 0.16), (0.22, -0.355 * s, 0.33 * s), "White")
        for o in (c, band, lab):
            o.rotation_euler = (0, 0, math.radians(yaw)); o.location.x += x; o.location.y += y; o.location.z += z
        p += [c, band, lab]
    ob = K.join(p, "SM_CINE_CrateStack")
    K.flat(ob)
    export([ob], "CrateStack", out); K.clear()


def cot(out):
    p = [K.boxz("frameA", (0.04, 1.9, 0.04), (-0.34, 0, 0.38), "Steel"),
         K.boxz("frameB", (0.04, 1.9, 0.04), (0.34, 0, 0.38), "Steel"),
         K.boxz("canvas", (0.7, 1.86, 0.03), (0, 0, 0.4), "Rubber")]
    for sy in (-0.85, 0.85):
        for sx in (-0.3, 0.3):
            leg = K.boxz("leg", (0.03, 0.03, 0.42), (sx, sy, 0), "Steel")
            leg.rotation_euler = (math.radians(18 if sy > 0 else -18), 0, 0)
            p.append(leg)
    roll = K.cyl("roll", 0.13, 0.62, (0, 0.72, 0.56), "Concrete", 14, 'X')
    p.append(roll)
    blanket = K.boxz("blanket", (0.72, 0.9, 0.05), (0.02, -0.35, 0.43), "Concrete", chamfer=0.02)
    blanket.rotation_euler = (0, math.radians(3), math.radians(4))
    p.append(blanket)
    ob = K.join(p, "SM_CINE_Cot")
    export([ob], "Cot", out); K.clear()


def monitor_bank(out):
    p = [K.boxz("desk", (2.4, 0.8, 0.06), (0, 0, 0.74), "Graphite", chamfer=0.01)]
    for sx in (-1.1, 1.1):
        p.append(K.boxz("deskLeg", (0.06, 0.7, 0.74), (sx, 0, 0), "Steel"))
    for i, (x, yaw) in enumerate([(-0.75, 18), (0.0, 0), (0.75, -18)]):
        body = K.box("mon", (0.66, 0.06, 0.42), (0, 0, 0), "Graphite", chamfer=0.01)
        scr = K.box("screen", (0.6, 0.01, 0.36), (0, -0.036, 0.0), "Glass")
        stand = K.boxz("stand", (0.06, 0.06, 0.18), (0, 0.04, -0.39), "Steel")
        for o in (body, scr, stand):
            o.rotation_euler = (0, 0, math.radians(yaw))
            o.location = Vector((o.location.x, o.location.y, o.location.z)) + Vector((x, 0.1, 1.2))
            p.append(o)
    p.append(K.boxz("kb", (0.5, 0.18, 0.025), (0, -0.2, 0.8), "Graphite"))
    # anchors for TMP text: centre screen front (Unity: +Z = Blender -Y)
    ob = K.join(p, "SM_CINE_MonitorBank")
    K.flat(ob)
    a = bpy.data.objects.new("ScreenAnchor", None); bpy.context.scene.collection.objects.link(a)
    a.location = (0, 0.1 - 0.05, 1.2); a.parent = ob
    export([ob, a], "MonitorBank", out); K.clear()


def barricade(out):
    p = []
    for row in range(3):
        n = 6 - row
        for i in range(n):
            a = math.radians(-35 + 70 * (i + 0.5 * row) / max(1, 5))
            x, y = math.sin(a) * 1.8, -math.cos(a) * 0.6 + 0.6
            bag = K.box("bag", (0.55, 0.32, 0.2), (x, y, 0.1 + row * 0.18), "Concrete", chamfer=0.07)
            bag.rotation_euler = (0, 0, a)
            p.append(bag)
    ob = K.join(p, "SM_CINE_Barricade")
    for q in ob.data.polygons:
        q.use_smooth = True
    export([ob], "Barricade", out); K.clear()


def rubble(out):
    import random
    rnd = random.Random(14)
    p = []
    for i in range(14):
        s = rnd.uniform(0.12, 0.55)
        b = K.boxz("chunk", (s * rnd.uniform(0.8, 1.6), s * rnd.uniform(0.7, 1.3), s * rnd.uniform(0.4, 0.9)),
                   (rnd.uniform(-1.4, 1.4), rnd.uniform(-0.9, 0.9), -0.05), "Concrete", chamfer=0.04)
        b.rotation_euler = (rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4), rnd.uniform(0, 3.1))
        p.append(b)
    for i in range(3):
        r = K.cyl("rebar", 0.012, rnd.uniform(0.6, 1.1), (rnd.uniform(-1, 1), rnd.uniform(-0.6, 0.6), 0.25), "Steel", 6)
        r.rotation_euler = (rnd.uniform(0.6, 1.2), 0, rnd.uniform(0, 3))
        p.append(r)
    ob = K.join(p, "SM_CINE_Rubble")
    K.flat(ob)
    export([ob], "Rubble", out); K.clear()


def banner(out):
    """Resistance banner: rubber-black cloth, off-white broken-chain mark (geometry inlay), torn lower corner."""
    p = [K.cyl("pole", 0.025, 2.6, (0, 0, 1.3), "Steel", 8)]
    cloth = K.box("cloth", (1.0, 0.012, 1.5), (0.52, 0, 1.75), "Rubber")
    p.append(cloth)
    # mark: two chain links (ceramic) with a red break
    for k, z in enumerate((1.98, 1.62)):
        p.append(K.box("link", (0.34, 0.014, 0.16), (0.52, -0.004, z), "Ceramic", chamfer=0.05))
    p.append(K.box("break", (0.06, 0.016, 0.12), (0.52, -0.006, 1.80), "Red"))
    tear = K.box("tearMask", (0.3, 0.02, 0.3), (0.98, 0, 1.02), "Rubber")
    tear.rotation_euler = (0, math.radians(45), 0)
    ob = K.join(p, "banner")
    # cut the torn corner with a boolean
    b = ob.modifiers.new("tear", 'BOOLEAN'); b.object = tear; b.operation = 'DIFFERENCE'
    K._activate(ob); bpy.ops.object.modifier_apply(modifier=b.name)
    bpy.data.objects.remove(tear, do_unlink=True)
    ob.name = "SM_CINE_Banner"
    K.flat(ob)
    export([ob], "Banner", out); K.clear()


def elevator_cab(out):
    """Interior shell (inward), door wall at -Y (front, Unity +Z faces the doors from inside looking out)."""
    W, D, H, T = 2.6, 2.6, 3.0, 0.08
    p = []
    p.append(K.boxz("floor", (W, D, T), (0, 0, -T), "Graphite"))
    p.append(K.boxz("floorInlay", (W - 0.5, D - 0.5, 0.005), (0, 0, 0), "Steel"))
    p.append(K.boxz("ceil", (W, D, T), (0, 0, H), "Graphite"))
    for sx in (-1, 1):
        p.append(K.boxz("wallS", (T, D, H), (sx * (W / 2 + T / 2), 0, 0), "Graphite"))
        # ceramic panel trims + red hairline at waist height
        for k in range(3):
            p.append(K.boxz("panel", (0.012, 0.72, 2.2), (sx * (W / 2 - 0.006), -0.82 + 0.82 * k, 0.4), "Ceramic"))
        p.append(K.boxz("redLine", (0.01, D - 0.2, 0.02), (sx * (W / 2 - 0.012), 0, 1.02), "Red"))
        p.append(K.cyl("rail", 0.022, D - 0.4, (sx * (W / 2 - 0.08), 0, 0.95), "Steel", 10, 'Y'))
    p.append(K.boxz("wallBack", (W, T, H), (0, D / 2 + T / 2, 0), "Graphite"))
    for k in range(3):
        p.append(K.boxz("panelB", (0.72, 0.012, 2.2), (-0.82 + 0.82 * k, D / 2 - 0.006, 0.4), "Ceramic"))
    # front wall with door opening 1.3 m wide x 2.3 m
    p.append(K.boxz("frontL", ((W - 1.3) / 2, T, H), (-(W + 1.3) / 4, -D / 2 - T / 2, 0), "Graphite"))
    p.append(K.boxz("frontR", ((W - 1.3) / 2, T, H), ((W + 1.3) / 4, -D / 2 - T / 2, 0), "Graphite"))
    p.append(K.boxz("frontTop", (1.3, T, H - 2.3), (0, -D / 2 - T / 2, 2.3), "Graphite"))
    # ceiling light strips (white) + one red strip (emergency) for the flicker beat
    for x in (-0.7, 0.7):
        p.append(K.boxz("lightStrip", (0.12, D - 0.6, 0.02), (x, 0, H - 0.02), "White"))
    p.append(K.boxz("emStrip", (0.06, D - 0.6, 0.02), (0, 0, H - 0.02), "Red"))
    # control column next to the door
    p.append(K.boxz("panelCol", (0.18, 0.04, 0.5), (0.9, -D / 2 + 0.02, 1.0), "Steel"))
    p.append(K.boxz("button", (0.06, 0.012, 0.06), (0.9, -D / 2 + 0.045, 1.2), "Amber"))
    ob = K.join(p, "SM_CINE_ElevatorCab")
    K.flat(ob)
    # door leaves exported in the same file as separate objects (animated in Unity)
    l = K.boxz("DoorLeafL", (0.66, 0.05, 2.3), (-0.33, -D / 2 - T - 0.02, 0), "Steel")
    r = K.boxz("DoorLeafR", (0.66, 0.05, 2.3), (0.33, -D / 2 - T - 0.02, 0), "Steel")
    for leaf in (l, r):
        K.apply_transform(leaf)
    export([ob, l, r], "ElevatorCab", out); K.clear()


def floor_indicator(out):
    p = [K.box("housing", (0.7, 0.08, 0.26), (0, 0, 0), "Graphite", chamfer=0.02),
         K.box("screen", (0.6, 0.01, 0.18), (0, -0.045, 0), "Glass"),
         K.box("redTick", (0.6, 0.012, 0.012), (0, -0.046, -0.1), "Red")]
    ob = K.join(p, "SM_CINE_FloorIndicator")
    K.bevel(ob, 0.004)
    export([ob], "FloorIndicator", out); K.clear()


def drone_hatch(out):
    W, D = 2.4, 1.6
    p = [K.boxz("frameN", (W + 0.3, 0.15, 0.06), (0, D / 2 + 0.075, -0.03), "Graphite"),
         K.boxz("frameS", (W + 0.3, 0.15, 0.06), (0, -D / 2 - 0.075, -0.03), "Graphite"),
         K.boxz("frameW", (0.15, D, 0.06), (-W / 2 - 0.075, 0, -0.03), "Graphite"),
         K.boxz("frameE", (0.15, D, 0.06), (W / 2 + 0.075, 0, -0.03), "Graphite"),
         K.boxz("well", (W, D, 0.02), (0, 0, -1.6), "Graphite")]
    for sx in (-1, 1):
        p.append(K.boxz("wellWall", (0.02, D, 1.6), (sx * W / 2, 0, -1.6), "Steel"))
    for sy in (-1, 1):
        p.append(K.boxz("wellWall2", (W, 0.02, 1.6), (0, sy * D / 2, -1.6), "Steel"))
        p.append(K.boxz("hazard", (W + 0.3, 0.05, 0.005), (0, sy * (D / 2 + 0.12), 0.0), "Hazard"))
    p.append(K.boxz("redRing", (W - 0.1, 0.03, 0.02), (0, D / 2 - 0.02, -0.2), "Red"))
    p.append(K.boxz("redRing2", (W - 0.1, 0.03, 0.02), (0, -D / 2 + 0.02, -0.2), "Red"))
    ob = K.join(p, "SM_CINE_DroneHatch")
    K.flat(ob)
    export([ob], "DroneHatch", out); K.clear()
    # leaf: hinge on origin edge (x = 0), extends +X by W/2
    leaf = K.boxz("leaf", (W / 2, D, 0.05), (W / 4, 0, -0.05), "Graphite", chamfer=0.01)
    stripe = K.boxz("stripe", (W / 2 - 0.2, 0.06, 0.005), (W / 4, 0, 0.0), "Ceramic")
    ob = K.join([leaf, stripe], "SM_CINE_DroneHatchLeaf")
    K.flat(ob)
    export([ob], "DroneHatchLeaf", out); K.clear()


# ------------------------------------------------------------------ preview
def contact_preview(out, prev):
    if not prev:
        return
    os.makedirs(prev, exist_ok=True)
    K.clear()
    names = ["TsukasaFigure", "HoloProjector", "MapTable", "CrateStack", "Cot", "MonitorBank", "Barricade", "Rubble",
             "Banner", "FloorIndicator", "DroneHatch", "DroneHatchLeaf"]
    x = 0.0
    for n in names:
        bpy.ops.import_scene.fbx(filepath=os.path.join(out, "Meshes", "SM_CINE_" + n + ".fbx"))
        objs = list(bpy.context.selected_objects)
        mn = min((o.matrix_world @ Vector(c)).x for o in objs if o.type == 'MESH' for c in o.bound_box)
        mx = max((o.matrix_world @ Vector(c)).x for o in objs if o.type == 'MESH' for c in o.bound_box)
        for o in objs:
            if o.parent is None:
                o.location.x += x - mn
        x += (mx - mn) + 0.6
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            for ms in o.material_slots:
                if ms.material and ms.material.name.split('.')[0] in ("INF_Red", "INF_Amber", "INF_White", "CINE_Holo"):
                    m = ms.material; m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
                    o_ = nt.nodes.new("ShaderNodeOutputMaterial"); e = nt.nodes.new("ShaderNodeEmission")
                    col = (0.9, 0.93, 1.0, 1) if "Holo" in m.name else m.diffuse_color
                    e.inputs[0].default_value = col; e.inputs[1].default_value = 3
                    nt.links.new(e.outputs[0], o_.inputs[0])
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = 2000; sc.render.resolution_y = 600
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.07, 1)
    ld = bpy.data.lights.new("sun", 'SUN'); ld.energy = 3; lo = bpy.data.objects.new("sun", ld); sc.collection.objects.link(lo)
    lo.rotation_euler = (math.radians(55), 0, math.radians(30))
    cd = bpy.data.cameras.new("c"); cd.type = 'ORTHO'; cd.ortho_scale = x * 0.5
    cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (x / 2, -30, 1.4); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.render.filepath = os.path.join(prev, "cine_props_preview.png")
    bpy.ops.render.render(write_still=True)
    # second view: the two interiors
    K.clear()
    for n, ox in (("ElevatorCab", 0.0), ("HideoutShell", 10.0)):
        bpy.ops.import_scene.fbx(filepath=os.path.join(out, "Meshes", "SM_CINE_" + n + ".fbx"))
        for o in bpy.context.selected_objects:
            if o.parent is None:
                o.location.x += ox
    cd2 = bpy.data.cameras.new("c2"); cd2.lens = 18
    cam2 = bpy.data.objects.new("c2", cd2); sc.collection.objects.link(cam2); sc.camera = cam2
    cam2.location = (10.0, -4.0, 1.7); cam2.rotation_euler = (math.radians(88), 0, 0)
    lp = bpy.data.lights.new("p", 'POINT'); lp.energy = 2000; lpo = bpy.data.objects.new("p", lp); sc.collection.objects.link(lpo)
    lpo.location = (10.0, 0, 3.2)
    sc.render.resolution_x = 1280; sc.render.resolution_y = 720
    sc.render.filepath = os.path.join(prev, "cine_hideout_preview.png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    OUT, PREV = args()
    K.reset()
    tsukasa_figure(OUT); holo_projector(OUT); hideout_shell(OUT); map_table(OUT); crate_stack(OUT); cot(OUT)
    monitor_bank(OUT); barricade(OUT); rubble(OUT); banner(OUT); elevator_cab(OUT); floor_indicator(OUT)
    drone_hatch(OUT)
    print("CINE KIT", sum(t for _, t in EXPORTED), "tris total;", ", ".join("%s=%d" % e for e in EXPORTED))
    contact_preview(OUT, PREV)
