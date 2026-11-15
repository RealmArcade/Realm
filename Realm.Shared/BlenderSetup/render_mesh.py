import argparse
import math
import os
from pathlib import Path

import bpy
import mathutils
from PIL import Image, ImageDraw, ImageFont

VIEW_AZIMUTHS = {
    "front": 0.0,
    "three_quarter": 45.0,
    "side": 90.0,
    "back": 180.0,
    "left": 270.0,
}

DEFAULT_VIEWS = ["front", "three_quarter", "side", "back"]

RENDER_SUPERSAMPLE = 2

FRAMING_MARGIN = 1.12

KEY_LIGHT_ROTATION = (45.0, 15.0, 35.0)
FILL_LIGHT_ROTATION = (-30.0, 20.0, -145.0)
RIM_LIGHT_ROTATION = (-40.0, -30.0, 45.0)


def parse_arguments():
    parser = argparse.ArgumentParser(description="Render a static GLB model to a deterministic turnaround sheet using bpy")
    parser.add_argument("--model", required=True, help="Path to the .glb model to render")
    parser.add_argument("--output", required=True, help="Sheet path in sheet mode, output directory in views mode")
    parser.add_argument("--name", default=None, help="File name prefix used in views mode")
    parser.add_argument("--mode", default="sheet", choices=["sheet", "views"], help="sheet writes one image, views writes one image per view")
    parser.add_argument("--size", type=int, default=512, help="Width and height of the model area in pixels")
    parser.add_argument("--views", default=",".join(DEFAULT_VIEWS), help="Comma separated view names")
    parser.add_argument("--background", default="#14171F", help="Card background colour as #RRGGBB, or 'transparent'")
    parser.add_argument("--engine", default="auto", choices=["auto", "eevee", "workbench"], help="Render engine")
    parser.add_argument("--samples", type=int, default=32, help="EEVEE render samples")
    parser.add_argument("--elevation", type=float, default=18.0, help="Camera elevation above the horizon in degrees")
    parser.add_argument("--no-labels", action="store_true", help="Do not draw the view name under each image")
    return parser.parse_args()


def resolve_views(view_argument):
    names = [name.strip() for name in view_argument.split(",") if name.strip()]
    if not names:
        raise ValueError("At least one view name is required.")

    for name in names:
        if name not in VIEW_AZIMUTHS:
            raise ValueError(f"Unknown view '{name}'. Known views: {', '.join(VIEW_AZIMUTHS)}.")

    return names


def parse_background(value):
    if value is None or value.strip().lower() in ("transparent", "none", "alpha"):
        return (0, 0, 0, 0)

    text = value.strip().lstrip("#")
    if len(text) == 3:
        text = "".join(character * 2 for character in text)
    if len(text) != 6:
        raise ValueError(f"Background must be 'transparent' or a #RRGGBB colour, got '{value}'.")

    red = int(text[0:2], 16)
    green = int(text[2:4], 16)
    blue = int(text[4:6], 16)
    return (red, green, blue, 255)


def load_label_font(point_size):
    try:
        return ImageFont.load_default(size=point_size)
    except TypeError:
        pass

    try:
        return ImageFont.load_default()
    except Exception:
        return None


def collect_mesh_objects():
    return [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]


def compute_model_bounds(mesh_objects):
    min_corner = mathutils.Vector((math.inf, math.inf, math.inf))
    max_corner = mathutils.Vector((-math.inf, -math.inf, -math.inf))

    for obj in mesh_objects:
        matrix = obj.matrix_world
        for corner in obj.bound_box:
            point = matrix @ mathutils.Vector(corner)
            min_corner.x = min(min_corner.x, point.x)
            min_corner.y = min(min_corner.y, point.y)
            min_corner.z = min(min_corner.z, point.z)
            max_corner.x = max(max_corner.x, point.x)
            max_corner.y = max(max_corner.y, point.y)
            max_corner.z = max(max_corner.z, point.z)

    return min_corner, max_corner


def compute_view_frame(min_corner, max_corner, center, azimuth_degrees, elevation_degrees):
    azimuth = math.radians(azimuth_degrees)
    elevation = math.radians(elevation_degrees)

    to_camera = mathutils.Vector((
        math.sin(azimuth) * math.cos(elevation),
        -math.cos(azimuth) * math.cos(elevation),
        math.sin(elevation),
    )).normalized()

    forward = -to_camera
    right = forward.cross(mathutils.Vector((0.0, 0.0, 1.0)))
    if right.length < 1e-6:
        right = mathutils.Vector((1.0, 0.0, 0.0))
    right.normalize()
    up = right.cross(forward).normalized()

    half_right = 0.0
    half_up = 0.0
    for x in (min_corner.x, max_corner.x):
        for y in (min_corner.y, max_corner.y):
            for z in (min_corner.z, max_corner.z):
                offset = mathutils.Vector((x, y, z)) - center
                half_right = max(half_right, abs(offset.dot(right)))
                half_up = max(half_up, abs(offset.dot(up)))

    return to_camera, half_right, half_up


def create_world(scene):
    world = bpy.data.worlds.new("RealmTurnaroundWorld")
    world.color = (20.0 / 255.0, 23.0 / 255.0, 31.0 / 255.0)
    scene.world = world
    return world


def create_sun(name, energy, color, rotation_degrees):
    light_data = bpy.data.lights.new(name, type="SUN")
    light_data.energy = energy
    light_data.color = color
    light_object = bpy.data.objects.new(name, light_data)
    bpy.context.collection.objects.link(light_object)
    light_object.rotation_euler = tuple(math.radians(angle) for angle in rotation_degrees)
    return light_object


def create_lights(scene):
    key_light = create_sun("TurnaroundKeySun", 3.2, (1.0, 0.98, 0.95), KEY_LIGHT_ROTATION)
    fill_light = create_sun("TurnaroundFillSun", 1.4, (0.7, 0.8, 1.0), FILL_LIGHT_ROTATION)
    rim_light = create_sun("TurnaroundRimSun", 1.8, (0.85, 0.92, 1.0), RIM_LIGHT_ROTATION)
    return key_light, fill_light, rim_light


def apply_eevee_settings(scene, samples):
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in available_engines() else "BLENDER_EEVEE"

    if hasattr(scene, "eevee"):
        eevee = scene.eevee
        for attribute in ("taa_render_samples", "render_samples"):
            if hasattr(eevee, attribute):
                setattr(eevee, attribute, samples)
        if hasattr(eevee, "use_gtao"):
            eevee.use_gtao = True


def apply_workbench_settings(scene):
    scene.render.engine = "BLENDER_WORKBENCH"
    if not hasattr(scene, "display") or not hasattr(scene.display, "shading"):
        return

    shading = scene.display.shading
    for attribute, value in (("light", "STUDIO"), ("color_type", "TEXTURE"), ("show_shadows", True), ("show_cavity", True)):
        if hasattr(shading, attribute):
            setattr(shading, attribute, value)


def available_engines():
    try:
        return [engine.identifier for engine in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    except Exception:
        return []


def configure_engine(scene, requested_engine, samples):
    engines = available_engines()

    use_eevee = requested_engine == "eevee" or (
        requested_engine == "auto" and ("BLENDER_EEVEE_NEXT" in engines or "BLENDER_EEVEE" in engines)
    )

    if use_eevee:
        try:
            apply_eevee_settings(scene, samples)
            return "eevee"
        except Exception:
            pass

    apply_workbench_settings(scene)
    return "workbench"


def render_single_view(scene, camera_object, rotation_axes, azimuth_degrees, frame_file):
    for light_object, base_rotation in rotation_axes:
        light_object.rotation_euler = (
            math.radians(base_rotation[0]),
            math.radians(base_rotation[1]),
            math.radians(base_rotation[2]) + math.radians(azimuth_degrees),
        )

    scene.camera = camera_object
    scene.render.filepath = str(frame_file)
    bpy.ops.render.render(write_still=True)
    return Path(frame_file).exists()


def build_card(rendered_image, view_name, width, background, label_font, draw_labels):
    if draw_labels:
        label_height = max(18, int(round(width * 0.075)))
    else:
        label_height = 0

    card = Image.new("RGBA", (width, width + label_height), background)
    card.alpha_composite(rendered_image, (0, 0))

    if draw_labels and label_font is not None:
        draw = ImageDraw.Draw(card)
        text = view_name.replace("_", " ").upper()
        try:
            text_width = draw.textlength(text, font=label_font)
        except Exception:
            text_width = len(text) * label_font.size * 0.6

        draw.text(
            ((width - text_width) / 2.0, width + (label_height - label_font.size) / 2.0 - 1),
            text,
            font=label_font,
            fill=(198, 206, 222, 255),
        )

    return card


def main():
    args = parse_arguments()
    views = resolve_views(args.views)
    background = parse_background(args.background)

    if args.size < 16:
        raise ValueError("--size must be at least 16 pixels.")

    model_path = os.path.abspath(args.model)
    if not os.path.isfile(model_path):
        raise FileNotFoundError(f"Model not found: {model_path}")

    output_path = Path(os.path.abspath(args.output))
    name = args.name or Path(model_path).stem

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    create_world(scene)
    bpy.ops.import_scene.gltf(filepath=model_path)

    mesh_objects = collect_mesh_objects()
    if not mesh_objects:
        raise RuntimeError(f"No mesh geometry found in {model_path}.")

    bpy.context.view_layer.update()

    measured_min, measured_max = compute_model_bounds(mesh_objects)
    center = (measured_min + measured_max) * 0.5
    extent = measured_max - measured_min
    diagonal = max(0.001, math.sqrt(extent.x ** 2 + extent.y ** 2 + extent.z ** 2))

    frames = []
    for view_name in views:
        to_camera, half_right, half_up = compute_view_frame(
            measured_min,
            measured_max,
            center,
            VIEW_AZIMUTHS[view_name],
            args.elevation,
        )
        frames.append((view_name, to_camera, max(half_right, half_up)))

    largest_half_extent = max(frame[2] for frame in frames)
    ortho_scale = max(0.001, 2.0 * largest_half_extent * FRAMING_MARGIN)

    camera_data = bpy.data.cameras.new("TurnaroundCamera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = ortho_scale
    camera_data.clip_start = 0.01
    camera_data.clip_end = diagonal * 8.0 + 10.0
    camera_object = bpy.data.objects.new("TurnaroundCamera", camera_data)
    bpy.context.collection.objects.link(camera_object)

    key_light, fill_light, rim_light = create_lights(scene)
    rotation_axes = [
        (key_light, KEY_LIGHT_ROTATION),
        (fill_light, FILL_LIGHT_ROTATION),
        (rim_light, RIM_LIGHT_ROTATION),
    ]

    distance = diagonal * 3.0 + 1.0

    render_size = args.size * RENDER_SUPERSAMPLE
    scene.render.resolution_x = render_size
    scene.render.resolution_y = render_size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True

    if hasattr(scene, "view_settings"):
        try:
            scene.view_settings.view_transform = "Standard"
        except Exception:
            pass
        try:
            scene.view_settings.look = "None"
        except Exception:
            pass

    active_engine = configure_engine(scene, args.engine, args.samples)

    label_font = None
    if not args.no_labels:
        label_font = load_label_font(max(12, int(round(args.size * 0.055))))

    temp_dir = output_path.parent / f"bpy_mesh_tmp_{os.getpid()}"
    if args.mode == "views":
        temp_dir = output_path / f"bpy_mesh_tmp_{os.getpid()}"
    temp_dir.mkdir(parents=True, exist_ok=True)

    resample_filter = getattr(Image, "Resampling", Image).LANCZOS
    cards = []

    try:
        for view_name, to_camera, _ in frames:
            camera_object.location = center + to_camera * distance
            direction = (center - camera_object.location).normalized()
            camera_object.rotation_mode = "QUATERNION"
            camera_object.rotation_quaternion = direction.to_track_quat("-Z", "Y")

            frame_file = temp_dir / f"{view_name}.png"
            rendered = render_single_view(
                scene,
                camera_object,
                rotation_axes,
                VIEW_AZIMUTHS[view_name],
                frame_file,
            )

            if not rendered:
                if active_engine == "eevee" and args.engine == "auto":
                    apply_workbench_settings(scene)
                    active_engine = "workbench"
                    rendered = render_single_view(
                        scene,
                        camera_object,
                        rotation_axes,
                        VIEW_AZIMUTHS[view_name],
                        frame_file,
                    )

            if not rendered:
                raise RuntimeError(f"Blender produced no image for view '{view_name}' (engine: {active_engine}).")

            raw_image = Image.open(str(frame_file)).convert("RGBA")
            model_image = raw_image.resize((args.size, args.size), resample_filter)
            cards.append(build_card(model_image, view_name, args.size, background, label_font, not args.no_labels))

        if args.mode == "sheet":
            card_width, card_height = cards[0].size
            sheet = Image.new("RGBA", (card_width * len(cards), card_height), (0, 0, 0, 0))
            for index, card in enumerate(cards):
                sheet.paste(card, (index * card_width, 0))
            output_path.parent.mkdir(parents=True, exist_ok=True)
            sheet.save(str(output_path), "PNG", optimize=True)
            print(f"Rendered turnaround sheet ({len(cards)} views, engine {active_engine}) -> {output_path}")
        else:
            output_path.mkdir(parents=True, exist_ok=True)
            for view_name, card in zip(views, cards):
                card_path = output_path / f"{name}_{view_name}.png"
                card.save(str(card_path), "PNG", optimize=True)
                print(f"Rendered {view_name} (engine {active_engine}) -> {card_path}")
    finally:
        for leftover in temp_dir.glob("*.png"):
            try:
                leftover.unlink()
            except Exception:
                pass
        try:
            temp_dir.rmdir()
        except Exception:
            pass


if __name__ == "__main__":
    main()
