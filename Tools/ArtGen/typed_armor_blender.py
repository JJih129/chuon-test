"""Type-D hard-surface armor generator (iteration 10). Blender 5.x headless.

Run:
  blender -b --factory-startup --python typed_armor_blender.py -- <in_dir> <out_json> [preview_dir]

<in_dir> must contain the Type-D reference exported from Unity in the edit-mode T-pose
(Body.obj / Object012.obj + *_bones.txt with the dominant bone per vertex, bones.txt with
HumanBodyBones positions). Coordinates are boss-root local metres converted to Blender axes:
  blender (x, y, z) = unity (x, z, y)   (x = character right, y = front, z = up)

Every part is fitted to the reference body:
  * torso plates are convex hulls of a body region (mirrored for symmetry) -> outward-facing faces ->
    limited dissolve (clean planar facets) -> pushed out along the normals -> solidify -> bevel.
    The convex hull bridges the chest, so the cuirass reads as a flat, gender-neutral armour plate.
  * pauldrons are faceted domes centred on the shoulder joint (radius > skin distance), so they
    stay clear of the deforming shoulder when the arm rotates about the joint.
  * glowing inlays are boolean channels with a narrower flush insert (material 'Glow').
The result is written as JSON (per-corner positions/normals/uvs in Unity axes, triangles per
material) that the Unity side (TypeDArmorBuilder) turns into Mesh assets bound to bones.
Materials: 0 Metal (graphite brushed), 1 Ceramic (corporate white), 2 Glow, 3 Visor, 4 Core.
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
IN = argv[0]
OUT = argv[1]
PREVIEW = argv[2] if len(argv) > 2 else None
VISOR_DROP = float(argv[3]) if len(argv) > 3 else 0.115  # visor height below the helmet top (m)

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = ["Metal", "Ceramic", "Glow", "Visor", "Core"]
MAT = {}
COLORS = {"Metal": (0.16, 0.17, 0.19, 1), "Ceramic": (0.86, 0.87, 0.88, 1), "Glow": (0.4, 0.9, 1.0, 1),
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

# reference body object (preview + ray casts)
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

# ---------------------------------------------------------------- helpers
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
    """Convex hull -> keep(face_center, face_normal) faces -> planar facets -> pushed outward."""
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
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(dissolve_deg), verts=bm.verts[:], edges=bm.edges[:])
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * push
    # 깔끔한 외곽선: 평면으로 잘라낸다 (co, no): no 쪽을 버림
    for co, no in clips:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector(co), plane_no=Vector(no).normalized(), clear_outer=True)
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    return bm

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

def finish(ob, thickness, bevel=0.004, segments=2, smooth_angle=32.0):
    s = ob.modifiers.new("solid", 'SOLIDIFY')
    s.thickness = thickness
    s.offset = 1.0
    s.use_even_offset = True
    s.use_rim = True
    if bevel > 0:
        b = ob.modifiers.new("bevel", 'BEVEL')
        b.width = bevel
        b.segments = segments
        b.limit_method = 'ANGLE'
        b.angle_limit = math.radians(35)
        b.harden_normals = False
    apply_all(ob)
    shade(ob, smooth_angle)
    return ob

def apply_all(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)

def shade(ob, angle):
    for p in ob.data.polygons:
        p.use_smooth = True
    try:
        ob.data.set_sharp_from_angle(angle=math.radians(angle))
    except Exception:
        pass

def boolean(target, cutter, op):
    m = target.modifiers.new("bool", 'BOOLEAN')
    m.operation = op
    m.object = cutter
    try:
        m.solver = 'EXACT'
    except Exception:
        pass
    apply_all(target)

def box_obj(name, center, size, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    R = (Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X'))
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(center)) @ R, verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob

def copy_object(ob, name):
    c = ob.copy(); c.data = ob.data.copy(); c.name = name
    bpy.context.scene.collection.objects.link(c)
    return c

def inlay(plate, center, size, rot, insert_ratio=0.5):
    """Carve a channel and return a flush glowing insert (separate object, material Glow)."""
    solid = copy_object(plate, plate.name + "_src")
    cut = box_obj("cut", center, size, rot)
    boolean(plate, cut, 'DIFFERENCE')
    ins_cut = box_obj("ins", center, (size[0] * insert_ratio, size[1] * 1.02, size[2] * insert_ratio) if size[1] > size[0] else (size[0] * 1.02, size[1] * insert_ratio, size[2] * insert_ratio), rot)
    boolean(solid, ins_cut, 'INTERSECT')
    for p in solid.data.polygons:
        p.material_index = MATS.index("Glow")
    bpy.data.objects.remove(cut); bpy.data.objects.remove(ins_cut)
    return solid

def join(objs, name):
    objs = [o for o in objs if o is not None and len(o.data.polygons) > 0]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    return ob

def dome(center, radius, keep, seg=12, rings=8, scale=(1, 1, 1), push=0.0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=radius)
    bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    bm.normal_update()
    dead = [f for f in bm.faces if not keep(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=dead, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    return bm

def surface_point(origin, direction, maxd=1.0):
    hit = REF_BVH.ray_cast(origin, direction.normalized(), maxd)
    return hit[0]

def smart_uv(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')

PARTS = []  # (object, bone, role, anchor)

def add_part(ob, bone, role="Plate", anchor=None):
    PARTS.append((ob, bone, role, anchor))

# ---------------------------------------------------------------- shaping helpers (silhouette / inlays)
def prism(name, pts_xz, y0, y1):
    """Polygon in the XZ (front view) plane extruded along Y (front-back)."""
    bm = bmesh.new()
    a = [bm.verts.new((x, y0, z)) for x, z in pts_xz]
    b2 = [bm.verts.new((x, y1, z)) for x, z in pts_xz]
    bm.faces.new(a)
    bm.faces.new(b2[::-1])
    n = len(pts_xz)
    for i in range(n):
        bm.faces.new((a[i], a[(i + 1) % n], b2[(i + 1) % n], b2[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob

def cut_xz(target, polys, y0=-1.0, y1=1.0):
    for i, poly in enumerate(polys):
        c = prism("cut%d" % i, poly, y0, y1)
        boolean(target, c, 'DIFFERENCE')
        bpy.data.objects.remove(c)

def trim_z(target, z0, z1, xr=1.5):
    """Clean horizontal edges: remove everything below z0 / above z1 (hull triangles leave jagged rims)."""
    polys = []
    if z0 is not None:
        polys.append([(-xr, z0 - 2.0), (xr, z0 - 2.0), (xr, z0), (-xr, z0)])
    if z1 is not None:
        polys.append([(-xr, z1), (xr, z1), (xr, z1 + 2.0), (-xr, z1 + 2.0)])
    cut_xz(target, polys)

def mirror_poly(poly):
    return [(-x, z) for x, z in reversed(poly)]

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

def inlay_on(plate, origin, ray_dir, tangent, length, width, depth=0.005, ratio=0.45):
    """Ray-cast the plate from outside; carve a channel along `tangent`; return the flush glow insert."""
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(plate, dg)
    ray_dir = Vector(ray_dir).normalized()
    hit = bvh.ray_cast(Vector(origin) + ray_dir * 0.4, -ray_dir, 1.0)
    if hit[0] is None:
        print("inlay miss", plate.name, origin)
        return None
    p, n = hit[0], hit[1].normalized()
    t = Vector(tangent); t = (t - n * t.dot(n)).normalized(); b = n.cross(t).normalized()
    solid = copy_object(plate, plate.name + "_src")
    cut = framed_box("cut", p, t, b, n, (length, width, depth * 2.0))
    boolean(plate, cut, 'DIFFERENCE')
    ins = framed_box("ins", p, t, b, n, (length * 0.97, width * ratio, depth * 2.0))
    boolean(solid, ins, 'INTERSECT')
    bpy.data.objects.remove(cut); bpy.data.objects.remove(ins)
    if len(solid.data.polygons) == 0:
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

# ---------------------------------------------------------------- measurements
chest_b = BONES["UpperChest"]; spine1 = BONES["Chest"]; spine = BONES["Spine"]; hips = BONES["Hips"]
neck = BONES["Neck"]; head = BONES["Head"]

# ---------------------------------------------------------------- 1. chest plate (cuirass front)
# 볼록 껍질이 가슴 사이를 평평하게 잇는다 -> 성별 중립 실루엣. 아래는 V 자, 위 모서리는 겨드랑이 쪽으로 깎는다.
torso = mirrored(region({"Spine1", "Spine2"}, lambda v: abs(v.x) < 0.21 and 1.9 < v.z < 2.3))
chest_z0, chest_z1 = 1.93, 2.255
cuirass = new_object("ChestPlate", hull_shell(
    torso, lambda c, n: n.y > 0.22 and chest_z0 - 0.04 < c.z < chest_z1 + 0.04 and abs(c.x) < 0.185, push=0.02,
    clips=xlim(0.178) + [below(chest_z1), above(chest_z0 - 0.06),
           keep_side((0.0, chest_z0), (0.3, chest_z0 + 0.09), (0.0, 2.1)),
           keep_side((0.0, chest_z0), (-0.3, chest_z0 + 0.09), (0.0, 2.1)),
           keep_side((0.1, chest_z1), (0.3, chest_z1 - 0.09), (0.0, 2.0)),
           keep_side((-0.1, chest_z1), (-0.3, chest_z1 - 0.09), (0.0, 2.0))]), "Metal")
finish(cuirass, 0.018, bevel=0.005)
cz = 2.06
chev = []
for side in (-1, 1):
    g = inlay_on(cuirass, (side * 0.06, 0.3, cz - 0.03), (0, 1, 0), (side * 0.9, 0, 0.42), 0.12, 0.016)
    chev.append(g)
front = BVHTree.FromObject(cuirass, bpy.context.evaluated_depsgraph_get()).ray_cast(Vector((0, 0.5, cz + 0.06)), Vector((0, -1, 0)), 1.0)
lens_c = front[0] if front[0] is not None else Vector((0, 0.2, cz + 0.06))
lens_n = front[1] if front[1] is not None else Vector((0, 1, 0))
lc = lens(lens_c, lens_n, 0.03, 0.03, 6, "Metal")
boolean(cuirass, lc, 'DIFFERENCE'); bpy.data.objects.remove(lc)
chest_core = lens(lens_c - lens_n * 0.006, lens_n, 0.024, 0.016, 6, "Core", taper=0.7)
chest_core.name = "ChestCore"
shade(chest_core, 40)
chest = join([cuirass] + chev, "ChestPlate")
add_part(chest, "UpperChest")
add_part(chest_core, "UpperChest", "Core")

# collar band around the neck base
collar_pts = mirrored(region({"Spine2", "Neck"}, lambda v: 2.2 < v.z < 2.36 and abs(v.x) < 0.14))
collar = new_object("Collar", hull_shell(collar_pts, lambda c, n: abs(n.z) < 0.5 and 2.25 < c.z < 2.33, push=0.012, dissolve_deg=10), "Ceramic")
finish(collar, 0.012, bevel=0.003)
add_part(collar, "UpperChest")

# ab plates (two bands, on the Spine bone) -> hide the waist curve
for k, (z0, z1) in enumerate(((1.865, 1.925), (1.795, 1.855))):
    pts = mirrored(region({"Spine", "Spine1", "Hips"}, lambda v, a=z0, b=z1: a - 0.05 < v.z < b + 0.05 and abs(v.x) < 0.18))
    ab = new_object("AbPlate%d" % k, hull_shell(pts, lambda c, n, a=z0, b=z1: n.y > 0.3 and a - 0.03 < c.z < b + 0.03 and abs(c.x) < 0.125 - 0.012 * k, push=0.018, dissolve_deg=12,
                     clips=xlim(0.118 - 0.012 * k) + [above(z0), below(z1)]), "Metal")
    finish(ab, 0.014, bevel=0.004)
    add_part(ab, "Spine")

# ---------------------------------------------------------------- 2. back core housing + spine plates
back = mirrored(region({"Spine1", "Spine2"}, lambda v: abs(v.x) < 0.2 and 1.92 < v.z < 2.3))
housing = new_object("BackHousing", hull_shell(
    back, lambda c, n: n.y < -0.3 and 1.94 < c.z < 2.3 and abs(c.x) < 0.16, push=0.024,
    clips=xlim(0.15) + [below(2.262), above(1.95), keep_side((0.0, 1.985), (0.3, 2.06), (0.0, 2.15)), keep_side((0.0, 1.985), (-0.3, 2.06), (0.0, 2.15))]), "Ceramic")
finish(housing, 0.02, bevel=0.005)
hb = BVHTree.FromObject(housing, bpy.context.evaluated_depsgraph_get()).ray_cast(Vector((0, -0.6, 2.13)), Vector((0, 1, 0)), 1.0)
core_c = (hb[0] if hb[0] is not None else Vector((0, -0.2, 2.13))) + Vector((0, -0.018, 0))
ring = lens(core_c, (0, -1, 0), 0.072, 0.05, 16, "Metal", taper=0.9)
ring.name = "ReactorRing"
hole = lens(core_c + Vector((0, -0.02, 0)), (0, -1, 0), 0.05, 0.06, 16, "Metal")
boolean(ring, hole, 'DIFFERENCE'); bpy.data.objects.remove(hole)
b = ring.modifiers.new("bevel", 'BEVEL'); b.width = 0.004; b.segments = 2; b.limit_method = 'ANGLE'
apply_all(ring); shade(ring, 35)
bm = bmesh.new()
bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.048)
bmesh.ops.scale(bm, vec=Vector((1, 0.55, 1)), verts=bm.verts)
bmesh.ops.translate(bm, vec=core_c + Vector((0, 0.004, 0)), verts=bm.verts)
back_core = new_object("BackCore", bm, "Core")
shade(back_core, 60)
vents = [inlay_on(housing, (sx * 0.095, -0.5, 2.13), (0, -1, 0), (0, 0, 1), 0.14, 0.018) for sx in (-1, 1)]
back_part = join([housing, ring] + vents, "BackHousing")
add_part(back_part, "UpperChest")
add_part(back_core, "UpperChest", "Core")

for bone_name, z0, z1 in (("Chest", 1.925, 1.985), ("Spine", 1.81, 1.9)):
    pts = mirrored(region({"Spine", "Spine1", "Hips"}, lambda v, a=z0, b=z1: abs(v.x) < 0.1 and a - 0.05 < v.z < b + 0.05))
    if len(pts) < 8:
        continue
    seg = new_object("Spine_" + bone_name, hull_shell(pts, lambda c, n, a=z0, b=z1: n.y < -0.4 and a - 0.03 < c.z < b + 0.03 and abs(c.x) < 0.065, push=0.02, dissolve_deg=14,
                      clips=xlim(0.058) + [above(z0), below(z1)]), "Metal")
    finish(seg, 0.016, bevel=0.004)
    add_part(seg, bone_name)

# ---------------------------------------------------------------- 3. pauldrons (layered faceted domes about the joint)
for side, tag in ((-1, "Left"), (1, "Right")):
    J = BONES[tag + "UpperArm"]
    pts = region({tag + "Arm", tag + "Shoulder"}, lambda v, J=J: (v - J).length < 0.15 and v.z > J.z - 0.06)
    dmax = max(((p - J).length for p in pts), default=0.1)
    R = min(dmax + 0.03, 0.165)
    print(tag, "pauldron dmax", round(dmax, 3), "R", round(R, 3))
    lame1 = new_object(tag + "Pauldron", dome(J + Vector((side * 0.012, 0, 0.006)), R,
        lambda c, n, J=J: c.z > J.z - 0.012 and side * (c.x - J.x) > -0.05, seg=12, rings=9, scale=(1.12, 1.0, 0.92)), "Ceramic")
    finish(lame1, 0.014, bevel=0.0035, smooth_angle=26)
    lame2 = new_object(tag + "PauldronLame", dome(J + Vector((side * 0.03, 0, -0.004)), R * 0.94,
        lambda c, n, J=J: c.z > J.z - 0.075 and side * (c.x - J.x) > 0.035, seg=12, rings=9, scale=(1.08, 1.0, 0.9)), "Metal")
    finish(lame2, 0.012, bevel=0.003, smooth_angle=26)
    edge = inlay_on(lame1, (J.x + side * 0.06, J.y, J.z + 0.05), (side * 0.6, 0, 0.8), (0, 1, 0), 0.16, 0.014)
    add_part(join([lame1, lame2, edge], tag + "Pauldron"), tag + "UpperArm")

# ---------------------------------------------------------------- 4. vambraces (forearm guards over the gauntlet layer)
for side, tag in ((-1, "Left"), (1, "Right")):
    E = BONES[tag + "LowerArm"]; W = BONES[tag + "Hand"]
    L = (W - E).length
    pts = region({tag + "ForeArm"}, lambda v, E=E: E.x * side + 0.08 * L < v.x * side < E.x * side + 0.86 * L)
    if len(pts) < 8:
        continue
    vam = new_object(tag + "Vambrace", hull_shell(pts, lambda c, n: n.z > -0.25 and side * (c.x - E.x) < 0.8 * L, push=0.012, dissolve_deg=11), "Ceramic")
    finish(vam, 0.012, bevel=0.003)
    mid = E + (W - E) * 0.5
    line = inlay_on(vam, (mid.x, mid.y, mid.z), (0, 0, 1), (1, 0, 0), L * 0.5, 0.012)
    add_part(join([vam, line], tag + "Vambrace"), tag + "LowerArm")

# ---------------------------------------------------------------- 5. helmet: visor strip (telegraph glow) + hood + crest fin
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
shade(visor, 50)
hood_pts = []
for i in range(-12, 13):
    a = math.radians(i * 6.0)
    d = Vector((math.sin(a), math.cos(a), 0.0))
    for dz in (-0.03, -0.018, 0.018, 0.03):
        hit = surface_point(Vector((hc.x, hc.y, visor_z + dz)) + d * 0.5, -d, 0.6)
        if hit is not None:
            hood_pts.append(hit)
hood = new_object("VisorHood", hull_shell(hood_pts, lambda c, n: n.y > 0.15 and abs(n.z) < 0.8, push=0.003, dissolve_deg=6), "Visor")
finish(hood, 0.005, bevel=0.0015, smooth_angle=40)
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
    height = 0.01 + 0.07 * math.sin(math.pi * min(1.0, 0.15 + t * 1.05)) * (1.0 - 0.35 * t)
    nrm = (p - hc).normalized()
    w = 0.007 * (1.0 - 0.5 * t) + 0.003
    base = p - nrm * 0.006
    top = p + nrm * height + Vector((0, -0.02 * t, 0))
    fin_rows.append([bm.verts.new(base + Vector((-w, 0, 0))), bm.verts.new(top + Vector((-w * 0.35, 0, 0))),
                     bm.verts.new(top + Vector((w * 0.35, 0, 0))), bm.verts.new(base + Vector((w, 0, 0)))])
for a, b2 in zip(fin_rows[:-1], fin_rows[1:]):
    for k in range(4):
        bm.faces.new((a[k], a[(k + 1) % 4], b2[(k + 1) % 4], b2[k]))
bm.faces.new(fin_rows[0][::-1]); bm.faces.new(fin_rows[-1])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
fin = new_object("CrestFin", bm, "Metal")
b = fin.modifiers.new("bevel", 'BEVEL'); b.width = 0.0015; b.segments = 1; b.limit_method = 'ANGLE'
apply_all(fin); shade(fin, 30)
add_part(join([hood, fin], "Helmet"), "Head")
anchor = Vector((0.0, max(v.co.y for v in visor.data.vertices), visor_z))
add_part(visor, "Head", "Visor", anchor)

# ---------------------------------------------------------------- 6. waist: fauld + side tassets
fauld_pts = mirrored(region({"Hips", "Spine"}, lambda v: v.y > -0.02 and 1.62 < v.z < 1.82 and abs(v.x) < 0.2))
fauld = new_object("Fauld", hull_shell(fauld_pts, lambda c, n: n.y > 0.3 and 1.6 < c.z < 1.81 and abs(c.x) < 0.16, push=0.022, dissolve_deg=12,
                  clips=xlim(0.152) + [below(1.785), keep_side((0.0, 1.64), (0.2, 1.7), (0.0, 1.75)), keep_side((0.0, 1.64), (-0.2, 1.7), (0.0, 1.75))]), "Metal")
finish(fauld, 0.016, bevel=0.004)
add_part(fauld, "Hips")
for side, tag in ((-1, "Left"), (1, "Right")):
    pts = region({tag + "UpLeg", "Hips"}, lambda v: side * v.x > 0.12 and 1.34 < v.z < 1.68)
    if len(pts) < 8:
        continue
    tas = new_object(tag + "Tasset", hull_shell(pts, lambda c, n: side * n.x > 0.42 and 1.34 < c.z < 1.68, push=0.024, dissolve_deg=12,
                     clips=[((side * 0.135, 0, 0), (-side, 0, 0)), below(1.655), keep_side((side * 0.1, 1.42), (side * 0.3, 1.36), (side * 0.2, 1.55))]), "Ceramic")
    finish(tas, 0.014, bevel=0.004)
    add_part(tas, tag + "UpperLeg")

# ---------------------------------------------------------------- UVs + export
def export():
    """JSON for Unity's JsonUtility: flat float arrays (Unity axes), submeshes as a list."""
    data = {"parts": [], "materials": MATS}
    for ob, bone, role, anchor in PARTS:
        smart_uv(ob)
        me = ob.data
        me.calc_loop_triangles()
        uvl = me.uv_layers.active.data if me.uv_layers.active else None
        cn = me.corner_normals
        pos, nor, uvs = [], [], []
        tris = {i: [] for i in range(len(MATS))}
        count = 0
        for tri in me.loop_triangles:
            idx = []
            for li in tri.loops:
                co = me.vertices[me.loops[li].vertex_index].co
                nn = cn[li].vector
                pos += [round(co.x, 5), round(co.z, 5), round(co.y, 5)]
                nor += [round(nn.x, 4), round(nn.z, 4), round(nn.y, 4)]
                uv = uvl[li].uv if uvl else (0, 0)
                uvs += [round(uv[0], 4), round(uv[1], 4)]
                idx.append(count)
                count += 1
            tris[tri.material_index].extend([idx[0], idx[2], idx[1]])  # axis swap flips handedness
        part = {"name": ob.name, "bone": bone, "role": role, "positions": pos, "normals": nor, "uvs": uvs,
                "submeshes": [{"material": MATS[k], "indices": v} for k, v in tris.items() if v],
                "anchor": [anchor.x, anchor.z, anchor.y] if anchor is not None else []}
        data["parts"].append(part)
        print("part", ob.name, bone, role, "tris", sum(len(v) for v in tris.values()) // 3)
    with open(OUT, "w") as f:
        json.dump(data, f)

export()

# ---------------------------------------------------------------- preview renders (workbench)
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_cavity = True
    sc.render.resolution_x = 900; sc.render.resolution_y = 900
    cam_data = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam); sc.camera = cam
    cam_data.lens = 60
    for nm, eye, tgt in (("front", (0.0, 2.6, 2.05), (0, 0, 2.0)), ("side", (2.6, 0.2, 2.05), (0, 0, 2.0)),
                         ("back", (0.4, -2.6, 2.1), (0, 0, 2.0)), ("tq", (1.6, 1.9, 2.3), (0, 0, 2.05)),
                         ("head", (0.35, 1.0, 2.62), (0, 0, 2.52)), ("full", (0.8, 5.0, 1.4), (0, 0, 1.3)), ("back34", (-1.6, -1.9, 2.3), (0, 0, 2.05))):
        cam.location = Vector(eye)
        dirv = Vector(tgt) - Vector(eye)
        cam.rotation_euler = dirv.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(PREVIEW, "armor_" + nm + ".png")
        bpy.ops.render.render(write_still=True)
print("DONE", len(PARTS))
