"""Infiltration tutorial (iteration 13) environment kit -- Blender 5.x headless.

  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b -P Tools/ArtGen/tutorial_city_blender.py -- [out_dir] [preview_dir]

Writes Assets/Art/ChuonPolish/Tutorial/Meshes/SM_INF_*.fbx (one kit piece per file, origin = base centre,
front = -Y in Blender = +Z in Unity). Style: corporate-military graphite + off-white ceramic + thin TSUKASA red
light lines; near-future far-east city block at dusk (Arasaka/Militech = style reference only, not neon).

Material slots (FBX material names, remapped by InfiltrationMapBuilder.cs):
  INF_Graphite INF_Ceramic INF_Concrete INF_Glass INF_Red INF_Amber INF_White INF_Rubber INF_Hazard
  INF_FacadeA INF_FacadeB INF_Container INF_Shutter INF_Steel
UVs are world-space metres (box projection per face); Unity materials set tiling (e.g. facade 1/12 x 1/14.4).
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

MATS = ["Graphite", "Ceramic", "Concrete", "Glass", "Red", "Amber", "White", "Rubber", "Hazard",
        "FacadeA", "FacadeB", "Container", "Shutter", "Steel"]
PREVIEW = {"Graphite": (0.05, 0.052, 0.058), "Ceramic": (0.7, 0.7, 0.68), "Concrete": (0.32, 0.32, 0.31),
           "Glass": (0.03, 0.05, 0.07), "Red": (1.0, 0.05, 0.05), "Amber": (1.0, 0.5, 0.05), "White": (1, 1, 1),
           "Rubber": (0.02, 0.02, 0.02), "Hazard": (0.8, 0.5, 0.05), "FacadeA": (0.08, 0.1, 0.13),
           "FacadeB": (0.4, 0.4, 0.42), "Container": (0.2, 0.2, 0.22), "Shutter": (0.35, 0.35, 0.36), "Steel": (0.3, 0.31, 0.33)}
MAT = {}


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.abspath(os.path.join(here, "..", ".."))
    out = os.path.abspath(argv[0]) if len(argv) > 0 and argv[0] not in ("", "-") else os.path.join(root, "Assets", "Art", "ChuonPolish", "Tutorial")
    prev = os.path.abspath(argv[1]) if len(argv) > 1 and argv[1] not in ("", "-") else None
    os.makedirs(os.path.join(out, "Meshes"), exist_ok=True)
    return out, prev


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MAT.clear()
    for n in MATS:
        m = bpy.data.materials.new("INF_" + n)
        m.diffuse_color = (*PREVIEW[n], 1.0)
        MAT[n] = m


# ------------------------------------------------------------------ primitives
def _link(name, bm, mat):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for n in MATS:
        ob.data.materials.append(MAT[n])
    idx = MATS.index(mat)
    for p in ob.data.polygons:
        p.material_index = idx
    return ob


def box(name, size, loc, mat="Graphite", chamfer=0.0):
    """Axis box with min-corner-free centre `loc` (x, y, z of the box CENTRE)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if chamfer > 0:
        edges = [e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co).normalized().z) > 0.99]
        bmesh.ops.bevel(bm, geom=edges, offset=chamfer, segments=1, affect='EDGES', profile=0.5)
    ob = _link(name, bm, mat)
    ob.location = loc
    return ob


def boxz(name, size, base, mat="Graphite", chamfer=0.0):
    """Box standing on z = base[2] (base = centre x, centre y, bottom z)."""
    return box(name, size, (base[0], base[1], base[2] + size[2] / 2), mat, chamfer)


def cyl(name, r, depth, loc, mat="Steel", verts=16, axis='Z', r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r, radius2=r if r2 is None else r2, depth=depth)
    ob = _link(name, bm, mat)
    if axis == 'X':
        ob.rotation_euler = (0, math.pi / 2, 0)
    elif axis == 'Y':
        ob.rotation_euler = (math.pi / 2, 0, 0)
    ob.location = loc
    return ob


def prism(name, profile_xz, depth, loc, mat="Concrete"):
    """Extrude a closed XZ polygon along Y by depth (centred on loc)."""
    bm = bmesh.new()
    vs = [bm.verts.new((x, -depth / 2, z)) for x, z in profile_xz]
    f = bm.faces.new(vs)
    bmesh.ops.recalc_face_normals(bm, faces=[f])
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, depth, 0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _link(name, bm, mat)
    ob.location = loc
    return ob


def _activate(ob):
    for o in list(bpy.context.view_layer.objects):
        if o is not None:
            o.select_set(False)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)


def apply_transform(ob):
    _activate(ob)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def join(objs, name):
    objs = [o for o in objs if o is not None]
    for o in objs:
        apply_transform(o)
    for o in list(bpy.context.view_layer.objects):
        if o is not None:
            o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name; ob.data.name = name
    return ob


def bevel(ob, width=0.02, segments=1, angle=40.0):
    for p in ob.data.polygons:
        p.use_smooth = True
    b = ob.modifiers.new("bevel", 'BEVEL')
    b.width = width; b.segments = segments; b.limit_method = 'ANGLE'; b.angle_limit = math.radians(angle)
    b.harden_normals = True
    w = ob.modifiers.new("wn", 'WEIGHTED_NORMAL'); w.keep_sharp = True
    _activate(ob)
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def flat(ob):
    for p in ob.data.polygons:
        p.use_smooth = False


def metre_uv(ob):
    """World-metre box projection per face (u along the face's horizontal, v up)."""
    me = ob.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uvl = me.uv_layers.active.data
    for p in me.polygons:
        n = p.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        if ax == 0:
            tu, tv = Vector((0, 1 if n.x < 0 else -1, 0)), Vector((0, 0, 1))
        elif ax == 1:
            tu, tv = Vector((1 if n.y < 0 else -1, 0, 0)), Vector((0, 0, 1))
        else:
            tu, tv = Vector((1, 0, 0)), Vector((0, 1, 0))
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = (-c.dot(tu), c.dot(tv))  # Blender->Unity mirrors X: pre-mirror u so text reads correctly


def set_mat_where(ob, mat, pred):
    idx = MATS.index(mat)
    for p in ob.data.polygons:
        if pred(p.center, p.normal):
            p.material_index = idx


EXPORTED = []


def export(objs, name, out):
    path = os.path.join(out, "Meshes", "SM_INF_" + name + ".fbx")
    for o in objs:
        if o.type == 'MESH':
            metre_uv(o)
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
    print("EXPORTED %-28s tris=%d" % (name, tris))
    EXPORTED.append((name, tris))


def clear():
    for o in list(bpy.context.scene.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    bpy.context.view_layer.update()


# ------------------------------------------------------------------ pieces
def hq_tower(out):
    """30 x 30 m footprint, 220 m. Glass body (FacadeA), ceramic corner piers with red light grooves,
    graphite fins, setback crown with emblem mount (south = -Y), aviation lights."""
    parts = []
    W, H = 30.0, 196.0
    parts.append(boxz("body", (W - 2.4, W - 2.4, H), (0, 0, 0), "FacadeA"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(boxz("pier", (2.6, 2.6, H + 4), (sx * (W / 2 - 1.3), sy * (W / 2 - 1.3), 0), "Ceramic", chamfer=0.35))
            # red light groove on the outward diagonal
            parts.append(boxz("groove", (0.18, 0.18, H - 6), (sx * (W / 2 + 0.02), sy * (W / 2 + 0.02), 4), "Red"))
    # vertical fins on each face every 3.4 m
    for i in range(-3, 4):
        x = i * 3.4
        parts.append(boxz("finS", (0.22, 0.9, H - 2), (x, -(W / 2 - 1.0), 2), "Graphite"))
        parts.append(boxz("finN", (0.22, 0.9, H - 2), (x, (W / 2 - 1.0), 2), "Graphite"))
        parts.append(boxz("finW", (0.9, 0.22, H - 2), (-(W / 2 - 1.0), x, 2), "Graphite"))
        parts.append(boxz("finE", (0.9, 0.22, H - 2), ((W / 2 - 1.0), x, 2), "Graphite"))
    # floor bands every 24 m (mechanical floors)
    for k in range(1, 8):
        parts.append(boxz("band", (W - 1.6, W - 1.6, 1.4), (0, 0, k * 24.0), "Graphite"))
    # crown
    parts.append(boxz("crown", (W - 6, W - 6, 22), (0, 0, H), "Ceramic", chamfer=1.2))
    parts.append(boxz("crownTop", (W - 9, W - 9, 3), (0, 0, H + 22), "Graphite"))
    parts.append(boxz("crownLine", (W - 5.6, W - 5.6, 0.3), (0, 0, H + 21.6), "Red"))
    # emblem mount plate (south face) + the emblem itself is a Unity quad on "EmblemAnchor"
    parts.append(boxz("emblemPlate", (17, 0.6, 17), (0, -(W - 6) / 2 - 0.3, H + 2.5), "Graphite", chamfer=0.4))
    # antenna + aviation lights
    parts.append(cyl("mast", 0.35, 18, (0, 0, H + 25 + 9), "Steel", 10))
    parts.append(boxz("avi", (0.6, 0.6, 0.6), (0, 0, H + 43), "Red"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(boxz("avi2", (0.5, 0.5, 0.5), (sx * (W / 2 - 4), sy * (W / 2 - 4), H + 25), "Red"))
    ob = join(parts, "SM_INF_HQTower")
    flat(ob)
    anchor = bpy.data.objects.new("EmblemAnchor", None); bpy.context.scene.collection.objects.link(anchor)
    anchor.location = (0, -(W - 6) / 2 - 0.65, H + 11)
    anchor.parent = ob
    export([ob, anchor], "HQTower", out); clear()


def hq_podium(out):
    """100 (x) x 50 (y) x 22 m podium. South face (-Y) has: sealed main entrance (centre, LOCKDOWN red),
    side entrance B at x = +26 (recess 8 m wide x 6.5 m tall x 12 m deep -> glass lobby doors at the back)."""
    p = []
    X0, X1, D, H = -50.0, 50.0, 50.0, 22.0
    ex, ew, eh, ed = 26.0, 8.0, 6.5, 12.0
    # front wall segments around the side-entrance opening (wall thickness 1 m at y = 0..1)
    p.append(boxz("wL", (ex - ew / 2 - X0, 1.0, H), ((X0 + ex - ew / 2) / 2, 0.5, 0), "Ceramic"))
    p.append(boxz("wR", (X1 - (ex + ew / 2), 1.0, H), ((ex + ew / 2 + X1) / 2, 0.5, 0), "Ceramic"))
    p.append(boxz("wTop", (ew, 1.0, H - eh), (ex, 0.5, eh), "Ceramic"))
    # body behind (solid block, starts behind the vestibule depth so the recess stays hollow)
    p.append(boxz("bodyL", (ex - ew / 2 - X0, D - 1, H), ((X0 + ex - ew / 2) / 2, 1 + (D - 1) / 2, 0), "Ceramic"))
    p.append(boxz("bodyR", (X1 - (ex + ew / 2), D - 1, H), ((ex + ew / 2 + X1) / 2, 1 + (D - 1) / 2, 0), "Ceramic"))
    p.append(boxz("bodyBack", (ew, D - 1 - ed, H), (ex, 1 + ed + (D - 1 - ed) / 2, 0), "Ceramic"))
    p.append(boxz("bodyTop", (ew, ed, H - eh), (ex, 1 + ed / 2, eh), "Ceramic"))
    # vestibule lining (graphite walls, white ceiling light strips)
    p.append(boxz("vL", (0.3, ed, eh), (ex - ew / 2 + 0.15, 1 + ed / 2, 0), "Graphite"))
    p.append(boxz("vR", (0.3, ed, eh), (ex + ew / 2 - 0.15, 1 + ed / 2, 0), "Graphite"))
    p.append(boxz("vCeil", (ew, ed, 0.3), (ex, 1 + ed / 2, eh - 0.3), "Graphite"))
    for k in range(4):
        p.append(boxz("vLight", (ew - 1.6, 0.25, 0.05), (ex, 2.5 + k * 2.8, eh - 0.36), "White"))
    p.append(boxz("vFloor", (ew, ed + 1, 0.12), (ex, 0.5 + ed / 2, 0), "Concrete"))
    # glass lobby doors at the back of the vestibule (y = 1 + ed): frame + glass + amber panel slot
    gy = 1 + ed - 0.2
    p.append(boxz("gFrameT", (ew - 0.6, 0.35, 0.5), (ex, gy, eh - 0.8), "Graphite"))
    for dx in (-3.5, -1.2, 1.2, 3.5):
        p.append(boxz("gMull", (0.18, 0.35, eh - 0.8), (ex + dx, gy, 0), "Graphite"))
    p.append(boxz("gGlass", (ew - 0.8, 0.06, eh - 0.8), (ex, gy, 0), "Glass"))
    p.append(boxz("gRedLine", (ew - 0.8, 0.08, 0.06), (ex, gy - 0.2, eh - 0.9), "Red"))
    # entrance canopy
    p.append(boxz("canopy", (ew + 6, 5.0, 0.45), (ex, -2.0, eh + 0.2), "Graphite", chamfer=0.1))
    p.append(boxz("canopyLine", (ew + 5.6, 0.08, 0.06), (ex, -4.52, eh + 0.3), "White"))
    # ground floor glazing band along the south face (shopfront look) + graphite plinth
    p.append(boxz("plinthL", (ex - ew / 2 - X0, 1.6, 0.7), ((X0 + ex - ew / 2) / 2, -0.3, 0), "Graphite"))
    p.append(boxz("plinthR", (X1 - (ex + ew / 2), 1.6, 0.7), ((ex + ew / 2 + X1) / 2, -0.3, 0), "Graphite"))
    for s0, s1 in ((X0 + 2, ex - ew / 2 - 2), (ex + ew / 2 + 2, X1 - 2)):
        p.append(boxz("band", (s1 - s0, 0.2, 3.6), ((s0 + s1) / 2, -0.05, 0.9), "Glass"))
    # horizontal red line at 8 m (corporate trim) and white top line
    p.append(boxz("redTrim", (X1 - X0, 0.12, 0.12), (0, -0.07, 8.0), "Red"))
    p.append(boxz("topTrim", (X1 - X0 + 0.4, 1.4, 0.8), (0, 0.4, H), "Graphite"))
    # sealed main entrance (centre): big graphite shutters + red status strip
    p.append(boxz("mainRecess", (16, 0.4, 9), (0, -0.21, 0), "Graphite"))
    for k in range(4):
        p.append(boxz("mainShut", (3.6, 0.2, 8.4), (-5.7 + k * 3.8, -0.45, 0), "Shutter"))
    p.append(boxz("mainRed", (15.6, 0.1, 0.12), (0, -0.62, 8.6), "Red"))
    ob = join(p, "SM_INF_HQPodium")
    flat(ob)
    a1 = bpy.data.objects.new("LobbyDoorAnchor", None); bpy.context.scene.collection.objects.link(a1)
    a1.location = (ex, gy - 0.8, 0); a1.parent = ob
    a2 = bpy.data.objects.new("SideSignAnchor", None); bpy.context.scene.collection.objects.link(a2)
    a2.location = (ex, -4.6, eh + 1.6); a2.parent = ob
    export([ob, a1, a2], "HQPodium", out); clear()


def security_wall(out):
    """10 m segment, 0.8 m thick, 8 m tall: graphite base, ceramic panels, red line, top cap with spikes."""
    p = [boxz("base", (10, 0.9, 1.2), (0, 0, 0), "Graphite", chamfer=0.05),
         boxz("panel", (9.96, 0.7, 6.0), (0, 0, 1.2), "Ceramic"),
         boxz("cap", (10, 1.0, 0.6), (0, 0, 7.2), "Graphite"),
         boxz("red", (10, 0.05, 0.08), (0, -0.37, 6.9), "Red"),
         boxz("redB", (10, 0.05, 0.08), (0, 0.37, 6.9), "Red")]
    for k in range(5):
        p.append(boxz("seam", (0.06, 0.74, 6.0), (-4 + k * 2.0, 0, 1.2), "Graphite"))
    for k in range(10):
        p.append(boxz("spike", (0.08, 0.6, 0.5), (-4.5 + k, 0, 7.8), "Steel"))
    ob = join(p, "SM_INF_SecurityWall"); bevel(ob, 0.015)
    export([ob], "SecurityWall", out); clear()


def wall_pillar(out):
    p = [boxz("pil", (1.4, 1.4, 9.0), (0, 0, 0), "Graphite", chamfer=0.15),
         boxz("lamp", (1.0, 0.1, 0.25), (0, -0.72, 8.2), "White"),
         boxz("red", (0.1, 0.1, 7.0), (0, -0.72, 0.8), "Red")]
    ob = join(p, "SM_INF_WallPillar"); bevel(ob, 0.02)
    export([ob], "WallPillar", out); clear()


def checkpoint_booth(out):
    p = [boxz("floor", (4.4, 3.4, 0.3), (0, 0, 0), "Graphite"),
         boxz("low", (4.0, 3.0, 1.0), (0, 0, 0.3), "Ceramic", chamfer=0.1),
         boxz("glass", (3.9, 2.9, 1.7), (0, 0, 1.3), "Glass"),
         boxz("roof", (5.2, 4.2, 0.45), (0, 0, 3.0), "Ceramic", chamfer=0.25),
         boxz("roofLine", (5.24, 0.06, 0.08), (0, -2.12, 3.2), "Red"),
         boxz("desk", (3.0, 0.6, 0.15), (0, 0.6, 1.1), "Graphite"),
         boxz("screen", (0.8, 0.05, 0.5), (0.6, 0.85, 1.3), "White")]
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(boxz("post", (0.18, 0.18, 1.7), (sx * 1.95, sy * 1.45, 1.3), "Graphite"))
    p.append(boxz("door", (0.06, 1.0, 2.6), (2.0, 0.6, 0.3), "Graphite"))
    p.append(boxz("cam", (0.2, 0.35, 0.18), (2.2, -1.9, 2.75), "Graphite"))
    ob = join(p, "SM_INF_Booth"); bevel(ob, 0.02)
    export([ob], "Booth", out); clear()


def boom_barrier(out):
    post = join([boxz("p", (0.55, 0.55, 1.1), (0, 0, 0), "Graphite", chamfer=0.06),
                 boxz("lamp", (0.3, 0.06, 0.12), (0, -0.29, 0.85), "Red"),
                 boxz("cap", (0.6, 0.6, 0.08), (0, 0, 1.1), "Ceramic")], "SM_INF_BoomPost")
    bevel(post, 0.01)
    arm = join([box("a", (6.0, 0.12, 0.14), (3.0, 0, 0), "Ceramic"),
                box("s1", (0.6, 0.125, 0.145), (1.0, 0, 0), "Red"),
                box("s2", (0.6, 0.125, 0.145), (2.6, 0, 0), "Red"),
                box("s3", (0.6, 0.125, 0.145), (4.2, 0, 0), "Red"),
                box("tip", (0.2, 0.14, 0.16), (5.95, 0, 0), "Amber")], "Arm")
    arm.location = (0, 0, 0.95)
    arm.parent = post
    export([post, arm], "BoomBarrier", out); clear()


def id_gate(out):
    """One turnstile lane: two pedestals + glass flaps + overhead scanner arch (2.6 m)."""
    p = []
    for sx in (-0.75, 0.75):
        p.append(boxz("ped", (0.28, 1.7, 1.05), (sx, 0, 0), "Ceramic", chamfer=0.08))
        p.append(boxz("top", (0.3, 1.72, 0.04), (sx, 0, 1.05), "Graphite"))
        p.append(boxz("strip", (0.3, 1.2, 0.03), (sx, 0, 1.09), "Amber"))
        p.append(boxz("flap", (0.42, 0.05, 0.7), (sx - 0.36 * (1 if sx > 0 else -1), 0, 0.4), "Glass"))
    p.append(boxz("archL", (0.22, 0.3, 2.6), (-1.05, 0, 0), "Graphite"))
    p.append(boxz("archR", (0.22, 0.3, 2.6), (1.05, 0, 0), "Graphite"))
    p.append(boxz("archT", (2.32, 0.3, 0.3), (0, 0, 2.6), "Graphite"))
    p.append(boxz("archRed", (2.0, 0.05, 0.05), (0, -0.16, 2.72), "Red"))
    ob = join(p, "SM_INF_IDGate"); bevel(ob, 0.01)
    export([ob], "IDGate", out); clear()


def jersey(out):
    prof = [(-0.3, 0), (0.3, 0), (0.3, 0.08), (0.12, 0.3), (0.1, 0.82), (-0.1, 0.82), (-0.12, 0.3), (-0.3, 0.08)]
    b = prism("j", prof, 2.0, (0, 0, 0), "Concrete")
    b.rotation_euler = (0, 0, math.pi / 2)
    s1 = boxz("s1", (2.0, 0.21, 0.12), (0, 0, 0.55), "Red")
    s2 = boxz("s2", (2.0, 0.215, 0.06), (0, 0, 0.7), "White")
    ob = join([b, s1, s2], "SM_INF_Jersey"); bevel(ob, 0.015)
    export([ob], "Jersey", out); clear()


def bollard(out):
    ob = join([cyl("b", 0.14, 1.0, (0, 0, 0.5), "Graphite", 14),
               cyl("r", 0.145, 0.06, (0, 0, 0.85), "Amber", 14),
               cyl("c", 0.15, 0.05, (0, 0, 1.0), "Steel", 14)], "SM_INF_Bollard")
    for p in ob.data.polygons:
        p.use_smooth = True
    export([ob], "Bollard", out); clear()


def street_light(out):
    p = [cyl("pole", 0.12, 8.0, (0, 0, 4.0), "Graphite", 12, r2=0.08),
         boxz("base", (0.5, 0.5, 0.6), (0, 0, 0), "Graphite", chamfer=0.08),
         box("arm", (0.12, 2.2, 0.12), (0, -1.05, 7.9), "Graphite"),
         box("head", (0.5, 1.2, 0.16), (0, -2.0, 7.85), "Graphite"),
         box("lens", (0.4, 1.0, 0.02), (0, -2.0, 7.76), "White"),
         box("red", (0.04, 0.6, 0.03), (0, -1.0, 7.98), "Red")]
    ob = join(p, "SM_INF_StreetLight")
    for f in ob.data.polygons:
        f.use_smooth = abs(f.normal.z) < 0.9
    anchor = bpy.data.objects.new("LightAnchor", None); bpy.context.scene.collection.objects.link(anchor)
    anchor.location = (0, -2.0, 7.7); anchor.parent = ob
    export([ob, anchor], "StreetLight", out); clear()


def flood_mast(out):
    p = [cyl("mast", 0.18, 11.0, (0, 0, 5.5), "Graphite", 12, r2=0.12),
         boxz("base", (0.7, 0.7, 0.5), (0, 0, 0), "Concrete", chamfer=0.1),
         box("bar", (2.4, 0.15, 0.15), (0, 0, 10.9), "Graphite")]
    for k in range(4):
        x = -0.9 + k * 0.6
        p.append(box("fl", (0.5, 0.3, 0.4), (x, -0.25, 10.6), "Graphite"))
        p.append(box("fl_l", (0.42, 0.02, 0.32), (x, -0.41, 10.6), "White"))
    ob = join(p, "SM_INF_FloodMast")
    flat(ob)
    a = bpy.data.objects.new("LightAnchor", None); bpy.context.scene.collection.objects.link(a)
    a.location = (0, -0.6, 10.5); a.parent = ob
    export([ob, a], "FloodMast", out); clear()


def container(out):
    L, Wd, H = 6.06, 2.44, 2.59
    p = [boxz("body", (Wd, L, H), (0, 0, 0), "Container")]
    for sy in (-1, 1):
        for sx in (-1, 1):
            p.append(boxz("cp", (0.16, 0.16, H), (sx * (Wd / 2 - 0.06), sy * (L / 2 - 0.06), 0), "Steel"))
    p.append(boxz("rail", (Wd + 0.02, 0.12, 0.14), (0, -L / 2, H - 0.14), "Steel"))
    p.append(boxz("railb", (Wd + 0.02, 0.12, 0.14), (0, -L / 2, 0), "Steel"))
    for sx in (-0.45, 0.45):
        p.append(boxz("bar", (0.05, 0.08, H - 0.4), (sx, -L / 2 - 0.05, 0.2), "Steel"))
    ob = join(p, "SM_INF_Container"); bevel(ob, 0.01)
    export([ob], "Container", out); clear()


def trailer(out):
    p = [boxz("box", (2.5, 12.0, 2.8), (0, 1.5, 1.2), "Container"),
         boxz("chassis", (2.2, 12.0, 0.3), (0, 1.5, 0.9), "Graphite"),
         boxz("cab", (2.5, 2.4, 2.6), (0, -5.9, 0.6), "Ceramic", chamfer=0.25),
         boxz("wind", (2.3, 0.05, 0.9), (0, -7.12, 2.0), "Glass"),
         boxz("grill", (1.8, 0.06, 0.5), (0, -7.12, 1.0), "Graphite"),
         boxz("hlL", (0.35, 0.06, 0.12), (-0.95, -7.13, 1.35), "White"),
         boxz("hlR", (0.35, 0.06, 0.12), (0.95, -7.13, 1.35), "White"),
         boxz("redline", (2.52, 2.42, 0.08), (0, -5.9, 2.4), "Red")]
    for y in (-6.2, -4.6, 5.0, 6.3):
        for sx in (-1, 1):
            p.append(cyl("w", 0.5, 0.35, (sx * 1.1, y, 0.5), "Rubber", 16, axis='X'))
    ob = join(p, "SM_INF_Truck"); bevel(ob, 0.012)
    export([ob], "Truck", out); clear()


def dock_building(out):
    """Warehouse / dock front: 40 m wide (x), 20 m deep (+y), 12 m tall, dock platform 1.2 m along -y face,
    3 shutters 5 x 5 m (centre one = open passage, 'PassageAnchor'), ramp at the east end, amber beacons."""
    p = []
    W, D, H = 40.0, 20.0, 12.0
    sh_x = (-12.0, 0.0, 12.0)
    sw, shh = 5.0, 5.0
    pz = 1.2  # platform height
    # front wall with shutter openings (y = 0 .. 0.6)
    xs = [-W / 2] + [v for x in sh_x for v in (x - sw / 2, x + sw / 2)] + [W / 2]
    for i in range(0, len(xs), 2):
        a, b = xs[i], xs[i + 1]
        p.append(boxz("fw", (b - a, 0.6, H), ((a + b) / 2, 0.3, 0), "Graphite"))
    for x in sh_x:
        p.append(boxz("fwTop", (sw, 0.6, H - pz - shh), (x, 0.3, pz + shh), "Graphite"))
        p.append(boxz("fwLow", (sw, 0.6, pz), (x, 0.3, 0), "Graphite"))
        p.append(boxz("frame", (sw + 0.5, 0.2, 0.3), (x, -0.05, pz + shh), "Hazard"))
        p.append(boxz("beacon", (0.3, 0.3, 0.3), (x + sw / 2 + 0.6, -0.2, pz + shh + 0.6), "Amber"))
    # closed shutters (outer two) ; centre is the passage (rolled up: only a drum at the top)
    for x in (sh_x[0], sh_x[2]):
        p.append(boxz("shutter", (sw, 0.15, shh), (x, 0.45, pz), "Shutter"))
    p.append(cyl("drum", 0.4, sw, (sh_x[1], 0.2, pz + shh - 0.4), "Shutter", 14, axis='X'))
    # ceramic upper band + red line + roof
    p.append(boxz("band", (W, 0.7, 2.6), (0, 0.25, H - 2.6), "Ceramic"))
    p.append(boxz("red", (W, 0.05, 0.1), (0, -0.12, H - 2.8), "Red"))
    p.append(boxz("roof", (W + 0.6, D + 0.6, 0.5), (0, D / 2, H), "Graphite"))
    # side + back walls (shell; interior unseen except the passage tunnel)
    p.append(boxz("sideL", (0.6, D, H), (-W / 2 + 0.3, D / 2, 0), "Graphite"))
    p.append(boxz("sideR", (0.6, D, H), (W / 2 - 0.3, D / 2, 0), "Graphite"))
    # back wall with the tunnel's rear opening (centre)
    p.append(boxz("backL", (W / 2 - sw / 2, 0.6, H), ((-W / 2 + (-sw / 2)) / 2, D - 0.3, 0), "Graphite"))
    p.append(boxz("backR", (W / 2 - sw / 2, 0.6, H), ((W / 2 + sw / 2) / 2, D - 0.3, 0), "Graphite"))
    p.append(boxz("backTop", (sw, 0.6, H - pz - shh), (0, D - 0.3, pz + shh), "Graphite"))
    p.append(boxz("backLow", (sw, 0.6, pz), (0, D - 0.3, 0), "Graphite"))
    # passage tunnel through the building behind the centre shutter (4.6 m wide, ceiling 5 m)
    tx = sh_x[1]
    p.append(boxz("tunL", (0.3, D - 1.2, shh), (tx - sw / 2 + 0.15, D / 2, pz), "Ceramic"))
    p.append(boxz("tunR", (0.3, D - 1.2, shh), (tx + sw / 2 - 0.15, D / 2, pz), "Ceramic"))
    p.append(boxz("tunC", (sw, D - 1.2, 0.3), (tx, D / 2, pz + shh), "Graphite"))
    p.append(boxz("tunF", (sw, D, pz), (tx, D / 2, 0), "Concrete"))
    for k in range(5):
        p.append(boxz("tunLight", (0.25, 1.6, 0.05), (tx, 2.5 + k * 3.6, pz + shh - 0.06), "White"))
    # back opening of the tunnel
    p.append(boxz("backOpenFrame", (sw + 0.5, 0.3, 0.3), (tx, D + 0.05, pz + shh), "Hazard"))
    # dock platform in front (raised 1.2 m, 3 m deep) with bumpers + yellow edge
    p.append(boxz("dock", (W, 3.0, pz), (0, -1.5, 0), "Concrete"))  # full width: the ramp (east end) lands on it
    p.append(boxz("edge", (W - 6, 0.12, 0.08), (-3, -3.0, pz - 0.04), "Hazard"))
    for x in sh_x:
        for dx in (-1.8, 1.8):
            p.append(boxz("bump", (0.35, 0.3, 0.6), (x + dx, -3.1, 0.3), "Rubber"))
    # ramp at the east end (x 14..20) rising toward the building
    # wedge ramp: x in [W/2-6, W/2], foot at y = -11 (z 0) rising to the platform edge y = -3 (z = pz)
    bm = bmesh.new()
    x0, x1, yf, yt = W / 2 - 6.0, W / 2, -11.0, -3.0
    a = bm.verts.new((x0, yf, 0)); b = bm.verts.new((x1, yf, 0))
    c = bm.verts.new((x1, yt, 0)); d = bm.verts.new((x0, yt, 0))
    e = bm.verts.new((x1, yt, pz)); f = bm.verts.new((x0, yt, pz))
    for face in ((a, d, c, b), (a, b, e, f), (d, f, e, c), (a, f, d), (b, c, e)):
        bm.faces.new(face)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ramp = _link("ramp", bm, "Concrete")
    p.append(ramp)
    ob = join(p, "SM_INF_DockBuilding")
    flat(ob)
    a = bpy.data.objects.new("PassageAnchor", None); bpy.context.scene.collection.objects.link(a)
    a.location = (tx, -0.5, pz); a.parent = ob
    t = bpy.data.objects.new("TurretAnchor", None); bpy.context.scene.collection.objects.link(t)
    t.location = (6.0, -0.35, 6.8); t.parent = ob
    export([ob, a, t], "DockBuilding", out); clear()


def city_block(out, name, w, d, h, facade, setback=True, seed=0):
    """Generic tower block: storefront podium (5 m: glass + graphite + shutters), facade body, optional setback,
    rooftop units, red aviation light."""
    p = []
    p.append(boxz("store", (w, d, 5.0), (0, 0, 0), "Graphite"))
    p.append(boxz("storeGlass", (w - 1.0, 0.1, 3.4), (0, -d / 2 - 0.02, 0.6), "Glass"))
    p.append(boxz("storeSign", (w - 1.0, 0.25, 0.7), (0, -d / 2 - 0.1, 4.1), "Graphite"))
    p.append(boxz("storeLine", (w - 1.0, 0.05, 0.05), (0, -d / 2 - 0.24, 4.05), "White"))
    p.append(boxz("canopy", (w, 1.6, 0.2), (0, -d / 2 - 0.8, 3.8), "Ceramic"))
    body_h = h - 5.0
    if setback and h > 40:
        h1 = body_h * 0.62
        p.append(boxz("body1", (w, d, h1), (0, 0, 5.0), facade))
        p.append(boxz("ledge", (w + 0.4, d + 0.4, 0.8), (0, 0, 5.0 + h1), "Ceramic"))
        p.append(boxz("body2", (w - 4, d - 4, body_h - h1), (0, 0, 5.8 + h1), facade))
        top = 5.8 + body_h
        tw, td = w - 4, d - 4
    else:
        p.append(boxz("body", (w, d, body_h), (0, 0, 5.0), facade))
        top = h
        tw, td = w, d
    p.append(boxz("parapet", (tw + 0.3, td + 0.3, 1.2), (0, 0, top), "Graphite"))
    p.append(boxz("unit1", (tw * 0.35, td * 0.3, 2.4), (-tw * 0.18, td * 0.1, top + 1.2), "Steel"))
    p.append(boxz("unit2", (tw * 0.2, td * 0.25, 3.4), (tw * 0.25, -td * 0.15, top + 1.2), "Graphite"))
    p.append(cyl("ant", 0.12, 7, (tw * 0.3, td * 0.3, top + 4.7), "Steel", 8))
    p.append(boxz("avi", (0.35, 0.35, 0.35), (tw * 0.3, td * 0.3, top + 8.2), "Red"))
    # vertical corporate light line on one corner
    p.append(boxz("vline", (0.12, 0.12, h - 7), (w / 2 + 0.02, -d / 2 - 0.02, 5.5), "White" if seed % 2 else "Red"))
    ob = join(p, "SM_INF_" + name)
    flat(ob)
    export([ob], name, out); clear()


def scanner_frame(out):
    """Security scan gate across a 12 m street: two pylons + top beam. The laser curtain is a Unity quad."""
    p = []
    for sx in (-6.4, 6.4):
        p.append(boxz("py", (0.7, 0.9, 4.2), (sx, 0, 0), "Graphite", chamfer=0.1))
        p.append(boxz("em", (0.08, 0.5, 3.6), (sx - 0.36 * (1 if sx > 0 else -1), 0, 0.3), "Red"))
        p.append(boxz("cap", (0.8, 1.0, 0.2), (sx, 0, 4.2), "Ceramic"))
    p.append(boxz("beam", (13.5, 0.5, 0.5), (0, 0, 4.4), "Graphite"))
    p.append(boxz("beamLine", (12.6, 0.06, 0.06), (0, -0.26, 4.5), "Red"))
    ob = join(p, "SM_INF_ScannerFrame"); bevel(ob, 0.015)
    export([ob], "ScannerFrame", out); clear()


def service_wall(out):
    """Service lane wall, 12 m x 9 m: graphite panels, pipes, cable trays, AC units, small lamps."""
    p = [boxz("wall", (12, 0.5, 9), (0, 0.25, 0), "Concrete")]
    for k in range(3):
        p.append(cyl("pipe", 0.16 + k * 0.04, 12, (0, -0.25 - k * 0.38, 3.4 + k * 0.55), "Steel", 12, axis='X'))
    p.append(boxz("tray", (12, 0.4, 0.08), (0, -0.25, 6.2), "Steel"))
    for x in (-3.5, 2.8):
        p.append(boxz("ac", (1.2, 0.6, 0.9), (x, -0.3, 1.2), "Ceramic", chamfer=0.05))
        p.append(cyl("fan", 0.32, 0.05, (x, -0.62, 1.65), "Graphite", 16, axis='Y'))
    p.append(boxz("lamp", (0.5, 0.25, 0.12), (0, -0.15, 7.0), "White"))
    p.append(boxz("hz", (12, 0.04, 0.25), (0, -0.02, 0.4), "Hazard"))
    ob = join(p, "SM_INF_ServiceWall"); flat(ob)
    export([ob], "ServiceWall", out); clear()


def drone(out):
    """Ambient surveillance drone (non-combat): disc body, four rotor rings, red eye, spotlight anchor."""
    p = [cyl("body", 0.45, 0.22, (0, 0, 0), "Ceramic", 20), cyl("top", 0.3, 0.12, (0, 0, 0.16), "Graphite", 16),
         boxz("eye", (0.16, 0.08, 0.1), (0, -0.44, -0.08), "Red")]
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        x, y = math.cos(ang) * 0.62, math.sin(ang) * 0.62
        p.append(box("armx", (0.5, 0.08, 0.05), (x * 0.55, y * 0.55, 0), "Graphite"))
        p[-1].rotation_euler = (0, 0, ang)
        p.append(cyl("ring", 0.24, 0.05, (x, y, 0.02), "Graphite", 16))
    ob = join(p, "SM_INF_Drone")
    for f in ob.data.polygons:
        f.use_smooth = True
    export([ob], "Drone", out); clear()


def planter(out):
    ob = join([boxz("p", (2.4, 1.0, 0.6), (0, 0, 0), "Ceramic", chamfer=0.08),
               boxz("s", (2.2, 0.8, 0.05), (0, 0, 0.56), "Rubber"),
               boxz("l", (2.42, 0.04, 0.04), (0, -0.5, 0.45), "White")], "SM_INF_Planter")
    bevel(ob, 0.01)
    export([ob], "Planter", out); clear()


def door_panel(out):
    """Amber access panel (interactable) and a red 'locked' variant share this mesh."""
    ob = join([boxz("b", (0.5, 0.12, 0.8), (0, 0, 0), "Graphite", chamfer=0.04),
               boxz("s", (0.36, 0.02, 0.5), (0, -0.065, 0.2), "Amber")], "SM_INF_DoorPanel")
    bevel(ob, 0.008)
    export([ob], "DoorPanel", out); clear()


def contact_preview(out, prev):
    if not prev:
        return
    os.makedirs(prev, exist_ok=True)
    clear()
    names = ["Booth", "BoomBarrier", "IDGate", "Jersey", "Bollard", "StreetLight", "FloodMast", "Container", "Truck",
             "ScannerFrame", "SecurityWall", "WallPillar", "Planter", "Drone", "DoorPanel", "ServiceWall"]
    x = 0.0
    for n in names:
        bpy.ops.import_scene.fbx(filepath=os.path.join(out, "Meshes", "SM_INF_" + n + ".fbx"))
        objs = list(bpy.context.selected_objects)
        mn = min((o.matrix_world @ Vector(c)).x for o in objs if o.type == 'MESH' for c in o.bound_box)
        mx = max((o.matrix_world @ Vector(c)).x for o in objs if o.type == 'MESH' for c in o.bound_box)
        for o in objs:
            if o.parent is None:
                o.location.x += x - mn
        x += (mx - mn) + 1.5
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            for ms in o.material_slots:
                if ms.material and ms.material.name.split('.')[0] in ("INF_Red", "INF_Amber", "INF_White"):
                    m = ms.material; m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
                    o_ = nt.nodes.new("ShaderNodeOutputMaterial"); e = nt.nodes.new("ShaderNodeEmission")
                    e.inputs[0].default_value = m.diffuse_color; e.inputs[1].default_value = 6
                    nt.links.new(e.outputs[0], o_.inputs[0])
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = 1800; sc.render.resolution_y = 700
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.06, 0.09, 1)
    ld = bpy.data.lights.new("sun", 'SUN'); ld.energy = 3; lo = bpy.data.objects.new("sun", ld); sc.collection.objects.link(lo)
    lo.rotation_euler = (math.radians(55), 0, math.radians(30))
    cd = bpy.data.cameras.new("c"); cd.type = 'ORTHO'; cd.ortho_scale = x * 0.52
    cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (x / 2, 60, 18); cam.rotation_euler = (math.radians(82), 0, math.radians(180))
    sc.render.filepath = os.path.join(prev, "inf_kit_preview.png")
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    OUT, PREV = args()
    reset()
    hq_tower(OUT); hq_podium(OUT); security_wall(OUT); wall_pillar(OUT); checkpoint_booth(OUT); boom_barrier(OUT)
    id_gate(OUT); jersey(OUT); bollard(OUT); street_light(OUT); flood_mast(OUT); container(OUT); trailer(OUT)
    dock_building(OUT); scanner_frame(OUT); service_wall(OUT); drone(OUT); planter(OUT); door_panel(OUT)
    city_block(OUT, "BlockA", 22, 18, 64, "FacadeA", True, 1)
    city_block(OUT, "BlockB", 18, 16, 38, "FacadeB", False, 2)
    city_block(OUT, "BlockC", 26, 20, 92, "FacadeA", True, 3)
    city_block(OUT, "BlockD", 16, 14, 28, "FacadeB", False, 4)
    print("KIT", sum(t for _, t in EXPORTED), "tris total;", ", ".join("%s=%d" % e for e in EXPORTED))
    contact_preview(OUT, PREV)
