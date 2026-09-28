shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back;

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
uniform float emission_boost : hint_range(0.0, 20.0) = 0.0;
uniform vec4 core_color : source_color = vec4(1.0, 0.9, 0.5, 1.0);
uniform float core_threshold : hint_range(0.0, 1.0) = 0.8;
uniform vec4 subsurface_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float subsurface_strength : hint_range(0.0, 5.0) = 0.0;

uniform bool use_detail_texture = false;
uniform sampler2D detail_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec2 detail_uv_scale = vec2(1.0, 1.0);
uniform vec2 detail_uv_scroll = vec2(0.0, 0.0);
uniform float detail_alpha : hint_range(0.0, 1.0) = 0.5;
uniform int detail_blend_mode = 0;

uniform sampler2D depth_texture : hint_depth_texture, filter_linear;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

uniform sampler2D shroud_texture : hint_default_black;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);
uniform bool shroud_enabled = false;

varying vec3 v_world_pos;
varying vec3 v_world_normal;

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float caustic_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);

	float is_flat = smoothstep(0.4, 0.8, abs(v_world_normal.y));
	float w1 = sin(v_world_pos.x * 0.4 + TIME * wave_speed * 1.8);
	float w2 = cos(v_world_pos.z * 0.4 + TIME * wave_speed * 1.4);
	VERTEX.y += (w1 + w2) * wave_strength * is_flat;
}

void fragment() {
	float flat_factor = smoothstep(0.4, 0.8, abs(v_world_normal.y));
	float waterfall_factor = 1.0 - flat_factor;

	vec2 flow_vec = length(flow_direction) > 0.001 ? normalize(flow_direction) * flow_speed : vec2(1.0, 0.0) * flow_speed;
	if (use_flow_map) {
		vec2 flow_sample = texture(flow_map_texture, v_world_pos.xz * 0.1).rg * 2.0 - 1.0;
		flow_vec = flow_sample * flow_speed;
	}

	vec2 p1 = v_world_pos.xz * 0.25 + flow_vec * TIME * wave_speed * 0.12;
	vec2 p2 = v_world_pos.xz * 0.35 - flow_vec * TIME * wave_speed * 0.09;

	vec3 flat_perturbed_normal;
	if (use_normal_texture) {
		vec3 n_tex1 = texture(normal_texture, p1 * normal_scale).rgb * 2.0 - 1.0;
		vec3 n_tex2 = texture(normal_texture, p2 * normal_scale).rgb * 2.0 - 1.0;
		flat_perturbed_normal = normalize(vec3(n_tex1.xy + n_tex2.xy, n_tex1.z + n_tex2.z));
	} else {
		float n1 = sin(p1.x + sin(p1.y * 1.2)) * 0.5 + 0.5;
		float n2 = cos(p2.x - cos(p2.y * 1.3)) * 0.5 + 0.5;
		flat_perturbed_normal = normalize(vec3((n1 - 0.5) * 0.25 * normal_scale, 1.0, (n2 - 0.5) * 0.25 * normal_scale));
	}

	float flow_x = (v_world_pos.x + v_world_pos.z) * 0.3;
	float flow_y = v_world_pos.y * 1.2 + TIME * wave_speed * 1.1;

	float streak1 = sin(flow_y * 2.2 + sin(flow_x * 1.8)) * 0.5 + 0.5;
	float streak2 = cos(flow_y * 4.2 - cos(flow_x * 3.1)) * 0.5 + 0.5;
	float waterfall_foam_raw = streak1 * 0.5 + streak2 * 0.5;
	float waterfall_foam = smoothstep(0.25, 0.95, waterfall_foam_raw) * 0.28;

	vec3 wf_normal = normalize(vec3((streak1 - 0.5) * 0.08, (streak2 - 0.5) * 0.08, (streak1 - 0.5) * 0.08));
	vec3 local_normal = mix(wf_normal, flat_perturbed_normal, flat_factor);
	NORMAL = normalize(TANGENT * local_normal.x + BINORMAL * local_normal.z + NORMAL * local_normal.y);

	vec2 refr_offset = (refraction_strength > 0.0) ? local_normal.xz * refraction_strength * 0.05 : vec2(0.0);
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

	float pulse = sin(water_depth * 18.0 - TIME * wave_speed * 3.5) * 0.5 + 0.5;
	float flat_foam_mask = smoothstep(0.2, 0.8, shore_proximity * 0.6 + pulse * shore_proximity * 0.4);
	float final_foam_mask = mix(waterfall_foam, flat_foam_mask, flat_factor);

	vec3 screen_color = (refraction_strength > 0.0) ? texture(screen_texture, screen_uv_sampled).rgb : vec3(0.0);

	float caustic_val = 0.0;
	if (caustic_strength > 0.001) {
		vec2 c_uv1 = (v_world_pos.xz + flow_vec * TIME * caustic_speed * 0.5) * caustic_scale * 0.8;
		vec2 c_uv2 = (v_world_pos.xz - flow_vec * TIME * caustic_speed * 0.3) * caustic_scale * 1.2;
		float c1 = caustic_noise(c_uv1);
		float c2 = caustic_noise(c_uv2);
		caustic_val = pow(c1 * c2, 1.5) * caustic_strength * (1.0 - depth_factor);
	}

	vec3 base_liquid_rgb = mix(water_col.rgb, foam_color.rgb, final_foam_mask * foam_color.a);
	float final_alpha = mix(water_col.a, 0.85, final_foam_mask) * shore_fade;

	if (refraction_strength > 0.0) {
		base_liquid_rgb = mix(screen_color, base_liquid_rgb, clamp(water_col.a + final_foam_mask, 0.0, 1.0));
		final_alpha = shore_fade;
	}
	base_liquid_rgb += vec3(caustic_val);

	if (use_detail_texture) {
		vec2 det_uv = v_world_pos.xz * detail_uv_scale + detail_uv_scroll * TIME;
		vec4 det_sample = texture(detail_texture, det_uv);
		if (detail_blend_mode == 1) {
			base_liquid_rgb += det_sample.rgb * det_sample.a * detail_alpha;
		} else if (detail_blend_mode == 2) {
			base_liquid_rgb *= mix(vec3(1.0), det_sample.rgb, det_sample.a * detail_alpha);
		} else {
			base_liquid_rgb = mix(base_liquid_rgb, det_sample.rgb, det_sample.a * detail_alpha);
		}
	}

	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	float shroud_factor = shroud_enabled ? texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r : 0.0;
	vec3 final_albedo = base_liquid_rgb * (1.0 - shroud_factor * 0.98);

	vec3 emissive_out = vec3(0.0);
	if (emission_boost > 0.001) {
		float core_factor = smoothstep(core_threshold, 1.0, 1.0 - depth_factor * 0.5);
		vec3 heat_color = mix(emission_color.rgb, core_color.rgb, core_factor);
		emissive_out = heat_color * emission_boost * (1.0 - shroud_factor * 0.98);
	}

	if (subsurface_strength > 0.001) {
		float sss_factor = (1.0 - depth_factor) * subsurface_strength;
		final_albedo += subsurface_color.rgb * sss_factor * (1.0 - shroud_factor * 0.98);
	}

	ALBEDO = final_albedo;
	ALPHA = final_alpha;
	EMISSION = emissive_out;

	ROUGHNESS = mix(mix(0.1, 0.35, waterfall_factor), 0.9, shroud_factor);
	METALLIC = 0.0;
	SPECULAR = mix(0.10, 0.20, waterfall_factor) * (1.0 - shroud_factor * 0.98);
}
