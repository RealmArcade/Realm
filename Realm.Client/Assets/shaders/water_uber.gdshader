shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back;

#include "res://Assets/shaders/shroud.gdshaderinc"

uniform vec4 shallow_color : source_color = vec4(0.05, 0.30, 0.38, 0.55);
uniform vec4 deep_color : source_color = vec4(0.01, 0.06, 0.14, 0.98);
uniform vec4 foam_color : source_color = vec4(0.85, 0.95, 1.0, 0.85);
uniform float max_depth = 2.0;
uniform float foam_depth = 0.6;
uniform float wave_speed = 1.2;
uniform float wave_strength = 0.06;

uniform bool use_normal_texture = false;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float normal_scale : hint_range(0.0, 3.0) = 1.0;
uniform vec2 flow_direction = vec2(1.0, 0.0);
uniform float flow_speed = 0.5;
uniform bool use_flow_map = false;
uniform sampler2D flow_map_texture : hint_default_white, filter_linear_mipmap, repeat_enable;

uniform float refraction_strength : hint_range(0.0, 1.0) = 0.0;
uniform float caustic_strength : hint_range(0.0, 2.0) = 0.0;
uniform float caustic_scale : hint_range(0.1, 10.0) = 1.0;
uniform float caustic_speed : hint_range(0.0, 5.0) = 1.0;

uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float emission_boost : hint_range(0.0, 2.0) = 0.0;
uniform vec4 core_color : source_color = vec4(1.0, 0.9, 0.5, 1.0);
uniform float core_threshold : hint_range(0.0, 1.0) = 0.8;
uniform vec4 subsurface_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float subsurface_strength : hint_range(0.0, 5.0) = 0.0;

uniform bool use_painted_ocean = false;
uniform sampler2D painted_ocean : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_detail_texture = false;
uniform sampler2D detail_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform int detail_tile_mode : hint_range(0, 1) = 1;
uniform vec2 detail_uv_scale = vec2(1.0, 1.0);
uniform vec2 detail_uv_scroll = vec2(0.0, 0.0);
uniform float detail_stochastic_tile_size : hint_range(0.5, 3.0) = 1.0;
uniform float detail_cross_fade : hint_range(0.0, 0.10) = 0.0;
uniform float detail_alpha : hint_range(0.0, 1.0) = 0.5;
uniform int detail_blend_mode : hint_range(0, 2) = 0;

uniform sampler2D depth_texture : hint_depth_texture, filter_linear;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear;

varying vec3 v_world_pos;
varying vec3 v_world_normal;

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

vec2 stochastic_hash_2d(vec2 p) {
	ivec2 ip = ivec2(floor(p));
	uvec2 q = uvec2(ip);
	q = q * uvec2(1597334677u, 3812015801u);
	uint n = (q.x ^ q.y) * 1597334677u;
	return vec2(float(n & 0xFFFFu), float((n >> 16u) & 0xFFFFu)) * (1.0 / 65535.0);
}

vec4 sample_semi_grid_2d(sampler2D tex, vec2 uv, float blend_width) {
	if (blend_width <= 0.0001) {
		return texture(tex, uv);
	}
	vec2 f = fract(uv);
	vec2 edge = smoothstep(vec2(0.0), vec2(blend_width), f) * 
	            smoothstep(vec2(0.0), vec2(blend_width), vec2(1.0) - f);
	float center_weight = edge.x * edge.y;

	vec4 col_center = texture(tex, uv);
	if (center_weight > 0.999) {
		return col_center;
	}
	vec4 col_border = texture(tex, uv + vec2(0.5, 0.5));
	return mix(col_border, col_center, center_weight);
}

vec4 sample_stochastic_2d(sampler2D tex, vec2 uv, float stoch_tile_size) {
	const float F2 = 0.36602540378;
	const float G2 = 0.2113248654;

	vec2 stoch_uv = uv / max(0.01, stoch_tile_size);
	vec2 skew_uv = stoch_uv + (stoch_uv.x + stoch_uv.y) * F2;
	vec2 i = floor(skew_uv);
	vec2 f = fract(skew_uv);

	vec2 i0, i1, i2;
	vec3 w;

	if (f.x > f.y) {
		i0 = i;
		i1 = i + vec2(1.0, 0.0);
		i2 = i + vec2(1.0, 1.0);
		w = vec3(1.0 - f.x, f.x - f.y, f.y);
	} else {
		i0 = i;
		i1 = i + vec2(0.0, 1.0);
		i2 = i + vec2(1.0, 1.0);
		w = vec3(1.0 - f.y, f.y - f.x, f.x);
	}

	vec2 off0 = stochastic_hash_2d(i0);
	vec2 off1 = stochastic_hash_2d(i1);
	vec2 off2 = stochastic_hash_2d(i2);

	vec4 col0 = texture(tex, uv + off0);
	vec4 col1 = texture(tex, uv + off1);
	vec4 col2 = texture(tex, uv + off2);

	const vec3 lum_weights = vec3(0.299, 0.587, 0.114);
	vec3 h_scores = vec3(dot(col0.rgb, lum_weights), dot(col1.rgb, lum_weights), dot(col2.rgb, lum_weights));
	vec3 safe_w = max(w, vec3(0.0));
	vec3 combined_weights = pow(safe_w, vec3(4.0)) * exp(h_scores * 2.0);
	float sum_cw = combined_weights.x + combined_weights.y + combined_weights.z;
	vec3 sw = sum_cw > 0.0001 ? (combined_weights / sum_cw) : w;

	vec3 mean_color = col0.rgb * sw.x + col1.rgb * sw.y + col2.rgb * sw.z;
	float mean_alpha = col0.a * sw.x + col1.a * sw.y + col2.a * sw.z;
	return vec4(mean_color, mean_alpha);
}

vec4 sample_tiled_2d(sampler2D tex, vec2 uv, vec2 uv_scroll, int tile_mode, float stoch_tile_size, float cross_fade) {
	if (tile_mode == 1) {
		vec2 scroll_vec = length(uv_scroll) > 0.001 ? uv_scroll : vec2(0.02, 0.01);
		vec2 u1 = uv + scroll_vec * TIME;
		vec2 u2 = uv * 0.88 - vec2(scroll_vec.y, -scroll_vec.x) * TIME * 0.8 + vec2(0.37, 0.63);
		vec4 c1 = texture(tex, u1);
		vec4 c2 = texture(tex, u2);
		return mix(c1, c2, 0.5);
	}
	vec2 u1 = uv + uv_scroll * TIME;
	return sample_semi_grid_2d(tex, u1, cross_fade);
}

vec4 sample_triplanar_tiled_2d(sampler2D tex, vec3 world_pos, vec3 world_norm, vec2 uv_scale, vec2 uv_scroll, int tile_mode, float stoch_tile_size, float cross_fade) {
	vec3 blend_weights = abs(world_norm);
	blend_weights = pow(blend_weights, vec3(4.0));
	blend_weights /= max(0.0001, (blend_weights.x + blend_weights.y + blend_weights.z));

	vec2 uv_x = world_pos.zy * uv_scale * 0.12;
	vec2 uv_y = world_pos.xz * uv_scale * 0.12;
	vec2 uv_z = world_pos.xy * uv_scale * 0.12;

	if (blend_weights.y > 0.999) {
		return sample_tiled_2d(tex, uv_y, uv_scroll, tile_mode, stoch_tile_size, cross_fade);
	}

	vec4 col_x = sample_tiled_2d(tex, uv_x, uv_scroll, tile_mode, stoch_tile_size, cross_fade);
	vec4 col_y = sample_tiled_2d(tex, uv_y, uv_scroll, tile_mode, stoch_tile_size, cross_fade);
	vec4 col_z = sample_tiled_2d(tex, uv_z, uv_scroll, tile_mode, stoch_tile_size, cross_fade);

	return col_x * blend_weights.x + col_y * blend_weights.y + col_z * blend_weights.z;
}

float voronoi_caustic(vec2 p) {
	vec2 n = floor(p);
	vec2 f = fract(p);
	float m = 8.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 o = hash22(n + g);
			vec2 r = g - f + (0.5 + 0.5 * sin(TIME * caustic_speed * 1.5 + 6.2831853 * o));
			float d = dot(r, r);
			m = min(m, d);
		}
	}
	return sqrt(m);
}

varying float shore_packet;

float coastal_noise(vec2 position) {
	vec2 cell = floor(position);
	vec2 fraction = fract(position);
	vec2 blend = fraction * fraction * (3.0 - 2.0 * fraction);
	return mix(mix(hash22(cell).x, hash22(cell + vec2(1.0, 0.0)).x, blend.x),
	           mix(hash22(cell + vec2(0.0, 1.0)).x, hash22(cell + vec2(1.0)).x, blend.x), blend.y);
}

float shore_phase(vec2 position, float distance_to_land) {
	vec2 heading = flow_direction / max(length(flow_direction), 0.001);
	return distance_to_land * 0.86 + TIME * wave_speed * 1.65 + dot(position, heading) * 0.27
	       + 1.8 * sin(dot(position, vec2(0.078, -0.063)));
}

float breaker_height(float phase) {
	float crest = 0.5 + 0.5 * cos(phase);
	return crest * crest * crest;
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	float is_flat = smoothstep(0.4, 0.8, abs(v_world_normal.y));
	float swell = sin(dot(v_world_pos.xz, vec2(0.24, 0.17)) + TIME * wave_speed)
	            + 0.55 * sin(dot(v_world_pos.xz, vec2(-0.14, 0.31)) + TIME * wave_speed * 1.27);
	float reach = step(0.001, foam_depth) * (1.0 - smoothstep(0.0, max(0.01, sqrt(max(foam_depth, 0.0)) * 16.0), UV.x));
	float phase = shore_phase(v_world_pos.xz, UV.x);
	shore_packet = smoothstep(0.30, 0.74, coastal_noise(v_world_pos.xz * 0.094 + vec2(0.055, -0.032) * TIME * wave_speed));
	float rebound = cos(phase - UV.x * 1.72 - 0.72);
	float surface_height = (wave_strength * (use_painted_ocean ? 2.0 : 1.0) * swell + sqrt(max(wave_strength, 0.0)) * reach * shore_packet * (2.4 * breaker_height(phase) - 0.48 + 0.49 * rebound)) * is_flat;
	VERTEX.y += surface_height;
	v_world_pos.y += surface_height;
}

void fragment() {
	float flat_factor = smoothstep(0.4, 0.8, abs(v_world_normal.y));
	float waterfall_factor = 1.0 - flat_factor;

	vec2 flow_vec = length(flow_direction) > 0.001 ? normalize(flow_direction) * flow_speed : vec2(1.0, 0.0) * flow_speed;
	if (use_flow_map) {
		vec2 flow_sample = texture(flow_map_texture, v_world_pos.xz * 0.1).rg * 2.0 - 1.0;
		flow_vec = flow_sample * flow_speed;
	}

	vec2 ripple_position = v_world_pos.xz + 2.7 * sin(vec2(dot(v_world_pos.xz, vec2(0.081, -0.053)),
	                                                       dot(v_world_pos.xz, vec2(0.034, 0.068))) + TIME * wave_speed * 0.14);
	vec2 p_w1 = ripple_position * 0.45 + flow_vec * TIME * wave_speed * 0.25;
	vec2 p_w2 = ripple_position * 0.85 - vec2(flow_vec.y, -flow_vec.x) * TIME * wave_speed * 0.18 + vec2(1.2, 3.4);
	vec2 p_w3 = ripple_position * 1.60 + vec2(flow_vec.y, flow_vec.x) * TIME * wave_speed * 0.15 + vec2(5.6, 2.1);

	float w1 = sin(p_w1.x * 1.4 + sin(p_w1.y * 1.6 + TIME * wave_speed * 0.4));
	float w2 = cos(p_w2.y * 1.5 + cos(p_w2.x * 1.8 - TIME * wave_speed * 0.3));
	float w3 = sin((p_w3.x + p_w3.y) * 1.2 + sin((p_w3.x - p_w3.y) * 1.1 + TIME * wave_speed * 0.5));

	float wave_height_sample = (w1 + w2 + w3) * 0.333;

	float d_w1_x = cos(p_w1.x * 1.4 + sin(p_w1.y * 1.6 + TIME * wave_speed * 0.4)) * 1.4;
	float d_w1_y = cos(p_w1.x * 1.4 + sin(p_w1.y * 1.6 + TIME * wave_speed * 0.4)) * cos(p_w1.y * 1.6 + TIME * wave_speed * 0.4) * 1.6;

	float d_w2_x = -sin(p_w2.y * 1.5 + cos(p_w2.x * 1.8 - TIME * wave_speed * 0.3)) * (-sin(p_w2.x * 1.8 - TIME * wave_speed * 0.3)) * 1.8;
	float d_w2_y = -sin(p_w2.y * 1.5 + cos(p_w2.x * 1.8 - TIME * wave_speed * 0.3)) * 1.5;

	vec2 wave_grad = (vec2(d_w1_x, d_w1_y) * 0.45 + vec2(d_w2_x, d_w2_y) * 0.85) * 0.28;

	vec3 flat_perturbed_normal;
	if (use_normal_texture) {
		vec2 n_uv1 = v_world_pos.xz * 0.15 * normal_scale + flow_vec * TIME * wave_speed * 0.15;
		vec2 n_uv2 = v_world_pos.xz * 0.28 * normal_scale - vec2(flow_vec.y, -flow_vec.x) * TIME * wave_speed * 0.10 + vec2(0.3, 0.7);
		vec3 n_tex1 = texture(normal_texture, n_uv1).rgb * 2.0 - 1.0;
		vec3 n_tex2 = texture(normal_texture, n_uv2).rgb * 2.0 - 1.0;
		flat_perturbed_normal = normalize(vec3(n_tex1.xy + n_tex2.xy + wave_grad * 0.15, max(0.2, n_tex1.z + n_tex2.z)));
	} else {
		flat_perturbed_normal = normalize(vec3(-wave_grad.x * normal_scale * 0.35, -wave_grad.y * normal_scale * 0.35, 1.0));
	}

	float flow_x = (v_world_pos.x + v_world_pos.z) * 0.3;
	float flow_y = v_world_pos.y * 1.2 + TIME * wave_speed * 1.1;

	float streak1 = sin(flow_y * 2.2 + sin(flow_x * 1.8)) * 0.5 + 0.5;
	float streak2 = cos(flow_y * 4.2 - cos(flow_x * 3.1)) * 0.5 + 0.5;
	float waterfall_foam_raw = streak1 * 0.5 + streak2 * 0.5;
	float waterfall_foam = smoothstep(0.25, 0.95, waterfall_foam_raw) * 0.28;

	vec3 wf_normal = normalize(vec3((streak1 - 0.5) * 0.08, (streak2 - 0.5) * 0.08, 1.0));
	vec3 local_normal = mix(wf_normal, flat_perturbed_normal, flat_factor);
	vec3 surface_normal = normalize(v_world_normal);
	vec3 reference_axis = abs(surface_normal.y) > 0.8 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
	vec3 surface_tangent = normalize(cross(surface_normal, reference_axis));
	vec3 surface_bitangent = cross(surface_tangent, surface_normal);
	vec2 swell_slope = wave_strength * (use_painted_ocean ? 2.0 : 1.0) * (vec2(0.24, 0.17) * cos(dot(v_world_pos.xz, vec2(0.24, 0.17)) + TIME * wave_speed)
	                  + vec2(-0.14, 0.31) * 0.55 * cos(dot(v_world_pos.xz, vec2(-0.14, 0.31)) + TIME * wave_speed * 1.27));
	vec2 across = dFdx(v_world_pos.xz);
	vec2 down = dFdy(v_world_pos.xz);
	float area = across.x * down.y - down.x * across.y;
	vec2 shore_direction = (vec2(down.y, -down.x) * dFdx(UV.x) + vec2(-across.y, across.x) * dFdy(UV.x))
	                       * sign(area) / max(abs(area), 0.000001);
	float reach = step(0.001, foam_depth) * (1.0 - smoothstep(0.0, max(0.01, sqrt(max(foam_depth, 0.0)) * 16.0), UV.x));
	float phase = shore_phase(v_world_pos.xz, UV.x);
	float crest = 0.5 + 0.5 * cos(phase);
	float return_phase = phase - UV.x * 1.72 - 0.72;
	float breaker_slope = -4.128 * crest * crest * sin(phase) + 0.559 * sin(return_phase);
	swell_slope += shore_direction * (0.75 * sqrt(max(wave_strength, 0.0))) * reach * shore_packet * breaker_slope;
	vec3 ripple_normal = normalize(surface_tangent * local_normal.x + surface_bitangent * local_normal.y + surface_normal * local_normal.z - vec3(swell_slope.x, 0.0, swell_slope.y) * flat_factor);
	NORMAL = mat3(VIEW_MATRIX) * ripple_normal;

	vec2 refr_offset = (refraction_strength > 0.0) ? local_normal.xy * refraction_strength * 0.05 : vec2(0.0);
	vec2 screen_uv_sampled = clamp(SCREEN_UV + refr_offset, 0.0, 1.0);

	float depth_raw = texture(depth_texture, screen_uv_sampled).r;
	vec4 upos = INV_PROJECTION_MATRIX * vec4(screen_uv_sampled * 2.0 - 1.0, depth_raw, 1.0);
	float pixel_z = -upos.z / upos.w;
	float water_z = -VERTEX.z;
	float water_depth = max(0.0, pixel_z - water_z);

	float depth_factor = clamp(water_depth / max(0.001, max_depth), 0.0, 1.0);
	vec4 water_col = mix(shallow_color, deep_color, smoothstep(0.0, 0.85, depth_factor));

	float shore_fade = mix(1.0, smoothstep(0.001, 0.02, water_depth), flat_factor);
	float shore_proximity = (1.0 - smoothstep(0.0, max(0.001, foam_depth), water_depth)) * shore_fade;

	float flat_foam_mask = 0.0;
	float contact = max(reach * reach, shore_proximity);
	if (contact > 0.001 && foam_depth > 0.0 && wave_strength > 0.0 && flat_factor > 0.0) {
		vec2 wash_uv = v_world_pos.xz * 1.35 + vec2(w1, w2) * 0.44
		             - shore_direction * (0.43 * sin(phase) + 0.31 * sin(return_phase));
		float eddy = coastal_noise(wash_uv);
		float pockets = smoothstep(0.25, 0.68, eddy + w3 * 0.17);
		float breaking = smoothstep(0.66, 0.97, crest + (eddy - 0.5) * 0.47);
		float receding = smoothstep(0.2, 0.92, cos(return_phase));
		float threads = 1.0 - smoothstep(0.035, 0.17, abs(eddy - 0.48 + w2 * 0.12));
		float impact = breaking * pockets;
		float residue = receding * threads * (0.3 + 0.7 * pockets);
		flat_foam_mask = contact * shore_packet * smoothstep(0.0, 0.035, wave_strength)
		               * (0.87 * impact + 0.26 * residue * (1.0 - breaking));
	}

	float final_foam_mask = mix(waterfall_foam, flat_foam_mask, flat_factor);

	vec3 screen_color = (refraction_strength > 0.0) ? texture(screen_texture, screen_uv_sampled).rgb : vec3(0.0);

	float caustic_val = 0.0;
	if (caustic_strength > 0.001 && depth_factor < 1.0) {
		vec2 c_uv1 = (v_world_pos.xz + flow_vec * TIME * caustic_speed * 0.5) * caustic_scale * 0.8;
		vec2 c_uv2 = (v_world_pos.xz - flow_vec * TIME * caustic_speed * 0.3) * caustic_scale * 1.2;
		float c1 = voronoi_caustic(c_uv1);
		float c2 = voronoi_caustic(c_uv2);
		float caustic_raw = min(c1, c2);
		caustic_val = pow(caustic_raw, 2.0) * caustic_strength * (1.0 - depth_factor) * 0.5;
	}

	vec3 base_liquid_rgb = water_col.rgb;
	float final_alpha = mix(water_col.a, 0.85, final_foam_mask) * shore_fade;

	if (refraction_strength > 0.0) {
		base_liquid_rgb = mix(screen_color, base_liquid_rgb, clamp(water_col.a + final_foam_mask * 0.5, 0.0, 1.0));
		final_alpha = mix(water_col.a, 1.0, final_foam_mask) * shore_fade;
	}
	base_liquid_rgb += water_col.rgb * caustic_val;

	vec4 det_sample = vec4(1.0);
	if (use_detail_texture) {
		det_sample = sample_triplanar_tiled_2d(detail_texture, v_world_pos, v_world_normal, detail_uv_scale, detail_uv_scroll, detail_tile_mode, detail_stochastic_tile_size, detail_cross_fade);
		if (detail_blend_mode == 1) {
			base_liquid_rgb += det_sample.rgb * det_sample.a * detail_alpha;
		} else if (detail_blend_mode == 2) {
			base_liquid_rgb *= mix(vec3(1.0), det_sample.rgb, det_sample.a * detail_alpha);
		} else {
			base_liquid_rgb = mix(base_liquid_rgb, det_sample.rgb, det_sample.a * detail_alpha);
		}
	}

	if (use_painted_ocean) {
		vec2 surface_uv = ripple_position * 0.039;
		vec2 drift = flow_vec * TIME * wave_speed * 0.018;
		vec3 swell_color = texture(painted_ocean, surface_uv + drift).rgb;
		vec2 cross_uv = mat2(vec2(0.80, 0.60), vec2(-0.60, 0.80)) * surface_uv * 1.37;
		vec3 ripple_color = texture(painted_ocean, cross_uv - drift * 0.64).rgb;
		vec3 painted_color = mix(swell_color, ripple_color, 0.31) * mix(1.05, 0.76, depth_factor);
		base_liquid_rgb = mix(base_liquid_rgb, painted_color, flat_factor * 0.76);
		final_alpha = mix(final_alpha, 0.94 * shore_fade, flat_factor * 0.72);
	}
	base_liquid_rgb = mix(base_liquid_rgb, foam_color.rgb, clamp(final_foam_mask * foam_color.a, 0.0, 1.0));
	vec2 shroud_state = rts_shroud_sample(v_world_pos.xz);
	float shroud_light = rts_shroud_light_from_state(shroud_state);
	float shroud_explored_mask = rts_shroud_explored_mask(shroud_state);
	vec3 final_albedo = base_liquid_rgb * shroud_light;

	vec3 emissive_out = vec3(0.0);
	if (emission_boost > 0.001) {
		float surface_heat_variation = (wave_height_sample * 0.5 + 0.5) * 0.4 + (1.0 - depth_factor) * 0.6;
		float core_factor = smoothstep(core_threshold, 1.0, surface_heat_variation);
		vec3 heat_color = mix(emission_color.rgb, core_color.rgb, core_factor);
		emissive_out = heat_color * emission_boost * shroud_light;
		if (use_detail_texture) {
			float det_lum = clamp(dot(det_sample.rgb, vec3(0.299, 0.587, 0.114)) * 1.5, 0.1, 2.5);
			emissive_out *= mix(vec3(1.0), det_sample.rgb * det_lum, clamp(detail_alpha * 0.85, 0.0, 1.0));
		}
	}

	if (subsurface_strength > 0.001) {
		float sss_factor = (1.0 - depth_factor) * subsurface_strength;
		final_albedo += subsurface_color.rgb * sss_factor * shroud_light;
	}

	ALBEDO = final_albedo;
	ALPHA = final_alpha;
	EMISSION = emissive_out;

	float water_roughness = use_painted_ocean ? mix(0.20, 0.30, 0.5 + 0.5 * w2) : 0.23;
	ROUGHNESS = mix(mix(water_roughness, 0.66, final_foam_mask), 0.8, shroud_explored_mask);
	METALLIC = 0.0;
	SPECULAR = mix(use_painted_ocean ? 0.43 : 0.45, 0.30, waterfall_factor) * shroud_light;
}
