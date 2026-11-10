"""Level_Facility HQ lobby DETAIL kit -- shared tables (no third-party imports; used by Python 3 and Blender).

  lobby_detail_textures.py  -> T_LFD_Atlas_* (cells = ATLAS below)
  lobby_detail_blender.py   -> SM_LFD_*.fbx (atlas roles = 500 + ATLAS_KEYS.index(key)) + previews
  write_layout(path)        -> LFD_Layout.json, read by Assets/Editor/LobbyDetailPass.cs (Apply Lobby Detail Pass)

Coordinates (Unity, metres):
  frame "hall"  = UpperFloor_HQLobby/lobby_object position (currently (0, 40, 0)); hall-local y = 0 is the lobby floor.
  frame "world" = scene origin (B4 hangar floor y = 0).
  yaw = direction the prefab FRONT faces (Unity local +Z), degrees around +Y (0 = +Z, 90 = +X).
  Wall kinds: pos = point on the wall plane, y = bottom of the piece; 'off' = extra offset off the wall (stacking on panels).
Measured from lobby_object.fbx (hall: x +-12.03, z +-19.48; aisle soffit y 5 for |x| > 9.37 (front) / 6.23 (rear, z < -5.4);
nave ceiling y 20; core pillars z -4.1..-6.8 with the 6 m route opening at |x| < 2.8) and the scene (entry corridor
x +-2.4 z 19.5..33.65 ceiling 3.6; ribs z 21.5/25.1/28.7/32.3; glass deck x +-5 z 6..18; desk x +-3.6 z -1.1..0.1).
"""
import json

ATLAS_SIZE = 2048
# key: (x, y, w, h) pixels, top-left origin
ATLAS = {
    "sign_lift": (0, 0, 1024, 256), "sign_showroom": (1024, 0, 1024, 256),
    "sign_reception": (0, 256, 1024, 256), "sign_skydeck": (1024, 256, 1024, 256),
    "floor_1F": (0, 512, 512, 512), "floor_B4": (512, 512, 512, 512),
    "ind_lock": (1024, 512, 512, 256), "ind_b4": (1024, 768, 512, 256),
    "directory": (1536, 512, 256, 512),
    "halo_cyan": (1792, 512, 64, 512), "screen_off": (1856, 512, 64, 512), "volt": (1920, 512, 64, 512),
    "wash": (1984, 512, 64, 512),
    "screen_ad": (0, 1024, 1024, 576), "screen_news": (1024, 1024, 1024, 576),
    "emblem_inlay": (0, 1600, 448, 448), "stone_field": (448, 1600, 448, 448),
    "foliage": (896, 1600, 448, 448), "runner": (1344, 1600, 448, 448), "warn_plate": (1792, 1600, 256, 448),
}
ATLAS_KEYS = list(ATLAS.keys())


def atlas_uv(key):
    """(u0, v0, du, dv) in UV space (v up)."""
    x, y, w, h = ATLAS[key]
    S = float(ATLAS_SIZE)
    return (x / S, 1.0 - (y + h) / S, w / S, h / S)


# ------------------------------------------------------------------ slots
# kinds: ground (snapped, collider + carving, full exclusion checks) | wall (snapped to wall, no collider unless prefab has one)
#        overhead (ceiling, no checks) | decal (floor, snapped, no collider) | overlay (exact pose, points/route checks only)
DEFAULTS = {"clr": 1.6, "off": 0.0, "sx": 1.0, "sy": 1.0, "nosnap": 0, "zoneOk": 0, "light": ""}


def _s(room, p, frame, x, y, z, yaw, kind, **kw):
    """Every optional field is written explicitly (Unity JsonUtility cannot tell 'missing' from 0).
    clr = route clearance (m) for ground / overlay pieces, off = extra offset from the wall, sx / sy = local X / Y scale,
    nosnap = keep the authored pose (no wall / ceiling ray snap), zoneOk = allowed inside a combat zone, light = rig light id."""
    d = {"room": room, "p": p, "frame": frame, "x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "yaw": float(yaw), "kind": kind}
    d.update(DEFAULTS)
    if "snap" in kw:
        kw["nosnap"] = 0 if kw.pop("snap") else 1
    d.update(kw)
    return d


def slots():
    S = []
    H = "hall"
    # ---------------- 01 entry corridor
    R = "01_EntryCorridor"
    for z in (23.3, 26.9, 30.5):
        S.append(_s(R, "CorridorPanel", H, -2.4, 0, z, 90, "wall"))
        S.append(_s(R, "CorridorPanel", H, 2.4, 0, z, -90, "wall"))
    for z in (21.45, 25.05, 28.65, 32.25):
        S.append(_s(R, "CorridorCeiling", H, 0, 3.6, z, 0, "overhead"))
    S.append(_s(R, "FloorInlay_Emblem", H, 0, 0, 20.5, 0, "decal"))
    for z in (23.9, 27.5, 31.1):
        S.append(_s(R, "FloorInlay_Runner", H, 0, 0, z, 0, "decal"))
    S.append(_s(R, "SecurityScanArch", H, 0, 0, 20.6, 0, "overlay", clr=0.9))
    S.append(_s(R, "SpeedGate_Pair", H, -1.58, 0, 25.6, 0, "ground", clr=0.8))
    S.append(_s(R, "SpeedGate_Pair", H, 1.58, 0, 25.6, 0, "ground", clr=0.8))
    S.append(_s(R, "SignWayfinding_Skydeck", H, 2.4, 2.3, 22.0, -90, "wall", off=0.025))
    S.append(_s(R, "SignWayfinding_Reception", H, -2.4, 2.3, 30.8, 90, "wall", off=0.025))
    S.append(_s(R, "SignFloor_1F", H, 2.4, 1.2, 33.0, -90, "wall"))
    S.append(_s(R, "SloganLightbox_7", H, 0, 0.9, 33.65, 180, "wall"))
    # ---------------- 02 atrium skydeck (glass deck x +-5, z 6..18) + north strip
    R = "02_AtriumSkydeck"
    for sx in (-1, 1):
        for z in (7, 9, 11, 13, 15, 17):
            S.append(_s(R, "GlassBalustrade_2m", H, sx * 5.3, 0, z, 90 * sx, "ground", clr=1.2))
        S.append(_s(R, "Planter_Tall", H, sx * 3.5, 0, 18.95, 180, "ground"))
        S.append(_s(R, "HoloInfoPillar", H, sx * 5.25, 0, 18.95, 180, "ground"))
        S.append(_s(R, "WaitingBench", H, sx * 7.7, 0, 18.95, 180, "ground"))
        S.append(_s(R, "Planter_Low", H, sx * 10.7, 0, 18.95, 180, "ground"))
        for z in (3.5, 9.5, 15.5):
            S.append(_s(R, "ColumnClad", H, sx * 9.7, 0, z, -90 * sx, "ground"))
        for i, z in enumerate((17.98, 14.98, 11.98, 8.98, 5.98, 2.98, -0.02)):
            S.append(_s(R, "WallPanel_Tall", H, sx * 9.37, 5.0, z, -90 * sx, "wall"))
    S.append(_s(R, "WallScreen_Ad", H, -9.37, 7.6, 8.98, 90, "wall", off=0.07))
    S.append(_s(R, "WallScreen_News", H, 9.37, 7.8, 8.98, -90, "wall", off=0.07))
    S.append(_s(R, "SloganLightbox_4", H, 0, 5.4, 19.48, 180, "wall"))
    # ---------------- 03 front desk / reception (desk anchor (0, 0, 0.1), visitors on +Z)
    R = "03_FrontDesk"
    S.append(_s(R, "ReceptionDeskTop", H, 0, 0, 0.1, 0, "overlay", clr=0.0))
    S.append(_s(R, "ReceptionChair", H, -1.5, 0, -1.85, 0, "ground", clr=0.5, zoneOk=1))
    S.append(_s(R, "ReceptionChair", H, 1.5, 0, -1.85, 0, "ground", clr=0.5, zoneOk=1))
    for x in (-3.0, 0.0, 3.0):
        S.append(_s(R, "FloorInlay_Field3", H, x, 0, 3.0, 0, "decal"))
    for x in (-4.0, 0.0, 4.0):
        S.append(_s(R, "LightRig_LinearPendant", H, x, 20.0, 1.2, 90, "overhead"))
    # ---------------- 04 showroom aisles (outer walls x +-12.03, soffit y 5)
    R = "04_Showroom"
    for sx in (-1, 1):
        for z, v in ((-1.0, "A"), (2.0, "A"), (5.0, "B"), (8.0, "B"), (11.0, "B"), (14.0, "B"), (17.0, "A")):
            S.append(_s(R, "WallPanel_" + v, H, sx * 12.03, 0, z, -90 * sx, "wall"))
        for z in (-3.5, -0.5, 2.5, 5.5, 8.5, 11.5, 14.5, 17.5):
            S.append(_s(R, "CeilingCoffer_Aisle", H, sx * 10.7, 5.0, z, 0, "overhead"))
        S.append(_s(R, "SignWayfinding_Lift", H, sx * 12.03, 2.55, 0.5, -90 * sx, "wall", off=0.065))
        S.append(_s(R, "SignWayfinding_Showroom", H, sx * 12.03, 2.55, 17.0, -90 * sx, "wall", off=0.065))
    S.append(_s(R, "CleaningRobot", H, -11.25, 0, 1.0, 90, "ground"))
    # ---------------- 06 executive lift corridor (behind the core, z < -6.8; drone wave + mob encounter space)
    R = "06_ExecLift"
    S.append(_s(R, "ElevatorPortal_Main", H, 0, 0, -18.48, 0, "wall", snap=0))
    for sx in (-1, 1):
        for z in (-7.75, -14.75, -17.75):
            S.append(_s(R, "WallPanel_A", H, sx * 12.03, 0, z, -90 * sx, "wall"))
        for x in (6.45, 9.45):
            S.append(_s(R, "WallPanel_A", H, sx * x, 0, -19.48, 0, "wall"))
        S.append(_s(R, "SignWayfinding_Lift", H, sx * 6.45, 2.55, -19.48, 0, "wall", off=0.065))
        for x in (7.68, 10.58):
            for z in (-17.45, -14.55, -11.65, -8.75):
                S.append(_s(R, "CeilingCoffer_Rear", H, sx * x, 5.0, z, 0, "overhead"))
        for z in (-17.3, -13.3, -9.3):
            S.append(_s(R, "CableTray_4m", H, sx * 11.35, 4.78, z, 90, "overhead", nosnap=1))
        for z in (-17.98, -14.98, -11.98, -8.98):
            S.append(_s(R, "WallPanel_Tall", H, sx * 6.23, 5.0, z, -90 * sx, "wall"))
        S.append(_s(R, "DuctRun_4m", H, sx * 6.23, 11.2, -16.0, -90 * sx, "wall", off=0.07))
        S.append(_s(R, "WallVent", H, sx * 6.23, 11.0, -9.4, -90 * sx, "wall", off=0.07))
    S.append(_s(R, "WallScreen_News", H, -6.23, 6.0, -12.0, 90, "wall", off=0.07))
    S.append(_s(R, "SloganLightbox_1", H, 6.23, 6.0, -12.0, -90, "wall", off=0.07))
    # ---------------- 07 nave ceilings (y 20)
    R = "07_Ceiling"
    for x in (-6.0, 0.0, 6.0):
        for z in (16.58, 10.78, 4.98, -0.82):
            S.append(_s(R, "CeilingCoffer_Nave", H, x, 20.0, z, 0, "overhead"))
    for x in (-3.115, 3.115):
        for z in (-16.58, -10.78):
            S.append(_s(R, "CeilingCoffer_Nave", H, x, 20.0, z, 0, "overhead"))
    # ---------------- 08 B4 hangar: lift landing (shaft front z -18.48, door |x| < 2.65; Door5 trims are computed by the tool)
    R = "08_B4"
    W = "world"
    for sx in (-1, 1):
        S.append(_s(R, "B4_DoorPost", W, sx * 2.95, 0, -18.48, 0, "wall", snap=0, sy=1.3))
        S.append(_s(R, "LightRig_CageLamp", W, sx * 4.15, 3.5, -18.48, 0, "wall", light="cage"))
        S.append(_s(R, "PipeRun_4m", W, sx * 4.76, 0, -21.6, 90 * sx, "wall"))
        S.append(_s(R, "HazardFloorEdge", W, sx * 1.3, 0, -18.2, 0, "decal"))
    S.append(_s(R, "B4_DoorHeader_6", W, 0, 5.2, -18.48, 0, "wall", snap=0, sx=1.0))
    S.append(_s(R, "SignFloor_B4", W, 3.75, 1.6, -18.48, 0, "wall", off=0.02))
    S.append(_s(R, "WarnPlate", W, -3.75, 1.5, -18.48, 0, "wall", off=0.02))
    return S


# Combat spaces / no-dressing zones for ground pieces (frame, x0, z0, x1, z1)
ZONES = [
    ("hall", -9.2, -3.9, 9.2, 5.2),      # front hall: teach android + patrols + first drones
    ("hall", -12.1, -19.6, 12.1, -6.7),  # executive lift corridor: drone wave + guard encounter
    ("world", -13.0, -13.0, 13.0, 13.0),  # B4 arena centre (~24 m kept clear)
]


def write_layout(path):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"version": 1, "slots": slots(), "zones": [{"frame": z[0], "x0": z[1], "z0": z[2], "x1": z[3], "z1": z[4]} for z in ZONES]},
                  f, indent=1)


if __name__ == "__main__":
    import sys, os
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Art",
                                                             "ChuonPolish", "Level_Facility", "Detail", "LFD_Layout.json")
    write_layout(out)
    print("slots", len(slots()))
