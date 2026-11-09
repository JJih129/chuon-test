# Blender headless: 화면 갈라짐(유리 파편) 메시 — 보로노이 균열 평면 (반복 11)
# 실행: "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Tools/ArtGen/screen_shatter_blender.py
# 출력: Assets/Art/ChuonPolish/VFX/Resources/VFX/ScreenShatter_Shards.fbx
#
# 16:9 화면을 보로노이 셀 ~64개로 나눈다 (중앙 충격점 근처가 더 잘게). 각 셀은 중심점 팬 삼각형으로 분할하고
# 정점을 공유하지 않는다 (파편마다 독립적으로 날아가야 하므로).
# UV 채널 (Unity uv / uv2 / uv3 로 들어감):
#   UVMap      = 화면 uv (0..1) — 원래 위치이자 캡처 텍스처 좌표
#   Centroid   = 파편 중심의 화면 uv (모든 정점 동일)
#   EdgeSeed   = (x: 가장자리 0(중심)~1(외곽선), y: 파편 시드 0..1)
# 셰이더(Hidden/ChuOn/ScreenShatter)가 이 값만으로 균열선·비행을 계산하므로 정점 위치는 uv 와 같은 평면에 둔다.
import bpy, bmesh, math, os, random

OUT = r"C:\Users\ikh33\source\chuon-test\Assets\Art\ChuonPolish\VFX\Resources\VFX"
os.makedirs(OUT, exist_ok=True)

ASPECT = 16.0 / 9.0
CELLS = 64
random.seed(1107)

# ---------------- 시드 점: 중앙 밀집 + 최소 간격 (푸아송 비슷하게) ----------------
pts = []
tries = 0
while len(pts) < CELLS and tries < 20000:
    tries += 1
    # 반경 분포를 중앙 쪽으로 치우침
    r = random.random() ** 1.35
    a = random.random() * math.tau
    x = math.cos(a) * r * ASPECT * 1.05
    y = math.sin(a) * r * 1.05
    if abs(x) > ASPECT or abs(y) > 1.0:
        continue
    d_center = math.hypot(x / ASPECT, y)
    min_gap = 0.10 + 0.20 * d_center
    if all(math.hypot(x - px, y - py) > min_gap for px, py in pts):
        pts.append((x, y))

# 테두리 근처 큰 셀이 화면 밖으로 너무 길어지지 않게 가장자리 점 몇 개 추가
for gx in (-0.75, -0.25, 0.25, 0.75):
    for gy in (-1.0, 1.0):
        pts.append((gx * ASPECT + random.uniform(-0.1, 0.1), gy * random.uniform(0.86, 0.96)))
for gy in (-0.5, 0.5):
    for gx in (-1.0, 1.0):
        pts.append((gx * ASPECT * random.uniform(0.88, 0.96), gy + random.uniform(-0.1, 0.1)))

# ---------------- 반평면 클리핑으로 보로노이 셀 ----------------
def clip(poly, px, py, nx, ny, c):
    # keep points where nx*x + ny*y <= c
    out = []
    n = len(poly)
    for i in range(n):
        ax, ay = poly[i]
        bx, by = poly[(i + 1) % n]
        da = nx * ax + ny * ay - c
        db = nx * bx + ny * by - c
        if da <= 0:
            out.append((ax, ay))
        if (da < 0) != (db < 0) and abs(da - db) > 1e-12:
            t = da / (da - db)
            out.append((ax + (bx - ax) * t, ay + (by - ay) * t))
    return out

rect = [(-ASPECT, -1.0), (ASPECT, -1.0), (ASPECT, 1.0), (-ASPECT, 1.0)]
cells = []
for i, (sx, sy) in enumerate(pts):
    poly = list(rect)
    for j, (ox, oy) in enumerate(pts):
        if i == j:
            continue
        nx, ny = ox - sx, oy - sy
        c = (ox * ox + oy * oy - sx * sx - sy * sy) * 0.5
        poly = clip(poly, sx, sy, nx, ny, c)
        if len(poly) < 3:
            break
    if len(poly) >= 3:
        cells.append(poly)

# ---------------- 메시 ----------------
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
mesh = bpy.data.meshes.new("ScreenShatter_Shards")
obj = bpy.data.objects.new("ScreenShatter_Shards", mesh)
bpy.context.scene.collection.objects.link(obj)

bm = bmesh.new()
uv_screen = bm.loops.layers.uv.new("UVMap")
uv_centroid = bm.loops.layers.uv.new("Centroid")
uv_edge = bm.loops.layers.uv.new("EdgeSeed")

def to_uv(x, y):
    return ((x / ASPECT) * 0.5 + 0.5, y * 0.5 + 0.5)

tri_count = 0
for ci, poly in enumerate(cells):
    # 면적 가중 중심
    area = 0.0
    cx = cy = 0.0
    n = len(poly)
    for k in range(n):
        x0, y0 = poly[k]
        x1, y1 = poly[(k + 1) % n]
        cr = x0 * y1 - x1 * y0
        area += cr
        cx += (x0 + x1) * cr
        cy += (y0 + y1) * cr
    area *= 0.5
    if abs(area) < 1e-6:
        continue
    cx /= (6.0 * area)
    cy /= (6.0 * area)
    if area < 0:
        poly = poly[::-1]
    seed = random.random()
    cu = to_uv(cx, cy)
    # 평면은 Blender XZ (Unity XY) — 위치는 화면 uv 를 -1..1 로
    def vpos(x, y):
        u, v = to_uv(x, y)
        return (u * 2.0 - 1.0, 0.0, v * 2.0 - 1.0)
    center_v = bm.verts.new(vpos(cx, cy))
    ring = [bm.verts.new(vpos(x, y)) for (x, y) in poly]
    for k in range(len(ring)):
        a = ring[k]
        b = ring[(k + 1) % len(ring)]
        try:
            f = bm.faces.new((center_v, a, b))
        except ValueError:
            continue
        tri_count += 1
        for loop in f.loops:
            co = loop.vert.co
            u = (co.x + 1.0) * 0.5
            v = (co.z + 1.0) * 0.5
            loop[uv_screen].uv = (u, v)
            loop[uv_centroid].uv = cu
            loop[uv_edge].uv = (0.0 if loop.vert == center_v else 1.0, seed)

bm.normal_update()
bm.to_mesh(mesh)
bm.free()
mesh.update()

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
fbx = os.path.join(OUT, "ScreenShatter_Shards.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                         mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP')
print("EXPORTED", fbx, "cells", len(cells), "tris", tri_count, "verts", len(mesh.vertices))
