# I22: Level_Facility 엘리베이터 호출 콘솔 (UpperFloor_HQLobby/ElevatorButton 의 1 m 큐브 + 잡동사니 '08 - Default' 재질 대체)
# 실행: "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup -P Tools/ArtGen/lf_elevator_console_blender.py
# 원점 = 기존 ElevatorButton 트랜스폼 위치(바닥에서 1.57 m 위) -> 씬 트랜스폼/콜라이더/Interactable 앵커는 그대로 두고 메시·재질만 교체.
# Blender: Z 위, 정면 = -Y (= Unity +Z, 엘리베이터 문 쪽). X 대칭이라 축 뒤집힘과 무관.
# 슬롯: 0 LF_Graphite(M_Corp_Graphite) / 1 LF_Ceramic(M_LF_CeramicWhite) / 2 LF_Red(M_Corp_StripRed). 네온 금지: 레드는 얇은 선과 버튼만.
import bpy, bmesh, math, os
from mathutils import Matrix, Vector

OUT = r"C:\Users\ikh33\source\chuon-test\Assets\Art\ChuonPolish\Level_Facility\Meshes"
os.makedirs(OUT, exist_ok=True)
FLOOR = -1.57

bpy.ops.wm.read_factory_settings(use_empty=True)
mesh = bpy.data.meshes.new("SM_LF_ElevatorConsole")
obj = bpy.data.objects.new("SM_LF_ElevatorConsole", mesh)
bpy.context.scene.collection.objects.link(obj)
for n in ("LF_Graphite", "LF_Ceramic", "LF_Red"):
    mesh.materials.append(bpy.data.materials.new(n))
bm = bmesh.new()


def tag(geom_faces, mat):
    for f in geom_faces:
        f.material_index = mat


def box(center, size, mat, rot=Matrix.Identity(4), top_scale=(1.0, 1.0), bevel=0.0):
    r = bmesh.ops.create_cube(bm, size=1.0)
    vs = r["verts"]
    for v in vs:
        if v.co.z > 0:
            v.co.x *= top_scale[0]
            v.co.y *= top_scale[1]
    bmesh.ops.transform(bm, matrix=Matrix.Diagonal((size[0], size[1], size[2], 1.0)), verts=vs)
    bmesh.ops.transform(bm, matrix=rot, verts=vs)
    bmesh.ops.translate(bm, vec=Vector(center), verts=vs)
    tag(list({f for v in vs for f in v.link_faces}), mat)  # 베벨 전에 지정 -> 베벨 면이 인접 면 재질을 물려받는다
    if bevel > 0:
        edges = list({e for v in vs for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=vs + edges, offset=bevel, segments=1, affect='EDGES', profile=0.5, clamp_overlap=True)
    return vs


def cyl(center, r, h, mat, rot=Matrix.Identity(4), segs=28):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r, depth=h)
    vs = res["verts"]
    bmesh.ops.transform(bm, matrix=rot, verts=vs)
    bmesh.ops.translate(bm, vec=Vector(center), verts=vs)
    tag(list({f for v in vs for f in v.link_faces}), mat)


G, C, R = 0, 1, 2
# 바닥 플린스 + 레드 바닥선
box((0, 0, FLOOR + 0.04), (0.60, 0.48, 0.08), G, bevel=0.012)
box((0, -0.243, FLOOR + 0.085), (0.50, 0.006, 0.012), R)
# 기둥 (위로 살짝 좁아짐)
col_h = 1.08
col_z0 = FLOOR + 0.08
box((0, 0.02, col_z0 + col_h / 2), (0.34, 0.26, col_h), G, top_scale=(0.86, 0.9), bevel=0.01)
# 기둥 정면 세라믹 인레이 + 양옆 레드 헤어라인
box((0, -0.107, col_z0 + 0.52), (0.18, 0.012, 0.78), C, top_scale=(0.9, 1.0), bevel=0.004)
for sx in (-1, 1):
    box((sx * 0.118, -0.106, col_z0 + 0.52), (0.008, 0.008, 0.70), R)
# 헤드: 문 쪽(-Y)으로 32° 기울인 조작부
tilt = Matrix.Rotation(math.radians(32.0), 4, 'X')
head_c = Vector((0, -0.02, col_z0 + col_h + 0.05))
box(head_c, (0.52, 0.38, 0.12), G, rot=tilt, bevel=0.014)
up = tilt @ Vector((0, 0, 1))
plate_c = head_c + up * 0.066
box(plate_c, (0.44, 0.30, 0.014), C, rot=tilt, bevel=0.004)
# 호출 버튼: 그래파이트 링 + 레드 버튼 (판 앞쪽 2/3 지점)
fwd = tilt @ Vector((0, -1, 0))
btn_c = plate_c + fwd * 0.03 + up * 0.012
cyl(btn_c, 0.085, 0.012, G, rot=tilt)
cyl(btn_c + up * 0.012, 0.062, 0.016, R, rot=tilt)
# 상태 표시 슬릿 (뒤쪽), 층 표시 점 2개
box(plate_c - fwd * 0.105 + up * 0.009, (0.30, 0.03, 0.006), R, rot=tilt)
for sx in (-1, 1):
    box(plate_c + fwd * 0.03 + Vector((sx * 0.15, 0, 0)) + up * 0.009, (0.03, 0.03, 0.006), C, rot=tilt)
# 헤드 아래 목(그림자 홈)
box((0, 0.02, col_z0 + col_h + 0.005), (0.28, 0.22, 0.03), G)

bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(mesh)
bm.free()

for o in bpy.context.view_layer.objects:
    o.select_set(False)
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
path = os.path.join(OUT, "SM_LF_ElevatorConsole.fbx")
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         bake_space_transform=True, mesh_smooth_type='FACE', use_tspace=False,
                         add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
mesh.calc_loop_triangles()
print("EXPORTED", path, "tris=%d" % len(mesh.loop_triangles))

# 미리보기 (스크래치 용, 프로젝트 밖)
prev = os.environ.get("ARTGEN_PREVIEW")
if prev:
    sc = bpy.context.scene
    for i, col in enumerate(((0.14, 0.145, 0.155, 1), (0.86, 0.86, 0.84, 1), (0.9, 0.1, 0.08, 1))):
        mesh.materials[i].diffuse_color = col
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    sc.collection.objects.link(cam)
    cam.location = (1.3, -2.1, 0.45)
    d = Vector((0, 0, -0.85)) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.light = 'STUDIO'
    sc.render.resolution_x, sc.render.resolution_y = 400, 500
    sc.render.filepath = prev
    bpy.ops.render.render(write_still=True)
