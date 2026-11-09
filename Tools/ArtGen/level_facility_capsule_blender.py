"""Blender (5.x, headless) generator for the Level_Facility specimen capsule pod.
Run: blender -b --factory-startup --python level_facility_capsule_blender.py
Exports Assets/Art/ChuonPolish/Level_Facility/Meshes/SM_LF_SpecimenPod.fbx with objects
'Pod' (materials: Housing, Glass, Fluid, StatusRing) and 'Specimen' (material: Specimen)."""
import bpy, math

OUT = r"C:/Users/ikh33/source/chuon-test/Assets/Art/ChuonPolish/Level_Facility/Meshes/SM_LF_SpecimenPod.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    return m

M = {n: mat(n) for n in ("Housing", "Glass", "Fluid", "StatusRing", "Specimen")}

def cyl(name, r, z0, z1, verts=32, material="Housing", bevel=0.0, cap=True):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=z1 - z0, location=(0, 0, (z0 + z1) / 2),
                                        end_fill_type='NGON' if cap else 'NOTHING')
    o = bpy.context.active_object; o.name = name
    o.data.materials.append(M[material])
    if bevel > 0:
        b = o.modifiers.new("bev", 'BEVEL'); b.width = bevel; b.segments = 2; b.limit_method = 'ANGLE'
    bpy.ops.object.shade_smooth()
    return o

def box(name, size, loc, material="Housing", bevel=0.02):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object; o.name = name; o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(M[material])
    b = o.modifiers.new("bev", 'BEVEL'); b.width = bevel; b.segments = 2
    return o

parts = []
parts.append(cyl("Base", 0.86, 0.0, 0.34, material="Housing", bevel=0.03))
parts.append(cyl("BaseStep", 0.78, 0.34, 0.42, material="Housing", bevel=0.015))
parts.append(cyl("StatusRing", 0.74, 0.42, 0.47, material="StatusRing"))
parts.append(cyl("Glass", 0.70, 0.47, 2.52, verts=40, material="Glass", cap=False))
parts.append(cyl("Fluid", 0.66, 0.48, 2.18, verts=32, material="Fluid"))
parts.append(cyl("CapRing", 0.78, 2.52, 2.62, material="StatusRing"))
parts.append(cyl("Cap", 0.86, 2.62, 3.02, material="Housing", bevel=0.04))
parts.append(cyl("CapTop", 0.5, 3.02, 3.14, material="Housing", bevel=0.02))
# rear spine + bracing (pod stands against a wall, back = -Y)
parts.append(box("Spine", (0.42, 0.22, 3.1), (0, -0.9, 1.55)))
for z in (0.9, 2.1):
    parts.append(box("Brace", (0.18, 0.5, 0.1), (0, -0.68, z)))
# feed pipes from the cap into the spine
for x in (-0.28, 0.28):
    bpy.ops.curve.primitive_bezier_curve_add(location=(0, 0, 0))
    c = bpy.context.active_object; sp = c.data.splines[0]
    sp.bezier_points[0].co = (x, -0.3, 3.08); sp.bezier_points[0].handle_left = (x, -0.3, 2.9); sp.bezier_points[0].handle_right = (x, -0.3, 3.35)
    sp.bezier_points[1].co = (x, -0.9, 3.1); sp.bezier_points[1].handle_left = (x, -0.9, 3.4); sp.bezier_points[1].handle_right = (x, -0.9, 2.9)
    c.data.bevel_depth = 0.05; c.data.bevel_resolution = 3; c.data.resolution_u = 8
    c.data.materials.append(M["Housing"])
    bpy.ops.object.convert(target='MESH'); parts.append(bpy.context.active_object)

for p in parts:
    p.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
for p in parts:
    bpy.context.view_layer.objects.active = p
    for mod in list(p.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.ops.object.select_all(action='DESELECT')
for p in parts:
    p.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
pod = bpy.context.active_object; pod.name = "Pod"

# hunched humanoid silhouette suspended in the fluid
spec = []
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=0.14, location=(0, 0.08, 1.86)); spec.append(bpy.context.active_object)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=1, location=(0, 0, 1.42)); t = bpy.context.active_object
t.scale = (0.24, 0.16, 0.36); t.rotation_euler = (math.radians(12), 0, 0); spec.append(t)
for x in (-0.11, 0.11):
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.075, depth=0.8, location=(x, 0.02, 0.74)); l = bpy.context.active_object
    l.rotation_euler = (math.radians(-6), math.radians(4 if x > 0 else -4), 0); spec.append(l)
for x in (-0.3, 0.3):
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.06, depth=0.7, location=(x, 0.05, 1.32)); a = bpy.context.active_object
    a.rotation_euler = (math.radians(10), math.radians(18 if x > 0 else -18), 0); spec.append(a)
bpy.ops.object.select_all(action='DESELECT')
for s in spec:
    s.select_set(True)
bpy.context.view_layer.objects.active = spec[0]
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bpy.ops.object.join()
sp = bpy.context.active_object; sp.name = "Specimen"; sp.data.materials.clear(); sp.data.materials.append(M["Specimen"])
bpy.ops.object.shade_smooth()

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE',
                         add_leaf_bones=False, use_mesh_modifiers=True)
print("EXPORTED", OUT)
