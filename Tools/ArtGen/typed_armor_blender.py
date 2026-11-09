"""Type-D hard-surface armor generator (iteration 11: production quality pass). Blender 5.x headless.

Run:
  blender -b --factory-startup --python typed_armor_blender.py -- <in_dir> <out_json> [preview_dir] [visor_drop]

<in_dir> must contain the Type-D reference exported from Unity in the edit-mode T-pose
(Body.obj / Object012.obj / Plane009.obj + *_bones.txt with the dominant bone per vertex, bones.txt with
HumanBodyBones positions; Tools/ChuOn/Type-D/1. Export Reference). Coordinates are boss-root local metres:
  blender (x, y, z) = unity (x, z, y)   (x = character right, y = front, z = up)

Outputs (all next to <out_json>, consumed by Assets/Editor/TypeDArmorBuilder.cs):
  typed_armor.json         parts: welded vertices (Unity axes), custom (weighted/hardened) normals, atlas UVs,
                           triangles per material, bone binding, role
  typed_armor_albedo.png   sRGB  graphite metal / ceramic (value ~0.55) / rubber gaskets, edge wear, grime, paint
  typed_armor_normal.png   linear, OpenGL (+Y) tangent space = atlas UV axes: panel seams, rivets, grille slats
  typed_armor_osm.png      linear ASP StylizePBR mask: R occlusion (ray AO x cavity), G smoothness, B metallic

Hard-surface recipe (per plate):
  reference-fitted convex hull -> outward faces -> planar facets -> pushed off the body -> chamfer clips
  -> double inset (border band + raised centre panel = real panel step) -> solidify -> boolean light channels
  -> bevel (angle limited, harden normals, face strength) -> weighted normals (keep sharp).
Detail that would cost triangles (seams, rivets, slats, wear, grime) lives in the baked atlas instead.
Everything is deterministic (fixed seeds, no randomised operators).
Materials (submesh names): 0 Metal (graphite), 1 Ceramic (off-white accent), 2 Glow (thin Tsukasa-red light lines,
driven at runtime by TypeDArmorRig), 3 Visor (dark glass band), 4 Core (energy lenses).
Part names / bones are the iteration-10 set (TypeDArmorRig binds by bone) plus thigh/shin guards.
"""
import bpy, bmesh, json, math, os, sys
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
IN = argv[0]
OUT = argv[1]
PREVIEW = os.path.abspath(argv[2]) if len(argv) > 2 and argv[2] not in ("", "-") else None
VISOR_DROP = float(argv[3]) if len(argv) > 3 else 0.115  # visor height below the helmet top (m)
OUT_DIR = os.path.dirname(os.path.abspath(OUT))
TEX = 2048       # atlas resolution
AO_RES = 512     # ray-traced AO resolution (upsampled)

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = ["Metal", "Ceramic", "Glow", "Visor", "Core"]
MAT = {}
COLORS = {"Metal": (0.05, 0.055, 0.06, 1), "Ceramic": (0.3, 0.3, 0.31, 1), "Glow": (0.9, 0.1, 0.12, 1),
          "Visor": (0.02, 0.02, 0.03, 1), "Core": (0.6, 0.95, 1.0, 1), "Ref": (0.55, 0.45, 0.42, 1)}
for n in MATS + ["Ref"]:
    m = bpy.data.materials.new(n)
    m.diffuse_color = COLORS[n]
    MAT[n] = m

# ---------------------------------------------------------------- reference
def load_obj(name):
    V, F = [], []
    with open(os.path.join(IN, name + ".obj")) as f:
        for l in f:
            if l.startswith("v "):
                V.append(Vector(tuple(map(float, l.split()[1:4]))))
            elif l.startswith("f "):
                F.append([int(t.split('/')[0]) - 1 for t in l.split()[1:]])
    B = [l.strip() for l in open(os.path.join(IN, name + "_bones.txt"))]
    return V, F, B

BONES = {}
for l in open(os.path.join(IN, "bones.txt")):
    p = l.split()
    if len(p) >= 5:
        BONES[p[0]] = Vector((float(p[2]), float(p[3]), float(p[4])))

REF = {}
for nm in ("Body", "Object012", "Plane009"):
    if os.path.exists(os.path.join(IN, nm + ".obj")):
        REF[nm] = load_obj(nm)

def region(bones, filt=None, sources=("Body", "Object012", "Plane009")):
    pts = []
    for nm in sources:
        if nm not in REF:
            continue
        V, F, B = REF[nm]
        for v, b in zip(V, B):
            if b.split(':')[-1] in bones and (filt is None or filt(v)):
                pts.append(v.copy())
    return pts

def mirrored(pts):
    return pts + [Vector((-p.x, p.y, p.z)) for p in pts]

def ref_object():
    verts, faces = [], []
    for nm in ("Body", "Object012", "Plane009"):
        if nm not in REF:
            continue
        V, F, B = REF[nm]
        base = len(verts)
        verts += [tuple(v) for v in V]
        faces += [[i + base for i in f] for f in F]
    me = bpy.data.meshes.new("RefBody")
    me.from_pydata(verts, [], faces)
    me.update()
    ob = bpy.data.objects.new("RefBody", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(MAT["Ref"])
    return ob

REF_OB = ref_object()
_bm = bmesh.new(); _bm.from_mesh(REF_OB.data)
REF_BVH = BVHTree.FromBMesh(_bm)

# ---------------------------------------------------------------- mesh helpers
def new_object(name, bm, material):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for n in MATS:
        ob.data.materials.append(MAT[n])
    for p in ob.data.polygons:
        p.material_index = MATS.index(material)
    return ob

def hull_shell(points, keep, push, dissolve_deg=9.0, clips=()):
    """Convex hull -> keep(face_center, face_normal) faces -> planar facets -> pushed outward -> plane clips."""
    bm = bmesh.new()
    for p in points:
        bm.verts.new(p)
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:], use_existing_faces=False)
    junk = list({g for g in res.get("geom_interior", []) + res.get("geom_unused", []) if isinstance(g, bmesh.types.BMVert) and g.is_valid})
    if junk:
        bmesh.ops.delete(bm, geom=junk, context='VERTS')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    dead = [f for f in bm.faces if not keep(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    _drop_loose(bm)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(dissolve_deg), verts=bm.verts[:], edges=bm.edges[:])
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * push
    for co, no in clips:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector(co), plane_no=Vector(no).normalized(), clear_outer=True)
    _drop_loose(bm)
    return cleanup(bm)

def _drop_loose(bm):
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')

def above(z):
    return ((0, 0, z), (0, 0, -1))

def below(z):
    return ((0, 0, z), (0, 0, 1))

def xlim(x):
    return [((x, 0, 0), (1, 0, 0)), ((-x, 0, 0), (-1, 0, 0))]

def keep_side(p1, p2, keep):
    """Plane through the XZ line p1-p2 (extruded along Y); discards the side opposite to `keep`."""
    d = Vector((p2[0] - p1[0], 0, p2[1] - p1[1]))
    n = Vector((-d.z, 0, d.x)).normalized()
    if n.dot(Vector((keep[0] - p1[0], 0, keep[1] - p1[1]))) > 0:
        n = -n
    return ((p1[0], 0, p1[1]), tuple(n))

def keep_side_yz(p1, p2, keep, x=0.0):
    """Same in the YZ (side view) plane: p = (y, z)."""
    d = Vector((0, p2[0] - p1[0], p2[1] - p1[1]))
    n = Vector((0, -d.z, d.y)).normalized()
    if n.dot(Vector((0, keep[0] - p1[0], keep[1] - p1[1]))) > 0:
        n = -n
    return ((x, p1[0], p1[1]), tuple(n))

def cleanup(bm, dist=0.0018):
    """Remove slivers left by hull/bisect clipping (they make inset/solidify offsets explode)."""
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=dist)
    bmesh.ops.dissolve_degenerate(bm, dist=dist, edges=bm.edges[:])
    _drop_loose(bm)
    bm.normal_update()
    return bm

def _bm_sane(b2, ref_pts, reach):
    kd = KDTree(len(ref_pts))
    for i, c in enumerate(ref_pts):
        kd.insert(c, i)
    kd.balance()
    for v in b2.verts:
        if kd.find(v.co)[2] > reach:
            return False
    return True

def panel_step(bm, border, step=0.0028, rise=0.0032):
    """Real panel cut: a flat border band, then a raised centre panel with a steep (bevelled) step.
    Validated: if an inset explodes at an acute corner, retry without even offset, else skip the step."""
    if not bm.faces:
        return bm
    cleanup(bm)
    ref = [v.co.copy() for v in bm.verts]
    for even in (True, False):
        b2 = bm.copy()
        bmesh.ops.inset_region(b2, faces=b2.faces[:], thickness=border, depth=0.0, use_even_offset=even, use_boundary=True)
        band = {f for f in b2.faces if any(e.is_boundary for e in f.edges)}
        centre = [f for f in b2.faces if f not in band]
        if centre:
            bmesh.ops.inset_region(b2, faces=centre, thickness=step, depth=rise, use_even_offset=even, use_boundary=False)
        if _bm_sane(b2, ref, 0.03):
            bm.free()
            return b2
        b2.free()
    print("panel_step skipped (unstable inset)")
    return bm

def apply_all(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)

def _bbox(ob):
    vs = [v.co for v in ob.data.vertices]
    return (Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
            Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))))

def solidify(ob, thickness):
    """Plate thickness (outward). Even offset unless it spikes at an acute corner (validated against the bbox)."""
    if not ob.data.vertices:
        return ob
    lo, hi = _bbox(ob)
    src = ob.data.copy()
    for even in (True, False):
        s = ob.modifiers.new("solid", 'SOLIDIFY')
        s.thickness = thickness
        s.offset = 1.0
        s.use_even_offset = even
        s.use_rim = True
        apply_all(ob)
        lo2, hi2 = _bbox(ob)
        m = thickness * 1.6 + 0.004
        if all(lo2[i] >= lo[i] - m and hi2[i] <= hi[i] + m for i in range(3)):
            break
        print("solidify spike on", ob.name, "even", even)
        old = ob.data; ob.data = src.copy(); bpy.data.meshes.remove(old)
    bpy.data.meshes.remove(src)
    return ob

def hard_bevel(ob, width=0.0025, segments=1, angle=30.0):
    """Crisp hard-surface shading: angle-limited bevel with hardened normals + weighted normals (keep sharp).
    Bevel faces are recorded in the face attribute 'wear' (edge wear mask for the atlas)."""
    me = ob.data
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(angle))
    if width > 0:
        b = ob.modifiers.new("bevel", 'BEVEL')
        b.width = width
        b.segments = segments
        b.limit_method = 'ANGLE'
        b.angle_limit = math.radians(angle)
        b.harden_normals = True
        b.face_strength_mode = 'FSTR_AFFECTED'
        b.use_clamp_overlap = True
        b.miter_outer = 'MITER_ARC'
    w = ob.modifiers.new("wn", 'WEIGHTED_NORMAL')
    w.keep_sharp = True
    w.use_face_influence = True
    w.weight = 50
    apply_all(ob)
    me = ob.data
    fs = me.attributes.get('__mod_weightednormals_faceweight')
    wear = me.attributes.new("wear", 'FLOAT', 'FACE') if me.attributes.get("wear") is None else me.attributes["wear"]
    vals = [0.0] * len(me.polygons)
    if fs is not None and width > 0:
        for i in range(len(me.polygons)):
            vals[i] = 1.0 if fs.data[i].value <= 0 else 0.0
    wear.data.foreach_set("value", vals)
    return ob

def flat(ob, angle=40.0):
    for p in ob.data.polygons:
        p.use_smooth = True
    ob.data.set_sharp_from_angle(angle=math.radians(angle))
    return ob

def plate(name, bm, material, thickness, border=None, bevel=0.0025, segments=2, angle=30.0, channels=()):
    """Open shell bmesh -> panel step -> solidify -> light channels (Glow inserts) -> hard bevel. Returns joined part."""
    if border:
        panel_step(bm, border)
    ob = new_object(name, bm, material)
    solidify(ob, thickness)
    inserts = []
    for ch in channels:
        g = inlay_on(ob, *ch)
        if g is not None:
            inserts.append(g)
    hard_bevel(ob, bevel, segments, angle)
    for g in inserts:
        flat(g, 50)
    return join([ob] + inserts, name) if inserts else ob

def boolean(target, cutter, op, min_keep=0.0):
    """Boolean with validation: EXACT first, then FLOAT; reverts (returns False) if the result is empty/degenerate."""
    before = target.data.copy()
    n0 = len(before.polygons)
    for solver in ('EXACT', 'FLOAT'):
        m = target.modifiers.new("bool", 'BOOLEAN')
        m.operation = op
        m.object = cutter
        try:
            m.solver = solver
        except Exception:
            pass
        apply_all(target)
        n1 = len(target.data.polygons)
        if n1 > 0 and n1 >= n0 * min_keep:
            bpy.data.meshes.remove(before)
            return True
        old = target.data; target.data = before.copy(); bpy.data.meshes.remove(old)
    bpy.data.meshes.remove(before)
    print("boolean failed on", target.name, op)
    return False

def copy_object(ob, name):
    c = ob.copy(); c.data = ob.data.copy(); c.name = name
    bpy.context.scene.collection.objects.link(c)
    return c

def join(objs, name):
    objs = [o for o in objs if o is not None and len(o.data.polygons) > 0]
    for o in objs:   # every joined object carries the wear attribute (default 0 = no wear)
        if o.data.attributes.get("wear") is None:
            a = o.data.attributes.new("wear", 'FLOAT', 'FACE')
            a.data.foreach_set("value", [0.0] * len(o.data.polygons))
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    return ob

def framed_box(name, center, t, b, n, size):
    """Box with axes t (length), b (width), n (depth)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    M = Matrix(((t.x * size[0], b.x * size[1], n.x * size[2], center.x),
                (t.y * size[0], b.y * size[1], n.y * size[2], center.y),
                (t.z * size[0], b.z * size[1], n.z * size[2], center.z),
                (0, 0, 0, 1)))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob

def surface_hit(ob, origin, ray_dir):
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(ob, dg)
    ray_dir = Vector(ray_dir).normalized()
    hit = bvh.ray_cast(Vector(origin) + ray_dir * 0.4, -ray_dir, 1.0)
    if hit[0] is None:
        return None, None
    return hit[0], hit[1].normalized()

def inlay_on(plate_ob, origin, ray_dir, tangent, length, width, depth=0.0045, ratio=0.42):
    """Thin light line: carve a channel along `tangent` and return the flush Glow insert (separate object)."""
    p, n = surface_hit(plate_ob, origin, ray_dir)
    if p is None:
        print("inlay miss", plate_ob.name, tuple(origin))
        return None
    t = Vector(tangent); t = (t - n * t.dot(n)).normalized(); b = n.cross(t).normalized()
    solid = copy_object(plate_ob, plate_ob.name + "_src")
    cut = framed_box("cut", p, t, b, n, (length, width, depth * 2.0))
    ins = framed_box("ins", p - n * 0.0006, t, b, n, (length * 0.96, width * ratio, depth * 2.0))
    ok = boolean(solid, ins, 'INTERSECT') and boolean(plate_ob, cut, 'DIFFERENCE', 0.6)
    bpy.data.objects.remove(cut); bpy.data.objects.remove(ins)
    if not ok or len(solid.data.polygons) == 0:
        bpy.data.objects.remove(solid)
        return None
    for poly in solid.data.polygons:
        poly.material_index = MATS.index("Glow")
    return solid

def lens(center, normal, radius, depth, segments, material, taper=1.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=radius * taper, depth=depth)
    q = Vector((0, 0, 1)).rotation_difference(Vector(normal).normalized())
    bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=q.to_matrix(), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return new_object("lens", bm, material)

def bolt(p, n, r=0.0052, h=0.0032, material="Metal"):
    """Hex socket bolt head (geometry): 6-sided, slightly tapered, sunk 1 mm into the plate."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=r, radius2=r * 0.8, depth=h)
    bmesh.ops.translate(bm, vec=(0, 0, h * 0.5 - 0.001), verts=bm.verts)
    q = Vector((0, 0, 1)).rotation_difference(Vector(n).normalized())
    bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=q.to_matrix(), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(p), verts=bm.verts)
    ob = new_object("bolt", bm, material)
    flat(ob, 20)
    return ob

def bolts_on(ob, spots, r=0.0052):
    out = []
    for origin, d in spots:
        p, n = surface_hit(ob, origin, d)
        if p is not None:
            out.append(bolt(p, n, r))
    return out

def surface_point(origin, direction, maxd=1.0):
    hit = REF_BVH.ray_cast(origin, direction.normalized(), maxd)
    return hit[0]

PARTS = []  # (object, bone, role, anchor)

def add_part(ob, bone, role="Plate", anchor=None):
    PARTS.append((ob, bone, role, anchor))

# ---------------------------------------------------------------- atlas detail features (3D-defined, baked per texel)
FEAT = {}

def F(part):
    return FEAT.setdefault(part, {"seams": [], "rivets": [], "paint": []})

def seam(part, c, n, box, w=0.0017, depth=0.0011):
    """Panel seam = plane (c, n) intersected with the plate inside the AABB box ((x0,y0,z0),(x1,y1,z1))."""
    F(part)["seams"].append((Vector(c), Vector(n).normalized(), box, w, depth))

def slats(part, c, n, step, count, box, w=0.0014, depth=0.0012):
    """Grille: `count` parallel seams spaced `step` along n."""
    c = Vector(c); n = Vector(n).normalized()
    for i in range(count):
        seam(part, c + n * (step * (i - (count - 1) * 0.5)), n, box, w, depth)

def rivets(part, ob, spots, r=0.0038):
    for origin, d in spots:
        p, n = surface_hit(ob, origin, d)
        if p is not None:
            F(part)["rivets"].append((p, r))

def paint(part, c, n, box, w, rgb):
    F(part)["paint"].append((Vector(c), Vector(n).normalized(), box, w, rgb))

def B(x0, x1, y0, y1, z0, z1):
    return ((min(x0, x1), min(y0, y1), min(z0, z1)), (max(x0, x1), max(y0, y1), max(z0, z1)))

# ---------------------------------------------------------------- measurements
chest_b = BONES["UpperChest"]; spine1 = BONES["Chest"]; spine = BONES["Spine"]; hips = BONES["Hips"]
neck = BONES["Neck"]; head = BONES["Head"]
FRONT = (0, 1, 0); BACK = (0, -1, 0)

# ================================================================ 1. chest: graphite cuirass + layered ceramic pectoral plates
torso = mirrored(region({"Spine1", "Spine2"}, lambda v: abs(v.x) < 0.21 and 1.9 < v.z < 2.3))
chest_z0, chest_z1 = 1.93, 2.255
cz = 2.06

def torso_front(push, extra_clips):
    return hull_shell(torso, lambda c, n: n.y > 0.22 and chest_z0 - 0.04 < c.z < chest_z1 + 0.04 and abs(c.x) < 0.185, push=push,
                      clips=extra_clips)

cuirass_clips = xlim(0.176) + [below(chest_z1), above(chest_z0 - 0.06),
                keep_side((0.0, chest_z0 - 0.012), (0.3, chest_z0 + 0.085), (0.0, 2.1)),
                keep_side((0.0, chest_z0 - 0.012), (-0.3, chest_z0 + 0.085), (0.0, 2.1)),
                keep_side((0.09, chest_z1), (0.3, chest_z1 - 0.11), (0.0, 2.0)),
                keep_side((-0.09, chest_z1), (-0.3, chest_z1 - 0.11), (0.0, 2.0)),
                keep_side((0.176, 2.0), (0.14, 1.95), (0.0, 2.1)),
                keep_side((-0.176, 2.0), (-0.14, 1.95), (0.0, 2.1))]
cuirass = new_object("ChestPlate", panel_step(torso_front(0.02, cuirass_clips), 0.011), "Metal")
solidify(cuirass, 0.014)
chev = []
for side in (-1, 1):   # thin red chevron under the pectoral plates
    g = inlay_on(cuirass, (side * 0.055, 0.3, 1.985), FRONT, (side * 0.9, 0, 0.45), 0.105, 0.011)
    chev.append(g)
front = BVHTree.FromObject(cuirass, bpy.context.evaluated_depsgraph_get()).ray_cast(Vector((0, 0.5, cz + 0.06)), Vector((0, -1, 0)), 1.0)
lens_c = front[0] if front[0] is not None else Vector((0, 0.2, cz + 0.06))
lens_n = front[1] if front[1] is not None else Vector((0, 1, 0))
lc = lens(lens_c, lens_n, 0.031, 0.03, 6, "Metal")
boolean(cuirass, lc, 'DIFFERENCE'); bpy.data.objects.remove(lc)
bezel = lens(lens_c + lens_n * 0.002, lens_n, 0.037, 0.008, 6, "Metal", taper=0.86)   # hex bezel ring around the core
hole = lens(lens_c + lens_n * 0.002, lens_n, 0.029, 0.03, 6, "Metal")
boolean(bezel, hole, 'DIFFERENCE'); bpy.data.objects.remove(hole)
hard_bevel(bezel, 0.0012, 1, 30)
hard_bevel(cuirass, 0.0028, 1, 30)
for g in chev:
    if g is not None:
        flat(g, 50)
chest_core = lens(lens_c - lens_n * 0.006, lens_n, 0.024, 0.016, 6, "Core", taper=0.7)
chest_core.name = "ChestCore"
flat(chest_core, 40)

pecs = []
for side in (-1, 1):
    s = side
    clips = [((s * 0.042, 0, 0), (-s, 0, 0)), ((s * 0.166, 0, 0), (s, 0, 0)), below(2.236),
             keep_side((s * 0.042, 2.135), (s * 0.166, 2.045), (s * 0.1, 2.2)),      # swept lower edge (arrow -> core)
             keep_side((s * 0.042, 2.2), (s * 0.075, 2.236), (s * 0.1, 2.15)),        # upper-inner chamfer
             keep_side((s * 0.13, 2.236), (s * 0.166, 2.2), (s * 0.1, 2.15))]         # upper-outer chamfer
    pb = torso_front(0.039, clips)
    pec = new_object(("Right" if s > 0 else "Left") + "Pec", panel_step(pb, 0.008, 0.0022, 0.0022), "Ceramic")
    solidify(pec, 0.008)
    hard_bevel(pec, 0.0022, 2, 30)
    pecs.append(pec)
    pecs += bolts_on(pec, [((s * 0.075, 0.4, 2.212), FRONT), ((s * 0.148, 0.4, 2.075), FRONT)])
    rivets("ChestPlate", pec, [((s * 0.14, 0.4, 2.212), FRONT), ((s * 0.062, 0.4, 2.135), FRONT)])
bolts_c = bolts_on(cuirass, [((x, 0.4, 1.952), FRONT) for x in (-0.13, 0.13)])
seam("ChestPlate", (0, 0, 1.968), (0, -0.1, 1), B(-0.165, 0.165, 0.0, 0.4, 1.94, 2.0))   # lower cuirass band
seam("ChestPlate", (0, 0, 0), (1, 0, 0), B(-0.01, 0.01, 0.0, 0.4, 1.93, 2.03))           # sternum seam
slats("ChestPlate", (0, 0, 2.2), (0, 0, 1), 0.009, 3, B(-0.03, 0.03, 0.0, 0.4, 2.17, 2.23))  # upper sternum vent
chest = join([cuirass, bezel] + chev + pecs + bolts_c, "ChestPlate")
add_part(chest, "UpperChest")
add_part(chest_core, "UpperChest", "Core")

# collar band around the neck base (graphite, segmented)
collar_pts = mirrored(region({"Spine2", "Neck"}, lambda v: 2.2 < v.z < 2.36 and abs(v.x) < 0.14))
collar = new_object("Collar", hull_shell(collar_pts, lambda c, n: abs(n.z) < 0.5 and 2.25 < c.z < 2.33, push=0.012, dissolve_deg=10), "Metal")
solidify(collar, 0.011)
hard_bevel(collar, 0.0022, 1, 30)
add_part(collar, "UpperChest")
for a in range(0, 180, 45):
    r = math.radians(a)
    seam("Collar", (0, -0.02, 2.29), (math.cos(r), math.sin(r), 0), B(-0.2, 0.2, -0.25, 0.25, 2.24, 2.34))

# ab plates (two lamellar bands, Spine bone): chamfered ends, centre seam, rivets
for k, (z0, z1) in enumerate(((1.865, 1.925), (1.795, 1.855))):
    pts = mirrored(region({"Spine", "Spine1", "Hips"}, lambda v, a=z0, b=z1: a - 0.05 < v.z < b + 0.05 and abs(v.x) < 0.18))
    xw = 0.118 - 0.012 * k
    clips = xlim(xw) + [above(z0), below(z1),
                        keep_side((xw - 0.02, z0), (xw, z0 + 0.02), (0, (z0 + z1) * 0.5)),
                        keep_side((-xw + 0.02, z0), (-xw, z0 + 0.02), (0, (z0 + z1) * 0.5))]
    nm = "AbPlate%d" % k
    ab = new_object(nm, hull_shell(pts, lambda c, n, a=z0, b=z1, k=k: n.y > 0.3 and a - 0.03 < c.z < b + 0.03 and abs(c.x) < 0.125 - 0.012 * k,
                                   push=0.018 + 0.004 * (1 - k), dissolve_deg=12, clips=clips), "Metal")
    solidify(ab, 0.012)
    hard_bevel(ab, 0.0022, 1, 30)
    seam(nm, (0, 0, 0), (1, 0, 0), B(-0.006, 0.006, 0, 0.4, z0, z1))
    rivets(nm, ab, [((x, 0.4, (z0 + z1) * 0.5), FRONT) for x in (-xw + 0.018, xw - 0.018)])
    add_part(ab, "Spine")

# ================================================================ 2. back: graphite core housing, ceramic wing covers, reactor ring
back = mirrored(region({"Spine1", "Spine2"}, lambda v: abs(v.x) < 0.2 and 1.92 < v.z < 2.3))
def back_shell(push, clips):
    return hull_shell(back, lambda c, n: n.y < -0.3 and 1.94 < c.z < 2.3 and abs(c.x) < 0.16, push=push, clips=clips)

housing = new_object("BackHousing", panel_step(back_shell(0.024, xlim(0.15) + [below(2.262), above(1.95),
                     keep_side((0.0, 1.982), (0.3, 2.062), (0.0, 2.15)), keep_side((0.0, 1.982), (-0.3, 2.062), (0.0, 2.15)),
                     keep_side((0.12, 2.262), (0.15, 2.235), (0, 2.1)), keep_side((-0.12, 2.262), (-0.15, 2.235), (0, 2.1))]), 0.01), "Metal")
solidify(housing, 0.018)
hb = BVHTree.FromObject(housing, bpy.context.evaluated_depsgraph_get()).ray_cast(Vector((0, -0.6, 2.13)), Vector((0, 1, 0)), 1.0)
core_c = (hb[0] if hb[0] is not None else Vector((0, -0.2, 2.13))) + Vector((0, -0.018, 0))
hard_bevel(housing, 0.0025, 1, 30)
ring = lens(core_c, (0, -1, 0), 0.07, 0.044, 12, "Metal", taper=0.9)
ring.name = "ReactorRing"
hole = lens(core_c + Vector((0, -0.02, 0)), (0, -1, 0), 0.05, 0.06, 12, "Metal")
boolean(ring, hole, 'DIFFERENCE'); bpy.data.objects.remove(hole)
hard_bevel(ring, 0.0025, 1, 25)
bm = bmesh.new()
bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.048)
bmesh.ops.scale(bm, vec=Vector((1, 0.55, 1)), verts=bm.verts)
bmesh.ops.translate(bm, vec=core_c + Vector((0, 0.004, 0)), verts=bm.verts)
back_core = new_object("BackCore", bm, "Core")
flat(back_core, 60)
wings = []
for s in (-1, 1):
    wb = back_shell(0.047, [((s * 0.082, 0, 0), (-s, 0, 0)), ((s * 0.152, 0, 0), (s, 0, 0)), below(2.245), above(1.99),
                            keep_side((s * 0.082, 2.03), (s * 0.11, 1.99), (s * 0.12, 2.1)),
                            keep_side((s * 0.13, 2.245), (s * 0.152, 2.22), (s * 0.1, 2.1))])
    wing = new_object(("Right" if s > 0 else "Left") + "Wing", panel_step(wb, 0.007, 0.002, 0.002), "Ceramic")
    solidify(wing, 0.008)
    vent = inlay_on(wing, (s * 0.118, -0.5, 2.12), BACK, (0, 0, 1), 0.15, 0.011)
    hard_bevel(wing, 0.0022, 1, 30)
    if vent is not None:
        flat(vent, 50)
        wings.append(vent)
    wings.append(wing)
    wings += bolts_on(wing, [((s * 0.135, -0.5, 2.225), BACK), ((s * 0.1, -0.5, 2.005), BACK)])
rivets("BackHousing", ring, [((core_c.x + 0.06 * math.cos(math.radians(a)), -0.5, core_c.z + 0.06 * math.sin(math.radians(a))), BACK) for a in range(30, 360, 60)], r=0.0034)
seam("BackHousing", (0, 0, 2.215), (0, 0, 1), B(-0.08, 0.08, -0.4, 0.0, 2.2, 2.23))
slats("BackHousing", (0, 0, 2.02), (0, 0, 1), 0.008, 4, B(-0.045, 0.045, -0.4, 0.0, 1.99, 2.05))
back_part = join([housing, ring] + wings, "BackHousing")
add_part(back_part, "UpperChest")
add_part(back_core, "UpperChest", "Core")

for bone_name, z0, z1 in (("Chest", 1.925, 1.985), ("Spine", 1.81, 1.9)):
    pts = mirrored(region({"Spine", "Spine1", "Hips"}, lambda v, a=z0, b=z1: abs(v.x) < 0.1 and a - 0.05 < v.z < b + 0.05))
    if len(pts) < 8:
        continue
    nm = "Spine_" + bone_name
    seg = new_object(nm, hull_shell(pts, lambda c, n, a=z0, b=z1: n.y < -0.4 and a - 0.03 < c.z < b + 0.03 and abs(c.x) < 0.065, push=0.02, dissolve_deg=14,
                      clips=xlim(0.058) + [above(z0), below(z1), keep_side((0.04, z0), (0.058, z0 + 0.015), (0, (z0 + z1) / 2)),
                                           keep_side((-0.04, z0), (-0.058, z0 + 0.015), (0, (z0 + z1) / 2))]), "Metal")
    solidify(seg, 0.014)
    hard_bevel(seg, 0.0022, 1, 30)
    seam(nm, (0, 0, 0), (1, 0, 0), B(-0.005, 0.005, -0.4, 0, z0, z1))
    rivets(nm, seg, [((x, -0.5, (z0 + z1) / 2), BACK) for x in (-0.042, 0.042)])
    add_part(seg, bone_name)

# ================================================================ 3. pauldrons: slim ceramic cap following the shoulder + 2 graphite lames
for side, tag in ((-1, "Left"), (1, "Right")):
    s = side
    J = BONES[tag + "UpperArm"]
    nm = tag + "Pauldron"
    def off(c, J=J, s=s):
        return s * (c.x - J.x)
    pts = region({tag + "Arm", tag + "Shoulder"}, lambda v, J=J: (v - J).length < 0.16 and v.z > J.z - 0.07)
    # cap: top + outer + front/back of the deltoid, chamfered front/back and inner edges
    cap_bm = hull_shell(pts, lambda c, n, J=J: n.z > -0.25 and -0.065 < off(c) < 0.13 and c.z > J.z - 0.06, push=0.026, dissolve_deg=11,
                        clips=[((J.x - s * 0.055, 0, 0), (-s, 0, 0)), ((J.x + s * 0.112, 0, 0), (s, 0, 0)),
                               keep_side((J.x + s * 0.112, J.z - 0.02), (J.x + s * 0.085, J.z - 0.06), (J.x, J.z + 0.05)),
                               keep_side((J.x - s * 0.055, J.z + 0.035), (J.x - s * 0.02, J.z + 0.09), (J.x + s * 0.05, J.z)),
                               ((0, J.y + 0.09, 0), (0, 1, 0)), ((0, J.y - 0.105, 0), (0, -1, 0)),
                               keep_side_yz((J.y + 0.055, J.z - 0.07), (J.y + 0.09, J.z - 0.015), (J.y, J.z + 0.05), J.x),
                               keep_side_yz((J.y - 0.07, J.z - 0.07), (J.y - 0.105, J.z - 0.015), (J.y, J.z + 0.05), J.x),
                               below(J.z + 0.2), above(J.z - 0.065)])
    cap = new_object(tag + "Cap", panel_step(cap_bm, 0.009, 0.0024, 0.0026), "Ceramic")
    solidify(cap, 0.009)
    edge = inlay_on(cap, (J.x + s * 0.078, J.y - 0.01, J.z + 0.05), (s * 0.35, 0, 1), (0, 1, 0), 0.1, 0.011)
    hard_bevel(cap, 0.0024, 2, 30)
    pieces = [cap]
    if edge is not None:
        flat(edge, 50); pieces.append(edge)
    lames = []
    for k, (a0, a1, push) in enumerate(((0.095, 0.13, 0.017), (0.128, 0.16, 0.015))):
        lp = region({tag + "Arm"}, lambda v, J=J, a0=a0, a1=a1: a0 - 0.03 < s * (v.x - J.x) < a1 + 0.03)
        if len(lp) < 8:
            continue
        lb = hull_shell(lp, lambda c, n: n.z > -0.35 and abs(n.x) < 0.7, push=push, dissolve_deg=12,
                        clips=[((J.x + s * a0, 0, 0), (-s, 0, 0)), ((J.x + s * a1, 0, 0), (s, 0, 0)), above(J.z - 0.055)])
        lame = new_object(tag + "Lame%d" % k, lb, "Metal")
        solidify(lame, 0.008)
        hard_bevel(lame, 0.002, 1, 30)
        lames.append(lame)
        rivets(nm, lame, [((J.x + s * (a0 + a1) * 0.5, J.y + yy, J.z + 0.3), (0, 0, 1)) for yy in (-0.05, 0.03)], r=0.0032)
    pieces += lames
    pieces += bolts_on(cap, [((J.x + s * 0.0, J.y + 0.06, J.z + 0.3), (0, 0, 1)), ((J.x + s * 0.0, J.y - 0.075, J.z + 0.3), (0, 0, 1))], r=0.0048)
    paint(nm, (J.x + s * 0.045, 0, 0), (1, 0, 0), B(J.x - 0.2, J.x + 0.2, J.y - 0.2, J.y + 0.2, J.z + 0.02, J.z + 0.2), 0.0045, (0.58, 0.07, 0.09))
    seam(nm, (J.x - s * 0.02, 0, 0), (1, 0, 0), B(J.x - 0.2, J.x + 0.2, J.y - 0.2, J.y + 0.2, J.z + 0.02, J.z + 0.2))
    add_part(join(pieces, nm), tag + "UpperArm")

# ================================================================ 4. vambraces: tapered graphite guard + ceramic top plate
for side, tag in ((-1, "Left"), (1, "Right")):
    s = side
    E = BONES[tag + "LowerArm"]; W = BONES[tag + "Hand"]
    L = (W - E).length
    nm = tag + "Vambrace"
    pts = region({tag + "ForeArm"}, lambda v, E=E: E.x * s + 0.08 * L < v.x * s < E.x * s + 0.86 * L)
    if len(pts) < 8:
        continue
    vb = hull_shell(pts, lambda c, n, E=E: n.z > -0.25 and s * (c.x - E.x) < 0.8 * L, push=0.012, dissolve_deg=11,
                    clips=[((E.x + s * 0.12 * L, 0, 0), (-s, 0, 0)), ((E.x + s * 0.8 * L, 0, 0), (s, 0, 0))])
    vam = new_object(nm, panel_step(vb, 0.008, 0.0022, 0.0022), "Metal")
    solidify(vam, 0.01)
    mid = E + (W - E) * 0.5
    line = inlay_on(vam, (mid.x, mid.y + 0.06, mid.z), (0, 0.95, 0.3), (1, 0, 0), L * 0.42, 0.011)
    hard_bevel(vam, 0.0022, 1, 30)
    pieces = [vam]
    if line is not None:
        flat(line, 50); pieces.append(line)
    tp = hull_shell(pts, lambda c, n: n.z > 0.55, push=0.025, dissolve_deg=11,
                    clips=[((E.x + s * 0.2 * L, 0, 0), (-s, 0, 0)), ((E.x + s * 0.62 * L, 0, 0), (s, 0, 0)),
                           keep_side((E.x + s * 0.2 * L, E.z + 0.2), (E.x + s * 0.24 * L, E.z - 0.2), (E.x + s * 0.4 * L, E.z))])
    if tp.faces:
        top = new_object(tag + "VamTop", tp, "Ceramic")
        solidify(top, 0.007)
        hard_bevel(top, 0.002, 1, 30)
        pieces.append(top)
        pieces += bolts_on(top, [((E.x + s * 0.28 * L, E.y, E.z + 0.3), (0, 0, 1)), ((E.x + s * 0.56 * L, E.y, E.z + 0.3), (0, 0, 1))], r=0.0042)
    for t in (0.18, 0.7):
        seam(nm, (E.x + s * t * L, 0, 0), (1, 0, 0), B(E.x + s * (t - 0.05) * L, E.x + s * (t + 0.05) * L, -0.4, 0.4, 1.8, 2.6))
    add_part(join(pieces, nm), tag + "LowerArm")

# ================================================================ 5. helmet: visor strip (telegraph glow) + glass band + crest fin
head_pts = mirrored(region({"Head"}, lambda v: v.z > 2.38, sources=("Plane009",)))
hc = Vector((0.0, sum(p.y for p in head_pts) / len(head_pts), (min(p.z for p in head_pts) + max(p.z for p in head_pts)) * 0.5))
head_top = max(p.z for p in head_pts)
visor_z = head_top - VISOR_DROP
print("helmet top", round(head_top, 3), "centre", tuple(round(c, 3) for c in hc), "visor_z", round(visor_z, 3))
strip_out, strip_in = [], []
for i in range(-10, 11):
    a = math.radians(i * 6.5)
    d = Vector((math.sin(a), math.cos(a), 0.0))
    hit = surface_point(Vector((hc.x, hc.y, visor_z)) + d * 0.5, -d, 0.6)
    if hit is None:
        continue
    strip_out.append(hit + d * 0.011)
    strip_in.append(hit + d * 0.004)
bm = bmesh.new()
h = 0.008
rows = []
for po, pi in zip(strip_out, strip_in):
    rows.append([bm.verts.new(po + Vector((0, 0, h))), bm.verts.new(po - Vector((0, 0, h))),
                 bm.verts.new(pi - Vector((0, 0, h * 1.4))), bm.verts.new(pi + Vector((0, 0, h * 1.4)))])
for a, b2 in zip(rows[:-1], rows[1:]):
    for k in range(4):
        bm.faces.new((a[k], a[(k + 1) % 4], b2[(k + 1) % 4], b2[k]))
bm.faces.new(rows[0][::-1]); bm.faces.new(rows[-1])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
visor = new_object("VisorStrip", bm, "Glow")
flat(visor, 50)
hood_pts = []
for i in range(-12, 13):
    a = math.radians(i * 6.0)
    d = Vector((math.sin(a), math.cos(a), 0.0))
    for dz in (-0.03, -0.018, 0.018, 0.03):
        hit = surface_point(Vector((hc.x, hc.y, visor_z + dz)) + d * 0.5, -d, 0.6)
        if hit is not None:
            hood_pts.append(hit)
hood = new_object("VisorHood", hull_shell(hood_pts, lambda c, n: n.y > 0.15 and abs(n.z) < 0.8, push=0.003, dissolve_deg=6), "Visor")
solidify(hood, 0.005)
hard_bevel(hood, 0.0015, 1, 40)
prof = []
for i in range(0, 15):
    a = math.radians(40 + i * 10.5)
    d = Vector((0.0, math.cos(a), math.sin(a)))
    hit = surface_point(hc + d * 0.5, -d, 0.6)
    if hit is not None:
        prof.append(hit)
bm = bmesh.new()
fin_rows = []
n = len(prof)
for k, p in enumerate(prof):
    t = k / max(1, n - 1)
    height = 0.01 + 0.06 * math.sin(math.pi * min(1.0, 0.15 + t * 1.05)) * (1.0 - 0.35 * t)
    nrm = (p - hc).normalized()
    w = 0.006 * (1.0 - 0.5 * t) + 0.003
    base = p - nrm * 0.006
    top = p + nrm * height + Vector((0, -0.02 * t, 0))
    fin_rows.append([bm.verts.new(base + Vector((-w, 0, 0))), bm.verts.new(top + Vector((-w * 0.3, 0, 0))),
                     bm.verts.new(top + Vector((w * 0.3, 0, 0))), bm.verts.new(base + Vector((w, 0, 0)))])
for a, b2 in zip(fin_rows[:-1], fin_rows[1:]):
    for k in range(4):
        bm.faces.new((a[k], a[(k + 1) % 4], b2[(k + 1) % 4], b2[k]))
bm.faces.new(fin_rows[0][::-1]); bm.faces.new(fin_rows[-1])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
fin = new_object("CrestFin", bm, "Metal")
hard_bevel(fin, 0.0012, 1, 30)
add_part(join([hood, fin], "Helmet"), "Head")
anchor = Vector((0.0, max(v.co.y for v in visor.data.vertices), visor_z))
add_part(visor, "Head", "Visor", anchor)

# ================================================================ 6. waist: fauld (split centre gap) + layered tassets
fauld_pts = mirrored(region({"Hips", "Spine"}, lambda v: v.y > -0.02 and 1.62 < v.z < 1.82 and abs(v.x) < 0.2))
fb = hull_shell(fauld_pts, lambda c, n: n.y > 0.3 and 1.6 < c.z < 1.81 and abs(c.x) < 0.16, push=0.022, dissolve_deg=12,
                clips=xlim(0.152) + [below(1.785), keep_side((0.0, 1.64), (0.2, 1.7), (0.0, 1.75)), keep_side((0.0, 1.64), (-0.2, 1.7), (0.0, 1.75)),
                                     keep_side((0.13, 1.785), (0.152, 1.765), (0, 1.7)), keep_side((-0.13, 1.785), (-0.152, 1.765), (0, 1.7))])
fauld = new_object("Fauld", panel_step(fb, 0.009), "Metal")
solidify(fauld, 0.014)
hard_bevel(fauld, 0.0024, 1, 30)
seam("Fauld", (0, 0, 0), (1, 0, 0), B(-0.01, 0.01, 0, 0.4, 1.6, 1.79))
slats("Fauld", (0, 0, 1.757), (0, 0, 1), 0.007, 3, B(-0.06, 0.06, 0, 0.4, 1.74, 1.775))
rivets("Fauld", fauld, [((x, 0.4, 1.77), FRONT) for x in (-0.125, 0.125)])
fauld = join([fauld] + bolts_on(fauld, [((x, 0.4, 1.7), FRONT) for x in (-0.1, 0.1)]), "Fauld")
add_part(fauld, "Hips")
for side, tag in ((-1, "Left"), (1, "Right")):
    s = side
    nm = tag + "Tasset"
    pts = region({tag + "UpLeg", "Hips"}, lambda v: s * v.x > 0.12 and 1.34 < v.z < 1.68)
    if len(pts) < 8:
        continue
    tb = hull_shell(pts, lambda c, n: s * n.x > 0.42 and 1.34 < c.z < 1.68, push=0.022, dissolve_deg=12,
                    clips=[((s * 0.135, 0, 0), (-s, 0, 0)), below(1.65),
                           keep_side_yz((0.06, 1.36), (0.11, 1.45), (0.0, 1.5), 0), keep_side_yz((-0.14, 1.36), (-0.19, 1.45), (-0.05, 1.5), 0),
                           above(1.37)])
    tas = new_object(nm, panel_step(tb, 0.009), "Metal")
    solidify(tas, 0.012)
    hard_bevel(tas, 0.0024, 1, 30)
    cb = hull_shell(pts, lambda c, n: s * n.x > 0.42 and 1.34 < c.z < 1.68, push=0.038, dissolve_deg=12,
                    clips=[((s * 0.135, 0, 0), (-s, 0, 0)), below(1.635), above(1.535),
                           keep_side_yz((0.05, 1.535), (0.09, 1.6), (0.0, 1.6), 0), keep_side_yz((-0.12, 1.535), (-0.16, 1.6), (-0.05, 1.6), 0)])
    pieces = [tas]
    if cb.faces:
        capt = new_object(tag + "TassetCap", cb, "Ceramic")
        solidify(capt, 0.007)
        hard_bevel(capt, 0.002, 1, 30)
        pieces.append(capt)
        pieces += bolts_on(capt, [((s * 0.5, yy, 1.62), (s, 0, 0)) for yy in (-0.08, 0.02)], r=0.0045)
    seam(nm, (0, 0, 1.45), (0, 0, 1), B(s * 0.1, s * 0.4, -0.3, 0.3, 1.43, 1.47))
    rivets(nm, tas, [((s * 0.5, yy, 1.4), (s, 0, 0)) for yy in (-0.1, 0.03)])
    add_part(join(pieces, nm), tag + "UpperLeg")

# ================================================================ 7. legs: tapered thigh guards + shin guards with knee cops (new)
for side, tag in ((-1, "Left"), (1, "Right")):
    s = side
    nm = tag + "ThighGuard"
    pts = region({tag + "UpLeg"}, lambda v: 1.04 < v.z < 1.5)
    if len(pts) >= 8:
        cx = sum(p.x for p in pts if 1.1 < p.z < 1.4) / max(1, sum(1 for p in pts if 1.1 < p.z < 1.4))
        tb = hull_shell(pts, lambda c, n, cx=cx: n.y > 0.3 and abs(c.x - cx) < 0.1 and 1.08 < c.z < 1.46, push=0.016, dissolve_deg=11,
                        clips=[below(1.43), above(1.12),
                               keep_side((cx - 0.06, 1.43), (cx - 0.042, 1.12), (cx, 1.3)), keep_side((cx + 0.06, 1.43), (cx + 0.042, 1.12), (cx, 1.3)),
                               keep_side((cx - 0.042, 1.15), (cx - 0.012, 1.12), (cx, 1.3)), keep_side((cx + 0.042, 1.15), (cx + 0.012, 1.12), (cx, 1.3)),
                               keep_side((cx - 0.06, 1.405), (cx - 0.035, 1.43), (cx, 1.3)), keep_side((cx + 0.06, 1.405), (cx + 0.035, 1.43), (cx, 1.3))])
        if tb.faces:
            th = new_object(nm, panel_step(tb, 0.008, 0.0024, 0.0026), "Metal")
            solidify(th, 0.011)
            line = None
            hard_bevel(th, 0.0024, 1, 30)
            pieces = [th]
            if line is not None:
                flat(line, 50); pieces.append(line)
            pieces += bolts_on(th, [((cx - 0.038, 0.4, 1.405), FRONT), ((cx + 0.038, 0.4, 1.405), FRONT)], r=0.0045)
            seam(nm, (0, 0, 1.2), (0, 0.15, 1), B(cx - 0.08, cx + 0.08, 0, 0.4, 1.17, 1.23))
            rivets(nm, th, [((cx + xx, 0.4, 1.16), FRONT) for xx in (-0.025, 0.025)])
            add_part(join(pieces, nm), tag + "UpperLeg")

    nm = tag + "ShinGuard"
    pts = region({tag + "Leg"}, lambda v: 0.36 < v.z < 0.98)
    if len(pts) >= 8:
        cx = sum(p.x for p in pts if 0.5 < p.z < 0.8) / max(1, sum(1 for p in pts if 0.5 < p.z < 0.8))
        sb = hull_shell(pts, lambda c, n, cx=cx: n.y > 0.25 and abs(c.x - cx) < 0.1 and 0.4 < c.z < 0.86, push=0.015, dissolve_deg=11,
                        clips=[below(0.82), above(0.43),
                               keep_side((cx - 0.055, 0.82), (cx - 0.032, 0.43), (cx, 0.6)), keep_side((cx + 0.055, 0.82), (cx + 0.032, 0.43), (cx, 0.6)),
                               keep_side((cx - 0.032, 0.47), (cx, 0.43), (cx, 0.6)), keep_side((cx + 0.032, 0.47), (cx, 0.43), (cx, 0.6))])
        pieces = []
        if sb.faces:
            sh = new_object(nm, panel_step(sb, 0.008, 0.0024, 0.0026), "Metal")
            solidify(sh, 0.011)
            line = inlay_on(sh, (cx, 0.4, 0.66), FRONT, (0, 0, 1), 0.14, 0.01)
            hard_bevel(sh, 0.0024, 1, 30)
            pieces.append(sh)
            if line is not None:
                flat(line, 50); pieces.append(line)
            seam(nm, (0, 0, 0.56), (0, 0.2, 1), B(cx - 0.07, cx + 0.07, -0.1, 0.4, 0.53, 0.59))
            slats(nm, (0, 0, 0.775), (0, 0.1, 1), 0.007, 3, B(cx - 0.025, cx + 0.025, -0.1, 0.4, 0.75, 0.8))
            rivets(nm, sh, [((cx + xx, 0.4, 0.5), FRONT) for xx in (-0.02, 0.02)])
        kp = region({tag + "Leg", tag + "UpLeg"}, lambda v: 0.8 < v.z < 0.99)
        kb = hull_shell(kp, lambda c, n, cx=cx: n.y > 0.4 and abs(c.x - cx) < 0.08, push=0.024, dissolve_deg=12,
                        clips=[below(0.935), above(0.815), ((cx - 0.048, 0, 0), (-1, 0, 0)), ((cx + 0.048, 0, 0), (1, 0, 0)),
                               keep_side((cx - 0.048, 0.9), (cx - 0.02, 0.935), (cx, 0.87)), keep_side((cx + 0.048, 0.9), (cx + 0.02, 0.935), (cx, 0.87)),
                               keep_side((cx - 0.048, 0.845), (cx - 0.02, 0.815), (cx, 0.87)), keep_side((cx + 0.048, 0.845), (cx + 0.02, 0.815), (cx, 0.87))])
        if kb.faces:
            knee = new_object(tag + "KneeCop", kb, "Ceramic")
            solidify(knee, 0.009)
            hard_bevel(knee, 0.0022, 1, 30)
            pieces.append(knee)
            pieces += bolts_on(knee, [((cx, 0.4, 0.875), FRONT)], r=0.006)
        if pieces:
            add_part(join(pieces, nm), tag + "LowerLeg")

# ---------------------------------------------------------------- UVs (one shared atlas for Metal + Ceramic)
ATLAS_MATS = (0, 1)

def _uv_pass(objs, pred, **kw):
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_mode(type='FACE')
    for ob in objs:
        bm = bmesh.from_edit_mesh(ob.data)
        for f in bm.faces:
            f.select_set(False)
        for f in bm.faces:
            if pred(f.material_index):
                f.select_set(True)
        bm.select_flush_mode()
        bmesh.update_edit_mesh(ob.data)
    if kw.pop("pack", False):
        try:
            bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=0.003, shape_method=os.environ.get('TYPED_PACK_SHAPE', 'AABB'))  # AABB: reproducible layout (CONCAVE packs ~5% tighter but varies per run)
        except Exception as ex:
            print("pack_islands failed:", ex)
    else:
        bpy.ops.uv.smart_project(**kw)
    bpy.ops.object.mode_set(mode='OBJECT')

def _uv_islands(bm, uvl, faces):
    faces = set(faces); seen = set(); out = []
    for f in faces:
        if f in seen:
            continue
        seen.add(f); stack = [f]; isl = []
        while stack:
            g = stack.pop(); isl.append(g)
            for l in g.loops:
                a1 = l[uvl].uv; a2 = l.link_loop_next[uvl].uv
                for l2 in l.edge.link_loops:
                    h = l2.face
                    if h is g or h in seen or h not in faces:
                        continue
                    if l2.vert == l.vert:
                        b1 = l2[uvl].uv; b2 = l2.link_loop_next[uvl].uv
                    else:
                        b1 = l2.link_loop_next[uvl].uv; b2 = l2[uvl].uv
                    if (a1 - b1).length < 1e-5 and (a2 - b2).length < 1e-5:
                        seen.add(h); stack.append(h)
        out.append(isl)
    return out

def _shrink_hidden_islands(objs, scale=0.4):
    """Body-facing (inner) shell islands get less texel density: they are almost never seen."""
    bpy.ops.object.mode_set(mode='EDIT')
    shrunk = 0
    for ob in objs:
        bm = bmesh.from_edit_mesh(ob.data)
        uvl = bm.loops.layers.uv.active
        faces = [f for f in bm.faces if f.material_index in ATLAS_MATS]
        for isl in _uv_islands(bm, uvl, faces):
            a_in = a_all = 0.0
            for f in isl:
                c = f.calc_center_median(); ar = f.calc_area(); a_all += ar
                q = REF_BVH.find_nearest(c)[0]
                if q is not None and f.normal.dot(c - q) < -0.2 * (c - q).length:
                    a_in += ar
            if a_all > 0 and a_in / a_all > 0.75:
                cu = Vector((0.0, 0.0)); k = 0
                for f in isl:
                    for l in f.loops:
                        cu += l[uvl].uv; k += 1
                cu /= max(1, k)
                for f in isl:
                    for l in f.loops:
                        l[uvl].uv = cu + (l[uvl].uv - cu) * scale
                shrunk += 1
        bmesh.update_edit_mesh(ob.data)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("uv: shrunk %d hidden islands" % shrunk)

def unwrap_all():
    objs = [p[0] for p in PARTS]
    for ob in objs:
        if not ob.data.uv_layers:
            ob.data.uv_layers.new(name="UVMap")
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    # pass 1: energy / glass faces (own materials, never sample the atlas)
    _uv_pass(objs, lambda mi: mi not in ATLAS_MATS, angle_limit=math.radians(60), island_margin=0.02)
    # pass 2: armour faces share one atlas (uniform texel density across parts)
    _uv_pass(objs, lambda mi: mi in ATLAS_MATS, angle_limit=math.radians(66), island_margin=0.003, area_weight=0.0,
             correct_aspect=True, scale_to_bounds=False)
    _shrink_hidden_islands(objs)
    _uv_pass(objs, lambda mi: mi in ATLAS_MATS, pack=True)

def canonicalize(ob):
    """Rebuild the mesh in a canonical element order (sorted vertices, faces sorted by material + vertex ids).
    EXACT booleans are multi-threaded, so element order can differ between runs; UV unwrap/packing depend on it.
    Custom (hardened / weighted) corner normals and the wear attribute are carried over."""
    me = ob.data
    cn = me.corner_normals
    wear = me.attributes.get("wear")
    names = [m.name if m else "Metal" for m in me.materials]
    V = [tuple(round(c, 6) for c in v.co) for v in me.vertices]
    deg = [0] * len(V)
    for e in me.edges:
        deg[e.vertices[0]] += 1; deg[e.vertices[1]] += 1
    order = sorted(range(len(V)), key=lambda i: (V[i], deg[i]))
    remap = [0] * len(V)
    for new_i, old_i in enumerate(order):
        remap[old_i] = new_i
    faces = []
    for p in me.polygons:
        lis = list(p.loop_indices)
        vids = [remap[me.loops[li].vertex_index] for li in lis]
        nrm = [tuple(cn[li].vector) for li in lis]
        k = vids.index(min(vids))
        mi = MATS.index(names[p.material_index]) if names[p.material_index] in MATS else 0
        faces.append((mi, tuple(vids[k:] + vids[:k]), nrm[k:] + nrm[:k], wear.data[p.index].value if wear is not None else 0.0))
    faces.sort(key=lambda f: (f[0], f[1]))
    new = bpy.data.meshes.new(ob.name)
    new.from_pydata([me.vertices[i].co.copy() for i in order], [], [f[1] for f in faces])
    new.update()
    for n in MATS:
        new.materials.append(MAT[n])
    new.polygons.foreach_set("material_index", [f[0] for f in faces])
    new.polygons.foreach_set("use_smooth", [True] * len(faces))
    a = new.attributes.new("wear", 'FLOAT', 'FACE')
    a.data.foreach_set("value", [f[3] for f in faces])
    new.normals_split_custom_set([n for f in faces for n in f[2]])
    old = ob.data
    ob.data = new
    bpy.data.meshes.remove(old)

for _ob, *_rest in PARTS:
    canonicalize(_ob)
unwrap_all()

# ---------------------------------------------------------------- triangle soup for the bake
def gather():
    T = {"uv": [], "pos": [], "nrm": [], "mat": [], "part": [], "wear": []}
    for pi, (ob, bone, role, anchor) in enumerate(PARTS):
        me = ob.data
        me.calc_loop_triangles()
        uvl = me.uv_layers.active.data
        cn = me.corner_normals
        wear = me.attributes.get("wear")
        for tri in me.loop_triangles:
            if tri.material_index not in ATLAS_MATS:
                continue
            T["uv"].append([tuple(uvl[li].uv) for li in tri.loops])
            T["pos"].append([tuple(me.vertices[me.loops[li].vertex_index].co) for li in tri.loops])
            T["nrm"].append([tuple(cn[li].vector) for li in tri.loops])
            T["mat"].append(tri.material_index)
            T["part"].append(pi)
            T["wear"].append(wear.data[tri.polygon_index].value if wear is not None else 0.0)
    return {k: np.array(v, dtype=np.float64) for k, v in T.items()}

def rasterize(T, N):
    """Per texel: position, normal, material, part, wear (barycentric interpolation, pixel centres)."""
    pos = np.zeros((N, N, 3)); nrm = np.zeros((N, N, 3))
    mat = np.full((N, N), -1, np.int16); part = np.full((N, N), -1, np.int16); wear = np.zeros((N, N))
    uvs = T["uv"] * N - 0.5
    for i in range(len(uvs)):
        a, b, c = uvs[i]
        x0 = max(int(math.floor(min(a[0], b[0], c[0]))), 0); x1 = min(int(math.ceil(max(a[0], b[0], c[0]))), N - 1)
        y0 = max(int(math.floor(min(a[1], b[1], c[1]))), 0); y1 = min(int(math.ceil(max(a[1], b[1], c[1]))), N - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
        w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
        w2 = 1.0 - w0 - w1
        eps = -0.02
        m = (w0 >= eps) & (w1 >= eps) & (w2 >= eps)
        if not m.any():
            continue
        yy = ys[m]; xx = xs[m]
        W = np.stack([w0[m], w1[m], w2[m]], 1)
        pos[yy, xx] = W @ T["pos"][i]
        nrm[yy, xx] = W @ T["nrm"][i]
        mat[yy, xx] = T["mat"][i]; part[yy, xx] = T["part"][i]; wear[yy, xx] = T["wear"][i]
    ln = np.linalg.norm(nrm, axis=2, keepdims=True); nrm = nrm / np.maximum(ln, 1e-9)
    return pos, nrm, mat, part, wear

def dilate(img, covered, iters):
    """Push island colours into the gutters (mip/bilinear safe)."""
    img = img.copy(); cov = covered.copy()
    for _ in range(iters):
        acc = np.zeros_like(img); cnt = np.zeros(cov.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            sh = np.roll(np.roll(img, dy, 0), dx, 1); sc = np.roll(np.roll(cov, dy, 0), dx, 1)
            acc += sh * (sc[..., None] if img.ndim == 3 else sc); cnt += sc
        grow = (~cov) & (cnt > 0)
        if not grow.any():
            break
        if img.ndim == 3:
            img[grow] = acc[grow] / cnt[grow][:, None]
        else:
            img[grow] = acc[grow] / cnt[grow]
        cov = cov | grow
    return img

def hash3(ix, iy, iz):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFF).astype(np.float64) / 65535.0

def vnoise(P, freq, seed=0):
    """Smooth 3D value noise in [0,1] (vectorised, deterministic)."""
    q = P * freq + seed * 17.13
    i = np.floor(q).astype(np.int64); f = q - i
    f = f * f * (3 - 2 * f)
    out = 0.0
    for dz in (0, 1):
        for dy in (0, 1):
            for dx in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz)
    return out

def fbm(P, freq, octaves=3, seed=0):
    s = 0.0; a = 0.5; tot = 0.0
    for o in range(octaves):
        s = s + a * vnoise(P, freq * (2 ** o), seed + o); tot += a; a *= 0.5
    return s / tot

def blur(img, r):
    for _ in range(r):
        img = (img + np.roll(img, 1, 0) + np.roll(img, -1, 0) + np.roll(img, 1, 1) + np.roll(img, -1, 1)) / 5.0
    return img

def bake():
    T = gather()
    area3 = np.linalg.norm(np.cross(T["pos"][:, 1] - T["pos"][:, 0], T["pos"][:, 2] - T["pos"][:, 0]), axis=1).sum() * 0.5
    e1 = T["uv"][:, 1] - T["uv"][:, 0]; e2 = T["uv"][:, 2] - T["uv"][:, 0]
    auv = np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) * 0.5
    a3 = np.linalg.norm(np.cross(T["pos"][:, 1] - T["pos"][:, 0], T["pos"][:, 2] - T["pos"][:, 0]), axis=1) * 0.5
    good = (auv > 1e-10) & (a3 > 1e-8)
    ratio = np.sqrt(a3[good] / auv[good])          # metres per UV unit, per triangle
    o = np.argsort(ratio); cw = np.cumsum(a3[good][o]); m_per_uv = ratio[o][np.searchsorted(cw, cw[-1] * 0.4)]
    texel = m_per_uv / TEX                        # visible (non-shrunk) islands dominate the lower percentiles
    print("atlas: armour area %.3f m2, uv fill %.2f, texel %.2f mm" % (area3, auv.sum(), texel * 1000))

    pos, nrm, mat, part, wearf = rasterize(T, TEX)
    cov = mat >= 0
    idx = np.nonzero(cov)
    P = pos[idx]; Nn = nrm[idx]; M = mat[idx]; PT = part[idx]
    H = np.zeros(len(P)); PAINT = np.zeros((len(P), 3)); PAINTA = np.zeros(len(P)); GASKET = np.zeros(len(P))

    names = [p[0].name for p in PARTS]
    for pi, nm in enumerate(names):
        feats = FEAT.get(nm)
        if not feats:
            continue
        sel = np.nonzero(PT == pi)[0]
        if len(sel) == 0:
            continue
        Q = P[sel]
        for c, n, box, w, depth in feats["seams"]:
            inb = np.all((Q >= np.array(box[0]) - 1e-6) & (Q <= np.array(box[1]) + 1e-6), axis=1)
            if not inb.any():
                continue
            d = np.abs((Q[inb] - np.array(c)) @ np.array(n))
            prof = np.clip((w - d) / (w * 0.55), 0, 1)
            k = sel[inb]
            H[k] = np.minimum(H[k], -depth * prof)
            GASKET[k] = np.maximum(GASKET[k], np.clip((w * 0.55 - d) / (w * 0.3), 0, 1))
        for p, r in feats["rivets"]:
            d = np.linalg.norm(Q - np.array(p), axis=1)
            near = d < r * 1.7
            if not near.any():
                continue
            dd = d[near]; k = sel[near]
            dome = np.where(dd < r, 0.0011 * np.sqrt(np.clip(1 - (dd / r) ** 2, 0, 1)), 0.0)
            ring = np.where((dd >= r) & (dd < r * 1.45), -0.0006 * np.sin(np.clip((dd - r) / (r * 0.45), 0, 1) * math.pi), 0.0)
            H[k] = np.where(dd < r * 1.45, dome + ring, H[k])
        for c, n, box, w, rgb in feats["paint"]:
            if w <= 0:
                continue
            inb = np.all((Q >= np.array(box[0])) & (Q <= np.array(box[1])), axis=1)
            if not inb.any():
                continue
            d = np.abs((Q[inb] - np.array(c)) @ np.array(n))
            a = np.clip((w - d) / 0.0006, 0, 1)
            k = sel[inb]
            PAINT[k] = np.array(rgb); PAINTA[k] = np.maximum(PAINTA[k], a)

    # ---- height -> tangent-space normal (UV axes; neighbours must be the same surface)
    Himg = np.zeros((TEX, TEX)); Himg[idx] = H
    Pimg = pos
    def nb(dy, dx):
        hs = np.roll(np.roll(Himg, dy, 0), dx, 1)
        ps = np.roll(np.roll(Pimg, dy, 0), dx, 1)
        cs = np.roll(np.roll(cov, dy, 0), dx, 1)
        prt = np.roll(np.roll(part, dy, 0), dx, 1)
        ok = cs & (prt == part) & (np.linalg.norm(ps - Pimg, axis=2) < texel * 3.5)
        return hs, ok
    hxp, okxp = nb(0, -1); hxm, okxm = nb(0, 1); hyp, okyp = nb(-1, 0); hym, okym = nb(1, 0)
    dx = np.where(okxp & okxm, (hxp - hxm) * 0.5, np.where(okxp, hxp - Himg, np.where(okxm, Himg - hxm, 0.0)))
    dy = np.where(okyp & okym, (hyp - hym) * 0.5, np.where(okyp, hyp - Himg, np.where(okym, Himg - hym, 0.0)))
    nx = -dx / texel; ny = -dy / texel
    nz = np.ones_like(nx)
    L = np.sqrt(nx * nx + ny * ny + nz * nz)
    NM = np.stack([nx / L, ny / L, nz / L], 2)
    NM[~cov] = (0, 0, 1)
    NM = dilate(NM, cov, 10)

    # ---- ray-traced AO (armour + body), low res, upsampled
    aop, aon, aom, aopt, _ = rasterize(T, AO_RES)
    acov = aom >= 0
    all_bm = bmesh.new()
    for ob, *_ in PARTS:
        all_bm.from_mesh(ob.data)
    all_bm.from_mesh(REF_OB.data)
    bvh = BVHTree.FromBMesh(all_bm)
    rng = np.random.RandomState(7)
    dirs = []
    for k in range(14):   # cosine-ish hemisphere set (fixed)
        u, v = rng.rand(), rng.rand()
        r = math.sqrt(u); th = 2 * math.pi * v
        dirs.append((r * math.cos(th), r * math.sin(th), math.sqrt(max(0.0, 1 - u))))
    ao = np.ones((AO_RES, AO_RES))
    ai = np.nonzero(acov)
    for y, x in zip(*ai):
        n = Vector(aon[y, x]); p = Vector(aop[y, x]) + n * 0.0015
        t = n.orthogonal().normalized(); b = n.cross(t)
        occ = 0.0
        for dx_, dy_, dz_ in dirs:
            d = t * dx_ + b * dy_ + n * dz_
            hit = bvh.ray_cast(p, d, 0.09)
            if hit[0] is not None:
                occ += 1.0 - min(1.0, hit[3] / 0.09) * 0.6
        ao[y, x] = 1.0 - occ / len(dirs)
    ao = dilate(ao, acov, 6)
    # bilinear upsample
    g = (np.arange(TEX) + 0.5) * AO_RES / TEX - 0.5
    g0 = np.clip(np.floor(g).astype(int), 0, AO_RES - 1); g1 = np.clip(g0 + 1, 0, AO_RES - 1); gf = np.clip(g - g0, 0, 1)
    ao_r = ao[g0] * (1 - gf)[:, None] + ao[g1] * gf[:, None]
    AO = ao_r[:, g0] * (1 - gf)[None, :] + ao_r[:, g1] * gf[None, :]
    AOt = AO[idx]

    # ---- edge wear: bevel faces (+ soft spread), broken up by noise
    W = blur(np.where(cov, wearf, 0.0), 2)
    Wt = np.clip(W[idx] * 1.6, 0, 1)
    nz1 = fbm(P, 90.0, 3, 1); nz2 = fbm(P, 22.0, 3, 5)
    wear = np.clip((Wt - 0.25 + (nz1 - 0.5) * 0.9) * 2.2, 0, 1) * np.clip(Wt * 3, 0, 1)
    cav = np.clip(-H / 0.0011, 0, 1)
    grime = np.clip((1 - AOt) * 1.4 + cav * 0.6 + (nz2 - 0.55) * 0.35, 0, 1)
    up = np.clip(Nn[:, 2], 0, 1)   # dust settles on up-facing surfaces
    grime = np.clip(grime + up * 0.12 * (nz2 > 0.5), 0, 1)

    # ---- material layers (sRGB albedo, smoothness, metallic)
    metal = M == 0
    ceramic = M == 1
    tone = (fbm(P, 9.0, 2, 11) - 0.5) * 0.06
    brushed = (vnoise(P * np.array([1.0, 1.0, 0.04]), 260.0, 3) - 0.5) * 0.03
    alb = np.zeros((len(P), 3)); sm = np.zeros(len(P)); mt = np.zeros(len(P))
    gm = np.array([0.205, 0.215, 0.235])          # graphite gunmetal
    cer = np.array([0.555, 0.56, 0.572])          # corporate ceramic, value ~0.55
    steel = np.array([0.46, 0.47, 0.49])          # worn edge (bare metal)
    primer = np.array([0.36, 0.365, 0.38])        # chipped ceramic -> grey primer
    alb[metal] = gm * (1 + tone[metal, None] + brushed[metal, None])
    sm[metal] = 0.56 + brushed[metal] * 2; mt[metal] = 0.82
    alb[ceramic] = cer * (1 + tone[ceramic, None] * 0.6)
    sm[ceramic] = 0.5; mt[ceramic] = 0.0
    # paint (Tsukasa red, non-emissive) on ceramic
    pa = PAINTA[:, None] * (1 - wear[:, None] * 0.8)
    alb = alb * (1 - pa) + PAINT * pa
    sm = sm * (1 - PAINTA) + 0.42 * PAINTA
    # edge wear
    wm = wear[:, None]
    alb[metal] = alb[metal] * (1 - wm[metal]) + steel * wm[metal]
    sm[metal] = sm[metal] * (1 - wear[metal]) + 0.7 * wear[metal]
    wc = wm[ceramic] * 0.55
    alb[ceramic] = alb[ceramic] * (1 - wc) + primer * wc
    mt[ceramic] = mt[ceramic] * (1 - wc[:, 0]) + 0.3 * wc[:, 0]
    sm[ceramic] = sm[ceramic] * (1 - wear[ceramic]) + 0.55 * wear[ceramic]
    # rubber gaskets in seam bottoms
    gk = GASKET
    alb = alb * (1 - gk[:, None]) + np.array([0.07, 0.07, 0.075]) * gk[:, None]
    sm = sm * (1 - gk) + 0.18 * gk; mt = mt * (1 - gk)
    # grime / AO
    alb = alb * (1 - grime[:, None] * 0.38)
    sm = sm * (1 - grime * 0.35)
    occl = np.clip(AOt * (1 - cav * 0.55), 0, 1)

    def img(vals, ch, fill):
        I = np.zeros((TEX, TEX, ch)); I[...] = fill
        I[idx] = vals.reshape(len(P), ch)
        return dilate(I, cov, 10)
    ALB = img(np.clip(alb, 0, 1), 3, 0.2)
    OSM = img(np.stack([occl, np.clip(sm, 0, 1), np.clip(mt, 0, 1)], 1), 3, (1, 0.5, 0.5))
    save_png("typed_armor_albedo.png", ALB, 'sRGB')
    save_png("typed_armor_normal.png", NM * 0.5 + 0.5, 'Non-Color')
    save_png("typed_armor_osm.png", OSM, 'Non-Color')
    print("bake: texels %d" % len(P))

def save_png(name, rgb, cs):
    N = rgb.shape[0]
    im = bpy.data.images.new(name, N, N, alpha=False, float_buffer=False)
    im.colorspace_settings.name = cs
    px = np.ones((N, N, 4), np.float32); px[..., :3] = np.clip(rgb, 0, 1)
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = os.path.join(OUT_DIR, name)
    im.file_format = 'PNG'
    im.save()
    return im

# ---------------------------------------------------------------- export (welded vertices)
def export():
    """JSON for Unity's JsonUtility: flat float arrays (Unity axes), submeshes as a list."""
    data = {"parts": [], "materials": MATS}
    total = 0
    for ob, bone, role, anchor in PARTS:
        me = ob.data
        me.calc_loop_triangles()
        uvl = me.uv_layers.active.data
        cn = me.corner_normals
        pos, nor, uvs = [], [], []
        lut = {}
        tris = {i: [] for i in range(len(MATS))}
        for tri in me.loop_triangles:
            idx = []
            for li in tri.loops:
                co = me.vertices[me.loops[li].vertex_index].co
                nn = cn[li].vector
                uv = uvl[li].uv
                key = (round(co.x, 5), round(co.z, 5), round(co.y, 5), round(nn.x, 3), round(nn.z, 3), round(nn.y, 3), round(uv[0], 5), round(uv[1], 5))
                i = lut.get(key)
                if i is None:
                    i = len(lut); lut[key] = i
                    pos += [key[0], key[1], key[2]]
                    nor += [round(nn.x, 4), round(nn.z, 4), round(nn.y, 4)]
                    uvs += [key[6], key[7]]
                idx.append(i)
            tris[tri.material_index].extend([idx[0], idx[2], idx[1]])  # axis swap flips handedness
        part = {"name": ob.name, "bone": bone, "role": role, "positions": pos, "normals": nor, "uvs": uvs,
                "submeshes": [{"material": MATS[k], "indices": v} for k, v in tris.items() if v],
                "anchor": [anchor.x, anchor.z, anchor.y] if anchor is not None else []}
        data["parts"].append(part)
        nt = sum(len(v) for v in tris.values()) // 3
        total += nt
        print("part %-16s %-14s %-6s tris %5d verts %5d" % (ob.name, bone, role, nt, len(lut)))
    print("TOTAL TRIS", total)
    with open(OUT, "w") as f:
        json.dump(data, f)

export()
bake()

# ---------------------------------------------------------------- preview renders (EEVEE, PBR materials using the atlas)
def _nodes(idblock):
    try:
        idblock.use_nodes = True   # deprecated (always on) in Blender 5
    except Exception:
        pass

def preview_materials():
    def tex(name, cs):
        im = bpy.data.images.load(os.path.join(OUT_DIR, name), check_existing=True)
        im.colorspace_settings.name = cs
        return im
    alb, nrm, osm = tex("typed_armor_albedo.png", 'sRGB'), tex("typed_armor_normal.png", 'Non-Color'), tex("typed_armor_osm.png", 'Non-Color')
    for key in ("Metal", "Ceramic"):
        m = MAT[key]; _nodes(m)
        nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = alb
        tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = nrm
        to = nt.nodes.new("ShaderNodeTexImage"); to.image = osm
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nm = nt.nodes.new("ShaderNodeNormalMap")
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
        nt.links.new(to.outputs[0], sep.inputs[0])
        nt.links.new(ta.outputs[0], mul.inputs[6]); nt.links.new(sep.outputs[0], mul.inputs[7])
        nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
        nt.links.new(sep.outputs[1], inv.inputs[1]); nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
        nt.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
        nt.links.new(tn.outputs[0], nm.inputs["Color"]); nt.links.new(nm.outputs[0], bsdf.inputs["Normal"])
        nt.links.new(bsdf.outputs[0], out.inputs[0])
    def emissive(key, rgb, strength):
        m = MAT[key]; _nodes(m); nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial"); e = nt.nodes.new("ShaderNodeEmission")
        e.inputs[0].default_value = (*rgb, 1); e.inputs[1].default_value = strength
        nt.links.new(e.outputs[0], out.inputs[0])
    emissive("Glow", (0.878, 0.188, 0.227), 1.5)
    emissive("Core", (0.55, 0.85, 1.0), 2.0)
    for key, rgb, rough in (("Visor", (0.02, 0.022, 0.028), 0.12), ("Ref", (0.045, 0.047, 0.052), 0.75)):
        m = MAT[key]; _nodes(m); nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial"); b = nt.nodes.new("ShaderNodeBsdfPrincipled")
        b.inputs["Base Color"].default_value = (*rgb, 1); b.inputs["Roughness"].default_value = rough
        nt.links.new(b.outputs[0], out.inputs[0])
    # visor strip uses the visor glow (telegraph idle cyan) in game
    vg = bpy.data.materials.new("VisorGlowPreview"); _nodes(vg); nt = vg.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); e = nt.nodes.new("ShaderNodeEmission")
    e.inputs[0].default_value = (0.35, 0.7, 1.0, 1); e.inputs[1].default_value = 1.6
    nt.links.new(e.outputs[0], out.inputs[0])
    for ob, bone, role, anchor in PARTS:
        if role == "Visor":
            ob.data.materials[MATS.index("Glow")] = vg

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    preview_materials()
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 1000; sc.render.resolution_y = 1000
    sc.view_settings.view_transform = 'AgX'
    try:
        sc.eevee.taa_render_samples = 32
        sc.eevee.use_raytracing = True
    except Exception:
        pass
    world = bpy.data.worlds.new("w"); sc.world = world; _nodes(world)
    bg = world.node_tree.nodes.get("Background")
    sky = world.node_tree.nodes.new("ShaderNodeTexGradient")
    ramp = world.node_tree.nodes.new("ShaderNodeValToRGB")
    tc = world.node_tree.nodes.new("ShaderNodeTexCoord")
    sepz = world.node_tree.nodes.new("ShaderNodeSeparateXYZ")
    world.node_tree.links.new(tc.outputs["Generated"], sepz.inputs[0])
    world.node_tree.links.new(sepz.outputs[2], ramp.inputs[0])
    ramp.color_ramp.elements[0].color = (0.012, 0.012, 0.015, 1); ramp.color_ramp.elements[1].color = (0.11, 0.12, 0.14, 1)
    world.node_tree.links.new(ramp.outputs[0], bg.inputs[0]); bg.inputs[1].default_value = 1.0
    def light(name, kind, loc, energy, color, size=1.0):
        ld = bpy.data.lights.new(name, kind); ld.energy = energy; ld.color = color
        if kind == 'AREA':
            ld.size = size
        lo = bpy.data.objects.new(name, ld); sc.collection.objects.link(lo); lo.location = loc
        d = Vector((0, 0, 1.9)) - Vector(loc); lo.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    light("key", 'AREA', (1.6, 2.4, 3.2), 260, (1.0, 0.97, 0.93), 1.5)
    light("fill", 'AREA', (-2.2, 1.2, 2.2), 60, (0.75, 0.85, 1.0), 2.5)
    light("rim", 'AREA', (-0.6, -2.6, 3.0), 220, (1.0, 0.35, 0.33), 1.2)   # facility red rim
    light("rim2", 'AREA', (1.8, -2.0, 2.6), 120, (0.8, 0.9, 1.0), 1.2)
    cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam); sc.camera = cam
    cam_data.lens = 60
    shots = (("front", (0.0, 2.6, 2.05), (0, 0, 2.0)), ("tq", (1.6, 1.9, 2.3), (0, 0, 2.05)), ("side", (2.6, 0.2, 2.05), (0, 0, 2.0)),
             ("back34", (-1.6, -1.9, 2.3), (0, 0, 2.05)), ("full", (0.9, 5.2, 1.45), (0, 0, 1.3)), ("legs", (0.9, 2.4, 1.0), (0, 0, 0.95)),
             ("shoulder", (0.75, 0.75, 2.42), (0.2, 0, 2.18)), ("shoulder_l", (-0.75, 0.75, 2.42), (-0.2, 0, 2.18)),
             ("chest_close", (0.25, 1.1, 2.15), (0.0, 0.0, 2.08)))
    for nm_, eye, tgt in shots:
        cam.location = Vector(eye)
        dirv = Vector(tgt) - Vector(eye)
        cam.rotation_euler = dirv.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(PREVIEW, "armor_" + nm_ + ".png")
        bpy.ops.render.render(write_still=True)
print("DONE", len(PARTS))
