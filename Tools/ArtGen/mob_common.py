"""Shared helpers for the TSUKASA mob art scripts (Blender 5.x headless). Not run directly.

Style (matches Type-D, typed_armor_blender.py): corporate-military hard surface -- graphite metal, off-white
ceramic shells, thin TSUKASA-red accents and light lines, dark visor glass. Crisp angle-limited bevels with
hardened + weighted normals; surface detail (panel seams, rivets, ribs, speckle) lives in a shared TRIM SHEET
(T_Mob_Trim_*.png, written by mob_trim_atlas.py) instead of triangles.

Trim-sheet contract (also used by Assets/Editor/MobPrefabBuilder.cs):
  material slots (FBX material names)  Mob_Metal, Mob_Ceramic, Mob_Accent, Mob_Rubber, Mob_Glow, Mob_Visor
  UV v-bands in the atlas               BANDS below; u tiles (texture wrap = repeat), TEXEL uv-units per metre.
Coordinates: Blender metres, Z up, character/weapon FRONT = -Y (Blender convention). Exported with
axis_forward='-Z', axis_up='Y', bake_space_transform -> Unity: front = +Z, up = +Y, identity rotations.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

MATS = ["Metal", "Ceramic", "Accent", "Rubber", "Glow", "Visor"]
BANDS = {"Metal": (0.0, 0.32), "Ceramic": (0.32, 0.62), "Accent": (0.62, 0.74),
         "Rubber": (0.74, 0.86), "Glow": (0.86, 0.93), "Visor": (0.93, 1.0)}
TEXEL = 1.8        # uv units per metre (u wraps)
BAND_MARGIN = 0.012
PREVIEW_COLORS = {"Metal": (0.05, 0.053, 0.06), "Ceramic": (0.62, 0.62, 0.6), "Accent": (0.42, 0.03, 0.035),
                  "Rubber": (0.02, 0.02, 0.022), "Glow": (0.95, 0.12, 0.14), "Visor": (0.02, 0.022, 0.03)}

MAT = {}


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.abspath(os.path.join(here, "..", ".."))
    out_dir = os.path.abspath(argv[0]) if len(argv) > 0 and argv[0] not in ("", "-") else os.path.join(root, "Assets", "Art", "ChuonPolish", "Mobs")
    preview = os.path.abspath(argv[1]) if len(argv) > 1 and argv[1] not in ("", "-") else None
    if preview and preview.replace("\\", "/").lower().startswith("c:/") and "temp" not in preview.lower():
        raise SystemExit("preview renders must go to a scratch/temp directory")
    os.makedirs(os.path.join(out_dir, "Meshes"), exist_ok=True)
    return out_dir, preview


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MAT.clear()
    for n in MATS:
        m = bpy.data.materials.new("Mob_" + n)
        m.diffuse_color = (*PREVIEW_COLORS[n], 1.0)
        MAT[n] = m


# ---------------------------------------------------------------- primitives (bmesh)
def _link(name, bm, mat):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for n in MATS:
        ob.data.materials.append(MAT[n])
    idx = MATS.index(mat)
    for p in ob.data.polygons:
        p.material_index = idx
    return ob


def box(name, size, loc=(0, 0, 0), mat="Metal", chamfer=0.0, rot=None):
    """Axis box; chamfer > 0 clips the 4 vertical edges (hard-surface look)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if chamfer > 0:
        edges = [e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co).normalized().z) > 0.99]
        bmesh.ops.bevel(bm, geom=edges, offset=chamfer, segments=1, affect='EDGES', profile=0.5)
    ob = _link(name, bm, mat)
    if rot:
        ob.rotation_euler = rot
    ob.location = loc
    return ob


def cyl(name, r, depth, loc=(0, 0, 0), mat="Metal", verts=24, axis='Z', r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r, radius2=r if r2 is None else r2, depth=depth)
    ob = _link(name, bm, mat)
    if axis == 'X':
        ob.rotation_euler = (0, math.pi / 2, 0)
    elif axis == 'Y':
        ob.rotation_euler = (math.pi / 2, 0, 0)
    ob.location = loc
    return ob


def prism(name, profile_xz, depth, loc=(0, 0, 0), mat="Metal"):
    """Extrude a closed XZ polygon (list of (x, z)) along Y by depth (centred)."""
    bm = bmesh.new()
    vs = [bm.verts.new((x, -depth / 2, z)) for x, z in profile_xz]
    f = bm.faces.new(vs)
    bmesh.ops.recalc_face_normals(bm, faces=[f])
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, depth, 0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _link(name, bm, mat)
    ob.location = loc
    return ob


def ellipsoid(name, radii, loc=(0, 0, 0), mat="Ceramic", seg=24, rings=14):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2]))
    ob = _link(name, bm, mat)
    ob.location = loc
    return ob


def set_mat(ob, mat, pred=None):
    idx = MATS.index(mat)
    mw = ob.matrix_world
    for p in ob.data.polygons:
        if pred is None or pred(mw @ p.center, p.normal):
            p.material_index = idx


# ---------------------------------------------------------------- modifiers
def _activate(ob):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)


def apply_transform(ob):
    _activate(ob)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def apply_mods(ob):
    _activate(ob)
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def boolean(target, cutter, op='DIFFERENCE'):
    m = target.modifiers.new("bool", 'BOOLEAN')
    m.operation = op
    m.object = cutter
    m.solver = 'EXACT'
    apply_mods(target)
    bpy.data.objects.remove(cutter, do_unlink=True)
    return target


def solidify(ob, t, offset=-1.0):
    m = ob.modifiers.new("solid", 'SOLIDIFY')
    m.thickness = t
    m.offset = offset
    m.use_even_offset = True
    apply_mods(ob)


def hard_bevel(ob, width=0.003, segments=2, angle=30.0):
    """Angle-limited bevel + hardened normals + weighted normals (keep sharp) = crisp hard-surface shading."""
    apply_transform(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    b = ob.modifiers.new("bevel", 'BEVEL')
    b.width = width
    b.segments = segments
    b.limit_method = 'ANGLE'
    b.angle_limit = math.radians(angle)
    b.harden_normals = True
    b.miter_outer = 'MITER_ARC'
    try:
        b.face_strength_mode = 'FSTR_ALL'
    except Exception:
        pass
    w = ob.modifiers.new("wn", 'WEIGHTED_NORMAL')
    w.keep_sharp = True
    w.use_face_influence = True
    apply_mods(ob)


def join(objs, name):
    objs = [o for o in objs if o is not None]
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def set_origin(ob, point):
    """Move the object origin to a world point without moving geometry."""
    p = Vector(point)
    mw = ob.matrix_world.copy()
    local = mw.inverted() @ p
    ob.data.transform(Matrix.Translation(-local))
    ob.matrix_world = mw @ Matrix.Translation(local)


def parent(child, par):
    mw = child.matrix_world.copy()
    child.parent = par
    child.matrix_world = mw


def empty(name, loc, par=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'ARROWS'
    e.empty_display_size = 0.05
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    if par is not None:
        parent(e, par)
    return e


# ---------------------------------------------------------------- trim-sheet UVs
def trim_uv(ob):
    """Per face: planar projection on the dominant axis, u in metres * TEXEL (wraps), v squeezed into the
    material's band. Deterministic. Call after all modifiers are applied."""
    me = ob.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uvl = me.uv_layers.active.data
    for p in me.polygons:
        n = p.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        if ax == 0:
            tu, tv = Vector((0, 1, 0)), Vector((0, 0, 1))
        elif ax == 1:
            tu, tv = Vector((1, 0, 0)), Vector((0, 0, 1))
        else:
            tu, tv = Vector((1, 0, 0)), Vector((0, 1, 0))
        pts = [me.vertices[me.loops[li].vertex_index].co for li in p.loop_indices]
        us = [c.dot(tu) * TEXEL for c in pts]
        vs = [c.dot(tv) * TEXEL for c in pts]
        name = MATS[p.material_index] if p.material_index < len(MATS) else "Metal"
        b0, b1 = BANDS[name]
        lo, hi = b0 + BAND_MARGIN, b1 - BAND_MARGIN
        h = hi - lo
        vmin, vmax = min(vs), max(vs)
        span = vmax - vmin
        for k, li in enumerate(p.loop_indices):
            if span > h:
                v = lo + (vs[k] - vmin) / span * h
            else:
                v = lo + (h - span) * 0.5 + (vs[k] - vmin)
            uvl[li].uv = (us[k], v)


# ---------------------------------------------------------------- export / preview
def export_fbx(objs, path):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='OFF',
                             use_tspace=False, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
    tris = 0
    for o in objs:
        if o.type == 'MESH':
            o.data.calc_loop_triangles()
            tris += len(o.data.loop_triangles)
    print("EXPORTED %s  tris=%d" % (path, tris))
    return tris


def _nodes(idb):
    try:
        idb.use_nodes = True
    except Exception:
        pass


def preview(out_dir, preview_dir, prefix, shots):
    """EEVEE turntable-ish shots with PBR materials driven by the trim atlas (if present)."""
    if not preview_dir:
        return
    os.makedirs(preview_dir, exist_ok=True)
    tex_dir = os.path.join(out_dir, "Textures")

    def tex(name, cs):
        p = os.path.join(tex_dir, name)
        if not os.path.exists(p):
            return None
        im = bpy.data.images.load(p, check_existing=True)
        im.colorspace_settings.name = cs
        return im

    alb, nrm, osm = tex("T_Mob_Trim_Albedo.png", 'sRGB'), tex("T_Mob_Trim_Normal.png", 'Non-Color'), tex("T_Mob_Trim_OSM.png", 'Non-Color')
    for key in ("Metal", "Ceramic", "Accent", "Rubber", "Visor"):
        m = MAT[key]; _nodes(m); nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(bsdf.outputs[0], out.inputs[0])
        if alb is None:
            bsdf.inputs["Base Color"].default_value = (*PREVIEW_COLORS[key], 1)
            continue
        ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = alb
        tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = nrm
        to = nt.nodes.new("ShaderNodeTexImage"); to.image = osm
        sep = nt.nodes.new("ShaderNodeSeparateColor"); nm = nt.nodes.new("ShaderNodeNormalMap")
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs[0].default_value = 1.0
        nt.links.new(to.outputs[0], sep.inputs[0])
        nt.links.new(ta.outputs[0], mul.inputs[6]); nt.links.new(sep.outputs[0], mul.inputs[7])
        nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
        nt.links.new(sep.outputs[1], inv.inputs[1]); nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
        nt.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
        nt.links.new(tn.outputs[0], nm.inputs["Color"]); nt.links.new(nm.outputs[0], bsdf.inputs["Normal"])
    g = MAT["Glow"]; _nodes(g); nt = g.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); e = nt.nodes.new("ShaderNodeEmission")
    e.inputs[0].default_value = (1.0, 0.16, 0.18, 1); e.inputs[1].default_value = 4.0
    nt.links.new(e.outputs[0], out.inputs[0])

    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 900; sc.render.resolution_y = 900
    sc.view_settings.view_transform = 'AgX'
    try:
        sc.eevee.taa_render_samples = 32
        sc.eevee.use_raytracing = True
    except Exception:
        pass
    world = bpy.data.worlds.new("w"); sc.world = world; _nodes(world)
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.03, 0.033, 0.04, 1); bg.inputs[1].default_value = 1.0

    def light(name, kind, loc, energy, color, size, tgt):
        ld = bpy.data.lights.new(name, kind); ld.energy = energy; ld.color = color
        if kind == 'AREA':
            ld.size = size
        lo = bpy.data.objects.new(name, ld); sc.collection.objects.link(lo); lo.location = loc
        d = Vector(tgt) - Vector(loc); lo.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

    for nm_, eye, tgt, lens in shots:
        for o in [o for o in sc.objects if o.type in ('LIGHT', 'CAMERA')]:
            bpy.data.objects.remove(o, do_unlink=True)
        t = Vector(tgt); e_ = Vector(eye); d = (e_ - t).length
        side = (e_ - t).cross(Vector((0, 0, 1))).normalized()
        light("key", 'AREA', t + (e_ - t) * 0.6 + side * d * 0.5 + Vector((0, 0, d * 0.6)), 120 * d * d, (1.0, 0.97, 0.93), d * 0.5, tgt)
        light("fill", 'AREA', t + (e_ - t) * 0.5 - side * d * 0.7, 30 * d * d, (0.75, 0.85, 1.0), d * 0.8, tgt)
        light("rim", 'AREA', t - (e_ - t) * 0.9 + Vector((0, 0, d * 0.5)), 110 * d * d, (1.0, 0.32, 0.3), d * 0.4, tgt)
        cd = bpy.data.cameras.new("cam"); cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
        cd.lens = lens
        cam.location = e_
        cam.rotation_euler = (t - e_).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(preview_dir, prefix + nm_ + ".png")
        bpy.ops.render.render(write_still=True)
