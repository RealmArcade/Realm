import os
import sys
import json
import math
import struct
import argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

CANONICAL_BONE_MAP = {
    "hips": ["mixamorig:Hips", "Hips", "mixamorig_Hips", "pelvis", "root", "bip01_hips", "bip01 hips"],
    "spine": ["mixamorig:Spine", "Spine", "mixamorig_Spine", "bip01_spine"],
    "chest": ["mixamorig:Spine1", "Chest", "mixamorig_Spine1", "spine1", "bip01_spine1"],
    "upperchest": ["mixamorig:Spine2", "UpperChest", "mixamorig_Spine2", "spine2", "bip01_spine2"],
    "neck": ["mixamorig:Neck", "Neck", "mixamorig_Neck", "bip01_neck"],
    "head": ["mixamorig:Head", "Head", "mixamorig_Head", "bip01_head"],
    "leftshoulder": ["mixamorig:LeftShoulder", "LeftShoulder", "shoulder_l", "l_shoulder", "clavicle_l", "l_clavicle"],
    "leftupperarm": ["mixamorig:LeftArm", "LeftUpperArm", "upperarm_l", "l_upperarm", "arm_l", "LeftArm"],
    "leftlowerarm": ["mixamorig:LeftForeArm", "LeftLowerArm", "forearm_l", "l_forearm", "lowerarm_l", "LeftForeArm"],
    "lefthand": ["mixamorig:LeftHand", "LeftHand", "hand_l", "l_hand", "LeftHand"],
    "rightshoulder": ["mixamorig:RightShoulder", "RightShoulder", "shoulder_r", "r_shoulder", "clavicle_r", "r_clavicle"],
    "rightupperarm": ["mixamorig:RightArm", "RightUpperArm", "upperarm_r", "r_upperarm", "arm_r", "RightArm"],
    "rightlowerarm": ["mixamorig:RightForeArm", "RightLowerArm", "forearm_r", "r_forearm", "lowerarm_r", "RightForeArm"],
    "righthand": ["mixamorig:RightHand", "RightHand", "hand_r", "r_hand", "RightHand"],
    "leftupperleg": ["mixamorig:LeftUpLeg", "LeftUpperLeg", "thigh_l", "l_thigh", "upperleg_l", "LeftUpLeg"],
    "leftlowerleg": ["mixamorig:LeftLeg", "LeftLowerLeg", "shin_l", "calf_l", "lowerleg_l", "l_calf", "LeftLeg"],
    "leftfoot": ["mixamorig:LeftFoot", "LeftFoot", "foot_l", "l_foot", "LeftFoot"],
    "lefttoes": ["mixamorig:LeftToeBase", "LeftToes", "toe_l", "toes_l", "l_toe", "LeftToeBase"],
    "rightupperleg": ["mixamorig:RightUpLeg", "RightUpperLeg", "thigh_r", "r_thigh", "upperleg_r", "RightUpLeg"],
    "rightlowerleg": ["mixamorig:RightLeg", "RightLowerLeg", "shin_r", "calf_r", "lowerleg_r", "r_calf", "RightLeg"],
    "rightfoot": ["mixamorig:RightFoot", "RightFoot", "foot_r", "r_foot", "RightFoot"],
    "righttoes": ["mixamorig:RightToeBase", "RightToes", "toe_r", "toes_r", "r_toe", "RightToeBase"]
}

def get_key_val(key_dict, *names, default=0.0):
    for name in names:
        if name in key_dict:
            return float(key_dict[name])
    return default

def sample_position(keys, time):
    if not keys:
        return (0.0, 0.0, 0.0)

    def k_t(k): return get_key_val(k, "Time", "time")
    def k_x(k): return get_key_val(k, "X", "x")
    def k_y(k): return get_key_val(k, "Y", "y")
    def k_z(k): return get_key_val(k, "Z", "z")

    if len(keys) == 1 or time <= k_t(keys[0]):
        return (k_x(keys[0]), k_y(keys[0]), k_z(keys[0]))
    if time >= k_t(keys[-1]):
        return (k_x(keys[-1]), k_y(keys[-1]), k_z(keys[-1]))

    for i in range(len(keys) - 1):
        t0 = k_t(keys[i])
        t1 = k_t(keys[i + 1])
        if t0 <= time <= t1:
            seg = t1 - t0
            t = (time - t0) / seg if seg > 0.00001 else 0.0
            x = k_x(keys[i]) + t * (k_x(keys[i + 1]) - k_x(keys[i]))
            y = k_y(keys[i]) + t * (k_y(keys[i + 1]) - k_y(keys[i]))
            z = k_z(keys[i]) + t * (k_z(keys[i + 1]) - k_z(keys[i]))
            return (x, y, z)
    return (k_x(keys[0]), k_y(keys[0]), k_z(keys[0]))

def slerp(q0, q1, t):
    w0, x0, y0, z0 = q0
    w1, x1, y1, z1 = q1
    dot = w0 * w1 + x0 * x1 + y0 * y1 + z0 * z1
    if dot < 0.0:
        w1, x1, y1, z1 = -w1, -x1, -y1, -z1
        dot = -dot
    if dot > 0.9995:
        w = w0 + t * (w1 - w0)
        x = x0 + t * (x1 - x0)
        y = y0 + t * (y1 - y0)
        z = z0 + t * (z1 - z0)
        length = math.sqrt(w * w + x * x + y * y + z * z)
        return (w / length, x / length, y / length, z / length) if length > 0 else (1.0, 0.0, 0.0, 0.0)
    theta = math.acos(max(-1.0, min(1.0, dot)))
    sin_theta = math.sin(theta)
    s0 = math.sin((1.0 - t) * theta) / sin_theta
    s1 = math.sin(t * theta) / sin_theta
    return (s0 * w0 + s1 * w1, s0 * x0 + s1 * x1, s0 * y0 + s1 * y1, s0 * z0 + s1 * z1)

def sample_rotation(keys, time):
    if not keys:
        return (1.0, 0.0, 0.0, 0.0)

    def k_t(k): return get_key_val(k, "Time", "time")
    def k_w(k): return get_key_val(k, "W", "w", default=1.0)
    def k_x(k): return get_key_val(k, "X", "x")
    def k_y(k): return get_key_val(k, "Y", "y")
    def k_z(k): return get_key_val(k, "Z", "z")

    if len(keys) == 1 or time <= k_t(keys[0]):
        return (k_w(keys[0]), k_x(keys[0]), k_y(keys[0]), k_z(keys[0]))
    if time >= k_t(keys[-1]):
        return (k_w(keys[-1]), k_x(keys[-1]), k_y(keys[-1]), k_z(keys[-1]))

    for i in range(len(keys) - 1):
        t0 = k_t(keys[i])
        t1 = k_t(keys[i + 1])
        if t0 <= time <= t1:
            seg = t1 - t0
            t = (time - t0) / seg if seg > 0.00001 else 0.0
            q0 = (k_w(keys[i]), k_x(keys[i]), k_y(keys[i]), k_z(keys[i]))
            q1 = (k_w(keys[i + 1]), k_x(keys[i + 1]), k_y(keys[i + 1]), k_z(keys[i + 1]))
            return slerp(q0, q1, t)
    return (k_w(keys[0]), k_x(keys[0]), k_y(keys[0]), k_z(keys[0]))

def find_node_idx_for_track(node_by_name, track_name):
    clean = track_name.lower().replace("mixamorig:", "").replace("mixamorig_", "").replace(":", "")
    for name, idx in node_by_name.items():
        n_clean = name.lower().replace("mixamorig:", "").replace("mixamorig_", "").replace(":", "")
        if n_clean == clean:
            return idx

    if clean in CANONICAL_BONE_MAP:
        for alias in CANONICAL_BONE_MAP[clean]:
            for name, idx in node_by_name.items():
                if name.lower() == alias.lower():
                    return idx

    for key, aliases in CANONICAL_BONE_MAP.items():
        if clean == key or clean in [a.lower() for a in aliases]:
            for alias in aliases:
                for name, idx in node_by_name.items():
                    if name.lower() == alias.lower():
                        return idx
    return None

def draw_border(img):
    w, h = img.size
    draw = ImageDraw.Draw(img)
    draw.rectangle([0, 0, w - 1, h - 1], outline=(61, 66, 82, 230))

def draw_shadow(img, ground_y=None):
    w, h = img.size
    shadow_img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(shadow_img)
    target_ground_y = ground_y if ground_y is not None else int(round(h * (118.0 / 128.0)))
    radius_x = max(2, int(round(26.0 * (w / 128.0))))
    radius_y = max(1, int(round(7.0 * (h / 128.0))))
    center_x = w // 2
    draw.ellipse(
        [center_x - radius_x, target_ground_y - radius_y, center_x + radius_x, target_ground_y + radius_y],
        fill=(10, 13, 18, 180)
    )
    blur_radius = max(1, int(round(2.5 * (w / 128.0))))
    shadow_img = shadow_img.filter(ImageFilter.GaussianBlur(radius=blur_radius))
    img.alpha_composite(shadow_img)

def main():
    parser = argparse.ArgumentParser(description="Render animated humanoid GLB with .ranim keyframes using bpy")
    parser.add_argument("--model", required=True, help="Path to .glb model")
    parser.add_argument("--anim", required=True, help="Path to .ranim JSON file")
    parser.add_argument("--output", required=True, help="Output destination (.webp, .png, or .gif)")
    parser.add_argument("--format", default="webp", choices=["webp", "spritesheet", "gif"], help="Output format (webp, spritesheet)")
    parser.add_argument("--fps", type=float, default=12.0, help="Frames per second")
    parser.add_argument("--max-frames", type=int, default=None, help="Maximum frame count")
    parser.add_argument("--width", type=int, default=128, help="Frame width")
    parser.add_argument("--height", type=int, default=128, help="Frame height")
    parser.add_argument("--scale", type=float, default=1.0, help="Model scale factor")
    parser.add_argument("--quality", type=int, default=95, help="WebP quality factor (1-100)")
    parser.add_argument("--lossless", action="store_true", help="Use lossless WebP compression")
    parser.add_argument("--no-border", action="store_true", help="Disable border")
    parser.add_argument("--no-shadow", action="store_true", help="Disable floor shadow")
    args = parser.parse_args()

    import bpy
    import mathutils

    model_path = os.path.abspath(args.model)
    anim_path = os.path.abspath(args.anim)
    output_path = os.path.abspath(args.output)

    with open(model_path, "rb") as f:
        data = f.read()

    chunk_len, chunk_type = struct.unpack("<II", data[12:20])
    gltf_data = json.loads(data[20:20 + chunk_len].decode("utf-8"))
    nodes = gltf_data.get("nodes", [])

    node_by_name = {}
    parent_map = {}
    for idx, node in enumerate(nodes):
        name = node.get("name")
        if name:
            node_by_name[name] = idx
            for child_idx in node.get("children", []):
                parent_map[child_idx] = idx

    def get_node_rest_matrix(node):
        t = node.get("translation", [0.0, 0.0, 0.0])
        r = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
        s = node.get("scale", [1.0, 1.0, 1.0])
        mat_t = mathutils.Matrix.Translation(mathutils.Vector(t))
        mat_r = mathutils.Quaternion((r[3], r[0], r[1], r[2])).to_matrix().to_4x4()
        mat_s = mathutils.Matrix.Diagonal(mathutils.Vector((s[0], s[1], s[2], 1.0)))
        return mat_t, mat_r, mat_s, mat_t @ mat_r @ mat_s

    g_rest = {}
    def compute_g_rest(node_idx):
        if node_idx in g_rest:
            return g_rest[node_idx]
        _, _, _, local_m = get_node_rest_matrix(nodes[node_idx])
        if node_idx in parent_map:
            res = compute_g_rest(parent_map[node_idx]) @ local_m
        else:
            res = local_m
        g_rest[node_idx] = res
        return res

    for idx in range(len(nodes)):
        compute_g_rest(idx)

    with open(anim_path, "r", encoding="utf-8-sig") as f:
        anim_data = json.load(f)

    tracks_by_node = {}
    hips_pos_keys = None
    tracks_list = anim_data.get("Tracks", anim_data.get("tracks", []))
    for track in tracks_list:
        bname = track.get("BoneName", track.get("boneName", track.get("bone_name", "")))
        node_idx = find_node_idx_for_track(node_by_name, bname)
        if node_idx is not None:
            rot_keys = track.get("RotationKeys", track.get("rotationKeys", track.get("rotation_keys", [])))
            pos_keys = track.get("PositionKeys", track.get("positionKeys", track.get("position_keys", [])))
            tracks_by_node[node_idx] = {"rot": rot_keys, "pos": pos_keys}
            if "hips" in bname.lower() and pos_keys:
                hips_pos_keys = pos_keys

    bpy.ops.wm.read_factory_settings(use_empty=True)

    world = bpy.data.worlds.new("RealmWorld")
    world.color = (20.0 / 255.0, 23.0 / 255.0, 31.0 / 255.0)
    bpy.context.scene.world = world

    bpy.ops.import_scene.gltf(filepath=model_path)

    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    armature = armatures[0] if armatures else None

    if armature:
        for obj in list(bpy.context.scene.objects):
            if obj.type == "MESH":
                has_arm_mod = any(m.type == "ARMATURE" and m.object == armature for m in obj.modifiers)
                is_parented = (obj.parent == armature)
                has_vg = len(obj.vertex_groups) > 0
                if not has_arm_mod and not is_parented and not has_vg:
                    bpy.data.objects.remove(obj, do_unlink=True)

    mesh_objs = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]

    duration = anim_data.get("Duration", anim_data.get("duration", 1.0))
    if duration <= 0.0:
        duration = 1.0

    sample_fps = args.fps if args.fps > 0 else 12.0
    total_source_frames = max(1, int(math.ceil(duration * sample_fps)))

    modulus_step = 1
    if args.max_frames and args.max_frames > 0 and total_source_frames > args.max_frames:
        modulus_step = max(1, int(math.ceil(total_source_frames / args.max_frames)))

    selected_times = []
    for frame_idx in range(total_source_frames):
        if (frame_idx % modulus_step) == 0:
            time = (frame_idx / float(total_source_frames)) * duration
            selected_times.append(time)

    if not selected_times:
        selected_times.append(0.0)

    m_conv_4x4 = mathutils.Matrix(((1.0, 0.0, 0.0, 0.0),
                                   (0.0, 0.0, -1.0, 0.0),
                                   (0.0, 1.0, 0.0, 0.0),
                                   (0.0, 0.0, 0.0, 1.0)))
    m_conv_4x4_inv = m_conv_4x4.inverted()

    first_p = sample_position(hips_pos_keys, 0.0) if hips_pos_keys else (0.0, 0.0, 0.0)
    last_p = sample_position(hips_pos_keys, duration) if hips_pos_keys else (0.0, 0.0, 0.0)
    drift_x = last_p[0] - first_p[0]
    drift_z = last_p[2] - first_p[2]

    def apply_pose_at_time(time_val):
        g_anim = {}
        def compute_g_anim(node_idx):
            if node_idx in g_anim:
                return g_anim[node_idx]
            mat_t, mat_r, mat_s, _ = get_node_rest_matrix(nodes[node_idx])
            track = tracks_by_node.get(node_idx)
            if track:
                rot_keys = track.get("rot", [])
                if rot_keys:
                    w, x, y, z = sample_rotation(rot_keys, time_val)
                    mat_r = mathutils.Quaternion((w, x, y, z)).to_matrix().to_4x4()
                if hips_pos_keys and "hips" in nodes[node_idx].get("name", "").lower():
                    cur_p = sample_position(hips_pos_keys, time_val)
                    prog = time_val / duration if duration > 0.0 else 0.0
                    dx = (cur_p[0] - first_p[0]) - drift_x * prog
                    dy = cur_p[1] - first_p[1]
                    dz = (cur_p[2] - first_p[2]) - drift_z * prog
                    orig_t = nodes[node_idx].get("translation", [0.0, 0.0, 0.0])
                    mat_t = mathutils.Matrix.Translation(mathutils.Vector((orig_t[0] + dx, orig_t[1] + dy, orig_t[2] + dz)))
            local_anim = mat_t @ mat_r @ mat_s
            if node_idx in parent_map:
                res = compute_g_anim(parent_map[node_idx]) @ local_anim
            else:
                res = local_anim
            g_anim[node_idx] = res
            return res

        for n_idx in range(len(nodes)):
            compute_g_anim(n_idx)

        if not armature:
            return

        s_blender_dict = {}
        for n_idx, g_a in g_anim.items():
            node_name = nodes[n_idx].get("name")
            if node_name:
                g_r = g_rest[n_idx]
                s_gltf = g_a @ g_r.inverted()
                s_blender_dict[node_name] = m_conv_4x4 @ s_gltf @ m_conv_4x4_inv

        for pbone in armature.pose.bones:
            s_child = s_blender_dict.get(pbone.name, mathutils.Matrix.Identity(4))
            if pbone.parent:
                s_parent = s_blender_dict.get(pbone.parent.name, mathutils.Matrix.Identity(4))
                delta_s = s_parent.inverted() @ s_child
            else:
                delta_s = s_child
            m_rest = pbone.bone.matrix_local
            m_basis = m_rest.inverted() @ delta_s @ m_rest
            loc, rot, scale = m_basis.decompose()
            pbone.rotation_mode = "QUATERNION"
            pbone.location = loc
            pbone.rotation_quaternion = rot
            pbone.scale = scale

    all_coords = []
    sample_bbox_times = [0.0, duration * 0.25, duration * 0.5, duration * 0.75]
    for stime in sample_bbox_times:
        apply_pose_at_time(stime)
        bpy.context.view_layer.update()
        eval_dg = bpy.context.evaluated_depsgraph_get()
        for obj in mesh_objs:
            eval_obj = obj.evaluated_get(eval_dg)
            eval_mesh = eval_obj.to_mesh()
            matrix = eval_obj.matrix_world
            for v in eval_mesh.vertices:
                all_coords.append(matrix @ v.co)
            eval_obj.to_mesh_clear()

    if all_coords:
        min_x = min(v.x for v in all_coords)
        max_x = max(v.x for v in all_coords)
        min_y = min(v.y for v in all_coords)
        max_y = max(v.y for v in all_coords)
        min_z = min(v.z for v in all_coords)
        max_z = max(v.z for v in all_coords)
    else:
        min_x, max_x = -0.5, 0.5
        min_y, max_y = -0.5, 0.5
        min_z, max_z = -0.9, 0.9

    center_x = (min_x + max_x) * 0.5
    center_y = (min_y + max_y) * 0.5
    center_z = (min_z + max_z) * 0.5
    height_dim = max(0.5, max_z - min_z)
    width_dim = max(0.5, max_x - min_x)
    depth_dim = max(0.5, max_y - min_y)

    azimuth = 0.42
    elevation = 0.18
    dist = 10.0

    proj_w = width_dim * math.cos(azimuth) + depth_dim * math.sin(azimuth)
    proj_h = height_dim * math.cos(elevation) + depth_dim * math.sin(elevation)
    max_extent = max(proj_w, proj_h, height_dim, width_dim)

    cam_data = bpy.data.cameras.new("OrthoCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = (max_extent * 1.25) / max(0.1, args.scale)
    cam_obj = bpy.data.objects.new("OrthoCam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    bpy.context.scene.camera = cam_obj

    cam_x = center_x + dist * math.cos(elevation) * math.sin(azimuth)
    cam_y = center_y - dist * math.cos(elevation) * math.cos(azimuth)
    cam_z = center_z + dist * math.sin(elevation)
    cam_obj.location = (cam_x, cam_y, cam_z)

    direction = mathutils.Vector((center_x - cam_x, center_y - cam_y, center_z - cam_z)).normalized()
    rot_quat = direction.to_track_quat("-Z", "Y")
    cam_obj.rotation_mode = "QUATERNION"
    cam_obj.rotation_quaternion = rot_quat

    key_light_data = bpy.data.lights.new("KeySun", type="SUN")
    key_light_data.energy = 3.2
    key_light_data.color = (1.0, 0.98, 0.95)
    key_light_obj = bpy.data.objects.new("KeySun", key_light_data)
    bpy.context.collection.objects.link(key_light_obj)
    key_light_obj.rotation_euler = (math.radians(45), math.radians(15), math.radians(35))

    fill_light_data = bpy.data.lights.new("FillSun", type="SUN")
    fill_light_data.energy = 1.4
    fill_light_data.color = (0.7, 0.8, 1.0)
    fill_light_obj = bpy.data.objects.new("FillSun", fill_light_data)
    bpy.context.collection.objects.link(fill_light_obj)
    fill_light_obj.rotation_euler = (math.radians(-30), math.radians(20), math.radians(-145))

    rim_light_data = bpy.data.lights.new("RimSun", type="SUN")
    rim_light_data.energy = 1.8
    rim_light_data.color = (0.85, 0.92, 1.0)
    rim_light_obj = bpy.data.objects.new("RimSun", rim_light_data)
    bpy.context.collection.objects.link(rim_light_obj)
    rim_light_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(45))

    render_scale = 2
    bpy.context.scene.render.resolution_x = args.width * render_scale
    bpy.context.scene.render.resolution_y = args.height * render_scale
    bpy.context.scene.render.image_settings.file_format = "PNG"
    bpy.context.scene.render.image_settings.color_mode = "RGBA"
    bpy.context.scene.render.film_transparent = True

    try:
        engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
        if "BLENDER_EEVEE_NEXT" in engines:
            bpy.context.scene.render.engine = "BLENDER_EEVEE_NEXT"
        elif "BLENDER_EEVEE" in engines:
            bpy.context.scene.render.engine = "BLENDER_EEVEE"
    except Exception:
        pass

    if hasattr(bpy.context.scene, "eevee"):
        eevee = bpy.context.scene.eevee
        if hasattr(eevee, "taa_render_samples"):
            eevee.taa_render_samples = 64
        if hasattr(eevee, "render_samples"):
            eevee.render_samples = 64
        if hasattr(eevee, "use_gtao"):
            eevee.use_gtao = True
        if hasattr(eevee, "use_ssr"):
            eevee.use_ssr = True

    if hasattr(bpy.context.scene, "view_settings"):
        try:
            bpy.context.scene.view_settings.view_transform = "Standard"
        except Exception:
            pass
        try:
            bpy.context.scene.view_settings.look = "None"
        except Exception:
            pass

    computed_ground_y = int(round(args.height * (0.5 + ((center_z - min_z) * math.cos(elevation)) / cam_data.ortho_scale)))
    computed_ground_y = min(args.height - 4, max(4, computed_ground_y))

    temp_dir = Path(output_path).parent / f"bpy_tmp_{os.getpid()}"
    temp_dir.mkdir(parents=True, exist_ok=True)

    resample_filter = getattr(Image, "Resampling", Image).LANCZOS

    rendered_images = []
    try:
        for idx, time_val in enumerate(selected_times):
            apply_pose_at_time(time_val)
            bpy.context.view_layer.update()

            frame_file = temp_dir / f"frame_{idx:04d}.png"
            bpy.context.scene.render.filepath = str(frame_file)
            bpy.ops.render.render(write_still=True)

            if frame_file.exists():
                raw_img = Image.open(str(frame_file)).convert("RGBA")
                if render_scale > 1:
                    img = raw_img.resize((args.width, args.height), resample_filter)
                else:
                    img = raw_img

                bg = Image.new("RGBA", (args.width, args.height), (20, 23, 31, 255))
                if not args.no_shadow:
                    draw_shadow(bg, ground_y=computed_ground_y)
                bg.alpha_composite(img)

                if not args.no_border:
                    draw_border(bg)

                rendered_images.append(bg)

        if not rendered_images:
            raise RuntimeError("No frames were rendered by Blender.")

        out_dir = Path(output_path).parent
        out_dir.mkdir(parents=True, exist_ok=True)

        is_spritesheet = (
            args.format.lower() == "spritesheet" or
            (output_path.lower().endswith(".png") and not output_path.lower().endswith(".webp"))
        )

        if is_spritesheet:
            sheet_width = args.width * len(rendered_images)
            sheet_height = args.height
            spritesheet = Image.new("RGBA", (sheet_width, sheet_height), (0, 0, 0, 0))
            for i, frame in enumerate(rendered_images):
                spritesheet.paste(frame, (i * args.width, 0))
            spritesheet.save(output_path, "PNG", optimize=True)
        else:
            frame_duration_ms = max(20, int(round((duration / len(rendered_images)) * 1000.0)))
            rendered_images[0].save(
                output_path,
                "WEBP",
                save_all=True,
                append_images=rendered_images[1:],
                duration=frame_duration_ms,
                loop=0,
                quality=args.quality,
                lossless=args.lossless,
                method=6
            )

        print(f"Successfully rendered {len(rendered_images)} frames -> {output_path}")

    finally:
        for f in temp_dir.glob("*.png"):
            try:
                f.unlink()
            except Exception:
                pass
        try:
            temp_dir.rmdir()
        except Exception:
            pass

if __name__ == "__main__":
    main()
