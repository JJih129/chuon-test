# Blender headless: 궁극기 '위상단절검' EGO 칼날 정령 실루엣 메시 (Ult_EgoSpirit.fbx)
# 실행: blender.exe -b --factory-startup -P Tools/ArtGen/ult_spirit_blender.py
# 각진 세라믹 판갑 + 도포 자락 + 가면 볏 + 후광 고리 + 두 손으로 세워 든 장도. 발 기준 원점, 높이 ~1.08 (유니티에서 스케일).
# Blender 좌표: Z 위, 정면 = -Y. 평면 셰이딩(면마다 프레넬이 끊겨 도자기/결정 느낌).
import bpy, bmesh, math, os
from mathutils import Matrix, Vector

OUT = r"C:\Users\ikh33\source\chuon-test\Assets\Art\ChuonPolish\VFX\Ultimate\Resources\UltNightbringer"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
mesh = bpy.data.meshes.new("Ult_EgoSpirit")
obj = bpy.data.objects.new("Ult_EgoSpirit", mesh)
bpy.context.scene.collection.objects.link(obj)
bm = bmesh.new()


def xform(verts, m):
    bmesh.ops.transform(bm, matrix=m, verts=verts)


def box(center, size, rot=Matrix.Identity(4), top_scale=(1.0, 1.0)):
    r = bmesh.ops.create_cube(bm, size=1.0)
    vs = r["verts"]
    for v in vs:
        if v.co.z > 0:
            v.co.x *= top_scale[0]
            v.co.y *= top_scale[1]
    xform(vs, Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
    xform(vs, rot)
    xform(vs, Matrix.Translation(center))
    return vs


def cone(z0, z1, r0, r1, segs, offset=(0.0, 0.0), rot_z=0.0):
    depth = z1 - z0
    m = Matrix.Translation((offset[0], offset[1], z0 + depth * 0.5)) @ Matrix.Rotation(rot_z, 4, 'Z')
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r0, radius2=r1, depth=depth, matrix=m)


def limb(a, b, radius, segs=5):
    a = Vector(a); b = Vector(b)
    d = b - a
    length = d.length
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    m = Matrix.Translation((a + b) * 0.5) @ q.to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=radius, radius2=radius * 0.8, depth=length, matrix=m)


def gem(center, radius, scale=(1.0, 1.0, 1.0)):
    r = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=radius)
    xform(r["verts"], Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0)))
    xform(r["verts"], Matrix.Translation(center))


# ---- 도포 자락 (발끝이 안개로 녹는다) ----
cone(0.0, 0.47, 0.21, 0.085, 7)
cone(0.04, 0.40, 0.25, 0.10, 5, offset=(0.0, 0.03), rot_z=math.radians(18))   # 바깥 자락 (각지게 어긋남)
box((0.0, 0.11, 0.24), (0.16, 0.012, 0.42), Matrix.Rotation(math.radians(-14), 4, 'X'), top_scale=(0.6, 1.0))  # 뒤 꼬리

# ---- 몸통 / 판갑 ----
box((0.0, 0.0, 0.565), (0.17, 0.11, 0.21), top_scale=(1.45, 1.1))
box((0.0, -0.045, 0.60), (0.12, 0.03, 0.10), top_scale=(1.3, 1.0))          # 가슴판
for s in (-1, 1):
    rot = Matrix.Rotation(math.radians(-22 * s), 4, 'Y')
    box((0.165 * s, 0.0, 0.672), (0.12, 0.14, 0.045), rot, top_scale=(0.8, 0.9))   # 견갑
    box((0.19 * s, 0.0, 0.632), (0.07, 0.12, 0.035), rot, top_scale=(0.8, 0.9))    # 아래 겹 견갑

# ---- 머리 / 가면 볏 / 후광 ----
limb((0.0, 0.0, 0.67), (0.0, 0.0, 0.715), 0.028, 5)                       # 목
gem((0.0, -0.005, 0.765), 0.052, (0.85, 0.95, 1.45))                          # 머리 (길쭉한 결정)
box((0.0, 0.035, 0.83), (0.012, 0.11, 0.075), Matrix.Rotation(math.radians(28), 4, 'X'), top_scale=(0.4, 0.5))  # 볏
box((0.0, -0.05, 0.765), (0.07, 0.008, 0.016))                                # 가면 눈 슬릿 판

RING_R, TUBE_R, SEG, TSEG = 0.15, 0.0065, 36, 4
ring_center = Vector((0.0, 0.075, 0.79))
rows = []
for i in range(SEG):
    a = 2 * math.pi * i / SEG
    c = Vector((math.cos(a), 0.0, math.sin(a)))
    row = []
    for j in range(TSEG):
        b = 2 * math.pi * j / TSEG
        p = ring_center + c * (RING_R + TUBE_R * math.cos(b)) + Vector((0.0, TUBE_R * math.sin(b), 0.0))
        row.append(bm.verts.new(p))
    rows.append(row)
for i in range(SEG):
    for j in range(TSEG):
        bm.faces.new((rows[i][j], rows[(i + 1) % SEG][j], rows[(i + 1) % SEG][(j + 1) % TSEG], rows[i][(j + 1) % TSEG]))

# ---- 팔: 두 손으로 장도를 가슴 앞에 세워 든다 ----
hands = Vector((0.0, -0.135, 0.545))
for s in (-1, 1):
    shoulder = Vector((0.15 * s, 0.0, 0.64))
    elbow = Vector((0.13 * s, -0.085, 0.53))
    limb(shoulder, elbow, 0.03, 5)
    limb(elbow, hands + Vector((0.018 * s, 0.0, 0.0)), 0.026, 5)
gem(hands, 0.03, (1.3, 1.0, 1.0))

# ---- 장도: 손잡이 → 코등이 → 휜 칼날 (칼끝 위) ----
limb((0.0, -0.135, 0.47), (0.0, -0.137, 0.585), 0.011, 6)                  # 손잡이
bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=8, radius1=0.032, radius2=0.032, depth=0.007,
                      matrix=Matrix.Translation((0.0, -0.137, 0.59)))           # 코등이
BL_Z0, BL_Z1, BL_N = 0.596, 1.08, 7
prev = None
for k in range(BL_N + 1):
    t = k / BL_N
    z = BL_Z0 + (BL_Z1 - BL_Z0) * t
    y = -0.138 + 0.035 * t * t                       # 소리(휨): 칼끝이 뒤로 살짝
    w = 0.013 * (1.0 - 0.35 * t)                     # 칼날 폭 (x)
    th = 0.0035                                      # 두께 (y)
    if k == BL_N:
        tip = bm.verts.new((0.0, y + 0.006, z + 0.03))
        for a in range(4):
            bm.faces.new((prev[a], prev[(a + 1) % 4], tip))
        break
    ring = [bm.verts.new((-w, y - th, z)), bm.verts.new((w, y - th * 0.3, z)),
            bm.verts.new((w * 0.7, y + th, z)), bm.verts.new((-w, y + th, z))]
    if prev is None:
        bm.faces.new(list(reversed(ring)))
    else:
        for a in range(4):
            bm.faces.new((prev[a], prev[(a + 1) % 4], ring[(a + 1) % 4], ring[a]))
    prev = ring

bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(mesh)
bm.free()
for p in mesh.polygons:
    p.use_smooth = False
mesh.update()

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
fbx = os.path.join(OUT, "Ult_EgoSpirit.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                         mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP')
print("EXPORTED", fbx, len(mesh.vertices), len(mesh.polygons))
