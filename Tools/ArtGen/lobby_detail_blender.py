"""Level_Facility HQ lobby DETAIL kit -- Blender 5.x headless.

  python Tools/ArtGen/lobby_detail_textures.py                       # 1) T_LFD_Atlas_* (lobby-specific cells)
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b -P Tools/ArtGen/lobby_detail_blender.py -- [out_dir] [preview_dir]

Writes <out_dir>/Meshes/SM_LFD_*.fbx (default out_dir = Assets/Art/ChuonPolish/Level_Facility/Detail) and LFD_Layout.json.
preview_dir (scratchpad only): thumbs/*.png, lfd_stats.json, shot_*.png (composed interiors from the same layout table).

Reuses the tutorial detail kit (tutorial_detail_blender.py): the bmesh builder B, the role -> UV conventions and the shared
trim sheet T_INFD_Trim (strips wrap in U, 256 px/m) + slogan atlas T_INF_Signs. New roles: 500 + ATLAS_KEYS.index(key)
-> T_LFD_Atlas cell (lobby_detail_layout.ATLAS), 600 -> LFD_Glass (transparent, no texture).
Material slots (remapped by Assets/Editor/LobbyDetailPass.cs): INFD_Trim, INF_Signs, LFD_Atlas, LFD_Glass.
Conventions as the tutorial kit: origin = base centre (wall pieces: on the wall plane), front = -Y in Blender (= Unity +Z),
Blender (x, y, z) -> Unity (-x, z, -y). Multi-LOD assets contain <name>_LOD0/_LOD1; empties (LightAnchor) are top-level.
Style: TSUKASA HQ -- off-white ceramic panels with reveal lines, graphite, thin red light lines, indirect white coves;
amber only where something is a passage (lift), cyan only for holographic information.
"""
import bpy, math, os, sys, json, random
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import tutorial_detail_blender as T  # noqa: E402  (main() is guarded; we only borrow the builder + trim tables)
from lobby_detail_layout import ATLAS_KEYS, atlas_uv, slots, write_layout  # noqa: E402

ROOT = T.ROOT
TUT_DETAIL = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Tutorial", "Detail")
TUT_TEX = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Tutorial", "Textures")
R = T.R
B = T.B
SIGN0 = T.SIGN0
GLASS = 600
SLOTS = ["INFD_Trim", "INF_Signs", "LFD_Atlas", "LFD_Glass"]


def A(key):
    return 500 + ATLAS_KEYS.index(key)


def slot_of(role):
    if role >= GLASS: return 3
    if role >= 500: return 2
    if role >= 200: return 1
    return 0


# ------------------------------------------------------------------ UV + materials (patched into T so B.build uses them)
def apply_uv_mats(ob, imp=None):
    me = ob.data
    roles = [0] * len(me.polygons)
    attr = me.attributes.get("role")
    if attr is not None:
        attr.data.foreach_get("value", roles)
    used = sorted({slot_of(r) for r in roles})
    for s in used:
        me.materials.append(get_mat(SLOTS[s]))
    remap = {s: i for i, s in enumerate(used)}
    uvl = me.uv_layers.new(name="UVMap").data
    up = Vector((0, 0, 1))
    pad = 2.0 / T.TS
    for p, role in zip(me.polygons, roles):
        p.material_index = remap[slot_of(role)]
        n = p.normal
        cos = [me.vertices[me.loops[li].vertex_index].co for li in p.loop_indices]
        if abs(n.z) > 0.7:
            right = Vector((1, 0, 0)); tv = Vector((0, 1 if n.z > 0 else -1, 0))
        else:
            f = -n
            right = f.cross(up).normalized(); tv = right.cross(f).normalized()
        us = [c.dot(right) for c in cos]; vs = [c.dot(tv) for c in cos]
        umin, umax, vmin, vmax = min(us), max(us), min(vs), max(vs)
        if role < 16:
            x0, vb, x1, vt, hs = T.strip_rect(role)
            hv = vmax - vmin
            for li, uu, vv in zip(p.loop_indices, us, vs):
                t = (vv - vmin + (hs - hv) / 2) / hs if hv <= hs else (vv - vmin) / max(hv, 1e-6)
                uvl[li].uv = (uu / 8.0, vb + pad + t * (vt - vb - 2 * pad))
            continue
        if role == GLASS:
            for li, uu, vv in zip(p.loop_indices, us, vs):
                uvl[li].uv = (uu * 0.5, vv * 0.5)
            continue
        if role >= 500:
            rx, ry, rw, rh = atlas_uv(ATLAS_KEYS[role - 500])
        else:
            rx, ry, rw, rh = T.rect_for(role)
        pu, pv = rw * 0.004, rh * 0.004
        for li, uu, vv in zip(p.loop_indices, us, vs):
            tu = (uu - umin) / max(umax - umin, 1e-6); tv_ = (vv - vmin) / max(vmax - vmin, 1e-6)
            uvl[li].uv = (rx + pu + tu * (rw - 2 * pu), ry + pv + tv_ * (rh - 2 * pv))


T.apply_uv_mats = apply_uv_mats

MATS = {}
TEXDIRS = {}
MAT_TEX = {"INFD_Trim": (os.path.join(TUT_DETAIL, "Textures"), "T_INFD_Trim", 4.0),
           "INF_Signs": (TUT_TEX, "T_INF_Signs", 1.7),
           "LFD_Atlas": (None, "T_LFD_Atlas", 2.2)}


def get_mat(name):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    nt = T._nodes(m)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bs.outputs[0], out.inputs[0])
    if name == "LFD_Glass":
        bs.inputs["Base Color"].default_value = (0.72, 0.8, 0.84, 1)
        bs.inputs["Roughness"].default_value = 0.04
        bs.inputs["Alpha"].default_value = 0.16
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            m.blend_method = 'BLEND'
        MATS[name] = m
        return m
    d, base, emi = MAT_TEX[name]
    d = d or os.path.join(OUT, "Textures")

    def tp(suffix):
        p = os.path.join(d, base + suffix)
        return p if os.path.exists(p) else None
    a = tp("_Albedo.png")
    if a:
        ta = T._img(nt, a); nt.links.new(ta.outputs["Color"], bs.inputs["Base Color"])
    mk = tp("_Mask.png")
    if mk:
        tm = T._img(nt, mk, True); sep = nt.nodes.new("ShaderNodeSeparateColor"); inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        nt.links.new(tm.outputs["Color"], sep.inputs[0]); nt.links.new(sep.outputs[0], bs.inputs["Metallic"])
        nt.links.new(tm.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], bs.inputs["Roughness"])
    else:
        bs.inputs["Roughness"].default_value = 0.35
    nm = tp("_Normal.png")
    if nm:
        tn = T._img(nt, nm, True); nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(tn.outputs["Color"], nmap.inputs["Color"]); nt.links.new(nmap.outputs[0], bs.inputs["Normal"])
    e = tp("_Emission.png")
    if e:
        te = T._img(nt, e); nt.links.new(te.outputs["Color"], bs.inputs["Emission Color"])
        bs.inputs["Emission Strength"].default_value = emi
    MATS[name] = m
    return m


# ------------------------------------------------------------------ export
EXPORTED = []
LIB = {}


def export(name, builders, anchors=()):
    objs = []
    multi = len(builders) > 1
    for i, b in enumerate(builders):
        objs.append(b.build("SM_LFD_%s_LOD%d" % (name, i) if multi else "SM_LFD_" + name))
    for an, loc in anchors:
        e = bpy.data.objects.new(an, None); e.location = loc
        bpy.context.scene.collection.objects.link(e); objs.append(e)
    path = os.path.join(OUT, "Meshes", "SM_LFD_" + name + ".fbx")
    for o in list(bpy.context.view_layer.objects):
        if o is not None:
            o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=False, mesh_smooth_type='FACE',
                             use_tspace=False, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
    t = [T.tris(o.data) for o in objs if o.type == 'MESH']
    print("EXPORTED %-26s tris=%s" % (name, t))
    EXPORTED.append((name, t))
    LIB[name] = {"lods": [o.data for o in objs if o.type == 'MESH'], "anchors": list(anchors)}
    T.clear_scene()


# ------------------------------------------------------------------ architecture
def wall_panel(variant):
    def f(L):
        b = B(L)
        b.boxmm((-1.45, -0.02, 0), (1.45, 0, 5.0), R["graphite"], skip=('+y',))
        b.boxmm((-1.45, -0.075, 0), (1.45, -0.02, 0.16), R["graphite"], skip=('+y', '-z'))
        cols = [(-1.44, -0.006), (0.006, 1.44)] if variant == "A" else [(-1.44, -0.27), (0.27, 1.44)]
        if L == 0:
            for x0, x1 in cols:
                for r in range(4):
                    z0 = 0.2 + r * 1.025 + 0.006
                    b.boxmm((x0, -0.06, z0), (x1, -0.02, z0 + 1.013), R["ceramic"], skip=('+y',))
        else:
            for x0, x1 in cols:
                b.boxmm((x0, -0.06, 0.2), (x1, -0.02, 4.3), R["ceramic"], skip=('+y',))
        if variant == "B":
            b.boxmm((-0.02, -0.045, 0.45), (0.02, -0.02, 4.1), R["white_light"], skip=('+y',))
            b.boxmm((0.16, -0.03, 0.45), (0.175, -0.02, 4.1), R["red_light"], skip=('+y',))
        # indirect cove: shelf (downlight line underneath) + upstand; wall wash gradient above
        b.boxmm((-1.5, -0.24, 4.36), (1.5, -0.02, 4.44), R["graphite"], roles={'-z': R["white_light"]}, skip=('+y',))
        b.boxmm((-1.5, -0.26, 4.36), (1.5, -0.22, 4.56), R["graphite"], skip=('+y', '-z'))
        b.vquad(-1.45, 1.45, 4.44, 5.0, -0.021, A("wash"))
        return b
    return f


def wall_panel_tall(L):
    b = B(L)
    b.boxmm((-1.45, -0.02, 0), (1.45, 0, 15.0), R["graphite"], skip=('+y',))
    for k in range(3):
        z = 5.0 * k
        rows = [(z + 0.05, z + 2.25), (z + 2.27, z + 4.45)] if L == 0 else [(z + 0.05, z + 4.45)]
        for x0, x1 in ((-1.44, -0.006), (0.006, 1.44)):
            for z0, z1 in rows:
                b.boxmm((x0, -0.07, z0), (x1, -0.02, z1), R["ceramic"], skip=('+y',))
        b.boxmm((-1.5, -0.26, z + 4.5), (1.5, -0.02, z + 4.58), R["graphite"], roles={'-z': R["white_light"]}, skip=('+y',))
        b.vquad(-1.45, 1.45, z + 4.58, z + 5.0, -0.021, A("wash"))
    return b


def corridor_panel(L):
    b = B(L)
    b.boxmm((-1.65, -0.004, 0), (1.65, 0, 3.55), R["graphite"], skip=('+y',))
    b.boxmm((-1.65, -0.012, 0), (1.65, -0.004, 1.0), R["graph_rib"], skip=('+y',))
    if L == 0:
        for i in range(3):
            x0 = -1.65 + i * 1.1 + 0.006
            for z0, z1 in ((1.1, 2.15), (2.162, 3.2)):
                b.boxmm((x0, -0.02, z0), (x0 + 1.088, -0.004, z1), R["ceramic"], skip=('+y',))
    else:
        b.boxmm((-1.65, -0.02, 1.1), (1.65, -0.004, 3.2), R["ceramic"], skip=('+y',))
    b.boxmm((-1.65, -0.1, 3.24), (1.65, -0.004, 3.3), R["graphite"], roles={'-z': R["white_light"]}, skip=('+y',))
    b.vquad(-1.65, 1.65, 3.3, 3.55, -0.005, A("wash"))
    return b


def column_clad(L):
    b = B(L)
    b.boxmm((-0.45, -0.36, 0), (0.45, 0.36, 0.2), R["graphite"], skip=('-z',))
    b.boxmm((-0.4, -0.325, 0.2), (0.4, 0.325, 4.75), R["ceramic"], skip=('-z', '+z'))
    if L == 0:
        for sx in (-1, 1):
            for sy in (-1, 1):
                b.boxmm((sx * 0.4 - 0.025, sy * 0.325 - 0.025, 0.2), (sx * 0.4 + 0.025, sy * 0.325 + 0.025, 4.75), R["graphite"], skip=('-z', '+z'))
    b.boxmm((-0.45, -0.36, 4.75), (0.45, 0.36, 5.0), R["graphite"], skip=('+z',))
    b.boxmm((-0.452, -0.362, 4.74), (0.452, 0.362, 4.76), R["red_light"], skip=('+z', '-z'))
    for sy in (-1, 1):
        b.boxmm((-0.03, sy * 0.325 - 0.006, 0.5), (0.03, sy * 0.325 + 0.006, 4.5), R["white_light"], skip=('+z', '-z'))
    return b


def coffer(w, d, depth, beam, lights, deep_reveal=False):
    def f(L):
        b = B(L)
        b.boxmm((-w / 2, -d / 2, -0.03), (w / 2, d / 2, 0), R["white_paint"], skip=('+z',))
        hw, hd = w / 2, d / 2
        b.boxmm((-hw, -hd, -depth), (hw, -hd + beam, 0), R["ceramic"], skip=('+z',))
        b.boxmm((-hw, hd - beam, -depth), (hw, hd, 0), R["ceramic"], skip=('+z',))
        b.boxmm((-hw, -hd + beam, -depth), (-hw + beam, hd - beam, 0), R["ceramic"], skip=('+z',))
        b.boxmm((hw - beam, -hd + beam, -depth), (hw, hd - beam, 0), R["ceramic"], skip=('+z',))
        if deep_reveal and L == 0:
            r = 0.05
            b.boxmm((-hw + beam, -hd + beam, -depth * 0.35), (hw - beam, -hd + beam + r, -0.03), R["graphite"], skip=('+z',))
            b.boxmm((-hw + beam, hd - beam - r, -depth * 0.35), (hw - beam, hd - beam, -0.03), R["graphite"], skip=('+z',))
        for lx in lights:
            b.boxmm((lx - 0.05, -hd + beam + 0.12, -0.075), (lx + 0.05, hd - beam - 0.12, -0.03), R["white_light"], skip=('+z',))
        return b
    return f


def corridor_ceiling(L):
    b = B(L)
    for s in (-1, 1):
        x0, x1 = sorted((s * 0.2, s * 2.4))
        b.boxmm((x0, -1.79, -0.06), (x1, 1.79, 0), R["ceramic"], skip=('+z',))
        b.boxmm((s * 1.4 - 0.04, -1.7, -0.075), (s * 1.4 + 0.04, 1.7, -0.06), R["white_light"], skip=('+z',))
        if L == 0:
            b.boxmm((min(s * 0.2, s * 0.26), -1.79, -0.085), (max(s * 0.2, s * 0.26), 1.79, -0.06), R["graphite"], skip=('+z',))
    return b


def glass_balustrade(L):
    b = B(L)
    b.boxmm((-1.0, -0.06, 0), (1.0, 0.06, 0.1), R["steel"], skip=('-z',))
    b.boxmm((-0.97, -0.01, 0.1), (0.97, 0.01, 1.02), GLASS)
    if L == 0:
        for x in (-0.97, 0.97):
            b.boxmm((x - 0.025, -0.03, 0.1), (x + 0.025, 0.03, 1.06), R["steel"], skip=('-z',))
        b.cyl((0, 0, 1.09), 0.028, 2.0, R["steel"], 8, axis='X')
        b.boxmm((-1.0, -0.012, 1.045), (1.0, 0.012, 1.062), R["white_light"], skip=('+z',))
    else:
        b.boxmm((-1.0, -0.028, 1.04), (1.0, 0.028, 1.12), R["steel"])
    return b


def disc(b, r, n, role, z=0.0):
    b.quad([(math.cos(i / n * 2 * math.pi) * r, math.sin(i / n * 2 * math.pi) * r, z) for i in range(n)], role, (0, 0, 1))


def flat(sx, sy, role):
    def f(L):
        b = B(L)
        b.quad([(-sx / 2, -sy / 2, 0), (sx / 2, -sy / 2, 0), (sx / 2, sy / 2, 0), (-sx / 2, sy / 2, 0)], role, (0, 0, 1))
        return b
    return f


def emblem_inlay(L):
    b = B(L)
    disc(b, 1.6, 40, A("emblem_inlay"))
    return b


# ------------------------------------------------------------------ signage
def wayfinding(key):
    def f(L):
        b = B(L)
        b.boxmm((-0.84, -0.06, 0), (0.84, 0, 0.46), R["graphite"], skip=('+y',))
        b.vquad(-0.8, 0.8, 0.03, 0.43, -0.061, A(key))
        b.boxmm((-0.8, -0.05, -0.012), (0.8, -0.01, 0.0), R["white_light"], skip=('+y', '+z'))
        return b
    return f


def floor_sign(key):
    def f(L):
        b = B(L)
        b.boxmm((-0.62, -0.05, 0), (0.62, 0, 1.24), R["graphite"], skip=('+y',))
        b.vquad(-0.6, 0.6, 0.02, 1.22, -0.051, A(key))
        return b
    return f


def slogan(i):
    def f(L):
        b = B(L)
        b.boxmm((-1.9, -0.14, 0), (1.9, 0, 2.1), R["graphite"], skip=('+y',))
        b.vquad(-1.83, 1.83, 0.05, 2.05, -0.141, SIGN0 + i)
        b.boxmm((-1.9, -0.15, -0.02), (1.9, -0.1, 0.0), R["red_light"], skip=('+z',))
        return b
    return f


def warn_plate(L):
    b = B(L)
    b.boxmm((-0.27, -0.03, 0), (0.27, 0, 0.94), R["steel"], skip=('+y',))
    b.vquad(-0.25, 0.25, 0.02, 0.92, -0.031, A("warn_plate"))
    return b


def wall_screen(key, w, h):
    def f(L):
        b = B(L)
        b.boxmm((-w / 2 - 0.06, -0.1, -0.06), (w / 2 + 0.06, 0, h + 0.06), R["graphite"], skip=('+y',))
        b.vquad(-w / 2, w / 2, 0, h, -0.101, A(key))
        b.boxmm((-w / 2 - 0.06, -0.105, h + 0.06), (w / 2 + 0.06, -0.085, h + 0.075), R["red_light"])
        b.boxmm((-w / 2 + 0.2, -0.09, -0.08), (w / 2 - 0.2, -0.02, -0.06), R["white_light"], skip=('+z',))
        return b
    return f


# ------------------------------------------------------------------ lifts
def portal_bank(L):
    b = B(L)
    for s in (-1, 1):
        x0, x1 = sorted((s * 1.47, s * 1.95))
        b.boxmm((x0, -0.18, 0.15), (x1, 0, 3.7), R["ceramic"], skip=('+y',), roles={('-x' if s > 0 else '+x'): R["graph_rib"]})
        b.boxmm((x0 - 0.01, -0.19, 0), (x1 + 0.01, 0, 0.15), R["graphite"], skip=('+y', '-z'))
        b.vquad(min(s * 1.53, s * 1.89), max(s * 1.53, s * 1.89), 2.3, 2.48, -0.181, A("ind_lock"))
        if L == 0:
            xi = s * 1.47
            b.boxmm((min(xi, xi + s * 0.02), -0.186, 0.15), (max(xi, xi + s * 0.02), -0.18, 3.7), R["red_light"], skip=('+y',))
            xo = s * 1.95
            b.boxmm((min(xo, xo - s * 0.03), -0.185, 0.15), (max(xo, xo - s * 0.03), -0.18, 3.7), R["graphite"], skip=('+y',))
    b.boxmm((-1.95, -0.18, 3.7), (1.95, 0, 4.05), R["ceramic"], skip=('+y',))
    b.vquad(-1.2, 1.2, 3.78, 3.97, -0.181, A("screen_off"))
    return b


def portal_main(L):
    b = B(L)
    for s in (-1, 1):
        x0, x1 = sorted((s * 3.0, s * 4.0))
        xi = s * 3.0
        b.boxmm((x0, -0.32, 0.2), (x1, 0, 7.0), R["ceramic"], skip=('+y',), roles={('-x' if s > 0 else '+x'): R["graphite"]})
        b.boxmm((x0 - 0.02, -0.34, 0), (x1 + 0.02, 0, 0.2), R["graphite"], skip=('+y', '-z'))
        b.boxmm((x0 - 0.02, -0.34, 7.0), (x1 + 0.02, 0, 7.2), R["graphite"], skip=('+y',))
        b.boxmm((x0 - 0.022, -0.342, 7.0), (x1 + 0.022, -0.32, 7.02), R["red_light"], skip=('+y',))
        b.boxmm((min(xi + s * 0.08, xi + s * 0.13), -0.33, 0.35), (max(xi + s * 0.08, xi + s * 0.13), -0.32, 6.7), R["amber_light"], skip=('+y',))
        b.vquad(min(s * 3.18, s * 3.88), max(s * 3.18, s * 3.88), 2.0, 2.35, -0.321, A("ind_b4"))
        if L == 0:
            for z in (0.9, 3.0, 5.1):
                b.boxmm((x0 + 0.05, -0.325, z), (x1 - 0.05, -0.32, z + 0.012), R["graphite"], skip=('+y',))
    b.vquad(3.3, 3.62, 1.05, 1.55, -0.322, R["cell_panel"])
    return b


# ------------------------------------------------------------------ reception
def u(ux, uy, uz):
    """Unity local -> Blender local."""
    return (-ux, -uz, uy)


def desk_top(L):
    b = B(L)
    top = 1.14
    for sx in (-1, 1):
        x, y, z = u(sx * 1.5, top, -0.92)
        b.boxmm((x - 0.23, y - 0.08, z), (x + 0.23, y + 0.08, z + 0.02), R["graphite"], roles={'+z': R["graph_rib"]})
        x, y, z = u(sx * 2.55, top, -0.7)
        b.boxmm((x - 0.17, y - 0.12, z), (x + 0.17, y + 0.12, z + 0.06), R["steel"])
        b.boxmm((x - 0.15, y - 0.1, z + 0.06), (x + 0.15, y + 0.1, z + 0.065), R["white_paint"])
    x, y, z = u(0.6, top, -0.25)
    b.boxmm((x - 0.08, y - 0.06, z), (x + 0.08, y + 0.06, z + 0.07), R["graphite"], roles={'+z': R["cell_panel"]})
    x, y, z = u(0.0, top, -0.45)
    b.boxmm((x - 0.18, y - 0.04, z), (x + 0.18, y + 0.04, z + 0.22), R["ceramic"], roles={'-y': R["cell_emblem"]})
    x, y, z = u(-0.6, top, -0.3)
    b.cyl((x, y, z + 0.004), 0.06, 0.008, R["graphite"], 10)
    b.rod((x, y, z + 0.008), (x, y + 0.1, z + 0.32), 0.008, R["steel"], 4)
    b.boxmm((x - 0.12, y + 0.04, z + 0.3), (x + 0.12, y + 0.16, z + 0.33), R["graphite"], roles={'-z': R["white_light"]})
    return b


def chair(L):
    b = B(L)
    for i in range(5):
        a = i / 5 * 2 * math.pi
        e = (math.cos(a) * 0.3, math.sin(a) * 0.3, 0.06)
        b.rod((0, 0, 0.1), e, 0.018, R["graphite"], 4)
        if L == 0:
            b.boxz((e[0], e[1], 0), (0.05, 0.05, 0.05), R["rubber"])
    b.cyl((0, 0, 0.27), 0.025, 0.34, R["steel"], 6, caps=(False, False))
    b.boxmm((-0.25, -0.25, 0.44), (0.25, 0.23, 0.52), R["rubber"])
    b.boxmm((-0.24, 0.2, 0.55), (0.24, 0.26, 1.05), R["rubber"], roles={'-y': R["rubber"], '+y': R["graphite"]})
    b.boxmm((-0.03, 0.22, 0.44), (0.03, 0.27, 0.6), R["graphite"])
    if L == 0:
        b.boxmm((-0.2, 0.264, 0.62), (0.2, 0.268, 0.64), R["red_light"])
        for sx in (-1, 1):
            b.boxmm((sx * 0.27 - 0.02, -0.1, 0.52), (sx * 0.27 + 0.02, 0.15, 0.7), R["graphite"])
    return b


def bench(L):
    b = B(L)
    b.boxmm((-1.0, -0.18, 0), (1.0, 0.18, 0.38), R["graphite"], skip=('-z',))
    b.boxmm((-1.2, -0.28, 0.38), (1.2, 0.28, 0.46), R["ceramic"])
    b.boxmm((-1.15, -0.25, 0.46), (1.15, 0.25, 0.5), R["rubber"], skip=('-z',))
    b.boxmm((-1.0, -0.19, 0.33), (1.0, -0.18, 0.36), R["red_light"], skip=('+y',))
    if L == 0:
        for x in (-0.38, 0.38):
            b.boxmm((x - 0.006, -0.252, 0.47), (x + 0.006, 0.252, 0.502), R["graphite"])
    return b


def planter_low(L):
    b = B(L)
    b.boxmm((-0.9, -0.3, 0.06), (0.9, 0.3, 0.5), R["ceramic"], skip=('-z',))
    b.boxmm((-0.86, -0.32, 0), (0.86, 0.32, 0.06), R["graphite"], skip=('-z',))
    b.boxmm((-0.84, -0.24, 0.44), (0.84, 0.24, 0.5), R["graphite"], skip=('-z',))
    b.boxmm((-0.9, -0.305, 0.08), (0.9, -0.3, 0.1), R["red_light"], skip=('+y',))
    xs = (-0.6, -0.2, 0.2, 0.6) if L == 0 else (-0.42, 0.42)
    for i, x in enumerate(xs):
        s = 0.56 if L == 0 else 0.95
        b.lump((x, 0, 0.64), (s, 0.5, 0.42 + (i % 2) * 0.06), A("foliage"), 90 + i)
    return b


def planter_tall(L):
    b = B(L)
    b.boxmm((-0.36, -0.36, 0.08), (0.36, 0.36, 0.85), R["ceramic"], skip=('-z',))
    b.boxmm((-0.33, -0.33, 0), (0.33, 0.33, 0.08), R["graphite"], skip=('-z',))
    b.boxmm((-0.3, -0.3, 0.8), (0.3, 0.3, 0.86), R["graphite"], skip=('-z',))
    b.boxmm((-0.362, -0.362, 0.6), (0.362, 0.362, 0.615), R["red_light"], skip=('+z', '-z'))
    b.cyl((0, 0, 1.25), 0.045, 0.8, R["rubber"], 6, r2=0.03, caps=(False, False))
    parts = [((0, 0, 1.75), (0.95, 0.9, 0.7)), ((0.12, 0.08, 2.2), (0.72, 0.7, 0.55)), ((-0.08, -0.06, 2.55), (0.46, 0.44, 0.38))]
    for i, (c, s) in enumerate(parts if L == 0 else parts[:2]):
        b.lump(c, s, A("foliage"), 120 + i)
    return b


def info_pillar(L):
    b = B(L)
    b.boxmm((-0.32, -0.24, 0), (0.32, 0.24, 0.08), R["graphite"], skip=('-z',))
    b.boxmm((-0.26, -0.16, 0.08), (0.26, 0.16, 2.0), R["ceramic"])
    for rz in (0, math.pi):
        b.push(Matrix.Rotation(rz, 4, 'Z'))
        b.boxmm((-0.22, -0.17, 0.92), (0.22, -0.16, 1.78), R["graphite"])
        b.vquad(-0.2, 0.2, 0.95, 1.75, -0.171, A("directory"))
        b.pop()
    for sx in (-1, 1):
        b.boxmm((sx * 0.26 - 0.004, -0.04, 0.2), (sx * 0.26 + 0.004, 0.04, 1.95), A("halo_cyan"), skip=('+z', '-z'))
    b.cyl((0, 0, 2.02), 0.12, 0.04, A("halo_cyan"), 12)
    n = 20 if L == 0 else 12
    pts = [(math.cos(i / n * 2 * math.pi) * 0.3, math.sin(i / n * 2 * math.pi) * 0.3, 2.28) for i in range(n + 1)]
    b.tube(pts, 0.012, A("halo_cyan"), 4)
    return b


def scan_arch(L):
    b = B(L)
    for s in (-1, 1):
        x0, x1 = sorted((s * 1.9, s * 2.2))
        b.boxmm((x0, -0.25, 0), (x1, 0.25, 3.2), R["ceramic"], roles={('-x' if s > 0 else '+x'): R["graphite"]})
        xi = s * 1.9
        b.boxmm((min(xi, xi - s * 0.006), -0.04, 0.3), (max(xi, xi - s * 0.006), 0.04, 3.0), R["red_light"])
        b.boxmm((x0, -0.255, 0), (x1, 0.255, 0.12), R["graphite"])
        if L == 0:
            b.vquad(min(s * 1.95, s * 2.15), max(s * 1.95, s * 2.15), 1.2, 1.6, -0.251, R["cell_panel"])
    b.boxmm((-2.2, -0.25, 3.2), (2.2, 0.25, 3.45), R["graphite"])
    b.boxmm((-1.9, -0.03, 3.19), (1.9, 0.03, 3.2), R["red_light"], skip=('+z',))
    b.boxmm((-2.2, -0.255, 3.42), (2.2, 0.255, 3.44), R["white_light"], skip=('+z', '-z'))
    return b


def speed_gates(L):
    b = B(L)
    for s in (-1, 1):
        x = s * 0.48
        b.boxmm((x - 0.11, -0.7, 0.04), (x + 0.11, 0.7, 1.0), R["ceramic"], skip=('-z',))
        b.boxmm((x - 0.12, -0.71, 0), (x + 0.12, 0.71, 0.04), R["rubber"], skip=('-z',))
        b.boxmm((x - 0.115, -0.705, 1.0), (x + 0.115, 0.705, 1.04), R["graphite"])
        b.boxmm((x - 0.07, -0.62, 1.04), (x + 0.07, -0.4, 1.06), R["graphite"], roles={'+z': R["cell_panel"]})
        xi = x - s * 0.11
        b.boxmm((min(xi, xi - s * 0.004), -0.65, 0.84), (max(xi, xi - s * 0.004), 0.65, 0.86), R["amber_light"])
        if L == 0:
            b.boxmm((min(xi, xi - s * 0.05), -0.1, 0.45), (max(xi, xi - s * 0.05), 0.25, 0.85), GLASS)
            b.boxmm((min(xi, xi - s * 0.005), -0.69, 0.6), (max(xi, xi - s * 0.005), -0.66, 0.66), R["white_light"])
    return b


def cleaning_robot(L):
    b = B(L)
    b.boxmm((-0.25, 0.4, 0), (0.25, 0.55, 0.32), R["graphite"], skip=('-z',))
    b.boxmm((-0.2, 0.395, 0.24), (0.2, 0.4, 0.26), R["white_light"])
    b.boxmm((-0.3, 0.2, 0), (0.3, 0.55, 0.012), R["rubber"], skip=('-z',))
    b.cyl((0, 0, 0.075), 0.32, 0.09, R["white_paint"], 20, caps=(False, False))
    b.cyl((0, 0, 0.125), 0.3, 0.012, R["graphite"], 20, caps=(False, True))
    b.cyl((0, 0, 0.05), 0.326, 0.045, R["rubber"], 20, caps=(False, False))
    b.cyl((0, -0.14, 0.14), 0.05, 0.025, R["dark_glass"], 10, caps=(False, True))
    b.boxmm((-0.05, -0.33, 0.09), (0.05, -0.31, 0.105), R["amber_light"])
    if L == 0:
        b.boxmm((-0.14, -0.04, 0.131), (0.14, 0.04, 0.132), R["cell_emblem"])
    return b


# ------------------------------------------------------------------ service / B4
def cable_tray(L):
    b = B(L)
    for s in (-1, 1):
        y0, y1 = sorted((s * 0.18, s * 0.2))
        b.boxmm((-2.0, y0, -0.62), (2.0, y1, -0.5), R["steel"])
    if L == 0:
        for i in range(14):
            x = -1.95 + i * 0.3
            b.boxmm((x - 0.015, -0.18, -0.62), (x + 0.015, 0.18, -0.6), R["steel"], skip=('+x', '-x'))
        rng = random.Random(17)
        for k in range(5):
            y = -0.13 + k * 0.065; r = 0.022 + 0.008 * (k % 2)
            pts = [(-2.0 + i * 0.5, y + (rng.random() - 0.5) * 0.02, -0.6 + r + (rng.random()) * 0.01) for i in range(9)]
            b.tube(pts, r, R["rubber"] if k != 2 else R["red_paint"], 5)
        for x in (-1.5, 1.5):
            for s in (-1, 1):
                b.boxmm((x - 0.008, s * 0.21 - 0.008, -0.62), (x + 0.008, s * 0.21 + 0.008, 0), R["steel"], skip=('+z', '-z'))
            b.boxmm((x - 0.03, -0.25, -0.635), (x + 0.03, 0.25, -0.62), R["steel"])
    else:
        b.boxmm((-2.0, -0.17, -0.6), (2.0, 0.17, -0.54), R["rubber"])
    return b


def wall_vent(L):
    b = B(L)
    b.boxmm((-0.65, -0.06, 0), (0.65, 0, 0.7), R["graphite"], skip=('+y',))
    for k in range(3):
        b.vquad(-0.6, 0.6, 0.05 + k * 0.2, 0.25 + k * 0.2, -0.061, R["louvre"])
    b.boxmm((-0.65, -0.065, 0.66), (0.65, -0.06, 0.7), R["steel"])
    return b


def duct_run(L):
    b = B(L)
    b.boxmm((-2.0, -0.55, 0), (2.0, -0.05, 0.4), R["steel"])
    for x in ((-2.0, -0.67, 0.67, 2.0) if L == 0 else (-2.0, 2.0)):
        b.boxmm((x - 0.03, -0.58, -0.03), (x + 0.03, -0.02, 0.43), R["steel"])
    for x in (-1.3, 1.3):
        b.boxmm((x - 0.04, -0.05, 0.15), (x + 0.04, 0, 0.25), R["graphite"])
    if L == 0:
        b.vquad(0.9, 1.5, 0.08, 0.32, -0.551, R["louvre"])
    return b


def pipe_run(L):
    b = B(L)
    seg = 12 if L == 0 else 8
    b.cyl((0, -0.22, 2.7), 0.11, 4.0, R["red_paint"], seg, axis='X', caps=(False, False))
    b.cyl((0, -0.2, 3.08), 0.08, 4.0, R["graphite"], seg, axis='X', caps=(False, False))
    b.cyl((0, -0.46, 2.88), 0.07, 4.0, R["steel"], seg, axis='X', caps=(False, False))
    for x in (-1.3, 1.3):
        b.boxmm((x - 0.05, -0.6, 2.52), (x + 0.05, 0, 2.58), R["steel"])
        b.boxmm((x - 0.05, -0.6, 3.2), (x + 0.05, 0, 3.26), R["steel"])
        b.boxmm((x - 0.05, -0.6, 2.52), (x + 0.05, -0.56, 3.26), R["steel"])
    b.cyl((1.8, -0.46, 1.42), 0.07, 2.84, R["steel"], seg, caps=(False, False))
    if L == 0:
        for x in (-0.6, 0.6):
            b.cyl((x, -0.22, 2.7), 0.135, 0.06, R["red_paint"], seg, axis='X')
            b.cyl((x, -0.46, 2.88), 0.09, 0.05, R["steel"], seg, axis='X')
        b.cyl((0.2, -0.46, 2.88), 0.05, 0.2, R["steel"], 6, axis='Y')
        pts = [(0.2 + math.cos(i / 12 * 2 * math.pi) * 0.14, -0.58, 2.88 + math.sin(i / 12 * 2 * math.pi) * 0.14) for i in range(13)]
        b.tube(pts, 0.014, R["red_paint"], 4)
        b.vquad(-1.0, -0.4, 2.65, 2.75, -0.331, R["white_paint"])
    return b


def hazard_face(b, x0, x1, z0, z1, y):
    z = z0
    while z < z1 - 1e-4:
        zt = min(z1, z + 0.25)
        b.vquad(x0, x1, z, zt, y, R["hazard"])
        z = zt


def door_post(L):
    b = B(L)
    b.boxmm((-0.18, -0.2, 0), (0.18, 0, 4.0), R["steel"], skip=('+y',))
    hazard_face(b, -0.12, 0.12, 0.1, 3.9, -0.201)
    for s in (-1, 1):
        x0, x1 = sorted((s * 0.18, s * 0.2))
        b.boxmm((x0, -0.14, 0), (x1, -0.03, 4.0), R["rubber"], skip=('+y',))
    if L == 0:
        for z in (0.4, 2.0, 3.6):
            for sx in (-1, 1):
                b.cyl((sx * 0.15, -0.205, z), 0.018, 0.012, R["steel"], 6, axis='Y')
    return b


def door_header(w):
    def f(L):
        b = B(L)
        b.boxmm((-w / 2, -0.22, 0), (w / 2, 0, 0.45), R["steel"], skip=('+y',))
        b.vquad(-w / 2 + 0.05, w / 2 - 0.05, 0.1, 0.35, -0.221, R["hazard"])
        b.boxmm((-w / 2, -0.23, 0), (w / 2, -0.21, 0.04), R["rubber"])
        b.boxmm((-0.3, -0.24, 0.35), (0.3, -0.22, 0.42), R["amber_light"])
        return b
    return f


def cage_lamp(L):
    b = B(L)
    b.boxmm((-0.12, -0.04, -0.14), (0.12, 0, 0.14), R["steel"], skip=('+y',))
    b.cyl((0, -0.13, 0), 0.1, 0.18, R["graphite"], 12, axis='Y', caps=(False, False))
    b.cyl((0, -0.23, 0), 0.085, 0.02, R["white_light"], 12, axis='Y')
    if L == 0:
        for a in range(4):
            ang = a / 4 * 2 * math.pi + math.pi / 4
            p0 = (math.cos(ang) * 0.11, -0.04, math.sin(ang) * 0.11); p1 = (math.cos(ang) * 0.09, -0.28, math.sin(ang) * 0.09)
            b.rod(p0, p1, 0.007, R["steel"], 4)
        pts = [(math.cos(i / 10 * 2 * math.pi) * 0.09, -0.28, math.sin(i / 10 * 2 * math.pi) * 0.09) for i in range(11)]
        b.tube(pts, 0.007, R["steel"], 4)
    return b


def pendant(L):
    b = B(L)
    drop = 8.0
    b.boxmm((-3.0, -0.07, -drop - 0.12), (3.0, 0.07, -drop), R["graphite"], roles={'-z': R["white_light"]})
    for s in (-1, 1):
        x0, x1 = sorted((s * 3.0, s * 3.04))
        b.boxmm((x0, -0.072, -drop - 0.12), (x1, 0.072, -drop), R["red_light"])
        if L == 0:
            b.rod((s * 2.5, 0, -drop), (s * 2.5, 0, 0), 0.006, R["steel"], 4)
            b.cyl((s * 2.5, 0, -0.015), 0.06, 0.03, R["graphite"], 8)
    return b


# ------------------------------------------------------------------ catalogue: name, fn, lods, anchors
KIT = [
    ("WallPanel_A", wall_panel("A"), 2, []), ("WallPanel_B", wall_panel("B"), 2, []),
    ("WallPanel_Tall", wall_panel_tall, 2, []), ("CorridorPanel", corridor_panel, 2, []),
    ("ColumnClad", column_clad, 2, []),
    ("CeilingCoffer_Aisle", coffer(2.6, 3.0, 0.22, 0.18, [0.0]), 1, []),
    ("CeilingCoffer_Rear", coffer(2.9, 2.9, 0.22, 0.18, [0.0]), 1, []),
    ("CeilingCoffer_Nave", coffer(6.0, 5.8, 0.6, 0.35, [-1.6, 1.6], True), 1, []),
    ("CorridorCeiling", corridor_ceiling, 1, []),
    ("GlassBalustrade_2m", glass_balustrade, 2, []),
    ("FloorInlay_Emblem", emblem_inlay, 1, []), ("FloorInlay_Field3", flat(3.0, 3.0, A("stone_field")), 1, []),
    ("FloorInlay_Runner", flat(2.0, 3.6, A("runner")), 1, []), ("HazardFloorEdge", flat(2.6, 0.25, R["hazard"]), 1, []),
    ("SignWayfinding_Lift", wayfinding("sign_lift"), 1, []), ("SignWayfinding_Showroom", wayfinding("sign_showroom"), 1, []),
    ("SignWayfinding_Reception", wayfinding("sign_reception"), 1, []), ("SignWayfinding_Skydeck", wayfinding("sign_skydeck"), 1, []),
    ("SignFloor_1F", floor_sign("floor_1F"), 1, []), ("SignFloor_B4", floor_sign("floor_B4"), 1, []),
    ("SloganLightbox_1", slogan(1), 1, []), ("SloganLightbox_4", slogan(4), 1, []), ("SloganLightbox_7", slogan(7), 1, []),
    ("WarnPlate", warn_plate, 1, []),
    ("ElevatorPortal_Bank", portal_bank, 2, []), ("ElevatorPortal_Main", portal_main, 2, []),
    ("ReceptionDeskTop", desk_top, 1, []), ("ReceptionChair", chair, 2, []),
    ("WaitingBench", bench, 2, []), ("Planter_Low", planter_low, 2, []), ("Planter_Tall", planter_tall, 2, []),
    ("HoloInfoPillar", info_pillar, 2, [("LightAnchor", (0, 0, 2.28))]),
    ("SecurityScanArch", scan_arch, 2, []), ("SpeedGate_Pair", speed_gates, 2, []),
    ("WallScreen_Ad", wall_screen("screen_ad", 4.0, 2.25), 1, []), ("WallScreen_News", wall_screen("screen_news", 3.2, 1.8), 1, []),
    ("CleaningRobot", cleaning_robot, 1, []),
    ("CableTray_4m", cable_tray, 2, []), ("WallVent", wall_vent, 1, []), ("DuctRun_4m", duct_run, 2, []),
    ("PipeRun_4m", pipe_run, 2, []), ("B4_DoorPost", door_post, 2, []),
    ("B4_DoorHeader_3", door_header(3.0), 1, []), ("B4_DoorHeader_6", door_header(6.0), 1, []),
    ("LightRig_LinearPendant", pendant, 2, [("LightAnchor", (0, 0, -8.2))]),
    ("LightRig_CageLamp", cage_lamp, 1, [("LightAnchor", (0, -0.32, 0))]),
]


# ------------------------------------------------------------------ previews (scratchpad only)
def inst(name, loc, rz=0.0, scale=(1, 1, 1), lod=0):
    me = LIB[name]["lods"][min(lod, len(LIB[name]["lods"]) - 1)]
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = (0, 0, rz); ob.scale = scale
    return ob


def u2b(ux, uy, uz):
    return Vector((-ux, -uz, uy))


def uinst(name, ux, uy, uz, yaw, sx=1.0, sy=1.0):
    if name not in LIB:
        return None
    return inst(name, u2b(ux, uy, uz), -math.radians(yaw), (sx, 1.0, sy))


def flat_mat(name, rgb, rough=0.5, emit=0.0, alpha=1.0):
    m = bpy.data.materials.new(name)
    nt = T._nodes(m)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bs.outputs[0], out.inputs[0])
    bs.inputs["Base Color"].default_value = (*rgb, 1); bs.inputs["Roughness"].default_value = rough
    if emit:
        bs.inputs["Emission Color"].default_value = (*rgb, 1); bs.inputs["Emission Strength"].default_value = emit
    if alpha < 1:
        bs.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            pass
    return m


def ubox(umin, umax, mat):
    """Axis box from Unity min/max (preview stand-ins for existing scene geometry)."""
    (x0, y0, z0), (x1, y1, z1) = umin, umax
    me = bpy.data.meshes.new("ub")
    vs = [(-x, -z, y) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]
    fs = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    me.from_pydata(vs, [], fs); me.materials.append(mat)
    ob = bpy.data.objects.new("ub", me); bpy.context.scene.collection.objects.link(ob)
    return ob


def ucentre(c, s, mat):
    return ubox((c[0] - s[0] / 2, c[1] - s[1] / 2, c[2] - s[2] / 2), (c[0] + s[0] / 2, c[1] + s[1] / 2, c[2] + s[2] / 2), mat)


def shell(hall_mat):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "Assets", "Modeling", "lobby_object.fbx"))
    for o in [o for o in bpy.data.objects if o not in before]:
        if o.type == 'MESH':
            o.data.materials.clear(); o.data.materials.append(hall_mat)


def existing_dressing():
    """Stand-ins for Level_Facility objects measured from the scene (hall-local: y - 40)."""
    g = flat_mat("ex_graphite", (0.05, 0.052, 0.058), 0.4)
    red = flat_mat("ex_red", (1.0, 0.05, 0.05), 0.4, 6.0)
    amber = flat_mat("ex_amber", (1.0, 0.5, 0.06), 0.4, 5.0)
    white = flat_mat("ex_white", (1, 1, 1), 0.4, 6.0)
    glass = flat_mat("ex_glass", (0.02, 0.03, 0.04), 0.05, 0.0, 0.55)
    cer = flat_mat("ex_ceramic", (0.55, 0.55, 0.55), 0.5)
    holo = flat_mat("ex_holo", (0.25, 0.4, 0.5), 0.3, 0.5, 0.6)
    # entry corridor
    ucentre((0, -0.15, 26.65), (5.4, 0.3, 14.3), cer); ucentre((0, 3.75, 26.65), (5.4, 0.3, 14.3), cer)
    for sx in (-1, 1):
        ucentre((sx * 2.55, 1.8, 26.65), (0.3, 4.2, 14.3), cer)
        ucentre((sx * 2.39, 1.05, 26.65), (0.02, 0.04, 14.2), red)
        ucentre((sx * 1.85, 0.01, 26.2), (0.04, 0.02, 13.2), amber)
        for z in (21.5, 25.1, 28.7, 32.3):
            ucentre((sx * 2.3, 1.8, z), (0.2, 3.6, 0.22), g)
    ucentre((0, 1.8, 33.8), (5.4, 4.2, 0.3), cer)
    ucentre((0, 3.59, 26.4), (0.22, 0.03, 13.4), white)
    ucentre((-2.37, 1.95, 25.0), (0.01, 1.44, 4.6), holo); ucentre((2.37, 1.95, 29.0), (0.01, 1.3, 2.6), holo)
    # glass deck + frames + hazard
    ucentre((0, -0.04, 12), (10, 0.08, 12), glass)
    for c, s in (((0, -0.08, 18), (10.5, 0.2, 0.3)), ((0, -0.08, 6), (10.5, 0.2, 0.3)), ((-5, -0.08, 12), (0.3, 0.2, 12.3)), ((5, -0.08, 12), (0.3, 0.2, 12.3))):
        ucentre(c, s, g)
    for c, s in (((0, 0.01, 18.4), (10.9, 0.02, 0.05)), ((0, 0.01, 5.6), (10.9, 0.02, 0.05)), ((-5.4, 0.01, 12), (0.05, 0.02, 12.9)), ((5.4, 0.01, 12), (0.05, 0.02, 12.9))):
        ucentre(c, s, amber)
    ucentre((0, -10, 12), (10.6, 20, 12.6), flat_mat("ex_well", (0.03, 0.03, 0.035), 0.5))
    ucentre((0, -19.0, 12), (3.9, 0.4, 3.9), flat_mat("ex_beam", (1.0, 0.08, 0.06), 0.4, 8.0))
    # front desk accents + logo holo + core red edges
    ucentre((0, 1.13, -0.1), (5.6, 0.03, 0.02), white); ucentre((0, 0.78, 0.0), (5.6, 0.03, 0.02), red)
    ucentre((0, 0.05, -0.1), (5.6, 0.03, 0.02), red)
    ucentre((0, 5.3, -4.62), (3.6, 3.6, 0.01), flat_mat("ex_logo", (1.0, 0.1, 0.1), 0.3, 2.5, 0.8))
    for sx in (-1, 1):
        ucentre((sx * 3.0, 6.0, -4.36), (0.04, 9.0, 0.02), red)
    # showroom pods (stand-ins), wall light columns, aisle red skirts, leading lines
    for sx in (-1, 1):
        for z in (6.5, 12.5):
            ucentre((sx * 10.8, 0.09, z), (2.3, 0.18, 2.3), g)
            pod = bpy.data.objects.new("pod", bpy.data.meshes.new("pod"))
            import bmesh
            bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1.0)
            bm.to_mesh(pod.data); bm.free(); pod.data.materials.append(glass)
            bpy.context.scene.collection.objects.link(pod); pod.location = u2b(sx * 10.8, 1.5, z); pod.scale = (0.7, 0.7, 1.3)
        for z in (3.5, 9.5, 15.5):
            ucentre((sx * 11.96, 4.2, z), (0.04, 6.0, 0.1), white)
        ucentre((sx * 11.97, 0.12, 0), (0.03, 0.03, 38.6), red); ucentre((sx * 11.97, 7.6, 0), (0.03, 0.03, 38.6), red)
        ucentre((sx * 4.4, 0.01, -4.2), (0.05, 0.02, 19.6), amber)
        for z in (-4.5, -11.0):
            ucentre((sx * 11.88, 1.72, z), (0.1, 3.44, 2.44), g)
            ucentre((sx * 11.72, 3.8, z), (0.02, 0.07, 1.8), red)
    for x in (-6, 0, 6):
        ucentre((x, 19.85, 2.5), (0.35, 0.08, 30), white)
    # main lift: doors + header bars
    ucentre((0, 2.6, -18.6), (5.3, 5.2, 0.1), flat_mat("ex_door", (0.18, 0.18, 0.2), 0.3))
    ucentre((0, 5.75, -18.2), (6.0, 0.07, 0.07), white); ucentre((0, 5.6, -18.2), (6.0, 0.04, 0.02), amber)


def light(kind, loc, energy, color, aim=None, size=90):
    ld = bpy.data.lights.new("l", kind); ld.energy = energy; ld.color = color
    if kind == 'SPOT':
        ld.spot_size = math.radians(size); ld.spot_blend = 0.6
    if kind == 'AREA':
        ld.size = size
    o = bpy.data.objects.new("l", ld); bpy.context.scene.collection.objects.link(o); o.location = loc
    if aim is not None:
        o.rotation_euler = (Vector(aim) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    return o


def place_layout(only_frame="hall"):
    for s in slots():
        if s["frame"] != only_frame:
            continue
        p = s["p"]
        x, y, z = s["x"], s["y"], s["z"]
        yaw = s["yaw"]
        if s["kind"] == "wall":
            f = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
            x += f.x * s.get("off", 0.0); z += f.z * s.get("off", 0.0)
        if s["kind"] == "decal":
            y += 0.006
        uinst(p, x, y, z, yaw, s.get("sx", 1.0), s.get("sy", 1.0))
    if only_frame == "hall":
        for sx in (-1, 1):  # ElevatorBank_Inactive portals (computed from the lift doors by the editor tool)
            for zc in (-4.5, -11.0):
                uinst("ElevatorPortal_Bank", sx * 12.03, 0, zc, -90 * sx)


def render_shot(sc, prev, name, loc, tgt, lens, w=1600, h=900, exposure=0.0):
    for o in [o for o in sc.objects if o.type == 'CAMERA']:
        bpy.data.objects.remove(o, do_unlink=True)
    T.persp_cam(sc, u2b(*loc), u2b(*tgt), lens)
    T.setup_render(sc, w, h, 48, standard=False)
    sc.view_settings.exposure = exposure
    sc.render.filepath = os.path.join(prev, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("SHOT", name)


def interior_shots(prev):
    sc = bpy.context.scene
    T.clear_scene()
    T.world_color(sc, (0.16, 0.17, 0.2), 0.35)
    for attr, v in (("use_shadows", True), ("use_raytracing", True)):
        try:
            setattr(sc.eevee, attr, v)
        except Exception:
            pass
    shell(flat_mat("ex_hall", (0.36, 0.36, 0.37), 0.6))
    existing_dressing()
    place_layout("hall")
    W = (0.92, 0.95, 1.0)
    # existing scene lights (approximate), emissive kit does the rest
    light('POINT', u2b(0, 3.1, 30.5), 260, W); light('POINT', u2b(0, 3.1, 23.5), 260, W)
    for sx in (-1, 1):
        for z in (3.5, 9.5, 15.5):
            light('POINT', u2b(sx * 10.6, 4.2, z), 220, (1.0, 0.95, 0.88))
        for z in (6.5, 12.5):
            light('SPOT', u2b(sx * 8.2, 6.2, z), 900, W, u2b(sx * 10.8, 0, z), 50)
    light('POINT', u2b(0, 7.0, 4.0), 1600, W); light('POINT', u2b(0, 0.35, 1.0), 60, (1, 0.1, 0.1))
    light('POINT', u2b(-2.5, 4.6, -10.5), 900, (1.0, 0.9, 0.78)); light('POINT', u2b(6.5, 2.8, -8.5), 300, (0.6, 0.9, 1.0))
    light('POINT', u2b(0, 9.0, -12.0), 1400, W)
    for x in (-6, 0, 6):
        light('AREA', u2b(x, 19.5, 2.5), 3000, W, u2b(x, 0, 2.5), 4.0)
    light('POINT', u2b(0, -1.5, 12), 600, (1.0, 0.15, 0.1))
    shots = [("shot_corridor", (0.5, 1.75, 31.5), (0, 1.6, 15.0), 22),
             ("shot_lobby_deck", (-1.2, 2.2, 19.0), (0.5, 2.6, -2.0), 18),
             ("shot_showroom_aisle", (6.0, 1.9, 19.0), (11.0, 2.2, 4.0), 20),
             ("shot_exec_lift", (8.6, 1.9, -4.6), (0.5, 2.6, -18.5), 18)]
    for name, loc, tgt, lens in shots:
        render_shot(sc, prev, name, loc, tgt, lens, exposure=0.3)


def thumbnails(prev):
    tdir = os.path.join(prev, "thumbs"); os.makedirs(tdir, exist_ok=True)
    sc = bpy.context.scene
    for name, t in EXPORTED:
        T.clear_scene()
        ob = inst(name, (0, 0, 0))
        mn = Vector([min(Vector(c)[i] for c in ob.bound_box) for i in range(3)])
        mx = Vector([max(Vector(c)[i] for c in ob.bound_box) for i in range(3)])
        ctr = (mn + mx) / 2; size = max((mx - mn).length, 0.5)
        T.world_color(sc, (0.08, 0.085, 0.1), 1.0)
        sd = bpy.data.lights.new("key", 'SUN'); sd.energy = 2.4; sd.color = (1.0, 0.95, 0.9)
        so = bpy.data.objects.new("key", sd); sc.collection.objects.link(so); so.rotation_euler = (math.radians(50), 0, math.radians(-30))
        d = Vector((-0.6, -1.0, 0.45)).normalized()
        if name.startswith(("Ceiling", "Corridor_Ceiling", "CorridorCeiling", "LightRig_Linear", "CableTray")):
            d = Vector((-0.6, -1.0, -0.55)).normalized()
        if name.startswith(("FloorInlay", "HazardFloor")):
            d = Vector((0.0, -0.35, 1.0)).normalized()
        T.persp_cam(sc, ctr + d * size * 1.5, ctr, lens=50)
        T.setup_render(sc, 360, 360, 16, standard=False)
        sc.render.filepath = os.path.join(tdir, name + ".png")
        bpy.ops.render.render(write_still=True)
    with open(os.path.join(prev, "lfd_stats.json"), "w") as f:
        json.dump({n: t for n, t in EXPORTED}, f, indent=1)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    OUT = os.path.abspath(argv[0]) if len(argv) > 0 and argv[0] not in ("", "-") else os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Level_Facility", "Detail")
    PREV = os.path.abspath(argv[1]) if len(argv) > 1 and argv[1] not in ("", "-") else None
    os.makedirs(os.path.join(OUT, "Meshes"), exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    only = os.environ.get("LFD_ONLY", "")
    for name, fn, nl, anchors in KIT:
        if not only or name in only.split(","):
            export(name, [fn(l) for l in range(nl)], anchors)
    write_layout(os.path.join(OUT, "LFD_Layout.json"))
    print("KIT", json.dumps(EXPORTED))
    print("TOTAL_LOD0_TRIS", sum(t[0] for _, t in EXPORTED))
    if PREV and not os.environ.get("LFD_NOPREV"):
        os.makedirs(PREV, exist_ok=True)
        if not os.environ.get("LFD_NOTHUMBS"):
            thumbnails(PREV)
        interior_shots(PREV)
