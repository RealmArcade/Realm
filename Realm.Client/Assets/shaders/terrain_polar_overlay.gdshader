shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform bool grid_visible = false;
uniform bool polar_overlay_visible = false;
uniform vec2 terrain_size = vec2(1.0, 1.0);
uniform vec2 polar_center = vec2(0.0, 0.0);
uniform float polar_ring_spacing = 8.0;
uniform float polar_radial_step_deg = 45.0;
uniform float grid_spacing = 2.0;
uniform vec4 polar_ring_color = vec4(0.2, 0.8, 1.0, 0.4);
uniform vec4 polar_spoke_color = vec4(0.2, 0.8, 1.0, 0.25);
uniform vec4 grid_color_cell = vec4(0.6, 0.6, 0.6, 0.35);
uniform vec4 grid_color_group4 = vec4(0.2, 0.65, 1.0, 0.65);
uniform vec4 grid_color_group16 = vec4(1.0, 0.85, 0.0, 0.85);
uniform vec4 grid_color_center = vec4(0.2, 1.0, 0.3, 0.95);

varying vec3 v_world_pos;

void layer(inout vec3 rgb, inout float a, vec3 c, float ca) {
	ca = clamp(ca, 0.0, 1.0);
	rgb = c * ca + rgb * (1.0 - ca);
	a = ca + a * (1.0 - ca);
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float depth_raw = texture(depth_texture, SCREEN_UV).r;
	vec4 upos = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth_raw, 1.0);
	float scene_z = -upos.z / upos.w;
	if (scene_z < -VERTEX.z - 0.1) {
		discard;
	}

	vec3 ov_rgb = vec3(0.0);
	float ov_a = 0.0;

		if (grid_visible) {
			vec2 grid_uv = (v_world_pos.xz + terrain_size / 2.0) / grid_spacing;
			vec2 df_cell = fwidth(grid_uv) * 3.0;
			vec2 lines_cell = smoothstep(vec2(1.0) - df_cell, vec2(1.0), fract(grid_uv)) + 
			                  (1.0 - smoothstep(vec2(0.0), df_cell, fract(grid_uv)));
			float line_cell = max(lines_cell.x, lines_cell.y);
			vec2 grid_uv_4 = grid_uv / 4.0;
			vec2 df_4 = fwidth(grid_uv_4) * 3.0;
			vec2 lines_4 = smoothstep(vec2(1.0) - df_4, vec2(1.0), fract(grid_uv_4)) + 
			               (1.0 - smoothstep(vec2(0.0), df_4, fract(grid_uv_4)));
			float line_4 = max(lines_4.x, lines_4.y);
			vec2 grid_uv_16 = grid_uv / 16.0;
			vec2 df_16 = fwidth(grid_uv_16) * 3.0;
			vec2 lines_16 = smoothstep(vec2(1.0) - df_16, vec2(1.0), fract(grid_uv_16)) + 
			                (1.0 - smoothstep(vec2(0.0), df_16, fract(grid_uv_16)));
			float line_16 = max(lines_16.x, lines_16.y);
			vec2 center_df = fwidth(v_world_pos.xz) * 1.5;
			vec2 center_lines = 1.0 - smoothstep(vec2(0.0), center_df, abs(v_world_pos.xz));
			float center_line = max(center_lines.x, center_lines.y);
	
			if (center_line > 0.0) {
				layer(ov_rgb, ov_a, grid_color_center.rgb, grid_color_center.a * center_line);
			} else if (line_16 > 0.0) {
				layer(ov_rgb, ov_a, grid_color_group16.rgb, grid_color_group16.a * line_16);
			} else if (line_4 > 0.0) {
				layer(ov_rgb, ov_a, grid_color_group4.rgb, grid_color_group4.a * line_4);
			} else if (line_cell > 0.0) {
				layer(ov_rgb, ov_a, grid_color_cell.rgb, grid_color_cell.a * line_cell);
			}
		}

	if (polar_overlay_visible) {
		vec2 rel_pos = v_world_pos.xz - polar_center;
		float ring_uv = length(rel_pos) / (polar_ring_spacing * grid_spacing);
		float ring_df = fwidth(ring_uv) * 3.0;
		float ring_line = smoothstep(1.0 - ring_df, 1.0, fract(ring_uv)) + (1.0 - smoothstep(0.0, ring_df, fract(ring_uv)));
		float angle_deg = mod(degrees(atan(rel_pos.y, rel_pos.x)) + 360.0, 360.0);
		float spoke_uv = angle_deg / max(1.0, polar_radial_step_deg);
		float spoke_df = fwidth(spoke_uv) * 3.0;
		float spoke_line = smoothstep(1.0 - spoke_df, 1.0, fract(spoke_uv)) + (1.0 - smoothstep(0.0, spoke_df, fract(spoke_uv)));
		layer(ov_rgb, ov_a, polar_ring_color.rgb, polar_ring_color.a * ring_line);
		layer(ov_rgb, ov_a, polar_spoke_color.rgb, polar_spoke_color.a * spoke_line);
	}

	if (ov_a <= 0.001) {
		discard;
	}
	ALBEDO = ov_rgb / ov_a;
	ALPHA = ov_a;
}
