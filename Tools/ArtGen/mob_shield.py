"""TSUKASA Shield Enforcer kit: riot shield + shock baton (Blender 5.x headless).

Run:
  blender -b --factory-startup --python mob_shield.py -- [out_dir] [preview_dir]
Writes <out_dir>/Meshes/SM_Mob_EnforcerKit.fbx:
  Mob_RiotShield  origin = forearm strap/handle midpoint (back side), face = -Y (Unity +Z), long axis = Z (Unity +Y)
  Mob_ShockBaton  origin = grip centre, shaft along +Z (Unity +Y)
Shield: curved off-white ceramic face in a graphite frame, dark glass view slit, TSUKASA red chevron, thin red
edge lights (Glow, driven at runtime). Baton: rubber grip, graphite hex shaft, ceramic emitter with red rings.
"""
import os, sys, math
import bmesh
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mob_common import *  # noqa

OUT_DIR, PREVIEW = parse_args()
reset()

R_BEND = 1.35  # face curvature radius (m)
OUTLINE = [(-0.31, -0.50), (-0.25, -0.56), (0.25, -0.56), (0.31, -0.50), (0.31, 0.46), (0.22, 0.56), (-0.22, 0.56), (-0.31, 0.46)]


def grow(outline, d):
    return [(x + (d if x > 0 else -d), z + (d if z > 0 else -d)) for x, z in outline]


def slab(name, outline, y0, y1, mat, cuts=14):
    """Front-view outline extruded between y0 (front) and y1, sliced along X so it can bend smoothly."""
    o = prism(name, outline, y1 - y0, (0, (y0 + y1) / 2, 0), mat)
    apply_transform(o)
    bm = bmesh.new(); bm.from_mesh(o.data)
    xs = [v.co.x for v in bm.verts]
    lo, hi = min(xs), max(xs)
    for i in range(1, cuts):
        x = lo + (hi - lo) * i / cuts
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(x, 0, 0), plane_no=(1, 0, 0))
    bm.to_mesh(o.data); bm.free()
    return o


def slice_x(ob, step):
    apply_transform(ob)
    bm = bmesh.new(); bm.from_mesh(ob.data)
    mw = ob.matrix_world
    xs = [(mw @ v.co).x for v in bm.verts]
    lo, hi = min(xs), max(xs)
    n = int((hi - lo) / step)
    for i in range(1, n + 1):
        x = lo + (hi - lo) * i / (n + 1)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(x - mw.translation.x, 0, 0), plane_no=(1, 0, 0))
    bm.to_mesh(ob.data); bm.free()


def bend(ob):
    apply_transform(ob)
    mw = ob.matrix_world
    inv = mw.inverted()
    for v in ob.data.vertices:
        w = mw @ v.co
        w.y += R_BEND - math.sqrt(max(0.0, R_BEND * R_BEND - w.x * w.x))
        v.co = inv @ w


# ---------------------------------------------------------------- shield
frame = slab("frame", grow(OUTLINE, 0.022), -0.050, -0.012, "Metal")
face = slab("face", OUTLINE, -0.072, -0.048, "Ceramic")
slit_frame = box("slitFrame", (0.40, 0.012, 0.10), (0, -0.078, 0.33), "Metal", chamfer=0.012)
slit = box("slit", (0.34, 0.012, 0.055), (0, -0.084, 0.33), "Visor")
chev = []
for sx in (-1, 1):
    c = box("chevron", (0.30, 0.008, 0.05), (sx * 0.12, -0.075, -0.28), "Accent", rot=(0, sx * math.radians(28), 0))
    chev.append(c)
band = box("band", (0.52, 0.008, 0.022), (0, -0.075, -0.40), "Accent")
lights = []
for sx in (-1, 1):
    l = box("edgeLight", (0.008, 0.008, 0.70), (sx * 0.285, -0.074, 0.0), "Glow")
    lights.append(l)
cap = box("topLight", (0.22, 0.008, 0.008), (0, -0.074, 0.53), "Glow")
plates = [frame, face, slit_frame, slit, band, cap] + chev + lights
for p in plates[2:]:
    slice_x(p, 0.03)
for p in plates:
    bend(p)
hard_bevel(frame, 0.005, 2, 30)
hard_bevel(face, 0.004, 2, 30)
for p in plates[2:]:
    hard_bevel(p, 0.0015, 1, 35)

# back hardware (forearm strap low, grip handle high -> forearm along shield Z)
strap = box("strap", (0.16, 0.022, 0.06), (0, 0.035, -0.13), "Rubber", chamfer=0.008); hard_bevel(strap, 0.003, 1, 30)
strap_b = []
for sx in (-1, 1):
    b = box("strapBracket", (0.025, 0.05, 0.075), (sx * 0.09, 0.012, -0.13), "Metal", chamfer=0.006); hard_bevel(b, 0.002, 1, 30)
    strap_b.append(b)
handle = cyl("handle", 0.017, 0.15, (0, 0.04, 0.09), "Rubber", 14); hard_bevel(handle, 0.002, 1, 30)
hb = []
for sz in (-1, 1):
    b = box("handleBracket", (0.04, 0.055, 0.025), (0, 0.016, 0.09 + sz * 0.085), "Metal", chamfer=0.006); hard_bevel(b, 0.002, 1, 30)
    hb.append(b)
spine = box("backSpine", (0.06, 0.02, 0.9), (0, -0.002, 0.0), "Metal", chamfer=0.01); hard_bevel(spine, 0.003, 1, 30)
shield = join(plates + [strap, handle, spine] + strap_b + hb, "Mob_RiotShield")
set_origin(shield, (0, 0.04, -0.02))
trim_uv(shield)

# ---------------------------------------------------------------- shock baton (along +Z), placed beside for preview
X0 = 0.6
grip = cyl("grip", 0.017, 0.16, (X0, 0, 0), "Rubber", 16)
pommel = cyl("pommel", 0.021, 0.025, (X0, 0, -0.092), "Metal", 16)
collar = cyl("collar", 0.024, 0.03, (X0, 0, 0.095), "Metal", 16)
ring = cyl("ring", 0.025, 0.008, (X0, 0, 0.083), "Accent", 16)
shaft = cyl("shaft", 0.014, 0.38, (X0, 0, 0.30), "Metal", 6)
emitter = cyl("emitter", 0.019, 0.07, (X0, 0, 0.525), "Ceramic", 16)
g1 = cyl("g1", 0.0198, 0.006, (X0, 0, 0.505), "Glow", 16)
g2 = cyl("g2", 0.0198, 0.006, (X0, 0, 0.54), "Glow", 16)
tip = cyl("tip", 0.012, 0.025, (X0, 0, 0.572), "Metal", 16, r2=0.006)
parts = [grip, pommel, collar, ring, shaft, emitter, g1, g2, tip]
for p in parts:
    hard_bevel(p, 0.0015, 1, 35)
baton = join(parts, "Mob_ShockBaton")
set_origin(baton, (X0, 0, 0))
trim_uv(baton)

export_fbx([shield, baton], os.path.join(OUT_DIR, "Meshes", "SM_Mob_EnforcerKit.fbx"))

preview(OUT_DIR, PREVIEW, "shield_", [
    ("front34", (-0.9, -1.7, 0.5), (0.15, 0, 0.0), 45),
    ("back34", (1.0, 1.6, 0.4), (0.15, 0, 0.0), 45),
])
