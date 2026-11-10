"""Infiltration tutorial DETAIL kit (iteration 16) -- Blender 5.x headless.

  python Tools/ArtGen/tutorial_detail_textures.py            # 1) trim sheet / window atlas / puddles
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b -P Tools/ArtGen/tutorial_detail_blender.py -- [out_dir] [preview_dir]

Writes Assets/Art/ChuonPolish/Tutorial/Detail/Meshes/SM_INFD_*.fbx (+ Textures/T_INFD_Impostor_{Albedo,Emission}.png).
Same conventions as tutorial_city_blender.py: origin = base centre, front = -Y in Blender (Unity +Z local before yaw),
export axes identical, so detail buildings drop onto the plain kit transforms 1:1 (BlockA..D footprints/heights match).
Multi-LOD assets contain <name>_LOD0/_LOD1[/_LOD2] meshes; empties (LightAnchor*, etc.) are top-level.

Material slots (remapped by Assets/Editor/TutorialDetailPass.cs):
  INFD_Trim (shared trim sheet: strips wrap in U, 256 px/m)  INFD_Windows (8x8 interior atlas, emissive)
  INF_Signs (existing slogan atlas, same cell rects as InfiltrationMapBuilder.SignCell)  INFD_Impostor  INFD_Puddle
Faces carry an int 'role' while building; UVs + slots are derived from it at the end (see ROLE ids below).
Style: TSUKASA corporate graphite / off-white ceramic / thin red light lines; dusk office light, not neon.
"""
import bpy, bmesh, math, os, sys, json, random, zlib, struct
from mathutils import Vector, Matrix, Euler
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
TUT = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Tutorial")

# ---- must match tutorial_detail_textures.py
TRIM = [("ceramic", 0, 256), ("graph_rib", 256, 384), ("graphite", 384, 512), ("steel", 512, 640),
        ("concrete", 640, 768), ("louvre", 768, 832), ("red_light", 832, 896), ("white_light", 896, 960),
        ("amber_light", 960, 1024), ("rubber", 1024, 1088), ("hazard", 1088, 1152), ("dark_glass", 1152, 1216),
        ("cardboard", 1216, 1280), ("white_paint", 1280, 1344), ("red_paint", 1344, 1408), ("corrugated", 1408, 1536)]
CELLS = [("cell_vending", 0), ("cell_fan", 512), ("cell_emblem", 1024), ("cell_panel", 1536)]
WIN_ROWS = ["dddddddd", "ddddSSCC", "wwwwwwww", "cccccccc", "BBBBbbbb", "aaaaaaaa", "TTTTqqqq", "ddddPPPP"]
R = {n: i for i, (n, _, _) in enumerate(TRIM)}
for i, (n, _) in enumerate(CELLS):
    R[n] = 16 + i
WIN0, SIGN0, IMP_F, IMP_S, IMP_T, PUD0 = 100, 200, 300, 301, 302, 400
SLOTS = ["INFD_Trim", "INFD_Windows", "INF_Signs", "INFD_Impostor", "INFD_Puddle"]
TS = 2048.0
IMP_W, IMP_H = 2048, 2048  # 4 x 2 cells of 512 x 1024 (front 256 | side 256)


def slot_of(role):
    if role >= PUD0: return 4
    if role >= IMP_F: return 3
    if role >= SIGN0: return 2
    if role >= WIN0: return 1
    return 0


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = os.path.abspath(argv[0]) if len(argv) > 0 and argv[0] not in ("", "-") else os.path.join(TUT, "Detail")
    prev = os.path.abspath(argv[1]) if len(argv) > 1 and argv[1] not in ("", "-") else None
    os.makedirs(os.path.join(out, "Meshes"), exist_ok=True)
    os.makedirs(os.path.join(out, "Textures"), exist_ok=True)
    return out, prev


# ------------------------------------------------------------------ window cell picking
FAMILY = {
    "office": [("d", 30), ("w", 26), ("c", 22), ("B", 9), ("b", 9), ("C", 4)],
    "res": [("d", 30), ("a", 34), ("q", 14), ("T", 10), ("B", 12)],
    "srv": [("d", 40), ("S", 18), ("C", 14), ("c", 28)],
    "shop": [("P", 100)],
}
KIND_CELLS = {}
for _r, _row in enumerate(WIN_ROWS):
    for _c, _k in enumerate(_row):
        KIND_CELLS.setdefault(_k, []).append(_r * 8 + _c)


def win_role(rng, family, lit_bias=0.0):
    items = FAMILY[family]
    tot = sum(w for _, w in items)
    x = rng.random() * tot
    if lit_bias and rng.random() < lit_bias:
        x = rng.random() * (tot - items[0][1]) + items[0][1]
    for k, w in items:
        x -= w
        if x <= 0:
            return WIN0 + rng.choice(KIND_CELLS[k])
    return WIN0 + KIND_CELLS[items[-1][0]][0]


# ------------------------------------------------------------------ mesh builder (one bmesh per LOD object)
class B:
    def __init__(self, lod=0):
        self.bm = bmesh.new()
        self.rl = self.bm.faces.layers.int.new("role")
        self.M = Matrix.Identity(4)
        self.stack = []
        self.lod = lod
        self.imp = None
        self.roof_boxes = []

    @property
    def L0(self):
        return self.lod == 0

    def seg(self, n):
        return max(4, n if self.lod == 0 else n // 2)

    def push(self, M):
        self.stack.append(self.M); self.M = self.M @ M

    def pop(self):
        self.M = self.stack.pop()

    def _face(self, vs, role, expect, smooth=False):
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        f[self.rl] = role
        f.normal_update()
        if expect is not None and f.normal.dot(expect) < 0:
            f.normal_flip()
        f.smooth = smooth
        return f

    def box(self, c, s, role, rot=None, skip=(), roles=None):
        """Box centre c, size s. role: trim role id; roles: per-face overrides {'+x':id,...}; skip: faces to omit."""
        M = self.M @ Matrix.Translation(c)
        if rot is not None:
            M = M @ Euler(rot).to_matrix().to_4x4()
        R3 = M.to_3x3()
        h = (s[0] / 2, s[1] / 2, s[2] / 2)
        V = {}
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    V[(sx, sy, sz)] = self.bm.verts.new(M @ Vector((sx * h[0], sy * h[1], sz * h[2])))
        F = {'+x': (0, 1), '-x': (0, -1), '+y': (1, 1), '-y': (1, -1), '+z': (2, 1), '-z': (2, -1)}
        for key, (ax, sg) in F.items():
            if key in skip:
                continue
            others = [a for a in range(3) if a != ax]
            pts = []
            for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                k = [0, 0, 0]; k[ax] = sg; k[others[0]] = a; k[others[1]] = b
                pts.append(V[tuple(k)])
            d = Vector((0, 0, 0)); d[ax] = sg
            self._face(pts, (roles or {}).get(key, role), R3 @ d)

    def boxz(self, base, s, role, **kw):
        self.box((base[0], base[1], base[2] + s[2] / 2), s, role, **kw)

    def boxmm(self, mn, mx, role, **kw):
        self.box(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2),
                 (mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]), role, **kw)

    def quad(self, pts, role, normal):
        M = self.M
        vs = [self.bm.verts.new(M @ Vector(p)) for p in pts]
        self._face(vs, role, M.to_3x3() @ Vector(normal))

    def vquad(self, x0, x1, z0, z1, y, role, facing=-1):
        """Vertical quad in the XZ plane at y, facing -Y (facing=-1) or +Y."""
        self.quad([(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], role, (0, facing, 0))

    def cyl(self, c, r, h, role, seg=12, axis='Z', r2=None, caps=(True, True), cap_role=None, rot=None, smooth=True):
        seg = self.seg(seg)
        M = self.M @ Matrix.Translation(c)
        if axis == 'X':
            M = M @ Matrix.Rotation(math.pi / 2, 4, 'Y')
        elif axis == 'Y':
            M = M @ Matrix.Rotation(-math.pi / 2, 4, 'X')
        if rot is not None:
            M = M @ Euler(rot).to_matrix().to_4x4()
        R3 = M.to_3x3()
        r2 = r if r2 is None else r2
        bot, top = [], []
        for i in range(seg):
            a = i / seg * 2 * math.pi
            ca, sa = math.cos(a), math.sin(a)
            bot.append(self.bm.verts.new(M @ Vector((ca * r, sa * r, -h / 2))))
            top.append(self.bm.verts.new(M @ Vector((ca * r2, sa * r2, h / 2))))
        for i in range(seg):
            j = (i + 1) % seg
            a = (i + 0.5) / seg * 2 * math.pi
            self._face([bot[i], bot[j], top[j], top[i]], role, R3 @ Vector((math.cos(a), math.sin(a), 0)), smooth)
        if caps[0]:
            self._face(list(reversed(bot)), cap_role if cap_role is not None else role, R3 @ Vector((0, 0, -1)))
        if caps[1]:
            self._face(list(top), cap_role if cap_role is not None else role, R3 @ Vector((0, 0, 1)))

    def rod(self, p0, p1, r, role, seg=6):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        self.push(Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4())
        self.cyl((0, 0, 0), r, d.length, role, seg, caps=(False, False))
        self.pop()

    def tube(self, pts, r, role, seg=5):
        """Swept tube along a polyline (cables)."""
        seg = self.seg(seg)
        rings = []
        for i, p in enumerate(pts):
            p = Vector(p)
            t = (Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
            n = t.cross(Vector((0, 0, 1)))
            if n.length < 1e-4:
                n = Vector((1, 0, 0))
            n.normalize(); b = t.cross(n)
            ring = []
            for k in range(seg):
                a = k / seg * 2 * math.pi
                ring.append(self.bm.verts.new(self.M @ (p + (n * math.cos(a) + b * math.sin(a)) * r)))
            rings.append((ring, p, n, b))
        R3 = self.M.to_3x3()
        for i in range(len(rings) - 1):
            ra, pa, na, ba = rings[i]; rb = rings[i + 1][0]
            for k in range(seg):
                j = (k + 1) % seg
                a = (k + 0.5) / seg * 2 * math.pi
                self._face([ra[k], ra[j], rb[j], rb[k]], role, R3 @ (na * math.cos(a) + ba * math.sin(a)), True)

    def lump(self, c, s, role, seed):
        """Trash-bag like lump: subdivided cube (24 quads) pushed toward a sphere + jitter."""
        rng = random.Random(seed)
        bmt = bmesh.new()
        bmesh.ops.create_cube(bmt, size=2.0)
        bmesh.ops.subdivide_edges(bmt, edges=bmt.edges[:], cuts=1, use_grid_fill=True)
        for v in bmt.verts:
            v.co = v.co.normalized() * (0.85 + rng.random() * 0.25)
            v.co.z = max(v.co.z, -0.55)
        M = self.M @ Matrix.Translation(c) @ Matrix.Diagonal((s[0] / 2, s[1] / 2, s[2] / 2, 1))
        vmap = {v: self.bm.verts.new(M @ v.co) for v in bmt.verts}
        for f in bmt.faces:
            f.normal_update()
            self._face([vmap[v] for v in f.verts], role, M.to_3x3() @ f.normal, True)
        bmt.free()

    # ------------------------------------------------------------------ finalize
    def build(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        apply_uv_mats(ob, self.imp)
        me.use_fake_user = True
        return ob


def strip_rect(role):
    n, y0, y1 = TRIM[role]
    return (0.0, 1 - y1 / TS, 1.0, 1 - y0 / TS, (y1 - y0) / 256.0)


def rect_for(role):
    if role >= PUD0:
        i = role - PUD0
        return ((i % 2) * 0.5, 0.5 - (i // 2) * 0.5, 0.5, 0.5)
    if role >= SIGN0:
        i = role - SIGN0
        return ((i % 4) * 0.25, (1 - 300 / 1024) if i < 4 else (1 - 812 / 1024), 0.25, 280 / 1024)
    if role >= WIN0:
        k = role - WIN0; col, row = k % 8, k // 8
        return (col / 8, 1 - (row + 1) / 8, 1 / 8, 1 / 8)
    i = role - 16
    return (CELLS[i][1] / TS, 0.0, 512 / TS, 512 / TS)


def apply_uv_mats(ob, imp):
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
    pad = 2.0 / TS
    for p, role in zip(me.polygons, roles):
        p.material_index = remap[slot_of(role)]
        n = p.normal
        cos = [me.vertices[me.loops[li].vertex_index].co for li in p.loop_indices]
        if role >= IMP_F and role < PUD0:
            cell, w, d, hv, rz = imp
            cx0 = (cell % 4) * 512; cy0 = (cell // 4) * 1024
            vb = 1 - (cy0 + 1024) / IMP_H
            for li, c in zip(p.loop_indices, cos):
                if role == IMP_T:
                    u = (cx0 + 128) / IMP_W; v = vb + (2 + 1020 * min(0.995, max(0.0, (rz - 0.4) / hv))) / IMP_H
                elif role == IMP_F:
                    u = (cx0 + 2 + (c.x + w / 2) / w * 252) / IMP_W; v = vb + (2 + min(1, max(0, c.z / hv)) * 1020) / IMP_H
                else:
                    u = (cx0 + 258 + (c.y + d / 2) / d * 252) / IMP_W; v = vb + (2 + min(1, max(0, c.z / hv)) * 1020) / IMP_H
                uvl[li].uv = (u, v)
            continue
        if abs(n.z) > 0.7:
            right = Vector((1, 0, 0)); tv = Vector((0, 1 if n.z > 0 else -1, 0))
        else:
            f = -n
            right = f.cross(up).normalized(); tv = right.cross(f).normalized()
        us = [c.dot(right) for c in cos]; vs = [c.dot(tv) for c in cos]
        umin, umax, vmin, vmax = min(us), max(us), min(vs), max(vs)
        if role < 16:
            x0, vb, x1, vt, hs = strip_rect(role)
            hv = vmax - vmin
            for li, uu, vv in zip(p.loop_indices, us, vs):
                if hv <= hs:
                    t = (vv - vmin + (hs - hv) / 2) / hs
                else:
                    t = (vv - vmin) / max(hv, 1e-6)
                uvl[li].uv = (uu / 8.0, vb + pad + t * (vt - vb - 2 * pad))
        else:
            rx, ry, rw, rh = rect_for(role)
            pu, pv = rw * 0.01, rh * 0.01
            for li, uu, vv in zip(p.loop_indices, us, vs):
                tu = (uu - umin) / max(umax - umin, 1e-6); tv_ = (vv - vmin) / max(vmax - vmin, 1e-6)
                uvl[li].uv = (rx + pu + tu * (rw - 2 * pu), ry + pv + tv_ * (rh - 2 * pv))


# ------------------------------------------------------------------ materials (FBX names + Blender preview shading)
MATS = {}
TEX = {}


def tex(path, noncolor=False):
    key = (path, noncolor)
    if key not in TEX:
        img = bpy.data.images.load(path, check_existing=True)
        if noncolor:
            img.colorspace_settings.name = 'Non-Color'
        TEX[key] = img
    return TEX[key]


def _nodes(m):
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    nt.nodes.clear()
    return nt


def _img(nt, path, noncolor=False, interp='Linear'):
    n = nt.nodes.new("ShaderNodeTexImage"); n.image = tex(path, noncolor); n.interpolation = interp
    return n


def tex_path(name):
    for d in (os.path.join(OUT, "Textures"), os.path.join(TUT, "Textures")):
        p = os.path.join(d, name)
        if os.path.exists(p):
            return p
    return None


MAT_TEX = {"INFD_Trim": ("T_INFD_Trim", 4.0), "INFD_Windows": ("T_INFD_Windows", 1.5), "INF_Signs": ("T_INF_Signs", 1.7),
           "INFD_Impostor": ("T_INFD_Impostor", 1.5), "INFD_Puddle": ("T_INFD_Puddle", 0.0)}


def make_lit(name, m=None):
    m = m or bpy.data.materials.new(name)
    base, emi = MAT_TEX[name]
    nt = _nodes(m)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bs.outputs[0], out.inputs[0])
    a = tex_path(base + "_Albedo.png")
    if a:
        ta = _img(nt, a); nt.links.new(ta.outputs["Color"], bs.inputs["Base Color"])
        if name == "INFD_Puddle":
            nt.links.new(ta.outputs["Alpha"], bs.inputs["Alpha"])
            try:
                m.surface_render_method = 'DITHERED'
            except Exception:
                m.blend_method = 'CLIP'
    mk = tex_path(base + "_Mask.png")
    if mk:
        tm = _img(nt, mk, True); sep = nt.nodes.new("ShaderNodeSeparateColor"); inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        nt.links.new(tm.outputs["Color"], sep.inputs[0]); nt.links.new(sep.outputs[0], bs.inputs["Metallic"])
        nt.links.new(tm.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], bs.inputs["Roughness"])
    else:
        bs.inputs["Roughness"].default_value = 0.12 if name == "INFD_Windows" else 0.6
    nm = tex_path(base + "_Normal.png")
    if nm:
        tn = _img(nt, nm, True); nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(tn.outputs["Color"], nmap.inputs["Color"]); nt.links.new(nmap.outputs[0], bs.inputs["Normal"])
    e = tex_path(base + "_Emission.png")
    if e and emi > 0:
        te = _img(nt, e); nt.links.new(te.outputs["Color"], bs.inputs["Emission Color"])
        bs.inputs["Emission Strength"].default_value = emi
    return m


def make_flat(name, mode):
    """mode 'alb': unlit albedo x AO (impostor albedo pass); 'emi': emission only."""
    m = bpy.data.materials.new(mode.upper() + "_" + name)
    base, emi = MAT_TEX[name]
    nt = _nodes(m)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(em.outputs[0], out.inputs[0])
    src = tex_path(base + ("_Albedo.png" if mode == "alb" else "_Emission.png"))
    if src is None:
        em.inputs[1].default_value = 0.0
        return m
    t = _img(nt, src)
    if mode == "alb":
        try:
            ao = nt.nodes.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 1.2
            mul = nt.nodes.new("ShaderNodeMixRGB"); mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 0.85
            nt.links.new(t.outputs["Color"], mul.inputs[1]); nt.links.new(ao.outputs["AO"], mul.inputs[2])
            nt.links.new(mul.outputs[0], em.inputs[0])
        except Exception:
            nt.links.new(t.outputs["Color"], em.inputs[0])
        em.inputs[1].default_value = 1.0
    else:
        nt.links.new(t.outputs["Color"], em.inputs[0])
        em.inputs[1].default_value = {"INFD_Trim": 2.6, "INFD_Windows": 1.0, "INF_Signs": 1.2}.get(name, 0.0)
    return m


def get_mat(name):
    if name not in MATS:
        MATS[name] = make_lit(name)
    return MATS[name]


def set_mode(objs, mode):
    for o in objs:
        if o.type != 'MESH':
            continue
        for s in o.material_slots:
            if s.material is None:
                continue
            base = s.material.name.split("_", 1)[1] if s.material.name.startswith(("ALB_", "EMI_")) else s.material.name
            if mode == "lit":
                s.material = get_mat(base)
            else:
                key = mode + ":" + base
                if key not in MATS:
                    MATS[key] = make_flat(base, mode)
                s.material = MATS[key]


# ------------------------------------------------------------------ export bookkeeping
EXPORTED = []   # (name, [tris per LOD])
LIB = {}        # name -> {"lods": [mesh], "anchors": [(name, loc)]}


def clear_scene():
    for o in list(bpy.context.scene.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def tris(me):
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def export(name, builders, anchors=()):
    objs = []
    multi = len(builders) > 1
    for i, b in enumerate(builders):
        objs.append(b.build("SM_INFD_%s_LOD%d" % (name, i) if multi else "SM_INFD_" + name))
    for an, loc in anchors:
        e = bpy.data.objects.new(an, None); e.location = loc
        bpy.context.scene.collection.objects.link(e); objs.append(e)
    path = os.path.join(OUT, "Meshes", "SM_INFD_" + name + ".fbx")
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
    t = [tris(o.data) for o in objs if o.type == 'MESH']
    print("EXPORTED %-26s tris=%s" % (name, t))
    EXPORTED.append((name, t))
    LIB[name] = {"lods": [o.data for o in objs if o.type == 'MESH'], "anchors": list(anchors)}
    clear_scene()


# ------------------------------------------------------------------ facades (canonical: x in [-W/2, W/2], front plane
# y = 0 facing -Y, interior +Y, z absolute). Returns nothing; adds to builder b.
FH = 3.6


def facade(b, style, W, z0, z1, rng, detail=True, family=None):
    nfl = max(1, int((z1 - z0) / FH))
    fh = (z1 - z0) / nfl
    if style == "OFF":
        nb = max(2, round(W / 3.0)); bw = W / nb
        fam = family or "office"
        for f in range(nfl):
            zf = z0 + f * fh
            b.boxmm((-W / 2, 0.05, zf), (W / 2, 0.5, zf + 1.0), R["graphite"], skip=('+y',))
            band = detail and f % 6 == 5
            if band:
                b.boxmm((-W / 2 - 0.02, -0.25, zf), (W / 2 + 0.02, 0.5, zf + 0.6), R["ceramic"], skip=('+y',))
                b.boxmm((-W / 2, -0.27, zf - 0.07), (W / 2, -0.2, zf - 0.01), R["white_light"], skip=('+y',))
            for i in range(nb):
                x0 = -W / 2 + i * bw
                b.vquad(x0, x0 + bw, zf + 1.0, zf + fh, 0.5, win_role(rng, fam))
                if detail:
                    b.boxmm((x0 + 0.08, 0.32, zf + fh - 0.85), (x0 + bw - 0.08, 0.5, zf + fh - 0.75), R["graphite"], skip=('+y', '-z'))
        for i in range(nb + 1):
            x = -W / 2 + i * bw
            fin = detail and i % 3 == 0 and 0 < i < nb
            if fin:
                b.boxmm((x - 0.13, -0.75, z0), (x + 0.13, 0.5, z1), R["ceramic"], skip=('+y', '-z'))
            else:
                b.boxmm((x - 0.08, -0.1, z0), (x + 0.08, 0.5, z1), R["graphite"], skip=('+y', '-z'))
    elif style in ("CER", "CERW"):
        mat = R["ceramic"] if style == "CER" else R["white_paint"]
        nb = max(2, round(W / 3.2)); bw = W / nb
        fam = family or ("office" if style == "CER" else "res")
        pw = 0.8 if style == "CER" else 0.55
        for f in range(nfl):
            zf = z0 + f * fh
            sp = 1.3 if style == "CER" else 1.0
            special = detail and style == "CER" and f % 8 == 7
            b.boxmm((-W / 2, 0.0, zf), (W / 2, 0.6, zf + sp), R["graphite"] if special else mat, skip=('+y',))
            if special:
                b.boxmm((-W / 2, -0.04, zf + sp * 0.5 - 0.05), (W / 2, 0.0, zf + sp * 0.5 + 0.05), R["red_light"], skip=('+y',))
            b.boxmm((-W / 2, 0.0, zf + fh - 0.25), (W / 2, 0.6, zf + fh), mat, skip=('+y',))
            for i in range(nb):
                x0 = -W / 2 + i * bw + pw / 2; x1 = x0 + bw - pw
                b.vquad(x0, x1, zf + sp, zf + fh - 0.25, 0.6, win_role(rng, fam))
                if detail:
                    xm = (x0 + x1) / 2
                    b.boxmm((xm - 0.04, 0.5, zf + sp), (xm + 0.04, 0.6, zf + fh - 0.25), R["graphite"], skip=('+y', '-z', '+z'))
                    b.boxmm((x0, 0.3, zf + sp - 0.06), (x1, 0.6, zf + sp), R["steel"], skip=('+y', '-z'))
                    if rng.random() < 0.18:
                        cx = (x0 + x1) / 2 + (rng.random() - 0.5) * 0.6
                        b.boxmm((cx - 0.43, -0.45, zf + 0.3), (cx + 0.43, 0.0, zf + 0.9), R["white_paint"], skip=('+y',), roles={'-y': R["cell_fan"]})
                        b.boxmm((cx - 0.38, -0.4, zf + 0.22), (cx - 0.33, 0.0, zf + 0.3), R["steel"], skip=('+y',))
                        b.boxmm((cx + 0.33, -0.4, zf + 0.22), (cx + 0.38, 0.0, zf + 0.3), R["steel"], skip=('+y',))
        for i in range(nb + 1):
            x = -W / 2 + i * bw
            b.boxmm((max(-W / 2, x - pw / 2), -0.02, z0), (min(W / 2, x + pw / 2), 0.6, z1), mat, skip=('+y', '-z'))
    elif style == "RES":
        nb = max(2, round(W / 3.0)); bw = W / nb
        for f in range(nfl):
            zf = z0 + f * fh
            b.boxmm((-W / 2, -1.5, zf), (W / 2, 0.3, zf + 0.22), R["concrete"], skip=('+y',))
            b.boxmm((-W / 2, 0.0, zf + fh - 0.3), (W / 2, 0.3, zf + fh), R["white_paint"], skip=('+y',))
            for i in range(nb):
                x0 = -W / 2 + i * bw
                b.vquad(x0 + 0.25, x0 + bw - 0.25, zf + 0.22, zf + fh - 0.3, 0.3, win_role(rng, "res"))
                if detail:
                    b.boxmm((x0 + 0.05, -1.47, zf + 0.22), (x0 + bw - 0.05, -1.43, zf + 1.2), R["dark_glass"], skip=('-z',))
                    b.boxmm((x0, -1.5, zf + 1.2), (x0 + bw, -1.4, zf + 1.28), R["steel"])
                    if rng.random() < 0.45:
                        cx = x0 + 0.6 + rng.random() * (bw - 1.2)
                        b.boxmm((cx - 0.4, -1.0, zf + 0.22), (cx + 0.4, -0.6, zf + 0.82), R["white_paint"], roles={'-y': R["cell_fan"]}, skip=('-z',))
                    elif rng.random() < 0.25:
                        b.boxmm((x0 + 0.3, -0.7, zf + 0.22), (x0 + 0.9, -0.2, zf + 1.1), R["cardboard"], skip=('-z',))
            for i in range(0, nb + 1, 2):
                x = -W / 2 + i * bw
                b.boxmm((x - 0.08, -1.5, zf + 0.22), (x + 0.08, 0.3, zf + fh), R["concrete"], skip=('+y', '-z', '+z'))
        for i in range(nb + 1):
            x = -W / 2 + i * bw
            b.boxmm((max(-W / 2, x - 0.25), 0.0, z0), (min(W / 2, x + 0.25), 0.3, z1), R["white_paint"], skip=('+y', '-z'))
            if detail and i % 2 == 0 and 0 < i < nb and i % 4 == 2:
                b.cyl((x + 0.2, -1.42, (z0 + z1) / 2), 0.06, z1 - z0, R["steel"], 6, caps=(False, False))
    elif style == "SRV":
        nb = max(2, round(W / 3.0)); bw = W / nb
        for f in range(nfl):
            zf = z0 + f * fh
            b.boxmm((-W / 2, 0.0, zf), (W / 2, 0.25, zf + 1.6), R["corrugated"], skip=('+y',))
            b.boxmm((-W / 2, -0.1, zf + 3.0), (W / 2, 0.25, zf + fh), R["louvre"], skip=('+y',))
            for i in range(nb):
                x0 = -W / 2 + i * bw
                b.vquad(x0, x0 + bw, zf + 1.6, zf + 3.0, 0.25, win_role(rng, "srv"))
                b.boxmm((x0 - 0.05, 0.12, zf + 1.6), (x0 + 0.05, 0.25, zf + 3.0), R["steel"], skip=('+y', '-z', '+z'))
                if detail and rng.random() < 0.12:
                    cx = x0 + bw / 2
                    b.vquad(cx - 0.3, cx + 0.3, zf + 3.0, zf + 3.6, -0.11, R["cell_fan"])
            if detail and f % 3 == 0:
                for k, zz in enumerate((zf + 1.25, zf + 1.45)):
                    b.cyl((0, -0.25 - k * 0.28, zz), 0.1 + k * 0.03, W, R["steel"], 8, axis='X', caps=(False, False))
        if detail:
            for xx, s in ((-W * 0.3, 0.7), (W * 0.27, 0.45)):
                b.boxmm((xx - s / 2, -s - 0.1, z0), (xx + s / 2, -0.1, z1 + 0.6), R["steel"], skip=('+y', '-z'))


def corner(b, style, x, y, z0, z1, sx, sy):
    """Corner piece at footprint corner (x, y), sx/sy = outward signs."""
    if style == "OFF":
        c = 1.2
        b.boxmm((x - (c if sx > 0 else 0), y - (c if sy > 0 else 0), z0), (x + (0 if sx > 0 else c), y + (0 if sy > 0 else c), z1 + 0.6), R["ceramic"], skip=('-z',))
        b.boxmm((x + sx * 0.02 - 0.09, y + sy * 0.02 - 0.09, z0 + 1.0), (x + sx * 0.02 + 0.09, y + sy * 0.02 + 0.09, z1 - 0.5), R["red_light"], skip=('-z',))
    elif style in ("CER", "CERW"):
        c = 0.9
        b.boxmm((x - (c if sx > 0 else 0), y - (c if sy > 0 else 0), z0), (x + (0 if sx > 0 else c), y + (0 if sy > 0 else c), z1 + 0.3), R["graphite"], skip=('-z',))
        b.boxmm((x + sx * 0.03 - 0.05, y + sy * 0.03 - 0.05, z0 + 0.5), (x + sx * 0.03 + 0.05, y + sy * 0.03 + 0.05, z1 - 0.3), R["white_light"], skip=('-z',))
    elif style == "RES":
        c = 0.6
        b.boxmm((x - (c if sx > 0 else 0), y - (c if sy > 0 else 0), z0), (x + (0 if sx > 0 else c), y + (0 if sy > 0 else c), z1 + 0.2), R["concrete"], skip=('-z',))
    else:
        c = 0.5
        b.boxmm((x - (c if sx > 0 else 0), y - (c if sy > 0 else 0), z0), (x + (0 if sx > 0 else c), y + (0 if sy > 0 else c), z1 + 0.2), R["steel"], skip=('-z',))
    return c


CORNER_SIZE = {"OFF": 1.2, "CER": 0.9, "CERW": 0.9, "RES": 0.6, "SRV": 0.5}
SIDE_STYLE = {"OFF": "OFF", "CER": "CER", "RES": "CERW", "SRV": "SRV"}
FACES = [(-1, 0), (1, 90), (2, 180), (3, -90)]  # (index, rotation deg) front, +X, back, -X


def body(b, style, w, d, z0, z1, rng, family=None):
    """Four facades + corners around a w x d footprint centred at origin."""
    c = CORNER_SIZE[style]
    for idx, rot in FACES:
        if rot in (0, 180):
            W, off = w, d / 2
        else:
            W, off = d, w / 2
        st = style if rot in (0, 180) else SIDE_STYLE[style]
        if style == "RES" and rot == 180:
            st = "CERW"
        a = math.radians(rot)
        M = Matrix.Rotation(a, 4, 'Z') @ Matrix.Translation((0, -off, 0))
        b.push(M)
        facade(b, st, W - 2 * c, z0, z1, rng, detail=(rot != 180), family=family)
        b.pop()
    for sx in (-1, 1):
        for sy in (-1, 1):
            corner(b, style, sx * w / 2, sy * d / 2, z0, z1, sx, sy)
    # hidden core cap so the roof reads solid
    b.boxmm((-w / 2 + c * 0.5, -d / 2 + c * 0.5, z1 - 0.3), (w / 2 - c * 0.5, d / 2 - c * 0.5, z1), R["concrete"], skip=('-z',))


def storefront(b, w, d, rng, sign_cell=None):
    H = 5.0
    yf = -d / 2
    b.boxmm((-w / 2 + 0.3, yf + 0.8, 0), (w / 2 - 0.3, d / 2 - 0.3, H), R["graph_rib"], roles={'-y': R["graphite"]})
    ns = max(1, round((w - 1) / 7.0)); sw = w / ns
    shutter_i = rng.randrange(ns) if ns > 1 else -1
    for i in range(ns):
        x0 = -w / 2 + i * sw
        g0, g1 = x0 + 0.35, x0 + sw - 0.35
        b.vquad(g0, g1, 0.25, 3.6, yf + 0.79, WIN0 + rng.choice(KIND_CELLS["P"]))
        b.boxmm((g0, yf + 0.6, 0), (g1, yf + 0.8, 0.25), R["graphite"], skip=('+y', '-z'))
        for k in range(1, 3):
            xm = g0 + (g1 - g0) * k / 3
            b.boxmm((xm - 0.04, yf + 0.68, 0.25), (xm + 0.04, yf + 0.8, 3.6), R["graphite"], skip=('+y', '-z', '+z'))
        if i == shutter_i:
            for k in range(10):
                z = 3.6 - (k + 1) * 0.25
                b.vquad(g0, g1, z, z + 0.25, yf + 0.7, R["louvre"])
        if sign_cell is not None and i == ns // 2:
            sw_ = min(g1 - g0 - 0.4, 2.2); sh = sw_ / 1.83
            b.vquad(-sw_ / 2 + (g0 + g1) / 2, sw_ / 2 + (g0 + g1) / 2, 4.4 - sh / 2, 4.4 + sh / 2, yf + 0.08, SIGN0 + sign_cell)
        else:
            b.boxmm((g0 + 0.3, yf + 0.06, 4.25), (g1 - 0.3, yf + 0.1, 4.33), R["white_light"], skip=('+y',))
            b.boxmm((g0 + 0.3, yf + 0.06, 4.45), (g0 + 0.3 + (g1 - g0) * 0.35, yf + 0.1, 4.7), R["amber_light" if i % 2 else "white_light"], skip=('+y',))
    for i in range(ns + 1):
        x = -w / 2 + i * sw
        b.boxmm((max(-w / 2, x - 0.35), yf - 0.05, 0), (min(w / 2, x + 0.35), yf + 0.8, H), R["graphite"], skip=('+y', '-z'))
    b.boxmm((-w / 2, yf + 0.1, 3.8), (w / 2, yf + 0.8, H), R["graphite"], skip=('+y',))
    # canopy 1.2 m deep: street lamps stand 1.4 m off the facade line (InfiltrationMapBuilder x = +-5.6)
    b.boxmm((-w / 2, yf - 1.2, 3.6), (w / 2, yf + 0.1, 3.8), R["ceramic"], roles={'-z': R["graphite"]})
    b.boxmm((-w / 2 + 0.5, yf - 1.05, 3.57), (w / 2 - 0.5, yf - 0.95, 3.6), R["white_light"], skip=('+z',))
    b.boxmm((-w / 2, yf - 1.22, 3.74), (w / 2, yf - 1.18, 3.78), R["red_light"])
    # side / back: service door + vent
    b.push(Matrix.Rotation(math.radians(180), 4, 'Z') @ Matrix.Translation((0, -d / 2 + 0.28, 0)))
    b.vquad(-w * 0.25 - 0.6, -w * 0.25 + 0.6, 0.0, 2.3, 0, R["graphite"])
    b.vquad(-w * 0.25 + 0.75, -w * 0.25 + 1.05, 1.0, 1.5, -0.01, R["cell_panel"])
    b.vquad(w * 0.15, w * 0.15 + 2.4, 2.8, 3.3, 0, R["louvre"])
    b.pop()


def roof(b, style, tw, td, T, rng, sign_cell=None, tall=False):
    b.boxmm((-tw / 2 - 0.15, -td / 2 - 0.15, T), (tw / 2 + 0.15, -td / 2 + 0.15, T + 1.1), R["graphite"], skip=('-z',))
    b.boxmm((-tw / 2 - 0.15, td / 2 - 0.15, T), (tw / 2 + 0.15, td / 2 + 0.15, T + 1.1), R["graphite"], skip=('-z',))
    b.boxmm((-tw / 2 - 0.15, -td / 2 + 0.15, T), (-tw / 2 + 0.15, td / 2 - 0.15, T + 1.1), R["graphite"], skip=('-z',))
    b.boxmm((tw / 2 - 0.15, -td / 2 + 0.15, T), (tw / 2 + 0.15, td / 2 - 0.15, T + 1.1), R["graphite"], skip=('-z',))
    b.roof_boxes.append(((-tw / 2 - 0.15, -td / 2 - 0.15, T), (tw / 2 + 0.15, td / 2 + 0.15, T + 1.1)))

    def rb(mn, mx):
        b.roof_boxes.append((mn, mx))

    # stair / lift core
    cx, cy = -tw * 0.22, td * 0.18
    b.boxmm((cx - 1.6, cy - 2.0, T), (cx + 1.6, cy + 2.0, T + 3.2), R["graphite"], skip=('-z',))
    b.vquad(cx - 0.5, cx + 0.5, T, T + 2.2, cy - 2.01, R["graph_rib"])
    b.boxmm((cx - 0.6, cy - 2.05, T + 2.35), (cx + 0.6, cy - 2.0, T + 2.42), R["white_light"])
    rb((cx - 1.6, cy - 2.0, T), (cx + 1.6, cy + 2.0, T + 3.2))
    if style in ("OFF", "CER", "SRV"):
        n = 2 if tw > 18 else 1
        for i in range(n):
            x = tw * (0.12 + 0.2 * i); y = -td * 0.12
            r = 1.5 + rng.random() * 0.3
            b.cyl((x, y, T + 1.5), r, 3.0, R["corrugated"], 14, cap_role=R["cell_fan"])
            b.cyl((x, y, T + 3.15), r * 0.92, 0.3, R["graphite"], 14, cap_role=R["cell_fan"], caps=(False, True))
            rb((x - r, y - r, T), (x + r, y + r, T + 3.3))
    nu = 3 + rng.randrange(3)
    for i in range(nu):
        x = -tw * 0.35 + i * 1.9; y = -td * 0.33
        if x + 0.8 > tw / 2 - 0.5:
            break
        b.boxmm((x - 0.8, y - 0.5, T), (x + 0.8, y + 0.5, T + 1.1), R["white_paint" if style != "SRV" else "steel"], roles={'+z': R["cell_fan"]}, skip=('-z',))
    rb((-tw * 0.35 - 0.8, -td * 0.33 - 0.5, T), (min(tw / 2 - 0.5, -tw * 0.35 + (nu - 1) * 1.9 + 0.8), -td * 0.33 + 0.5, T + 1.1))
    if style == "RES":
        x, y = tw * 0.22, td * 0.2
        for sx in (-1, 1):
            for sy in (-1, 1):
                b.boxmm((x + sx * 0.9 - 0.07, y + sy * 0.9 - 0.07, T), (x + sx * 0.9 + 0.07, y + sy * 0.9 + 0.07, T + 1.2), R["steel"], skip=('-z', '+z'))
        b.cyl((x, y, T + 2.3), 1.3, 2.2, R["white_paint"], 14)
        rb((x - 1.3, y - 1.3, T), (x + 1.3, y + 1.3, T + 3.4))
    # duct run + antenna + aviation light
    b.boxmm((-tw * 0.4, td * 0.36 - 0.3, T), (tw * 0.3, td * 0.36 + 0.3, T + 0.6), R["steel"], skip=('-z',))
    ax, ay = tw * 0.32, td * 0.32
    mh = 7.0 if not tall else 11.0
    b.cyl((ax, ay, T + mh / 2), 0.1, mh, R["steel"], 6)
    b.boxmm((ax - 0.18, ay - 0.18, T + mh), (ax + 0.18, ay + 0.18, T + mh + 0.35), R["red_light"])
    rb((ax - 0.2, ay - 0.2, T), (ax + 0.2, ay + 0.2, T + mh + 0.35))
    if tall and style == "OFF":  # building maintenance unit
        b.boxmm((-1.2, td * 0.05 - 0.7, T), (1.2, td * 0.05 + 0.7, T + 1.6), R["white_paint"], skip=('-z',))
        b.boxmm((-0.2, td * 0.05 - 0.2, T + 1.6), (0.2, td * 0.05 + 0.2, T + 3.8), R["steel"])
        b.boxmm((-0.15, td * 0.05 - tw * 0.45, T + 3.5), (0.15, td * 0.05 + 0.2, T + 3.8), R["steel"])
        rb((-1.2, td * 0.05 - tw * 0.45, T), (1.2, td * 0.05 + 0.7, T + 3.8))
    if sign_cell is not None:  # rooftop TSUKASA billboard facing front (-Y)
        sw = min(tw - 3, 14.0); sh = sw / 1.83
        y = -td / 2 + 1.6; z0 = T + 2.0
        b.vquad(-sw / 2, sw / 2, z0, z0 + sh, y - 0.31, SIGN0 + sign_cell)
        b.boxmm((-sw / 2 - 0.3, y - 0.3, z0 - 0.3), (sw / 2 + 0.3, y, z0 + sh + 0.3), R["graphite"], roles={'-y': R["graphite"]})
        b.boxmm((-sw / 2 - 0.3, y - 0.34, z0 - 0.3), (sw / 2 + 0.3, y - 0.3, z0 - 0.22), R["red_light"])
        for xx in (-sw * 0.35, sw * 0.35):
            b.boxmm((xx - 0.12, y - 0.12, T), (xx + 0.12, y + 0.12, z0 - 0.3), R["steel"], skip=('-z',))
            b.boxmm((xx - 0.1, y - 0.12, T + 0.4), (xx + 0.1, y + 1.6, T + 0.6), R["steel"])
        rb((-sw / 2 - 0.3, y - 0.34, T), (sw / 2 + 0.3, y, z0 + sh + 0.3))


BLDG = [  # name, w, d, h, setback, style lower, style upper, seed, roof sign cell, impostor cell
    ("BlockA_1", 22, 18, 64, True, "CER", "OFF", 11, None, 0),
    ("BlockA_2", 22, 18, 64, True, "OFF", "OFF", 12, 1, 1),
    ("BlockB_1", 18, 16, 38, False, "RES", None, 21, None, 2),
    ("BlockB_2", 18, 16, 38, False, "CER", None, 22, 4, 3),
    ("BlockC_1", 26, 20, 92, True, "OFF", "OFF", 31, 0, 4),
    ("BlockC_2", 26, 20, 92, True, "CER", "CER", 32, None, 5),
    ("BlockD_1", 16, 14, 28, False, "RES", None, 41, None, 6),
    ("BlockD_2", 16, 14, 28, False, "SRV", None, 42, 6, 7),
]
KEEP = {}  # building name -> LOD0 object kept for impostor + preview


def building(spec):
    name, w, d, h, setback, st0, st1, seed, sign, cell = spec
    rng = random.Random(seed)
    b = B(0)
    storefront(b, w, d, rng, sign_cell=[2, 0, 5, 3, 1, 4, 7, 6][cell] if cell % 2 == 0 else None)
    if setback and h > 40:
        h1 = 5.0 + (h - 5.0) * 0.62
        body(b, st0, w, d, 5.0, h1, rng)
        b.boxmm((-w / 2 - 0.2, -d / 2 - 0.2, h1), (w / 2 + 0.2, d / 2 + 0.2, h1 + 0.8), R["ceramic"])
        b.boxmm((-w / 2 - 0.22, -d / 2 - 0.22, h1 + 0.05), (w / 2 + 0.22, d / 2 + 0.22, h1 + 0.13), R["red_light" if st0 == "OFF" else "white_light"], skip=('+z', '-z'))
        tw, td = w - 4, d - 4
        body(b, st1, tw, td, h1 + 0.8, h, rng)
        lower_top = h1 + 0.8
    else:
        body(b, st0, w, d, 5.0, h, rng)
        tw, td = w, d
        lower_top = None
    roof(b, st1 or st0, tw, td, h, rng, sign_cell=sign, tall=h > 80)
    roof_boxes = list(b.roof_boxes)
    lod0 = b
    # impostor view height = top of everything
    hv = h + max(mx[2] - h for mn, mx in roof_boxes) + 0.3
    imp = (cell, w, d, hv, h + 1.1)

    def mass(bb, with_roof):
        bb.imp = imp
        roles = {'+x': IMP_S, '-x': IMP_S, '+y': IMP_F, '-y': IMP_F, '+z': IMP_T}
        if lower_top is None:
            bb.boxmm((-w / 2, -d / 2, 0), (w / 2, d / 2, h + 1.1), IMP_F, roles=roles, skip=('-z',))
        else:
            bb.boxmm((-w / 2, -d / 2, 0), (w / 2, d / 2, lower_top), IMP_F, roles=roles, skip=('-z',))
            bb.boxmm((-tw / 2, -td / 2, lower_top), (tw / 2, td / 2, h + 1.1), IMP_F, roles=roles, skip=('-z',))
        if with_roof:
            for mn, mx in roof_boxes[1:]:
                bb.boxmm(mn, mx, IMP_F, roles=roles, skip=('-z',))
        if with_roof:  # storefront canopy silhouette
            bb.boxmm((-w / 2, -d / 2 - 1.2, 3.6), (w / 2, -d / 2, 3.8), IMP_F, roles=roles)
    b1 = B(1); mass(b1, True)
    b2 = B(2); mass(b2, False)
    export(name, [lod0, b1, b2])
    me = LIB[name]["lods"][0]
    ob = bpy.data.objects.new("KEEP_" + name, me)
    KEEP[name] = (ob, imp)


# ------------------------------------------------------------------ modular facade panels + corners (manual dressing)
def facade_panels():
    for st in ("OFF", "CER", "RES", "SRV"):
        b = B(0)
        facade(b, st if st != "RES" else "RES", 12.0, 0.0, 14.4, random.Random(77 + len(st)), True)
        b.boxmm((-6, 0.6, 0), (6, 0.9, 14.4), R["concrete"], skip=('-y',))
        export("FacadePanel_" + st, [b])
        b = B(0)
        corner(b, st, 0.0, 0.0, 0.0, 14.4, -1, -1)
        export("Corner_" + st, [b])


# ------------------------------------------------------------------ props
def p_street_lamp(L):
    b = B(L)
    b.boxz((0, 0, 0), (0.6, 0.6, 0.12), R["graphite"])
    b.boxz((0, 0, 0.12), (0.46, 0.46, 0.8), R["ceramic"])
    b.boxz((0, 0, 0.78), (0.48, 0.48, 0.05), R["amber_light"])
    b.cyl((0, 0, 4.45), 0.1, 7.1, R["graphite"], 10, r2=0.075)
    b.boxmm((-0.05, -2.05, 7.88), (0.05, 0.05, 8.0), R["graphite"])
    b.boxmm((-0.21, -2.6, 7.8), (0.21, -1.5, 7.94), R["ceramic"], roles={'-z': R["white_light"]})
    if b.L0:
        b.boxmm((-0.025, -1.4, 7.86), (0.025, -0.2, 7.88), R["red_light"])
        b.boxmm((-0.18, -2.62, 7.84), (0.18, -2.6, 7.9), R["red_light"])
        b.boxmm((-0.07, -0.35, 5.3), (0.07, -0.1, 5.45), R["graphite"], roles={'-y': R["dark_glass"]})
        b.boxmm((-0.02, -0.4, 4.0), (0.02, -0.08, 4.04), R["steel"])
        b.boxmm((-0.02, -0.4, 5.75), (0.02, -0.08, 5.79), R["steel"])
    b.boxmm((-0.02, -0.38, 4.05), (0.02, -0.12, 5.74), R["red_paint"])
    if b.L0:
        b.boxmm((-0.025, -0.39, 4.08), (0.025, -0.37, 5.7), R["white_light"])
    return b


def p_bollard(L):
    b = B(L)
    b.cyl((0, 0, 0.03), 0.16, 0.06, R["graphite"], 14, caps=(False, True))
    b.cyl((0, 0, 0.47), 0.13, 0.82, R["white_paint"], 14, caps=(False, False))
    b.cyl((0, 0, 0.9), 0.133, 0.05, R["amber_light"], 14, caps=(False, False))
    b.cyl((0, 0, 0.96), 0.135, 0.08, R["graphite"], 14, caps=(False, True))
    return b


def p_crowd_barrier(L):
    b = B(L)
    for x in (-0.95, 0.95):
        b.boxz((x, 0, 0), (0.08, 0.6, 0.04), R["steel"])
        b.cyl((x, 0, 0.55), 0.025, 1.1, R["steel"], 6, caps=(False, True))
    b.cyl((0, 0, 1.08), 0.025, 1.9, R["steel"], 6, axis='X', caps=(False, False))
    b.cyl((0, 0, 0.15), 0.02, 1.9, R["steel"], 6, axis='X', caps=(False, False))
    n = 9 if L == 0 else 0
    for i in range(n):
        x = -0.8 + i * 0.2
        b.cyl((x, 0, 0.615), 0.012, 0.93, R["steel"], 4, caps=(False, False))
    if L > 0:
        b.boxmm((-0.9, -0.005, 0.15), (0.9, 0.005, 1.08), R["steel"])
    b.boxmm((-0.5, -0.03, 0.55), (0.5, 0.0, 0.9), R["red_paint"], roles={'-y': R["red_paint"]})
    b.boxmm((-0.45, -0.035, 0.66), (0.45, -0.03, 0.7), R["white_paint"])
    return b


def p_water_barrier(L):
    b = B(L)
    b.boxz((0, 0, 0), (1.8, 0.5, 0.18), R["red_paint"])
    b.boxz((0, 0, 0.18), (1.7, 0.36, 0.5), R["red_paint"], skip=('-z',))
    b.boxz((0, 0, 0.68), (1.66, 0.3, 0.14), R["white_paint"], skip=('-z',))
    if L == 0:
        b.boxz((0, -0.19, 0.3), (1.2, 0.02, 0.06), R["white_paint"])
        b.cyl((0.6, 0, 0.84), 0.06, 0.04, R["white_paint"], 8)
    return b


def p_vending(L):
    b = B(L)
    b.boxz((0, 0.05, 0), (0.95, 0.75, 1.9), R["white_paint"])
    b.boxmm((-0.45, -0.34, 0.06), (0.45, -0.32, 1.86), R["white_paint"], roles={'-y': R["cell_vending"]})
    b.boxz((0, 0.05, 0), (0.97, 0.77, 0.06), R["rubber"])
    b.boxmm((-0.49, -0.3, 0.0), (-0.475, 0.42, 1.9), R["red_paint"], skip=('+x',))
    if L == 0:
        b.boxmm((-0.5, -0.45, 1.86), (0.5, 0.42, 1.95), R["graphite"])
        b.boxmm((-0.42, -0.46, 1.875), (0.42, -0.45, 1.9), R["white_light"])
    return b


def p_holo_pylon(L):
    b = B(L)
    b.boxz((0, 0, 0), (2.2, 0.5, 1.2), R["ceramic"])
    b.vquad(-0.55, 0.55, 0.2, 1.0, -0.26, R["cell_emblem"])
    b.boxz((0, 0, 1.2), (2.3, 0.55, 0.08), R["graphite"])
    b.boxz((0, 0, 1.28), (0.24, 0.24, 0.12), R["graphite"])
    b.boxz((0, 0, 1.4), (2.2, 0.3, 1.3), R["graphite"])
    sw, sh = 2.0, 2.0 / 1.83
    b.vquad(-sw / 2, sw / 2, 1.5, 1.5 + sh, -0.16, SIGN0 + 2)
    b.push(Matrix.Rotation(math.pi, 4, 'Z'))
    b.vquad(-sw / 2, sw / 2, 1.5, 1.5 + sh, -0.16, SIGN0 + 6)
    b.pop()
    b.boxz((0, 0, 2.7), (2.24, 0.34, 0.06), R["red_light"])
    return b


def p_billboard(cell):
    def f(L):
        b = B(L)
        W, H = 7.6, 4.4
        b.boxmm((-W / 2, -0.3, 0), (W / 2, 0, H), R["graphite"], skip=('+y',))
        sw = 7.2; sh = sw / 1.83
        b.vquad(-sw / 2, sw / 2, (H - sh) / 2, (H + sh) / 2, -0.31, SIGN0 + cell)
        b.boxmm((-W / 2, -0.34, H - 0.1), (W / 2, -0.3, H - 0.02), R["red_light"])
        if L == 0:
            for x in (-2.6, 0, 2.6):
                b.boxmm((x - 0.03, -1.0, H + 0.05), (x + 0.03, 0, H + 0.1), R["steel"])
                b.boxmm((x - 0.2, -1.15, H + 0.0), (x + 0.2, -0.85, H + 0.14), R["graphite"], roles={'-z': R["white_light"]})
            b.boxmm((-W / 2 + 0.1, -0.2, -0.3), (-W / 2 + 0.3, 0, 0), R["steel"])
            b.boxmm((W / 2 - 0.3, -0.2, -0.3), (W / 2 - 0.1, 0, 0), R["steel"])
        return b
    return f


def drone_parts(b, c):
    x0, y0, z0 = c
    b.boxmm((x0 - 0.35, y0 - 0.45, z0 + 0.3), (x0 + 0.35, y0 + 0.45, z0 + 0.55), R["ceramic"])
    b.boxmm((x0 - 0.3, y0 - 0.46, z0 + 0.38), (x0 + 0.3, y0 - 0.45, z0 + 0.42), R["red_light"])
    b.boxmm((x0 - 0.25, y0 - 0.3, z0 + 0.55), (x0 + 0.25, y0 + 0.3, z0 + 0.62), R["graphite"])
    for sx in (-1, 1):
        for sy in (-1, 1):
            ex, ey = x0 + sx * 0.62, y0 + sy * 0.62
            b.rod((x0 + sx * 0.25, y0 + sy * 0.3, z0 + 0.48), (ex, ey, z0 + 0.52), 0.035, R["graphite"], 6)
            b.cyl((ex, ey, z0 + 0.55), 0.06, 0.12, R["graphite"], 8)
            b.cyl((ex, ey, z0 + 0.62), 0.32, 0.015, R["dark_glass"], 14)
            if b.L0:
                b.boxmm((ex - 0.3, ey - 0.02, z0 + 0.63), (ex + 0.3, ey + 0.02, z0 + 0.64), R["steel"])
    b.boxmm((x0 - 0.26, y0 - 0.32, z0 + 0.05), (x0 + 0.26, y0 + 0.32, z0 + 0.3), R["white_paint"], roles={'+x': R["cell_emblem"], '-x': R["cell_emblem"]})
    for sx in (-1, 1):
        b.boxmm((x0 + sx * 0.3 - 0.02, y0 - 0.4, z0), (x0 + sx * 0.3 + 0.02, y0 + 0.4, z0 + 0.03), R["steel"])
        for sy in (-0.25, 0.25):
            b.boxmm((x0 + sx * 0.3 - 0.015, y0 + sy - 0.015, z0), (x0 + sx * 0.28 + 0.015, y0 + sy + 0.015, z0 + 0.32), R["steel"], skip=('+z', '-z'))


def p_delivery_drone(L):
    b = B(L)
    drone_parts(b, (0, 0, 0))
    return b


def p_drone_pad(L):
    b = B(L)
    b.boxz((0, 0, 0), (2.8, 2.8, 0.1), R["graphite"])
    for s in (-1, 1):
        b.boxmm((-1.4, s * 1.4 - 0.12 * (s > 0), 0.1), (1.4, s * 1.4 + 0.12 * (s < 0), 0.105), R["hazard"])
        b.boxmm((s * 1.4 - 0.12 * (s > 0), -1.28, 0.1), (s * 1.4 + 0.12 * (s < 0), 1.28, 0.105), R["hazard"])
        b.boxmm((-0.9, s * 0.9 - 0.04, 0.1), (0.9, s * 0.9 + 0.04, 0.11), R["amber_light"])
        b.boxmm((s * 0.9 - 0.04, -0.86, 0.1), (s * 0.9 + 0.04, 0.86, 0.11), R["amber_light"])
    b.boxz((1.6, 1.0, 0), (0.4, 0.3, 1.1), R["graphite"], roles={'-y': R["cell_panel"]})
    drone_parts(b, (0, 0, 0.1))
    return b


def p_cables(L):
    b = B(L)
    rng = random.Random(5)
    span = 14.4
    n = 14 if L == 0 else 7
    for k in range(5):
        y = -0.3 + k * 0.18; z = rng.random() * 0.5 - 0.2; sag = 0.6 + rng.random() * 0.7
        pts = [(-span / 2 + span * i / n, y, z - sag * (1 - (2 * i / n - 1) ** 2)) for i in range(n + 1)]
        b.tube(pts, 0.022 + 0.01 * (k % 2), R["rubber"], 5)
    for sx in (-1, 1):
        b.boxmm((sx * span / 2 - 0.1, -0.45, -0.35), (sx * span / 2 + 0.1, 0.55, 0.45), R["steel"])
    if L == 0:
        b.boxmm((1.1, -0.2, -0.95), (1.5, 0.1, -0.6), R["graphite"], roles={'-y': R["cell_panel"]})
        b.boxmm((-3.2, -0.05, -1.25), (-2.6, 0.0, -0.75), R["red_paint"])
    return b


def p_puddle(i, sx, sy):
    def f(L):
        b = B(L)
        b.quad([(-sx / 2, -sy / 2, 0), (sx / 2, -sy / 2, 0), (sx / 2, sy / 2, 0), (-sx / 2, sy / 2, 0)], PUD0 + i, (0, 0, 1))
        return b
    return f


def p_trash_bags(L):
    b = B(L)
    for i, (x, y, s) in enumerate(((0, 0, 0.75), (0.55, 0.15, 0.6), (-0.5, 0.1, 0.62), (0.15, -0.45, 0.55), (0.2, 0.25, 0.5))):
        b.lump((x, y, s * 0.42 + (0.35 if i == 4 else 0)), (s, s * 0.9, s * 0.85), R["rubber"] if i != 3 else R["white_paint"], 40 + i)
    return b


def p_trash_bin(L):
    b = B(L)
    b.boxz((0, 0, 0), (0.7, 0.55, 0.95), R["white_paint"])
    b.boxz((0, 0, 0.95), (0.74, 0.59, 0.1), R["red_paint"])
    b.boxz((0, 0, 1.05), (0.66, 0.5, 0.05), R["graphite"])
    if L == 0:
        b.boxmm((-0.2, -0.3, 0.8), (0.2, -0.275, 0.88), R["rubber"])
        b.boxmm((-0.25, -0.28, 0.35), (0.25, -0.275, 0.6), R["cell_emblem"], skip=('+y',))
    return b


def p_crates(L):
    b = B(L)
    rng = random.Random(9)
    for i, (x, y, z) in enumerate(((0, 0, 0), (0.62, 0.05, 0), (0.0, 0.42, 0), (0.3, 0.2, 0.31), (-0.02, 0.02, 0.31), (0.62, 0.05, 0.31), (0.3, 0.2, 0.62))):
        role = R["red_paint"] if rng.random() < 0.3 else R["graphite"]
        b.boxz((x, y, z), (0.6, 0.4, 0.3), role, rot=(0, 0, (rng.random() - 0.5) * 0.12))
        if L == 0:
            b.boxz((x, y - 0.205, z + 0.2), (0.22, 0.01, 0.05), R["rubber"], rot=(0, 0, 0))
    return b


def p_pallet(L):
    b = B(L)
    b.boxz((0, 0, 0), (1.2, 1.0, 0.14), R["cardboard"])
    rng = random.Random(3)
    for i, (x, y, z, s) in enumerate(((-0.3, -0.25, 0.14, 0.5), (0.3, -0.25, 0.14, 0.5), (-0.3, 0.25, 0.14, 0.5), (0.3, 0.25, 0.14, 0.5), (-0.15, 0.0, 0.62, 0.55), (0.32, 0.1, 0.62, 0.42))):
        b.boxz((x, y, z), (s, s * 0.9, s * 0.95), R["cardboard"], rot=(0, 0, (rng.random() - 0.5) * 0.2))
    return b


def p_wall_flood(L):
    b = B(L)
    b.boxmm((-0.35, -0.1, -0.5), (0.35, 0, 0.5), R["steel"])
    for x in (-0.25, 0.25):
        b.boxmm((x - 0.04, -1.25, -0.05), (x + 0.04, -0.1, 0.05), R["steel"])
    b.rod((0, -0.1, -0.45), (0, -1.1, 0.0), 0.03, R["steel"])
    b.boxmm((-1.15, -1.3, -0.05), (1.15, -1.2, 0.05), R["steel"])
    for x in (-0.8, 0.0, 0.8):
        b.box((x, -1.45, -0.2), (0.55, 0.26, 0.42), R["graphite"], rot=(math.radians(-35), 0, 0), roles={'-y': R["white_light"]})
    b.cyl((0.3, -0.05, -3.0), 0.03, 5.0, R["steel"], 6, caps=(False, False))
    return b


def p_light_tower(L):
    b = B(L)
    b.boxz((0, 0, 0.45), (1.3, 2.2, 0.95), R["white_paint"], roles={'-x': R["white_paint"]})
    b.boxz((0, 0, 0.75), (1.32, 2.22, 0.12), R["hazard"])
    b.vquad(-0.4, 0.4, 0.6, 1.2, -1.11, R["louvre"])
    b.vquad(0.45, 0.62, 0.9, 1.25, -1.11, R["cell_panel"])
    for sx in (-1, 1):
        b.cyl((sx * 0.68, 0.4, 0.35), 0.35, 0.22, R["rubber"], 12, axis='X')
    b.boxmm((-0.04, -2.0, 0.4), (0.04, -1.1, 0.48), R["steel"])
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.boxmm((min(sx * 0.6, sx * 1.4), sy * 0.9 - 0.04, 0.3), (max(sx * 0.6, sx * 1.4), sy * 0.9 + 0.04, 0.38), R["steel"])
            b.boxz((sx * 1.4, sy * 0.9, 0), (0.2, 0.2, 0.32), R["steel"])
    b.cyl((0, 0.6, 4.2), 0.09, 6.6, R["steel"], 8, r2=0.07)
    b.boxmm((-1.1, 0.5, 7.2), (1.1, 0.62, 7.32), R["steel"])
    for x in (-0.8, -0.27, 0.27, 0.8):
        b.box((x, 0.38, 7.15), (0.48, 0.2, 0.42), R["graphite"], rot=(math.radians(-25), 0, 0), roles={'-y': R["white_light"]})
    b.cyl((0, 0.6, 7.6), 0.09, 0.2, R["amber_light"], 10)
    return b


def p_bay_lamp(L):
    b = B(L)
    b.boxmm((-0.15, -0.06, -0.15), (0.15, 0, 0.15), R["steel"])
    b.boxmm((-0.03, -0.5, -0.03), (0.03, -0.06, 0.03), R["steel"])
    b.box((0, -0.62, -0.08), (0.6, 0.38, 0.22), R["graphite"], rot=(math.radians(-20), 0, 0), roles={'-z': R["white_light"]})
    if L == 0:
        for x in (-0.2, 0, 0.2):
            b.box((x, -0.62, -0.2), (0.02, 0.4, 0.02), R["steel"], rot=(math.radians(-20), 0, 0))
    return b


def p_beacon(L):
    b = B(L)
    b.boxmm((-0.08, -0.3, -0.04), (0.08, 0, 0.04), R["steel"])
    b.cyl((0, -0.3, 0.07), 0.12, 0.08, R["graphite"], 10)
    b.cyl((0, -0.3, 0.21), 0.1, 0.2, R["amber_light"], 10, r2=0.07)
    return b


def p_wall_utility(L):
    b = B(L)
    for x in (-1.6, 1.4):
        b.boxmm((x - 0.5, -0.42, 2.2), (x + 0.5, -0.02, 2.95), R["white_paint"], roles={'-y': R["cell_fan"]})
        b.boxmm((x - 0.45, -0.45, 2.15), (x - 0.4, 0, 2.2), R["steel"]); b.boxmm((x + 0.4, -0.45, 2.15), (x + 0.45, 0, 2.2), R["steel"])
        b.cyl((x + 0.3, -0.06, 4.2), 0.03, 2.5, R["steel"], 6, caps=(False, False))
    for i, x in enumerate((-2.7, -2.55, -2.4)):
        b.cyl((x, -0.06, 3.3), 0.035, 5.4, R["steel"], 6, caps=(False, False))
    b.boxmm((-2.9, -0.22, 1.2), (-2.2, 0, 1.95), R["graphite"], roles={'-y': R["cell_panel"]})
    b.boxmm((-3, -0.32, 6.1), (3, 0, 6.16), R["steel"])
    if L == 0:
        b.cyl((0, -0.18, 5.4), 0.08, 6.0, R["steel"], 8, axis='X', caps=(False, False))
        b.cyl((0, -0.38, 5.55), 0.06, 6.0, R["graphite"], 8, axis='X', caps=(False, False))
        b.boxmm((0.2, -0.18, 6.4), (0.7, 0, 6.55), R["graphite"], roles={'-z': R["white_light"]})
    return b


def p_hq_booth(L):
    b = B(L)
    b.boxz((0, 0, 0), (2.4, 2.0, 0.2), R["graphite"])
    b.boxz((0, 0, 0.2), (2.2, 1.8, 1.0), R["ceramic"])
    b.boxz((0, 0, 1.2), (2.12, 1.72, 1.2), R["dark_glass"], skip=('-z', '+z'))
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.boxz((sx * 1.04, sy * 0.84, 1.2), (0.12, 0.12, 1.2), R["graphite"], skip=('-z', '+z'))
    b.boxz((0, 0, 2.4), (2.7, 2.3, 0.35), R["ceramic"])
    b.boxmm((-1.36, -1.17, 2.5), (1.36, -1.15, 2.58), R["red_light"])
    b.vquad(-0.5, 0.5, 2.42, 2.73, -1.151 - 0.003, R["cell_emblem"])
    if L == 0:
        b.boxmm((-0.9, -0.95, 1.15), (0.9, -0.5, 1.22), R["graphite"])
        b.vquad(0.5, 0.95, 1.22, 1.5, -0.86, R["cell_panel"])
        b.boxmm((1.1, -1.15, 2.2), (1.25, -0.85, 2.35), R["graphite"], roles={'-y': R["dark_glass"]})
        b.boxmm((-0.25, -1.16, 0.4), (0.25, -1.1, 0.9), R["cell_panel"], skip=('+y',))
    return b


# ---- HQ podium overlay (podium-local coordinates of SM_INF_HQPodium: front wall y = 0, side entrance at x = 26)
def p_hq_podium(L):
    b = B(L)
    ex = 26.0
    rng = random.Random(17)
    if L == 0:
        # ceramic relief panels + fins above the glazing (avoid the emblem field |x| < 6.5)
        for seg in ((-49.2, -6.5), (6.5, 49.2)):
            n = int((seg[1] - seg[0]) / 2.4); x0 = (seg[0] + seg[1]) / 2 - n * 1.2
            for i in range(n):
                x = x0 + i * 2.4
                for r in range(4):
                    z = 9.4 + r * 3.0
                    b.boxmm((x + 0.04, -0.22, z + 0.04), (x + 2.36, 0.0, z + 2.96), R["ceramic"], skip=('+y',))
                if i % 4 == 0:
                    b.boxmm((x - 0.15, -0.9, 9.2), (x + 0.15, 0.0, 21.8), R["graphite"], skip=('+y',))
                    if i % 8 == 0:
                        b.boxmm((x - 0.03, -0.92, 9.6), (x + 0.03, -0.9, 21.4), R["red_light"])
            # glazing mullions + transom
        for seg in ((-48.0, -8.5), (8.5, ex - 6.0), (ex + 6.0, 48.0)):
            n = int((seg[1] - seg[0]) / 1.8)
            for i in range(n + 1):
                x = seg[0] + i * (seg[1] - seg[0]) / max(n, 1)
                b.boxmm((x - 0.06, -0.32, 0.9), (x + 0.06, -0.1, 4.5), R["graphite"], skip=('+y', '-z'))
            b.boxmm((seg[0], -0.25, 4.5), (seg[1], 0.0, 4.95), R["graphite"], skip=('+y',))
            b.boxmm((seg[0], -0.27, 4.9), (seg[1], -0.25, 4.95), R["red_light"])
        # security cameras
        for x in (ex - 5.5, ex + 5.5, -9.2, 9.2):
            b.boxmm((x - 0.03, -0.5, 6.0), (x + 0.03, 0, 6.06), R["steel"])
            b.box((x, -0.6, 5.92), (0.16, 0.34, 0.16), R["graphite"], rot=(math.radians(-20), 0, 0), roles={'-y': R["dark_glass"]})
    # plinth uplight
    for seg in ((-50.0, -8.2), (8.2, ex - 4.2), (ex + 4.2, 50.0)):
        b.boxmm((seg[0], -1.13, 0.7), (seg[1], -1.06, 0.77), R["white_light"], skip=('+y',))
    # TSUKASA emblem relief above the sealed main entrance
    b.boxmm((-5.6, -0.45, 9.6), (5.6, 0.0, 21.2), R["graphite"], skip=('+y',))
    b.boxmm((-4.8, -0.62, 10.4), (4.8, -0.45, 20.4), R["ceramic"], roles={'-y': R["cell_emblem"]})
    for (a, c) in (((-5.0, 20.5), (5.0, 20.6)), ((-5.0, 10.2), (5.0, 10.3)), ((-5.0, 10.3), (-4.9, 20.5)), ((4.9, 10.3), (5.0, 20.5))):
        b.boxmm((a[0], -0.66, a[1]), (c[0], -0.45, c[1]), R["red_light"])
    # side entrance B: wide cantilever canopy above the existing one (front edge kept at y = -4.45: lobby sign hangs at -4.65)
    b.boxmm((ex - 11, -4.45, 7.15), (ex + 11, 0.3, 7.65), R["ceramic"], roles={'-z': R["graphite"], '+z': R["concrete"]})
    b.boxmm((ex - 11, -4.47, 7.25), (ex + 11, -4.45, 7.33), R["white_light"])
    b.boxmm((ex - 11, -4.47, 7.52), (ex + 11, -4.45, 7.58), R["red_light"])
    for x in (ex - 10.2, ex - 8.4, ex + 8.4, ex + 10.2):
        b.boxmm((x - 0.08, -4.2, 7.12), (x + 0.08, -0.3, 7.15), R["white_light"])
    for x in (ex - 10.5, ex + 10.5):
        b.rod((x, -4.2, 7.65), (x, 0.0, 12.5), 0.05, R["steel"], 6)
    # TSUKASA red banners flanking the entrance
    for x in (ex - 13.0, ex + 13.0):
        b.boxmm((x - 0.7, -0.75, 9.4), (x + 0.7, -0.69, 16.6), R["red_paint"])
        b.boxmm((x - 0.72, -0.77, 16.45), (x + 0.72, -0.69, 16.6), R["white_light"])
        b.boxmm((x - 0.72, -0.77, 9.4), (x + 0.72, -0.69, 9.52), R["white_light"])
        b.boxmm((x - 0.04, -0.75, 16.6), (x + 0.04, 0.0, 16.7), R["steel"])
    # roof edge red line
    b.boxmm((-50.2, -0.34, 21.8), (50.2, -0.3, 21.88), R["red_light"])
    return b


# ---- tower base / emblem surround (tower-local: south face y = -15, low emblem plate centre z = 61)
def p_tower_base(L):
    b = B(L)
    yf = -15.0
    t = 0.8
    b.boxmm((-13.4, yf - 1.2, 47.6), (13.4, yf, 47.6 + t), R["graphite"])
    b.boxmm((-13.4, yf - 1.2, 74.4 - t), (13.4, yf, 74.4), R["graphite"])
    b.boxmm((-13.4, yf - 1.2, 47.6 + t), (-13.4 + t, yf, 74.4 - t), R["graphite"])
    b.boxmm((13.4 - t, yf - 1.2, 47.6 + t), (13.4, yf, 74.4 - t), R["graphite"])
    for (a, c) in (((-12.8, 48.6), (12.8, 48.7)), ((-12.8, 73.3), (12.8, 73.4)), ((-12.8, 48.7), (-12.7, 73.3)), ((12.7, 48.7), (12.8, 73.3))):
        b.boxmm((a[0], yf - 1.25, a[1]), (c[0], yf - 1.2, c[1]), R["red_light"])
    # sky-lobby light lines under the mechanical bands (south / east / west faces)
    for k in range(1, 8):
        z = k * 24.0 - 0.18
        for rot in (0, 90, -90):
            b.push(Matrix.Rotation(math.radians(rot), 4, 'Z'))
            b.boxmm((-12.6, -14.56, z), (12.6, -14.46, z + 0.08), R["white_light"])
            b.pop()
    # base portal (south)
    if L == 0:
        b.boxmm((-10, yf - 1.5, 0), (-8.6, yf, 14), R["ceramic"])
        b.boxmm((8.6, yf - 1.5, 0), (10, yf, 14), R["ceramic"])
        b.boxmm((-10, yf - 1.5, 14), (10, yf, 15.6), R["ceramic"], roles={'-y': R["ceramic"]})
        b.boxmm((-8.6, yf - 0.4, 0), (8.6, yf - 0.3, 14), R["dark_glass"])
        b.boxmm((-9.6, yf - 1.52, 14.3), (9.6, yf - 1.5, 14.4), R["red_light"])
    return b


PROPS = [
    # name, fn, lods, anchors
    ("StreetLamp", p_street_lamp, 2, [("LightAnchor", (0, -2.05, 7.72))]),
    ("Bollard", p_bollard, 1, []),
    ("CrowdBarrier", p_crowd_barrier, 2, []),
    ("WaterBarrier", p_water_barrier, 2, []),
    ("VendingMachine", p_vending, 2, []),
    ("HoloAdPylon", p_holo_pylon, 2, []),
    ("Billboard_0", p_billboard(0), 2, []),
    ("Billboard_1", p_billboard(1), 2, []),
    ("Billboard_4", p_billboard(4), 2, []),
    ("DeliveryDrone", p_delivery_drone, 2, []),
    ("DronePad", p_drone_pad, 2, []),
    ("CablesOverhead", p_cables, 2, []),
    ("Puddle_0", p_puddle(0, 2.6, 1.8), 1, []),
    ("Puddle_1", p_puddle(1, 3.6, 1.7), 1, []),
    ("Puddle_2", p_puddle(2, 1.7, 1.4), 1, []),
    ("Puddle_3", p_puddle(3, 4.2, 2.6), 1, []),
    ("TrashBags", p_trash_bags, 1, []),
    ("TrashBin", p_trash_bin, 1, []),
    ("CrateStack", p_crates, 1, []),
    ("PalletBoxes", p_pallet, 1, []),
    ("WallFloodRig", p_wall_flood, 2, [("LightAnchor", (0, -1.55, -0.3))]),
    ("LightTower", p_light_tower, 2, [("LightAnchor", (0, 0.2, 7.0))]),
    ("BayLamp", p_bay_lamp, 1, [("LightAnchor", (0, -0.65, -0.25))]),
    ("BeaconAmber", p_beacon, 1, [("LightAnchor", (0, -0.3, 0.25))]),
    ("WallUtilityKit", p_wall_utility, 2, []),
    ("HQSecurityBooth", p_hq_booth, 2, []),
    ("HQPodiumDetail", p_hq_podium, 2, []),
    ("TowerBaseDetail", p_tower_base, 2, []),
]


def props():
    for name, fn, nl, anchors in PROPS:
        export(name, [fn(l) for l in range(nl)], anchors)


# ------------------------------------------------------------------ impostor atlas (rendered from LOD0)
def write_png(path, arr):
    """arr: HxWx(3|4) uint8, row 0 = top."""
    h, w, c = arr.shape
    raw = b"".join(b"\x00" + arr[y].tobytes() for y in range(h))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6 if c == 4 else 2, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def read_png(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(h, w, 4)[::-1]  # row 0 = top


def resize(a, w, h):
    H0, W0 = a.shape[:2]
    ys = (np.arange(h) + 0.5) * H0 / h - 0.5; xs = (np.arange(w) + 0.5) * W0 / w - 0.5
    y0 = np.clip(np.floor(ys).astype(int), 0, H0 - 1); y1 = np.clip(y0 + 1, 0, H0 - 1); fy = np.clip(ys - y0, 0, 1)[:, None, None]
    x0 = np.clip(np.floor(xs).astype(int), 0, W0 - 1); x1 = np.clip(x0 + 1, 0, W0 - 1); fx = np.clip(xs - x0, 0, 1)[None, :, None]
    top = a[y0][:, x0] * (1 - fx) + a[y0][:, x1] * fx
    bot = a[y1][:, x0] * (1 - fx) + a[y1][:, x1] * fx
    return top * (1 - fy) + bot * fy


def setup_render(sc, w, h, samples=16, standard=True):
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = w; sc.render.resolution_y = h; sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    try:
        sc.eevee.taa_render_samples = samples
    except Exception:
        pass
    sc.view_settings.view_transform = 'Standard' if standard else 'AgX'
    sc.view_settings.look = 'None'
    sc.view_settings.exposure = 0.0; sc.view_settings.gamma = 1.0


def world_color(sc, c, strength=1.0):
    w = sc.world or bpy.data.worlds.new("W")
    sc.world = w
    nt = _nodes(w)
    bg = nt.nodes.new("ShaderNodeBackground"); out = nt.nodes.new("ShaderNodeOutputWorld")
    bg.inputs[0].default_value = (*c, 1); bg.inputs[1].default_value = strength
    nt.links.new(bg.outputs[0], out.inputs[0])


def ortho_cam(sc, loc, rot, scale):
    cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'; cd.ortho_scale = scale; cd.clip_end = 2000
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = loc; cam.rotation_euler = rot
    return cam


def impostors(tmp):
    os.makedirs(tmp, exist_ok=True)
    sc = bpy.context.scene
    ALB = np.zeros((IMP_H, IMP_W, 4), np.float32); EMI = np.zeros((IMP_H, IMP_W, 4), np.float32)
    ALB[..., :3] = 0.06; ALB[..., 3] = 1; EMI[..., 3] = 1
    for name, (ob, imp) in KEEP.items():
        clear_scene()
        sc.collection.objects.link(ob)
        cell, w, d, hv, _ = imp
        cx0 = (cell % 4) * 512; cy0 = (cell // 4) * 1024
        for view in ("front", "side"):
            ext = w if view == "front" else d
            k = 1024.0 / hv
            rw = max(64, min(2048, int(ext * k * 1.5))); rh = int(hv * k * 1.5)
            for mode, buf in (("alb", ALB), ("emi", EMI)):
                set_mode([ob], mode)
                world_color(sc, (0.06, 0.062, 0.07) if mode == "alb" else (0, 0, 0))
                setup_render(sc, rw, rh, 8)
                for o in [o for o in sc.objects if o.type == 'CAMERA']:
                    bpy.data.objects.remove(o, do_unlink=True)
                scale = max(ext, hv)
                if view == "front":
                    ortho_cam(sc, (0, -300, hv / 2), (math.radians(90), 0, 0), scale)
                else:
                    ortho_cam(sc, (300, 0, hv / 2), (math.radians(90), 0, math.radians(90)), scale)
                sc.render.filepath = os.path.join(tmp, "imp_%s_%s_%s.png" % (name, view, mode))
                bpy.ops.render.render(write_still=True)
                a = read_png(sc.render.filepath)
                a = resize(a, 252, 1020)
                ox = cx0 + (2 if view == "front" else 258)
                buf[cy0 + 2:cy0 + 1022, ox:ox + 252] = a
        set_mode([ob], "lit")
        sc.collection.objects.unlink(ob)
    td = os.path.join(OUT, "Textures")
    write_png(os.path.join(td, "T_INFD_Impostor_Albedo.png"), (np.clip(ALB[..., :3], 0, 1) * 255 + 0.5).astype(np.uint8))
    write_png(os.path.join(td, "T_INFD_Impostor_Emission.png"), (np.clip(EMI[..., :3], 0, 1) * 255 + 0.5).astype(np.uint8))
    print("IMPOSTOR atlas written")
    for img in list(bpy.data.images):
        if "Impostor" in img.filepath:
            img.reload()
    if "INFD_Impostor" in MATS:
        make_lit("INFD_Impostor", MATS["INFD_Impostor"])


# ------------------------------------------------------------------ previews (scratchpad only)
def inst(name, loc=(0, 0, 0), rz=0.0, lod=0, sz=1.0):
    me = LIB[name]["lods"][min(lod, len(LIB[name]["lods"]) - 1)]
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc; ob.rotation_euler = (0, 0, rz); ob.scale = (1, 1, sz)
    return ob


def u2b(ux, uy, uz, yaw=0.0):
    """Unity world position/yaw -> Blender location / z rotation (FBX import maps Blender (x,y,z) -> Unity (-x,z,-y))."""
    return (-ux, -uz, uy), -math.radians(yaw)


def uinst(name, ux, uy, uz, yaw, sy=1.0, lod=0):
    loc, rz = u2b(ux, uy, uz, yaw)
    return inst(name, loc, rz, lod, sy)


OLD_COLORS = {"Graphite": (0.05, 0.052, 0.058), "Ceramic": (0.62, 0.62, 0.6), "Concrete": (0.25, 0.25, 0.25),
              "Glass": (0.02, 0.03, 0.045), "Red": (1.0, 0.05, 0.05), "Amber": (1.0, 0.5, 0.05), "White": (1, 1, 1),
              "Rubber": (0.02, 0.02, 0.02), "Hazard": (0.6, 0.4, 0.04), "FacadeA": (0.05, 0.06, 0.08),
              "FacadeB": (0.3, 0.3, 0.32), "Container": (0.16, 0.16, 0.18), "Shutter": (0.28, 0.28, 0.29), "Steel": (0.3, 0.31, 0.33)}
OLD_MATS = {}


def old_mat(n):
    if n not in OLD_MATS:
        m = bpy.data.materials.new("OLD_" + n)
        nt = _nodes(m)
        out = nt.nodes.new("ShaderNodeOutputMaterial"); bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(bs.outputs[0], out.inputs[0])
        bs.inputs["Base Color"].default_value = (*OLD_COLORS.get(n, (0.3, 0.3, 0.3)), 1)
        bs.inputs["Roughness"].default_value = 0.5
        if n in ("Red", "Amber", "White"):
            bs.inputs["Emission Color"].default_value = (*OLD_COLORS[n], 1); bs.inputs["Emission Strength"].default_value = 5
        OLD_MATS[n] = m
    return OLD_MATS[n]


OLD_LIB = {}


def old(name, ux, uy, uz, yaw):
    """Instance of an existing plain-kit FBX (SM_INF_<name>) at a Unity transform."""
    if name not in OLD_LIB:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(TUT, "Meshes", "SM_INF_%s.fbx" % name))
        objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
        meshes = []
        for o in objs:
            for s in o.material_slots:
                if s.material:
                    s.material = old_mat(s.material.name.split(".")[0].replace("INF_", ""))
            o.data.use_fake_user = True
            meshes.append((o.data, o.matrix_world.copy()))
        for o in [o for o in bpy.data.objects if o not in before]:
            bpy.data.objects.remove(o, do_unlink=True)
        OLD_LIB[name] = meshes
    loc, rz = u2b(ux, uy, uz, yaw)
    M = Matrix.Translation(loc) @ Matrix.Rotation(rz, 4, 'Z')
    for me, mw in OLD_LIB[name]:
        ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
        ob.matrix_world = M @ mw


def ground_box(umin, umax, mat_rgb, tex=None, tile=4.0, rough=0.6):
    """Axis box from Unity min/max corners."""
    (x0, y0, z0), (x1, y1, z1) = umin, umax
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector(((v.co.x + 0.5) * (x1 - x0) + x0, (v.co.y + 0.5) * (z1 - z0) + z0, (v.co.z + 0.5) * (y1 - y0) + y0))
        v.co.x = -v.co.x; v.co.y = -v.co.y
    me = bpy.data.meshes.new("g"); bm.to_mesh(me); bm.free()
    uv = me.uv_layers.new().data
    for p in me.polygons:
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            uv[li].uv = (c.x / tile, c.y / tile) if abs(p.normal.z) > 0.5 else (c.x / tile + c.y / tile, c.z / tile)
    m = bpy.data.materials.new("G")
    nt = _nodes(m)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bs.outputs[0], out.inputs[0])
    bs.inputs["Base Color"].default_value = (*mat_rgb, 1); bs.inputs["Roughness"].default_value = rough
    if tex and os.path.exists(os.path.join(TUT, "Textures", tex)):
        t = _img(nt, os.path.join(TUT, "Textures", tex)); nt.links.new(t.outputs[0], bs.inputs["Base Color"])
    me.materials.append(m)
    ob = bpy.data.objects.new("g", me); bpy.context.scene.collection.objects.link(ob)
    return ob


def dusk(sc, sky=True):
    """World = Unity trilight ambient average (lighting); dusk panorama on a camera-only emissive dome."""
    world_color(sc, (0.21, 0.24, 0.33), 0.38)
    pano = os.path.join(TUT, "Textures", "T_INF_Sky_Pano.png")
    if sky and os.path.exists(pano):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=48, v_segments=24, radius=1800)
        me = bpy.data.meshes.new("dome"); bm.to_mesh(me); bm.free()
        m = bpy.data.materials.new("DOME"); nt = _nodes(m)
        out = nt.nodes.new("ShaderNodeOutputMaterial"); em = nt.nodes.new("ShaderNodeEmission")
        et = nt.nodes.new("ShaderNodeTexEnvironment"); et.image = tex(pano)
        tc = nt.nodes.new("ShaderNodeTexCoord"); mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Rotation"].default_value = (0, 0, math.radians(90))
        nt.links.new(tc.outputs["Object"], mp.inputs[0]); nt.links.new(mp.outputs[0], et.inputs[0])
        nt.links.new(et.outputs[0], em.inputs[0]); em.inputs[1].default_value = 1.0
        nt.links.new(em.outputs[0], out.inputs[0])
        me.materials.append(m)
        for p_ in me.polygons:
            p_.use_smooth = True
        dome = bpy.data.objects.new("dome", me); sc.collection.objects.link(dome)
        for attr in ("visible_diffuse", "visible_glossy", "visible_shadow", "visible_transmission", "visible_volume_scatter"):
            try:
                setattr(dome, attr, False)
            except Exception:
                pass
    sd = bpy.data.lights.new("sun", 'SUN'); sd.energy = 2.2; sd.color = (1.0, 0.8, 0.66); sd.angle = math.radians(2)
    so = bpy.data.objects.new("sun", sd); sc.collection.objects.link(so)
    # Unity sun euler (14, 62, 0): travel dir (0.857, -0.242, 0.455) -> Blender (-x, -z, y)
    so.rotation_euler = Vector((-0.857, -0.455, -0.242)).to_track_quat('-Z', 'Y').to_euler()


def spot(loc, aim, energy, color=(1, 0.93, 0.82), size=100):
    ld = bpy.data.lights.new("s", 'SPOT'); ld.energy = energy; ld.color = color; ld.spot_size = math.radians(size)
    ld.spot_blend = 0.5
    o = bpy.data.objects.new("s", ld); bpy.context.scene.collection.objects.link(o)
    d = (Vector(aim) - Vector(loc)).normalized()
    o.location = Vector(loc) + d * 0.7  # step out of the lamp housing (EEVEE shadows the light otherwise)
    o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    return o


def point(loc, energy, color=(1, 0.6, 0.2)):
    ld = bpy.data.lights.new("p", 'POINT'); ld.energy = energy; ld.color = color
    o = bpy.data.objects.new("p", ld); bpy.context.scene.collection.objects.link(o); o.location = loc


def persp_cam(sc, loc, target, lens=24):
    cd = bpy.data.cameras.new("cam"); cd.lens = lens; cd.clip_end = 3000
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    return cam


def thumbnails(prev):
    tdir = os.path.join(prev, "thumbs"); os.makedirs(tdir, exist_ok=True)
    sc = bpy.context.scene
    stats = {}
    for name, t in EXPORTED:
        stats[name] = t
        clear_scene()
        lods = LIB[name]["lods"]
        ob = inst(name, (0, 0, 0))
        mn = Vector([min((Vector(c)[i]) for c in ob.bound_box) for i in range(3)])
        mx = Vector([max((Vector(c)[i]) for c in ob.bound_box) for i in range(3)])
        ctr = (mn + mx) / 2; size = (mx - mn).length
        world_color(sc, (0.07, 0.08, 0.11), 1.0)
        sd = bpy.data.lights.new("key", 'SUN'); sd.energy = 2.2; sd.color = (1.0, 0.85, 0.72)
        so = bpy.data.objects.new("key", sd); sc.collection.objects.link(so); so.rotation_euler = (math.radians(55), 0, math.radians(-35))
        fl = bpy.data.lights.new("fill", 'SUN'); fl.energy = 0.6; fl.color = (0.6, 0.7, 1.0)
        fo = bpy.data.objects.new("fill", fl); sc.collection.objects.link(fo); fo.rotation_euler = (math.radians(60), 0, math.radians(150))
        if "Puddle" in name:
            g = ground_box((-6, -0.05, -6), (6, 0, 6), (0.08, 0.08, 0.09))
        d = Vector((-0.75, -1.0, 0.55)).normalized()
        if name.startswith("Block") or name in ("HQPodiumDetail", "TowerBaseDetail"):
            d = Vector((-0.55, -1.0, 0.3)).normalized()
        cam = persp_cam(sc, ctr + d * size * 1.45, ctr, lens=50)
        setup_render(sc, 420, 420, 12, standard=False)
        sc.render.filepath = os.path.join(tdir, name + ".png")
        bpy.ops.render.render(write_still=True)
    with open(os.path.join(prev, "detail_stats.json"), "w") as f:
        json.dump(stats, f, indent=1)


def street_shots(prev):
    sc = bpy.context.scene
    clear_scene()
    dusk(sc)
    try:
        sc.eevee.use_shadows = True
    except Exception:
        pass
    # ground (Unity coords)
    ground_box((-400, -0.6, -200), (400, -0.02, 600), (0.07, 0.07, 0.075), "T_INF_Asphalt_Albedo.png", 4.0, 0.35)
    ground_box((-4, -0.3, -70), (4, 0.01, 99.2), (0.08, 0.08, 0.08), "T_INF_Road_Albedo.png", 8.0, 0.4)
    for x0, x1 in ((-7, -4), (4, 7)):
        ground_box((x0, -0.3, -70), (x1, 0.12, 52), (0.3, 0.3, 0.3), "T_INF_Sidewalk_Albedo.png", 2.4, 0.6)
    for x0, x1 in ((-24, -4), (4, 24)):
        ground_box((x0, -0.3, 52), (x1, 0.02, 100), (0.3, 0.3, 0.3), "T_INF_Sidewalk_Albedo.png", 2.4, 0.6)
    ground_box((28, -0.3, 177), (70, 1.2, 201), (0.3, 0.3, 0.3), "T_INF_Sidewalk_Albedo.png", 2.4, 0.6)
    # street buildings (same transforms as InfiltrationMapBuilder.BuildStreet, detail variants as TutorialDetailPass)
    for k, x, z, yaw in (("BlockA_1", -16, 6, 90), ("BlockB_1", -15, 26, 90), ("BlockD_2", -14, 43, 90),
                         ("BlockC_1", 17, 8, -90), ("BlockB_2", 15, 30, -90), ("BlockD_1", 14, 44, -90),
                         ("BlockC_2", -17, -22, 90), ("BlockA_2", 16, -20, -90), ("BlockB_1", -15, -44, 90), ("BlockD_2", 14, -42, -90)):
        uinst(k, x, 0, z, yaw)
    uinst("BlockC_1", 72, 0, 290, 180, lod=1); uinst("BlockA_1", -55, 0, 285, 180, lod=1); uinst("BlockC_2", -30, 0, 330, 180, lod=1)
    rng = random.Random(1307)
    kinds = ["BlockA_1", "BlockA_2", "BlockB_1", "BlockB_2", "BlockC_1", "BlockC_2", "BlockD_1", "BlockD_2"]
    for gx in range(-8, 10):
        for gz in range(-4, 15):
            x = gx * 30 + rng.random() * 8 - 4; z = gz * 30 + rng.random() * 8 - 4
            if abs(x) < 34 and -80 < z < 300: continue
            if 20 < x < 80 and 95 < z < 260: continue
            if abs(x) > 260 or z > 430 or z < -130: continue
            if rng.random() < 0.35: continue
            uinst(kinds[rng.randrange(8)], x, 0, z, rng.randrange(4) * 90, 0.85 + rng.random() * 0.4, lod=1 if (x * x + z * z) < 160 ** 2 else 2)
    # HQ
    old("HQTower", 0, 0, 300, 180); old("HQPodium", 21, 1.2, 200, 180)
    uinst("HQPodiumDetail", 21, 1.2, 200, 180); uinst("TowerBaseDetail", 0, 0, 300, 180)
    uinst("HQSecurityBooth", 33, 1.2, 198.4, 180); uinst("HQSecurityBooth", 61, 1.2, 198.4, 180)
    # street dressing
    zs = (4, 14, 24, 34, 44)
    for i, z in enumerate(zs):
        west = i % 2 == 0
        x = -5.6 if west else 5.6
        uinst("StreetLamp", x, 0.12, z, 90 if west else -90)
        ax = x + (2.05 if west else -2.05)
        spot(u2b(ax, 7.7, z)[0], u2b(ax + (1.5 if west else -1.5), 0, z)[0], 1800)
    for z in range(-2, 50, 6):
        uinst("Bollard", -4.35, 0.12, z, 0); uinst("Bollard", 4.35, 0.12, z + 3, 0)
    old("ScannerFrame", 0, 0, 36, 0)
    uinst("CablesOverhead", 0, 10.0, 17, 0); uinst("CablesOverhead", 0, 9.6, 31.5, 0); uinst("CablesOverhead", 0, 10.3, 47, 0)
    uinst("VendingMachine", -6.55, 0.12, 18.5, 90); uinst("VendingMachine", -6.55, 0.12, 19.6, 90)
    uinst("TrashBin", -6.6, 0.12, 21.2, 90); uinst("TrashBags", 6.3, 0.12, 27.5, -90); uinst("CrateStack", 6.4, 0.12, 25.6, -90)
    uinst("HoloAdPylon", 6.45, 0.12, 40.5, -90); uinst("Billboard_4", 6.95, 8.0, 39.0, -90)
    uinst("Puddle_1", -1.8, 0.02, 12, 10); uinst("Puddle_3", 1.5, 0.02, 27, -5); uinst("Puddle_0", -5.2, 0.13, 33, 0); uinst("Puddle_2", 2.4, 0.02, 44, 0)
    uinst("DeliveryDrone", -3.0, 18.5, 22, 30); uinst("DeliveryDrone", 4.0, 20.0, 58, -60)
    # plaza / dock / side yard (subset)
    old("DockBuilding", 47, 0, 157, 180)
    for z in (113, 123, 133, 143, 152):
        old("SecurityWall", 30, 0, z, 90); old("SecurityWall", 68, 0, z, 90)
    for x in (35, 45, 55, 63):
        old("SecurityWall", x, 0, 104, 0)
    ground_box((30, -0.3, 104), (68, 0.0, 157), (0.07, 0.07, 0.075), "T_INF_Asphalt_Albedo.png", 4.0, 0.3)
    for (x, y, z, yaw) in ((40, 0, 128, 90), (40, 2.59, 128.4, 92), (53, 0, 134, 90), (45, 0, 144.5, 0), (59, 0, 141.5, 90)):
        old("Container", x, y, z, yaw)
    old("Truck", 35.5, 0, 146.5, 180)
    old("FloodMast", 32.5, 0, 106.5, 25); old("FloodMast", 65.5, 0, 106.5, -25)
    # dock dressing = TutorialDetailPass.Slots (same positions / aims)
    for x in (35, 47, 59):
        uinst("BayLamp", x, 7.0, 156.98, 180)
        spot(u2b(x, 6.75, 156.33)[0], u2b(x, 1.2, 154.2)[0], 900, (1, 0.94, 0.85), 110)
    for x in (33.5, 64.5):
        uinst("BeaconAmber", x, 6.6, 104.45, 0); point(u2b(x, 6.85, 104.75)[0], 40)
    for (x, z, yaw, aim) in ((30.45, 124, 90, (46, 0, 128)), (67.55, 138, -90, (52, 0, 140))):
        uinst("WallFloodRig", x, 6.6, z, yaw)
        p = (x + (1.55 if yaw == 90 else -1.55), 6.3, z)
        spot(u2b(*p)[0], u2b(*aim)[0], 9000, (0.92, 0.96, 1.0), 78)
    for (x, z, yaw, aim) in ((64.2, 113.5, -45, (50, 0, 126)), (32.3, 134.5, 90, (44, 0, 138))):
        uinst("LightTower", x, 0, z, yaw)
        spot(u2b(x, 7.0, z)[0], u2b(*aim)[0], 9000, (0.92, 0.96, 1.0), 72)
    uinst("WallUtilityKit", 30.45, 0, 131, 90); uinst("WallUtilityKit", 67.55, 0, 125, -90); uinst("Billboard_0", 67.55, 2.4, 147.5, -90)
    for (n, x, z, yaw) in (("Puddle_3", 47, 120, 20), ("Puddle_1", 56, 126, -15), ("Puddle_0", 38, 138, 0), ("Puddle_2", 50.5, 150.5, 0), ("Puddle_1", 62.5, 151, 80), ("Puddle_2", 34, 112, 0)):
        uinst(n, x, 0.012, z, yaw)
    uinst("PalletBoxes", 66.75, 0, 120.5, -90); uinst("CrateStack", 31.3, 0, 117, 90); uinst("TrashBags", 66.8, 0, 126.3, -90)
    uinst("WaterBarrier", 31.2, 0, 127.4, 90)
    # existing builder lights (dock floods)
    spot(u2b(32.5, 10.5, 106.0)[0], u2b(45, 0, 140)[0], 9000, (0.9, 0.95, 1.0), 70); spot(u2b(65.5, 10.5, 106.0)[0], u2b(55, 0, 145)[0], 8000, (0.9, 0.95, 1.0), 70)
    # side yard lights
    spot(u2b(44, 7.6, 197.5)[0], u2b(44, 1.2, 195)[0], 1500); spot(u2b(50, 7.6, 197.5)[0], u2b(50, 1.2, 195)[0], 1500)
    point(u2b(47, 6, 207)[0], 600, (1, 0.96, 0.9))
    try:
        sc.eevee.volumetric_end = 600
    except Exception:
        pass
    # no world volume: EEVEE (Next) world volumes occlude world lighting entirely
    shots = [("shot_street", u2b(0.6, 1.9, 1.5)[0], u2b(0, 14, 80)[0], 20),
             ("shot_dock", u2b(36, 3.0, 109)[0], u2b(52, 1.5, 145)[0], 18),
             ("shot_hq_entrance", u2b(44, 3.2, 181)[0], u2b(47, 9.0, 200)[0], 18)]
    for name, loc, tgt, lens in shots:
        for o in [o for o in sc.objects if o.type == 'CAMERA']:
            bpy.data.objects.remove(o, do_unlink=True)
        persp_cam(sc, loc, tgt, lens)
        setup_render(sc, 1600, 900, 32, standard=False)
        sc.view_settings.exposure = -0.2
        sc.render.filepath = os.path.join(prev, name + ".png")
        bpy.ops.render.render(write_still=True)
        print("SHOT", name)


if __name__ == "__main__":
    OUT, PREV = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    only = os.environ.get("INFD_ONLY", "")
    for spec in BLDG:
        if not only or spec[0] in only:
            building(spec)
    if not only:
        facade_panels()
        props()
    import tempfile
    impostors(os.path.join(PREV or tempfile.gettempdir(), "infd_imp"))
    print("KIT", json.dumps(EXPORTED))
    if PREV and not os.environ.get("INFD_NOPREV"):
        thumbnails(PREV)
        street_shots(PREV)
