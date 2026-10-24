shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, depth_test_disabled, shadows_disabled;

uniform float rim_start : hint_range(0.0, 1.0) = 0.55;
uniform float rim_softness : hint_range(0.01, 1.0) = 0.25;
uniform float outline_alpha : hint_range(0.0, 1.0) = 0.85;

varying vec4 v_instance_color;

void vertex() {
	v_instance_color = COLOR;
}

void fragment() {
	vec3 view_direction = normalize(-VERTEX);
	float facing = abs(dot(normalize(NORMAL), view_direction));
	float rim = 1.0 - facing;

	if (rim < rim_start) {
		discard;
	}

	float edge_fade = clamp((rim - rim_start) / rim_softness, 0.0, 1.0);
	ALBEDO = v_instance_color.rgb;
	ALPHA = edge_fade * outline_alpha * v_instance_color.a;
}
