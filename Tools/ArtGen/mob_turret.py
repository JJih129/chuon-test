"""TSUKASA sentry turret (Blender 5.x headless).

Run:
  blender -b --factory-startup --python mob_turret.py -- [out_dir] [preview_dir]
Writes <out_dir>/Meshes/SM_Mob_SentryTurret.fbx with the hierarchy the game rig expects:
  (flat; the prefab builder rebuilds the rig hierarchy Yaw -> Head -> Muzzle/PatternPivot)
  Turret_MountFloor   floor plate + collar               (prefab keeps one mount)
  Turret_MountWall    wall plate + cantilever arm (wall face at +0.33 m behind = Unity -Z)
  Turret_Yaw          turntable + yoke, origin = yaw axis (0, 0, 0.37)
  Turret_Head         sensor/gun housing, origin = pitch axis (0, 0, 0.55)
  Muzzle              between the barrel tips (Unity +Z = fire direction)
  PatternPivot        red sensor eye (cue flash anchor)
Materials: Mob_Metal / Mob_Ceramic / Mob_Accent / Mob_Rubber / Mob_Glow / Mob_Visor (trim sheet, mob_common).
"""
import os, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mob_common import *  # noqa

OUT_DIR, PREVIEW = parse_args()
reset()

YAW_Z = 0.37
PITCH_Z = 0.55


def octagon(name, r, h, z0, mat):
    o = cyl(name, r, h, (0, 0, z0 + h / 2), mat, verts=8)
    o.rotation_euler = (0, 0, math.pi / 8)
    return o


# ---------------------------------------------------------------- floor mount
plate = octagon("plate", 0.32, 0.05, 0.015, "Metal"); hard_bevel(plate, 0.006, 2, 30)
gasket = octagon("gasket", 0.335, 0.015, 0.0, "Rubber"); hard_bevel(gasket, 0.003, 1, 30)
plate_top = octagon("plateTop", 0.24, 0.02, 0.065, "Ceramic"); hard_bevel(plate_top, 0.004, 2, 30)
collar = cyl("collar", 0.11, 0.29, (0, 0, 0.08 + 0.145), "Metal", 24); hard_bevel(collar, 0.004, 2, 30)
ring = cyl("ring", 0.118, 0.03, (0, 0, 0.30), "Accent", 24); hard_bevel(ring, 0.003, 1, 30)
bolts = []
for i in range(8):
    a = i / 8 * math.tau + math.pi / 8
    b = cyl("bolt%d" % i, 0.014, 0.014, (math.cos(a) * 0.275, math.sin(a) * 0.275, 0.072), "Metal", 6)
    bolts.append(b)
floor_mount = join([plate, gasket, plate_top, collar, ring] + bolts, "Turret_MountFloor")

# ---------------------------------------------------------------- wall mount (wall face at y=+0.33)
wplate = box("wplate", (0.38, 0.05, 0.46), (0, 0.305, 0.27), "Metal", chamfer=0.03); hard_bevel(wplate, 0.005, 2, 30)
wgask = box("wgask", (0.40, 0.012, 0.48), (0, 0.334, 0.27), "Rubber"); hard_bevel(wgask, 0.003, 1, 30)
wcover = box("wcover", (0.28, 0.02, 0.32), (0, 0.272, 0.27), "Ceramic", chamfer=0.025); hard_bevel(wcover, 0.004, 2, 30)
arm = box("arm", (0.13, 0.30, 0.09), (0, 0.13, YAW_Z - 0.045), "Metal", chamfer=0.015); hard_bevel(arm, 0.004, 2, 30)
armtop = box("armtop", (0.10, 0.22, 0.012), (0, 0.15, YAW_Z - 0.004), "Ceramic"); hard_bevel(armtop, 0.002, 1, 30)
strut = box("strut", (0.06, 0.30, 0.05), (0, 0.17, 0.2), "Metal", rot=(math.radians(-38), 0, 0)); hard_bevel(strut, 0.004, 2, 30)
wstripe = box("wstripe", (0.30, 0.006, 0.035), (0, 0.26, 0.10), "Accent"); hard_bevel(wstripe, 0.0015, 1, 30)
wcollar = cyl("wcollar", 0.1, 0.06, (0, 0, YAW_Z - 0.03), "Metal", 24); hard_bevel(wcollar, 0.004, 2, 30)
wall_mount = join([wplate, wgask, wcover, arm, armtop, strut, wstripe, wcollar], "Turret_MountWall")

# ---------------------------------------------------------------- yaw (turntable + yoke)
disc = cyl("disc", 0.15, 0.05, (0, 0, YAW_Z + 0.025), "Metal", 32); hard_bevel(disc, 0.004, 2, 30)
disc_r = cyl("discR", 0.155, 0.012, (0, 0, YAW_Z + 0.006), "Rubber", 32); hard_bevel(disc_r, 0.002, 1, 30)
yoke = []
for sx in (-1, 1):
    a = box("yarm", (0.036, 0.15, 0.24), (sx * 0.152, 0.0, YAW_Z + 0.16), "Ceramic", chamfer=0.02); hard_bevel(a, 0.004, 2, 30)
    hub = cyl("hub", 0.045, 0.05, (sx * 0.158, 0, PITCH_Z), "Metal", 20, axis='X'); hard_bevel(hub, 0.003, 1, 30)
    stripe = box("ystripe", (0.006, 0.11, 0.03), (sx * 0.172, 0.0, YAW_Z + 0.09), "Accent"); hard_bevel(stripe, 0.0015, 1, 30)
    yoke += [a, hub, stripe]
base_block = box("yblock", (0.30, 0.13, 0.05), (0, 0, YAW_Z + 0.07), "Metal", chamfer=0.02); hard_bevel(base_block, 0.004, 2, 30)
yaw = join([disc, disc_r, base_block] + yoke, "Turret_Yaw")
set_origin(yaw, (0, 0, YAW_Z))

# ---------------------------------------------------------------- head (pitch)
# side profile (y forward = -Y, z up) around the pitch axis
prof = [(-0.25, -0.035), (-0.21, 0.075), (0.04, 0.105), (0.17, 0.075), (0.20, -0.045), (0.11, -0.09), (-0.19, -0.085)]
shell = prism("shell", [(y, z + PITCH_Z) for y, z in prof], 0.24, (0, 0, 0), "Ceramic")
shell.rotation_euler = (0, 0, math.pi / 2)
apply_transform(shell)
set_mat(shell, "Metal", lambda c, n: n.z < -0.35 or c.y > 0.15)
hard_bevel(shell, 0.006, 2, 30)
cheeks = []
for sx in (-1, 1):
    c = box("cheek", (0.012, 0.30, 0.1), (sx * 0.126, 0.0, PITCH_Z - 0.01), "Metal", chamfer=0.02); hard_bevel(c, 0.003, 1, 30)
    st = box("hstripe", (0.006, 0.20, 0.022), (sx * 0.134, -0.02, PITCH_Z + 0.035), "Accent"); hard_bevel(st, 0.0015, 1, 30)
    cheeks += [c, st]
glow_line = box("glowline", (0.012, 0.26, 0.006), (0, -0.03, PITCH_Z + 0.108), "Glow"); hard_bevel(glow_line, 0.0015, 1, 30)
lens_h = cyl("lensHousing", 0.058, 0.035, (0, -0.245, PITCH_Z + 0.03), "Metal", 28, axis='Y'); hard_bevel(lens_h, 0.003, 1, 30)
lens_ring = cyl("lensRing", 0.05, 0.012, (0, -0.265, PITCH_Z + 0.03), "Glow", 28, axis='Y')
lens = cyl("lens", 0.038, 0.01, (0, -0.272, PITCH_Z + 0.03), "Visor", 28, axis='Y')
shroud = box("shroud", (0.20, 0.13, 0.065), (0, -0.25, PITCH_Z - 0.055), "Metal", chamfer=0.02); hard_bevel(shroud, 0.004, 2, 30)
barrels = []
for sx in (-1, 1):
    b = cyl("barrel", 0.019, 0.27, (sx * 0.062, -0.43, PITCH_Z - 0.055), "Metal", 16, axis='Y'); hard_bevel(b, 0.002, 1, 30)
    brake = cyl("brake", 0.027, 0.05, (sx * 0.062, -0.545, PITCH_Z - 0.055), "Metal", 12, axis='Y'); hard_bevel(brake, 0.002, 1, 30)
    bore = cyl("bore", 0.012, 0.004, (sx * 0.062, -0.571, PITCH_Z - 0.055), "Visor", 12, axis='Y')
    barrels += [b, brake, bore]
fins = []
for i in range(6):
    x = -0.075 + i * 0.03
    f = box("fin", (0.005, 0.07, 0.11), (x, 0.215, PITCH_Z + 0.0), "Metal"); hard_bevel(f, 0.0012, 1, 30)
    fins.append(f)
head = join([shell, glow_line, lens_h, lens_ring, lens, shroud] + cheeks + barrels + fins, "Turret_Head")
set_origin(head, (0, 0, PITCH_Z))

for ob in (floor_mount, wall_mount, yaw, head):
    trim_uv(ob)

# Flat export (no parenting): nested hierarchies + bake_space_transform import with bogus child transforms.
# MobPrefabBuilder re-parents Head -> Yaw and Muzzle/PatternPivot -> Head keeping world poses.
muzzle = empty("Muzzle", (0, -0.58, PITCH_Z - 0.055))
pp = empty("PatternPivot", (0, -0.28, PITCH_Z + 0.03))

export_fbx([floor_mount, wall_mount, yaw, head, muzzle, pp], os.path.join(OUT_DIR, "Meshes", "SM_Mob_SentryTurret.fbx"))

wall_mount.hide_render = True
preview(OUT_DIR, PREVIEW, "turret_", [
    ("tq", (0.9, -1.2, 0.95), (0, 0, 0.4), 50),
    ("side", (1.4, 0.0, 0.6), (0, -0.05, 0.42), 50),
    ("back", (-0.8, 1.1, 1.0), (0, 0, 0.42), 50),
])
