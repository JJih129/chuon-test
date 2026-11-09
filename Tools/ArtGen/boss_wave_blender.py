# Blender headless: crescent sword-wave mesh + gradient textures for BossSwordWave
import bpy, bmesh, math, os, struct, zlib

OUT = r"C:\Users\ikh33\source\chuon-test\Assets\Art\ChuonPolish\VFX"
os.makedirs(OUT, exist_ok=True)

# ---------------- PNG helpers ----------------
def write_png(path, w, h, fn):
    raw = bytearray()
    for r in range(h):
        raw.append(0)
        v = 1.0 - r / (h - 1)  # unity uv: v=1 at top row
        for c in range(w):
            u = c / (w - 1)
            px = fn(u, v)
            raw.extend(bytes(max(0, min(255, int(round(x * 255)))) for x in px))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)

def sstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)

def lerp(a, b, t):
    return a + (b - a) * t

def wave_tex(u, v):
    tip = math.pow(max(0.0, math.sin(math.pi * u)), 0.55)
    body = v * v
    # 잔상 줄무늬 (뒤쪽으로 갈수록 강해짐)
    streak = 0.72 + 0.28 * (0.5 + 0.5 * math.sin(u * 61.0 + math.sin(u * 17.0) * 2.0)) * (0.5 + 0.5 * math.sin(u * 23.0 + 1.3))
    body *= lerp(1.0, streak, 1.0 - v)
    core = sstep(0.78, 0.96, v) * (1.0 - 0.45 * sstep(0.985, 1.0, v))
    a = min(1.0, body * 0.85 + core) * tip
    red = (1.0, 0.10, 0.04)
    org = (1.0, 0.42, 0.10)
    wht = (1.0, 0.93, 0.80)
    col = [lerp(red[i], org[i], body) for i in range(3)]
    col = [lerp(col[i], wht[i], core) for i in range(3)]
    return (col[0], col[1], col[2], a)

def dot_tex(u, v):
    r = min(1.0, math.hypot(u - 0.5, v - 0.5) * 2.0)
    a = math.pow(1.0 - r, 2.2)
    return (1.0, 1.0, 1.0, a)

def beam_tex(u, v):
    d = (v - 0.5)
    core = math.exp(-(d / 0.11) ** 2)
    glow = 0.45 * math.exp(-(d / 0.28) ** 2)
    ends = sstep(0.0, 0.06, u) * (1.0 - 0.6 * sstep(0.85, 1.0, u))
    a = min(1.0, core + glow) * ends
    w = 0.65 + 0.35 * core
    return (w, w, w, a)

write_png(os.path.join(OUT, "BossSwordWave_Gradient.png"), 256, 128, wave_tex)
write_png(os.path.join(OUT, "BossVfx_SoftDot.png"), 64, 64, dot_tex)
write_png(os.path.join(OUT, "BossVfx_WarningBeam.png"), 256, 32, beam_tex)

# ---------------- Mesh ----------------
# 유니티 기준 정규화 박스: X[-0.5,0.5], Y[-0.5,0.5], Z[0,1] (Z=진행방향)
bpy.ops.wm.read_factory_settings(use_empty=True)
mesh = bpy.data.meshes.new("BossSwordWave_Crescent")
obj = bpy.data.objects.new("BossSwordWave_Crescent", mesh)
bpy.context.scene.collection.objects.link(obj)
bm = bmesh.new()
uvl = bm.loops.layers.uv.new("UVMap")

A = math.radians(72.0)
R = 0.5 / math.sin(A)
ZC = 1.0 - R
SEG = 40
ROWS = 8
TMAX = 0.78

def to_blender(x, y, z):
    # unity(x,y,z) -> blender(x, -z, y)  (Blender 정면 = -Y)
    return (x, z, y)

def outer(t):
    th = lerp(-A, A, t)
    return R * math.sin(th), ZC + R * math.cos(th)

def thickness(t):
    s = math.cos((t - 0.5) * math.pi)  # 0 at tips, 1 center
    return TMAX * math.pow(max(0.0, s), 1.4) + 0.02

def add_grid(points, uvs, cols, rows):
    verts = [[bm.verts.new(to_blender(*points[c][r])) for r in range(rows + 1)] for c in range(cols + 1)]
    for c in range(cols):
        for r in range(rows):
            vs = (verts[c][r], verts[c + 1][r], verts[c + 1][r + 1], verts[c][r + 1])
            f = bm.faces.new(vs)
            idx = ((c, r), (c + 1, r), (c + 1, r + 1), (c, r + 1))
            for loop, (ci, ri) in zip(f.loops, idx):
                loop[uvl].uv = uvs[ci][ri]

# 1) 수평 초승달 (y=0): v=1 앞 날, v=0 뒤 잔상
pts, uvs = [], []
for c in range(SEG + 1):
    t = c / SEG
    x, z = outer(t)
    th = thickness(t)
    col_p, col_uv = [], []
    for r in range(ROWS + 1):
        k = r / ROWS  # 0 inner(back) .. 1 outer(front)
        zz = z - th * (1.0 - k) ** 1.15
        col_p.append((x * lerp(0.92, 1.0, k), 0.0, max(0.0, zz)))
        col_uv.append((t, k))
    pts.append(col_p); uvs.append(col_uv)
add_grid(pts, uvs, SEG, ROWS)

# 2) 수직 커튼 (앞 날을 따라 위아래로 얇게): 날의 단면감
VR = 6
pts, uvs = [], []
for c in range(SEG + 1):
    t = c / SEG
    x, z = outer(t)
    hs = math.pow(max(0.0, math.sin(math.pi * t)), 0.7)
    col_p, col_uv = [], []
    for r in range(VR + 1):
        k = r / VR
        y = (k - 0.5) * hs
        zz = z - 0.22 * abs(y) * 2.0 * 0.5
        col_p.append((x, y, zz))
        col_uv.append((t, 1.0 - abs(k - 0.5) * 2.0 * 0.92))
    pts.append(col_p); uvs.append(col_uv)
add_grid(pts, uvs, SEG, VR)

bm.to_mesh(mesh)
bm.free()
mesh.update()

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
fbx = os.path.join(OUT, "BossSwordWave_Crescent.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                         mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP')
print("EXPORTED", fbx, len(mesh.vertices), len(mesh.polygons))
