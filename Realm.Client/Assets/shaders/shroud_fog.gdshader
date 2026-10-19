shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled, blend_mix;

uniform sampler2D shroud_texture : hint_default_black;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);

varying vec3 v_world_pos;

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX += NORMAL * 0.05;
}

void fragment() {
	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	float shroud_alpha = texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r;
	ALBEDO = vec3(0.0, 0.0, 0.0);
	ALPHA = shroud_alpha;
	if (shroud_alpha < 0.01) { discard; }
}
