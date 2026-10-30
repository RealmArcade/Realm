shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, depth_test_disabled, shadows_disabled;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform float fill_alpha : hint_range(0.0, 1.0) = 0.3;
uniform float rim_alpha : hint_range(0.0, 1.0) = 0.85;
uniform float rim_start : hint_range(0.0, 1.0) = 0.45;
uniform float rim_end : hint_range(0.0, 1.0) = 0.85;
uniform float depth_bias : hint_range(0.0, 0.5) = 0.04;

instance uniform vec4 silhouette_color = vec4(1.0);
instance uniform float silhouette_alpha = 1.0;

void fragment() {
	float raw_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 scene_view_position = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw_depth, 1.0);
	float scene_depth = -scene_view_position.z / scene_view_position.w;
	float silhouette_depth = -VERTEX.z;

	if (scene_depth > silhouette_depth - depth_bias) {
		discard;
	}

	float facing = abs(dot(normalize(NORMAL), normalize(-VERTEX)));
	float rim = smoothstep(rim_start, rim_end, 1.0 - facing);

	ALBEDO = silhouette_color.rgb;
	ALPHA = mix(fill_alpha, rim_alpha, rim) * silhouette_alpha;
}
