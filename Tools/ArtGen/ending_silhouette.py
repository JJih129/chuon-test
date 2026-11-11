"""I22 엔딩 홀로그램 실루엣 텍스처 (TsukasaEndingPresenter 폴백 패널용).

  python Tools/ArtGen/ending_silhouette.py
    1) Blender 5.2 헤드리스로 SM_CINE_TsukasaFigure.fbx(롱코트 과학자, 시네마틱과 같은 메시)를 정사영 렌더 -> 마스크/셰이딩 PNG
    2) PIL 로 합성: 흰 세라믹 홀로 채움 + 4px 스캔라인 + 가장자리 림 + 발치 페이드 + TSUKASA 레드 헤어라인 2줄
  -> Assets/Art/ChuonPolish/Ending/Resources/Ending/T_END_TsukasaHoloSilhouette.png (256x512, RGBA)
  런타임 절차 SDF(128x256, 블롭 형태)를 대체한다. 네온/시안 금지: 흰색 기조, 레드는 얇은 선만.
"""
import os, sys, subprocess

ROOT = r"C:\Users\ikh33\source\chuon-test"
FBX = os.path.join(ROOT, r"Assets\Art\ChuonPolish\Cinematics\Meshes\SM_CINE_TsukasaFigure.fbx")
OUT = os.path.join(ROOT, r"Assets\Art\ChuonPolish\Ending\Resources\Ending\T_END_TsukasaHoloSilhouette.png")
TMP = os.path.join(ROOT, r"Temp\ArtGen_EndingSilhouette")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
W, H = 256, 512
SS = 2  # 슈퍼샘플


def blender_main():
    import bpy, math
    from mathutils import Vector
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (mn + mx) * 0.5
    height = mx.z - mn.z
    sc = bpy.context.scene
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = height * 1.06  # 세로 프레임 기준 (해상도 1:2 -> 세로가 긴 축)
    cam = bpy.data.objects.new("Cam", cam_data)
    sc.collection.objects.link(cam)
    yaw = math.radians(-18.0)  # 살짝 3/4 -> 뒤로 맞잡은 팔/코트 자락이 윤곽에 드러난다
    dist = 10.0
    # 정면 = Blender -Y. 카메라는 -Y 쪽에서 +Y 를 바라본다.
    cam.location = (ctr.x + math.sin(yaw) * dist, ctr.y - math.cos(yaw) * dist, mn.z + height * 0.5)
    cam.rotation_euler = (math.radians(90.0), 0.0, yaw)
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.resolution_x = W * SS
    sc.render.resolution_y = H * SS
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sh = sc.display.shading
    sh.color_type = 'SINGLE'
    sh.single_color = (1.0, 1.0, 1.0)
    sc.view_settings.view_transform = 'Standard'
    os.makedirs(TMP, exist_ok=True)
    for light, name in (('FLAT', 'mask'), ('STUDIO', 'shade')):
        sh.light = light
        if light == 'STUDIO':
            sh.show_cavity = True
            sh.cavity_type = 'WORLD'
        sc.render.filepath = os.path.join(TMP, name + ".png")
        bpy.ops.render.render(write_still=True)


def pil_main():
    from PIL import Image, ImageFilter, ImageChops
    os.makedirs(TMP, exist_ok=True)
    subprocess.run([BLENDER, "-b", "--factory-startup", "-P", os.path.abspath(__file__)], check=True,
                   stdout=subprocess.DEVNULL)
    mask = Image.open(os.path.join(TMP, "mask.png")).getchannel("A").resize((W, H), Image.LANCZOS)
    shade = Image.open(os.path.join(TMP, "shade.png")).convert("L").resize((W, H), Image.LANCZOS)
    inner = mask.filter(ImageFilter.MinFilter(5))
    rim = ImageChops.subtract(mask, inner).filter(ImageFilter.GaussianBlur(0.8))
    glow = mask.filter(ImageFilter.GaussianBlur(6))
    m, s, r, g = mask.load(), shade.load(), rim.load(), glow.load()
    out = Image.new("RGBA", (W, H))
    o = out.load()
    red_lines = (int(H * 0.36), int(H * 0.37), int(H * 0.705))  # 허리선/무릎 위 글리치 헤어라인
    for y in range(H):
        scan = 0.5 if (y % 4) == 0 else 1.0
        foot = min(1.0, (H - 1 - y) / 70.0)  # 발치 페이드 (위가 y=0)
        foot = 0.25 + 0.75 * max(0.0, foot)
        for x in range(W):
            a_m = m[x, y] / 255.0
            shd = s[x, y] / 255.0
            fill = a_m * (0.34 + 0.30 * shd) * scan
            rim_a = (r[x, y] / 255.0) * 0.95
            halo = (g[x, y] / 255.0) * 0.12 * (1.0 - a_m)
            a = max(fill, rim_a, halo) * foot
            lum = 236 + int(19 * max(shd, rim_a))
            if y in red_lines and a_m > 0.5:
                o[x, y] = (232, 58, 58, int(min(1.0, a + 0.35) * 255))
                continue
            o[x, y] = (min(lum, 255), min(lum, 255), min(lum + 2, 255), int(min(1.0, a) * 255))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    out.save(OUT)
    print("wrote", OUT, out.size)


try:
    import bpy  # noqa: F401
    blender_main()
except ImportError:
    pil_main()
