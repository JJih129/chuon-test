"""TSUKASA Security Android armor kit + HF blade (Blender 5.x headless).

Run:
  blender -b --factory-startup --python mob_android.py -- [out_dir] [preview_dir]
Writes <out_dir>/Meshes/SM_Mob_AndroidKit.fbx -- rigid attachments, fitted to the humanoid body by
Assets/Editor/MobPrefabBuilder.cs (ray-cast against the baked body mesh, then parented to the bone):
  Mob_Helmet / Mob_HelmetEnforcer  origin = head centre (shell inner radius ~0.112 m), face = -Y (Unity +Z)
  Mob_Chest                        origin = contact point on the sternum, plate grows towards -Y (front)
  Mob_Back                         origin = contact point between the shoulder blades, grows towards +Y
  Mob_PauldronL / Mob_PauldronR    origin = contact point on top of the shoulder (outward = -X / +X)
  Mob_HFBlade                      origin = grip centre, blade along +Z (Unity +Y), cutting edge -Y
Visual language = Type-D: graphite metal, off-white ceramic shells, TSUKASA-red accent + red light lines (Glow),
dark visor glass with a red eye line.
"""
import os, sys, math
import bmesh
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mob_common import *  # noqa

OUT_DIR, PREVIEW = parse_args()
reset()


def keep_faces(ob, pred):
    """Delete faces whose (object-space) centre fails pred."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    kill = [f for f in bm.faces if not pred(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(ob.data); bm.free()


def bend_axes(ob, rz=None, rx=None):
    """Wrap a front plate around the torso: rz bends around the vertical axis, rx around the horizontal."""
    apply_transform(ob)
    mw = ob.matrix_world; inv = mw.inverted()
    for v in ob.data.vertices:
        w = mw @ v.co
        if rz:
            w.y += rz - math.sqrt(max(0.0, rz * rz - w.x * w.x))
        if rx:
            w.y += rx - math.sqrt(max(0.0, rx * rx - w.z * w.z))
        v.co = inv @ w


def sliced_prism(name, outline, y0, y1, mat, step=0.025):
    o = prism(name, outline, y1 - y0, (0, (y0 + y1) / 2, 0), mat)
    apply_transform(o)
    bm = bmesh.new(); bm.from_mesh(o.data)
    xs = [v.co.x for v in bm.verts]; zs = [v.co.z for v in bm.verts]
    for axis, lo, hi in ((0, min(xs), max(xs)), (2, min(zs), max(zs))):
        n = max(1, int((hi - lo) / step))
        for i in range(1, n):
            c = lo + (hi - lo) * i / n
            co = [0, 0, 0]; co[axis] = c
            no = [0, 0, 0]; no[axis] = 1
            bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=co, plane_no=no)
    bm.to_mesh(o.data); bm.free()
    return o


# ---------------------------------------------------------------- helmets
def helmet(name, r=(0.118, 0.138, 0.142), cage=False):
    shell = ellipsoid(name + "_shell", r, (0, 0, 0.0), "Ceramic", 32, 18)
    apply_transform(shell)
    set_mat(shell, "Metal", lambda c, n: c.z < -0.045 or (c.z < 0.0 and c.y > 0.05))
    hard_bevel(shell, 0.002, 1, 25)

    visor = ellipsoid(name + "_visor", (r[0] * 1.035, r[1] * 1.035, r[2] * 1.035), (0, 0, 0), "Visor", 32, 18)
    keep_faces(visor, lambda c, n: -0.035 < c.z < 0.035 and c.y < -0.03)
    solidify(visor, 0.006, 1.0)
    eye = ellipsoid(name + "_eye", (r[0] * 1.06, r[1] * 1.06, r[2] * 1.06), (0, 0, 0), "Glow", 32, 36)
    keep_faces(eye, lambda c, n: -0.009 < c.z < 0.009 and c.y < -0.06)
    solidify(eye, 0.003, 1.0)

    crest = box(name + "_crest", (0.014, 0.17, 0.03), (0, 0.0, r[2] - 0.004), "Ceramic", chamfer=0.006)
    crest_s = box(name + "_crestS", (0.016, 0.09, 0.008), (0, -0.03, r[2] + 0.008), "Accent")
    ears = []
    for sx in (-1, 1):
        e = cyl(name + "_ear", 0.036, 0.03, (sx * (r[0] + 0.006), 0.01, -0.012), "Metal", 20, axis='X')
        er = cyl(name + "_earL", 0.022, 0.034, (sx * (r[0] + 0.008), 0.01, -0.012), "Glow", 16, axis='X')
        ears += [e, er]
    neck = cyl(name + "_neck", 0.062, 0.06, (0, 0.012, -r[2] + 0.005), "Rubber", 20)
    parts = [crest, crest_s, neck] + ears
    if cage:
        for i in range(5):
            x = -0.05 + i * 0.025
            bar = box(name + "_bar", (0.007, 0.012, 0.085), (x, -r[1] - 0.006, -0.01), "Metal")
            parts.append(bar)
        brow = box(name + "_brow", (0.17, 0.03, 0.022), (0, -r[1] + 0.02, 0.05), "Metal", chamfer=0.01)
        parts.append(brow)
    for p in parts:
        hard_bevel(p, 0.0015, 1, 35)
    return join([shell, visor, eye] + parts, name)


helm = helmet("Mob_Helmet")
helm_e = helmet("Mob_HelmetEnforcer", (0.126, 0.146, 0.15), cage=True)

# ---------------------------------------------------------------- chest
CHEST = [(-0.17, -0.12), (-0.19, 0.03), (-0.15, 0.15), (-0.06, 0.185), (0.06, 0.185), (0.15, 0.15), (0.19, 0.03), (0.17, -0.12), (0.07, -0.165), (-0.07, -0.165)]
chest = sliced_prism("chest", CHEST, -0.03, 0.0, "Ceramic")
set_mat(chest, "Metal", lambda c, n: abs(c.x) > 0.12 or c.z < -0.12)
ridge = sliced_prism("ridge", [(-0.022, -0.14), (0.022, -0.14), (0.03, 0.16), (-0.03, 0.16)], -0.04, -0.025, "Metal", 0.03)
line = []
for sx in (-1, 1):
    g = box("chestLight", (0.09, 0.006, 0.006), (sx * 0.085, -0.034, 0.06), "Glow")
    a = box("chestMark", (0.05, 0.006, 0.018), (sx * 0.1, -0.034, 0.12), "Accent" if sx > 0 else "Ceramic")
    line += [g, a]
collar = box("collar", (0.20, 0.05, 0.03), (0, 0.01, 0.185), "Metal", chamfer=0.012)
chest_parts = [chest, ridge, collar] + line
for p in chest_parts:
    bend_axes(p, rz=0.27, rx=0.6)
hard_bevel(chest, 0.003, 2, 30)
for p in chest_parts[1:]:
    hard_bevel(p, 0.0015, 1, 35)
chest = join(chest_parts, "Mob_Chest")

# ---------------------------------------------------------------- back unit
pack = box("pack", (0.24, 0.085, 0.26), (0, 0.045, -0.02), "Metal", chamfer=0.03); hard_bevel(pack, 0.004, 2, 30)
cover = box("cover", (0.17, 0.02, 0.19), (0, 0.092, -0.02), "Ceramic", chamfer=0.02); hard_bevel(cover, 0.003, 2, 30)
spine = box("spineLight", (0.008, 0.006, 0.16), (0, 0.103, -0.02), "Glow"); hard_bevel(spine, 0.001, 1, 35)
vents = []
for sx in (-1, 1):
    v_ = box("vent", (0.03, 0.06, 0.12), (sx * 0.125, 0.045, -0.04), "Rubber", chamfer=0.008); hard_bevel(v_, 0.002, 1, 30)
    st = box("packStripe", (0.006, 0.07, 0.03), (sx * 0.121, 0.045, 0.08), "Accent"); hard_bevel(st, 0.0012, 1, 35)
    vents += [v_, st]
back = join([pack, cover, spine] + vents, "Mob_Back")

# ---------------------------------------------------------------- pauldrons
def pauldron(name, side):
    sh = ellipsoid(name + "_s", (0.088, 0.105, 0.07), (0, 0, 0), "Ceramic", 24, 14)
    keep_faces(sh, lambda c, n: c.z > -0.012)
    solidify(sh, 0.008, -1.0)
    sh.location = (side * 0.035, 0, 0.0)
    apply_transform(sh)
    set_mat(sh, "Metal", lambda c, n: c.z < 0.012)
    hard_bevel(sh, 0.002, 1, 30)
    st = box(name + "_st", (0.012, 0.12, 0.012), (side * 0.06, 0.0, 0.052), "Accent", rot=(0, side * math.radians(35), 0)); hard_bevel(st, 0.0012, 1, 35)
    gl = box(name + "_gl", (0.006, 0.08, 0.006), (side * 0.105, 0.0, 0.03), "Glow", rot=(0, side * math.radians(60), 0)); hard_bevel(gl, 0.001, 1, 35)
    return join([sh, st, gl], name)


pl = pauldron("Mob_PauldronL", -1)
pr = pauldron("Mob_PauldronR", 1)

# ---------------------------------------------------------------- HF blade (along +Z, edge -Y)
grip = cyl("bgrip", 0.016, 0.22, (0, 0, 0), "Rubber", 12)
pommel = cyl("bpommel", 0.02, 0.03, (0, 0, -0.125), "Metal", 12)
guard = box("bguard", (0.034, 0.075, 0.024), (0, 0.0, 0.122), "Metal", chamfer=0.008)
guard_a = box("bguardA", (0.036, 0.03, 0.008), (0, -0.01, 0.122), "Accent")
prof = [(-0.018, 0.134), (0.016, 0.134), (0.016, 0.80), (0.002, 0.862), (-0.018, 0.80)]
blade = prism("blade", prof, 0.007, (0, 0, 0), "Metal")
blade.rotation_euler = (0, 0, math.pi / 2)
apply_transform(blade)
edge = box("bedge", (0.0085, 0.003, 0.64), (0, -0.0185, 0.46), "Glow")
spine_c = box("bspine", (0.0095, 0.012, 0.2), (0, 0.011, 0.25), "Ceramic")
mark = box("bmark", (0.0098, 0.008, 0.03), (0, 0.011, 0.31), "Accent")
bparts = [grip, pommel, guard, guard_a, blade, edge, spine_c, mark]
for p in bparts:
    hard_bevel(p, 0.001, 1, 35)
blade_ob = join(bparts, "Mob_HFBlade")

for ob in (helm, helm_e, chest, back, pl, pr, blade_ob):
    trim_uv(ob)

objs = [helm, helm_e, chest, back, pl, pr, blade_ob]
export_fbx(objs, os.path.join(OUT_DIR, "Meshes", "SM_Mob_AndroidKit.fbx"))

# preview lineup (positions only for the render; exported origins are unchanged above)
lay = {helm: (-0.45, 0, 0.45), helm_e: (-0.15, 0, 0.45), chest: (0.2, 0, 0.45), back: (0.5, 0, 0.45),
       pl: (-0.3, 0, 0.12), pr: (0.0, 0, 0.12), blade_ob: (0.35, 0, -0.35)}
for ob, p in lay.items():
    ob.location = p
preview(OUT_DIR, PREVIEW, "android_", [
    ("front", (0.0, -2.2, 0.55), (0.02, 0, 0.25), 45),
    ("tq", (1.3, -1.7, 0.8), (0.02, 0, 0.25), 45),
])
