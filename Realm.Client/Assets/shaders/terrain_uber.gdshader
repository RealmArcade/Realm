shader_type spatial;
render_mode blend_mix;

#include "res://Assets/shaders/shroud.gdshaderinc"

uniform sampler2DArray terrain_textures : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2DArray terrain_normals_pbr : hint_default_white, filter_linear_mipmap_anisotropic;
uniform float blend_softness = 0.04;
uniform float blend_noise_strength = 0.22;
uniform float blend_noise_scale = 0.22;
uniform float cliff_jitter_strength = 1.0;
uniform float cliff_jitter_scale = 0.20;
uniform sampler2D pathing_texture : hint_default_transparent, filter_nearest;
uniform bool pathing_visible = false;

uniform vec2 terrain_size = vec2(1.0, 1.0);
uniform float grid_spacing = 2.0;


uniform float texture_scale = 0.5;
uniform float macro_scale = 0.035;

uniform float uv_warp_strength = 0.8;
uniform float macro_albedo_contrast = 0.10;
uniform float macro_roughness_contrast = 0.08;
uniform float macro_normal_strength = 0.15;
uniform float macro_lacunarity = 2.0;
uniform float macro_gain = 0.5;

uniform bool enable_macro_noise = true;
uniform bool enable_height_blend = true;
uniform bool enable_normal_mapping = true;
uniform float specular_amt = 0.2;

uniform vec4 swatch_params[256];
uniform vec4 swatch_height_params[256];
uniform vec4 swatch_albedo_params[256];

varying flat vec4 v_tex_indices;
varying vec4 v_tex_weights;
varying flat vec4 v_cliff_tex_indices;
varying vec4 v_cliff_tex_weights;
varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec4 v_color;
varying flat float v_cliff_factor;

float macro_hash(vec2 p) {
	ivec2 ip = ivec2(floor(p));
	uvec2 q = uvec2(ip);
	q = q * uvec2(1597334677u, 3812015801u);
	uint n = (q.x ^ q.y) * 1597334677u;
	return float(n) * (1.0 / 4294967295.0);
}

float macro_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(macro_hash(i + vec2(0.0, 0.0)), macro_hash(i + vec2(1.0, 0.0)), u.x),
	           mix(macro_hash(i + vec2(0.0, 1.0)), macro_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float macro_fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	mat2 rot = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
	for (int i = 0; i < 4; ++i) {
		v += a * macro_noise(p);
		p = rot * p * 2.0 + vec2(100.0);
		a *= 0.5;
	}
	return v;
}

vec4 sample_quad_array(sampler2DArray tex_array, float layer, vec2 uv) {
	return texture(tex_array, vec3(uv, layer));
}

vec2 stochastic_hash(vec2 p) {
	ivec2 ip = ivec2(floor(p));
	uvec2 q = uvec2(ip);
	q = q * uvec2(1597334677u, 3812015801u);
	uint n = (q.x ^ q.y) * 1597334677u;
	return vec2(float(n & 0xFFFFu), float((n >> 16u) & 0xFFFFu)) * (1.0 / 65535.0);
}

vec4 sample_semi_grid(sampler2DArray tex_array, float layer, vec2 uv, vec2 dx, vec2 dy, float blend_width) {
	if (blend_width <= 0.0001) {
		return textureGrad(tex_array, vec3(uv, layer), dx, dy);
	}
	vec2 f = fract(uv);
	vec2 edge = smoothstep(vec2(0.0), vec2(blend_width), f) * 
	            smoothstep(vec2(0.0), vec2(blend_width), vec2(1.0) - f);
	float center_weight = edge.x * edge.y;

	vec4 col_center = textureGrad(tex_array, vec3(uv, layer), dx, dy);

	if (center_weight > 0.999) {
		return col_center;
	}

	vec4 col_border = textureGrad(tex_array, vec3(uv + vec2(0.5, 0.5), layer), dx, dy);

	return mix(col_border, col_center, center_weight);
}

vec4 sample_stochastic_layer(sampler2DArray tex_array, float layer, vec2 uv, vec2 dx, vec2 dy, float tile_mode, float stoch_tile_size, float cross_fade, bool is_vector_data) {
	if (tile_mode < 0.5) {
		return sample_semi_grid(tex_array, layer, uv, dx, dy, cross_fade);
	}

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

	vec2 off0 = stochastic_hash(i0);
	vec2 off1 = stochastic_hash(i1);
	vec2 off2 = stochastic_hash(i2);

	vec4 col0 = textureGrad(tex_array, vec3(uv + off0, layer), dx, dy);
	vec4 col1 = textureGrad(tex_array, vec3(uv + off1, layer), dx, dy);
	vec4 col2 = textureGrad(tex_array, vec3(uv + off2, layer), dx, dy);

	vec3 h_scores;
	if (is_vector_data) {
		h_scores = vec3(col0.b, col1.b, col2.b);
	} else {
		const vec3 lum_weights = vec3(0.299, 0.587, 0.114);
		h_scores = vec3(dot(col0.rgb, lum_weights), dot(col1.rgb, lum_weights), dot(col2.rgb, lum_weights));
	}

	vec3 safe_w = max(w, vec3(0.0));
	vec3 combined_weights = pow(safe_w, vec3(4.0)) * exp(h_scores * 2.0);
	float sum_cw = combined_weights.x + combined_weights.y + combined_weights.z;
	vec3 sw = sum_cw > 0.0001 ? (combined_weights / sum_cw) : w;

	if (is_vector_data) {
		return col0 * sw.x + col1 * sw.y + col2 * sw.z;
	}

	vec3 mean_color = col0.rgb * sw.x + col1.rgb * sw.y + col2.rgb * sw.z;
	return vec4(mean_color, col0.a * sw.x + col1.a * sw.y + col2.a * sw.z);
}

vec4 sample_planar_layer(sampler2DArray tex_array, float layer, vec2 uv_y, vec2 dx_y, vec2 dy_y, bool is_vector_data) {
	int layer_idx = int(clamp(round(layer), 0.0, 255.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;

	return sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
}

vec4 sample_triplanar_layer(sampler2DArray tex_array, float layer, vec2 uv_x, vec2 uv_y, vec2 uv_z, vec2 dx_x, vec2 dy_x, vec2 dx_y, vec2 dy_y, vec2 dx_z, vec2 dy_z, vec3 weights, bool is_vector_data) {
	int layer_idx = int(clamp(round(layer), 0.0, 255.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_x = uv_x * uv_scale;
	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_uv_z = uv_z * uv_scale;

	vec2 scaled_dx_x = dx_x * uv_scale;
	vec2 scaled_dy_x = dy_x * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;
	vec2 scaled_dx_z = dx_z * uv_scale;
	vec2 scaled_dy_z = dy_z * uv_scale;

	if (weights.y > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}
	if (weights.x > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}
	if (weights.z > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}

	vec4 col_x = sample_stochastic_layer(tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	vec4 col_y = sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	vec4 col_z = sample_stochastic_layer(tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, is_vector_data);

	vec3 tri_h;
	if (is_vector_data) {
		tri_h = vec3(col_x.b, col_y.b, col_z.b);
	} else {
		const vec3 lum_weights = vec3(0.299, 0.587, 0.114);
		tri_h = vec3(dot(col_x.rgb, lum_weights), dot(col_y.rgb, lum_weights), dot(col_z.rgb, lum_weights));
	}

	vec3 safe_w = max(weights, vec3(0.0));
	vec3 combined_tri_w = pow(safe_w, vec3(6.0)) * exp(tri_h * 2.0);
	float sum_tri_w = combined_tri_w.x + combined_tri_w.y + combined_tri_w.z;
	vec3 tw = sum_tri_w > 0.0001 ? (combined_tri_w / sum_tri_w) : weights;

	return col_x * tw.x + col_y * tw.y + col_z * tw.z;
}

vec2 unpack_normal_xy(vec2 raw_rg) {
	return raw_rg * 2.0 - 1.0;
}

vec3 unpack_triplanar_normal(vec4 norm_pbr, vec3 blend_w, vec3 geom_norm, float normal_scale) {
	vec2 t_xy = unpack_normal_xy(norm_pbr.rg) * normal_scale;

	vec3 sign_n = sign(geom_norm);
	sign_n.x = sign_n.x == 0.0 ? 1.0 : sign_n.x;
	sign_n.y = sign_n.y == 0.0 ? 1.0 : sign_n.y;
	sign_n.z = sign_n.z == 0.0 ? 1.0 : sign_n.z;

	vec3 dp_x = vec3(0.0, t_xy.y, t_xy.x * sign_n.x);
	vec3 dp_y = vec3(t_xy.x, 0.0, t_xy.y * sign_n.y);
	vec3 dp_z = vec3(t_xy.x * sign_n.z, t_xy.y, 0.0);

	vec3 total_dp = dp_x * blend_w.x + dp_y * blend_w.y + dp_z * blend_w.z;
	return normalize(geom_norm + total_dp);
}

struct TriplanarPbrData {
	vec3 normal;
	float ao;
	float roughness;
	float height;
};

TriplanarPbrData sample_triplanar_pbr(sampler2DArray norm_tex_array, float layer, vec2 uv_x, vec2 uv_y, vec2 uv_z, vec2 dx_x, vec2 dy_x, vec2 dx_y, vec2 dy_y, vec2 dx_z, vec2 dy_z, vec3 weights, vec3 geom_norm, float normal_scale, float roughness_scale) {
	int layer_idx = int(clamp(round(layer), 0.0, 255.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_x = uv_x * uv_scale;
	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_uv_z = uv_z * uv_scale;

	vec2 scaled_dx_x = dx_x * uv_scale;
	vec2 scaled_dy_x = dy_x * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;
	vec2 scaled_dx_z = dx_z * uv_scale;
	vec2 scaled_dy_z = dy_z * uv_scale;

	vec3 sign_n = sign(geom_norm);
	sign_n.x = sign_n.x == 0.0 ? 1.0 : sign_n.x;
	sign_n.y = sign_n.y == 0.0 ? 1.0 : sign_n.y;
	sign_n.z = sign_n.z == 0.0 ? 1.0 : sign_n.z;

	TriplanarPbrData result;

	if (weights.y > 0.99) {
		vec4 ny = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, true);
		vec2 ty = unpack_normal_xy(ny.rg) * normal_scale;
		float tzy = sqrt(max(0.0, 1.0 - dot(ty, ty)));
		vec3 dp_y = vec3(ty.x, 0.0, ty.y * sign_n.y);
		result.normal = normalize(geom_norm + dp_y);
		result.ao = clamp((0.35 + 0.65 * tzy) * (0.55 + 0.45 * ny.b), 0.05, 1.0);
		result.roughness = ny.a * roughness_scale;
		result.height = ny.b;
		return result;
	}
	if (weights.x > 0.99) {
		vec4 nx = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, true);
		vec2 tx = unpack_normal_xy(nx.rg) * normal_scale;
		float tzx = sqrt(max(0.0, 1.0 - dot(tx, tx)));
		vec3 dp_x = vec3(0.0, tx.y, tx.x * sign_n.x);
		result.normal = normalize(geom_norm + dp_x);
		result.ao = clamp((0.35 + 0.65 * tzx) * (0.55 + 0.45 * nx.b), 0.05, 1.0);
		result.roughness = nx.a * roughness_scale;
		result.height = nx.b;
		return result;
	}
	if (weights.z > 0.99) {
		vec4 nz = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, true);
		vec2 tz = unpack_normal_xy(nz.rg) * normal_scale;
		float tzz = sqrt(max(0.0, 1.0 - dot(tz, tz)));
		vec3 dp_z = vec3(tz.x * sign_n.z, tz.y, 0.0);
		result.normal = normalize(geom_norm + dp_z);
		result.ao = clamp((0.35 + 0.65 * tzz) * (0.55 + 0.45 * nz.b), 0.05, 1.0);
		result.roughness = nz.a * roughness_scale;
		result.height = nz.b;
		return result;
	}

	vec4 nx = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, true);
	vec4 ny = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, true);
	vec4 nz = sample_stochastic_layer(norm_tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, true);

	vec2 tx = unpack_normal_xy(nx.rg) * normal_scale;
	vec2 ty = unpack_normal_xy(ny.rg) * normal_scale;
	vec2 tz = unpack_normal_xy(nz.rg) * normal_scale;

	float tzx = sqrt(max(0.0, 1.0 - dot(tx, tx)));
	float tzy = sqrt(max(0.0, 1.0 - dot(ty, ty)));
	float tzz = sqrt(max(0.0, 1.0 - dot(tz, tz)));

	vec3 tri_h = vec3(nx.b, ny.b, nz.b);
	vec3 safe_w = max(weights, vec3(0.0));
	vec3 combined_tri_w = pow(safe_w, vec3(6.0)) * exp(tri_h * 2.0);
	float sum_tri_w = combined_tri_w.x + combined_tri_w.y + combined_tri_w.z;
	vec3 tw = sum_tri_w > 0.0001 ? (combined_tri_w / sum_tri_w) : weights;

	vec3 dp_x = vec3(0.0, tx.y, tx.x * sign_n.x);
	vec3 dp_y = vec3(ty.x, 0.0, ty.y * sign_n.y);
	vec3 dp_z = vec3(tz.x * sign_n.z, tz.y, 0.0);
	vec3 total_dp = dp_x * tw.x + dp_y * tw.y + dp_z * tw.z;

	result.normal = normalize(geom_norm + total_dp);

	float ao_x = clamp((0.35 + 0.65 * tzx) * (0.55 + 0.45 * nx.b), 0.05, 1.0);
	float ao_y = clamp((0.35 + 0.65 * tzy) * (0.55 + 0.45 * ny.b), 0.05, 1.0);
	float ao_z = clamp((0.35 + 0.65 * tzz) * (0.55 + 0.45 * nz.b), 0.05, 1.0);
	result.ao = ao_x * tw.x + ao_y * tw.y + ao_z * tw.z;

	result.roughness = (nx.a * tw.x + ny.a * tw.y + nz.a * tw.z) * roughness_scale;
	result.height = nx.b * tw.x + ny.b * tw.y + nz.b * tw.z;
	return result;
}

void vertex() {
	v_tex_indices = CUSTOM0;
	v_tex_weights = CUSTOM1;
	v_cliff_tex_indices = CUSTOM2;
	v_cliff_tex_weights = CUSTOM3;
	v_color = vec4(1.0, 1.0, 1.0, 1.0);
	v_cliff_factor = COLOR.r;

	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float cliff_mask = clamp(COLOR.a, 0.0, 1.0);

	if (cliff_jitter_strength > 0.0 && cliff_mask > 0.001) {
		vec2 jitter_uv_x = (world_pos.xz + vec2(world_pos.y * 0.75, world_pos.y * 1.25)) * cliff_jitter_scale;
		vec2 jitter_uv_z = (world_pos.xz + vec2(world_pos.y * 1.35 + 43.1, world_pos.y * 0.65 + 19.7)) * cliff_jitter_scale;
		float noise_x = (macro_fbm(jitter_uv_x) - 0.5) * 2.0;
		float noise_z = (macro_fbm(jitter_uv_z) - 0.5) * 2.0;
		vec3 world_disp = vec3(noise_x, 0.0, noise_z) * (cliff_jitter_strength * cliff_mask);
		world_pos += world_disp;
		VERTEX = (inverse(MODEL_MATRIX) * vec4(world_pos, 1.0)).xyz;
	}

	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_pos = world_pos;
}

void fragment() {
	vec3 geom_normal = normalize(v_world_normal);

	vec3 pos_warped = v_world_pos;
	float base_fbm_val = 0.5;

	if (enable_macro_noise) {
		base_fbm_val = macro_fbm(v_world_pos.xz * macro_scale);
		if (uv_warp_strength > 0.0) {
			float warp_x = (base_fbm_val - 0.5) * 2.0;
			float warp_y = (macro_fbm((v_world_pos.xz + vec2(17.3, 31.7)) * macro_scale) - 0.5) * 2.0;
			vec2 warp_offset = vec2(warp_x, warp_y);
			pos_warped += vec3(warp_offset.x, warp_offset.y, warp_offset.y) * uv_warp_strength * 0.05 / max(0.001, macro_scale);
		}
	}

	float slope_cliff_factor = v_cliff_factor > 0.5 ? 1.0 : 0.0;

	vec3 terrain_color = vec3(0.0);
	vec3 blended_world_normal = geom_normal;
	float blended_ao = 1.0;
	float blended_roughness = 0.85;

	if (slope_cliff_factor < 0.5) {
		vec2 uv_y = pos_warped.xz * texture_scale;
		vec2 dx_y = dFdx(uv_y);
		vec2 dy_y = dFdy(uv_y);

		vec4 raw_weights = v_tex_weights;
		raw_weights.x = raw_weights.x < 0.001 ? 0.0 : raw_weights.x;
		raw_weights.y = raw_weights.y < 0.001 ? 0.0 : raw_weights.y;
		raw_weights.z = raw_weights.z < 0.001 ? 0.0 : raw_weights.z;
		raw_weights.w = raw_weights.w < 0.001 ? 0.0 : raw_weights.w;

		float weight_sum = raw_weights.x + raw_weights.y + raw_weights.z + raw_weights.w;
		vec4 norm_weights = weight_sum > 0.0001 ? raw_weights / weight_sum : vec4(1.0, 0.0, 0.0, 0.0);

		float max_incoming_weight = max(max(norm_weights.x, norm_weights.y), max(norm_weights.z, norm_weights.w));
		float blend_transition_factor = clamp((1.0 - max_incoming_weight) * 2.5, 0.0, 1.0);

		if (blend_noise_strength > 0.0 && blend_transition_factor > 0.001) {
			vec2 blend_noise_uv = v_world_pos.xz * blend_noise_scale;
			float noise_ch_a = (macro_noise(blend_noise_uv) * 0.70 + macro_noise(blend_noise_uv * 2.13 + vec2(11.7, 7.3)) * 0.30) - 0.5;
			float noise_ch_b = (macro_noise(blend_noise_uv + vec2(19.3, 37.7)) * 0.70 + macro_noise(blend_noise_uv * 2.13 + vec2(43.3, 19.7)) * 0.30) - 0.5;
			vec4 weight_noise_offset = vec4(noise_ch_a, -noise_ch_a, noise_ch_b, -noise_ch_b) * (blend_noise_strength * blend_transition_factor);

			vec4 active_channel_mask = step(vec4(0.001), norm_weights);
			vec4 perturbed_weights = max(vec4(0.0), norm_weights + weight_noise_offset * active_channel_mask);
			float perturbed_weight_sum = perturbed_weights.x + perturbed_weights.y + perturbed_weights.z + perturbed_weights.w;
			norm_weights = perturbed_weight_sum > 0.0001 ? perturbed_weights / perturbed_weight_sum : norm_weights;
		}

		vec4 gn0 = raw_weights.x > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.x), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);
		vec4 gn1 = raw_weights.y > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.y), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);
		vec4 gn2 = raw_weights.z > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.z), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);
		vec4 gn3 = raw_weights.w > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.w), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 0.0, 1.0);

		vec4 c0 = raw_weights.x > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.x), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c1 = raw_weights.y > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.y), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c2 = raw_weights.z > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.z), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c3 = raw_weights.w > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.w), uv_y, dx_y, dy_y, false) : vec4(0.0);

		int idx0 = int(clamp(round(v_tex_indices.x), 0.0, 31.0));
		int idx1 = int(clamp(round(v_tex_indices.y), 0.0, 31.0));
		int idx2 = int(clamp(round(v_tex_indices.z), 0.0, 31.0));
		int idx3 = int(clamp(round(v_tex_indices.w), 0.0, 31.0));

		vec3 col0 = (c0.rgb * c0.rgb) * swatch_albedo_params[idx0].rgb;
		vec3 col1 = (c1.rgb * c1.rgb) * swatch_albedo_params[idx1].rgb;
		vec3 col2 = (c2.rgb * c2.rgb) * swatch_albedo_params[idx2].rgb;
		vec3 col3 = (c3.rgb * c3.rgb) * swatch_albedo_params[idx3].rgb;

		vec4 blend_layer_weights = norm_weights;
		vec3 ground_albedo = vec3(0.0);

		if (enable_height_blend) {
			float edge_noise = 0.0;
			if (enable_macro_noise) {
				float max_w = max(max(norm_weights.x, norm_weights.y), max(norm_weights.z, norm_weights.w));
				float transition_factor = clamp((1.0 - max_w) * 2.0, 0.0, 1.0);
				edge_noise = (base_fbm_val - 0.5) * 0.12 * transition_factor;
			}

			vec4 heights = vec4(gn0.b, gn1.b, gn2.b, gn3.b);
			vec4 noise_offsets = vec4(edge_noise, -edge_noise, edge_noise * 0.75, -edge_noise * 0.75);

			vec4 hp0 = swatch_height_params[idx0];
			vec4 hp1 = swatch_height_params[idx1];
			vec4 hp2 = swatch_height_params[idx2];
			vec4 hp3 = swatch_height_params[idx3];

			vec4 h_scales = vec4(
				hp0.x > 0.001 ? hp0.x : 1.0,
				hp1.x > 0.001 ? hp1.x : 1.0,
				hp2.x > 0.001 ? hp2.x : 1.0,
				hp3.x > 0.001 ? hp3.x : 1.0
			);

			vec4 h_biases = vec4(hp0.y, hp1.y, hp2.y, hp3.y);

			vec4 h_powers = vec4(
				hp0.z > 0.001 ? hp0.z : 1.0,
				hp1.z > 0.001 ? hp1.z : 1.0,
				hp2.z > 0.001 ? hp2.z : 1.0,
				hp3.z > 0.001 ? hp3.z : 1.0
			);

			vec4 shaped_heights = vec4(
				pow(clamp(heights.x, 0.0, 1.0), h_powers.x),
				pow(clamp(heights.y, 0.0, 1.0), h_powers.y),
				pow(clamp(heights.z, 0.0, 1.0), h_powers.z),
				pow(clamp(heights.w, 0.0, 1.0), h_powers.w)
			);

			vec4 scaled_ground_heights = vec4(
				shaped_heights.x * h_scales.x + h_biases.x,
				shaped_heights.y * h_scales.y + h_biases.y,
				shaped_heights.z * h_scales.z + h_biases.z,
				shaped_heights.w * h_scales.w + h_biases.w
			);

			vec4 height_scores = vec4(
				norm_weights.x > 0.001 ? (norm_weights.x + scaled_ground_heights.x + noise_offsets.x) : -100.0,
				norm_weights.y > 0.001 ? (norm_weights.y + scaled_ground_heights.y + noise_offsets.y) : -100.0,
				norm_weights.z > 0.001 ? (norm_weights.z + scaled_ground_heights.z + noise_offsets.z) : -100.0,
				norm_weights.w > 0.001 ? (norm_weights.w + scaled_ground_heights.w + noise_offsets.w) : -100.0
			);

			float max_score = max(max(height_scores.x, height_scores.y), max(height_scores.z, height_scores.w));
			float transition_depth = max(0.001, blend_softness);

			vec4 raw_blend = vec4(
				smoothstep(max_score - transition_depth, max_score, height_scores.x) * step(0.001, norm_weights.x),
				smoothstep(max_score - transition_depth, max_score, height_scores.y) * step(0.001, norm_weights.y),
				smoothstep(max_score - transition_depth, max_score, height_scores.z) * step(0.001, norm_weights.z),
				smoothstep(max_score - transition_depth, max_score, height_scores.w) * step(0.001, norm_weights.w)
			);

			float blend_sum = raw_blend.x + raw_blend.y + raw_blend.z + raw_blend.w;
			blend_layer_weights = blend_sum > 0.0001 ? raw_blend / blend_sum : norm_weights;

			ground_albedo = (col0 * blend_layer_weights.x +
			                 col1 * blend_layer_weights.y +
			                 col2 * blend_layer_weights.z +
			                 col3 * blend_layer_weights.w);
		} else {
			ground_albedo = (col0 * norm_weights.x +
			                 col1 * norm_weights.y +
			                 col2 * norm_weights.z +
			                 col3 * norm_weights.w);
		}

		vec3 ground_normal = geom_normal;
		float ground_ao = 1.0;
		float ground_roughness = 0.85;

		float gw0 = blend_layer_weights.x;
		float gw1 = blend_layer_weights.y;
		float gw2 = blend_layer_weights.z;
		float gw3 = blend_layer_weights.w;

		vec2 t0 = unpack_normal_xy(gn0.rg) * swatch_height_params[idx0].w;
		vec2 t1 = unpack_normal_xy(gn1.rg) * swatch_height_params[idx1].w;
		vec2 t2 = unpack_normal_xy(gn2.rg) * swatch_height_params[idx2].w;
		vec2 t3 = unpack_normal_xy(gn3.rg) * swatch_height_params[idx3].w;

		float tz0 = sqrt(max(0.0, 1.0 - dot(t0, t0)));
		float tz1 = sqrt(max(0.0, 1.0 - dot(t1, t1)));
		float tz2 = sqrt(max(0.0, 1.0 - dot(t2, t2)));
		float tz3 = sqrt(max(0.0, 1.0 - dot(t3, t3)));

		float ao0 = clamp((0.35 + 0.65 * tz0) * (0.55 + 0.45 * gn0.b), 0.05, 1.0);
		float ao1 = clamp((0.35 + 0.65 * tz1) * (0.55 + 0.45 * gn1.b), 0.05, 1.0);
		float ao2 = clamp((0.35 + 0.65 * tz2) * (0.55 + 0.45 * gn2.b), 0.05, 1.0);
		float ao3 = clamp((0.35 + 0.65 * tz3) * (0.55 + 0.45 * gn3.b), 0.05, 1.0);

		if (enable_normal_mapping) {
			vec3 gn0_vec = unpack_triplanar_normal(gn0, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx0].w);
			vec3 gn1_vec = unpack_triplanar_normal(gn1, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx1].w);
			vec3 gn2_vec = unpack_triplanar_normal(gn2, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx2].w);
			vec3 gn3_vec = unpack_triplanar_normal(gn3, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[idx3].w);

			ground_normal = normalize(gn0_vec * gw0 + gn1_vec * gw1 + gn2_vec * gw2 + gn3_vec * gw3);
			ground_ao = (ao0 * gw0 + ao1 * gw1 + ao2 * gw2 + ao3 * gw3);
			ground_roughness = (gn0.a * swatch_albedo_params[idx0].w * gw0 +
			                    gn1.a * swatch_albedo_params[idx1].w * gw1 +
			                    gn2.a * swatch_albedo_params[idx2].w * gw2 +
			                    gn3.a * swatch_albedo_params[idx3].w * gw3);
		} else {
			float max_gw = max(max(gw0, gw1), max(gw2, gw3));
			float dom_layer = round(v_tex_indices.x);
			if (gw1 >= max_gw) dom_layer = round(v_tex_indices.y);
			else if (gw2 >= max_gw) dom_layer = round(v_tex_indices.z);
			else if (gw3 >= max_gw) dom_layer = round(v_tex_indices.w);

			int dom_idx = int(clamp(dom_layer, 0.0, 255.0));
			float uv_scale = swatch_params[dom_idx].y > 0.001 ? swatch_params[dom_idx].y : 1.0;
			vec2 dom_uv = uv_y * uv_scale;
			vec2 dom_dx = dx_y * uv_scale;
			vec2 dom_dy = dy_y * uv_scale;

			vec4 gn = textureGrad(terrain_normals_pbr, vec3(dom_uv, dom_layer), dom_dx, dom_dy);
			vec2 dom_t = unpack_normal_xy(gn.rg) * swatch_height_params[dom_idx].w;
			ground_normal = unpack_triplanar_normal(gn, vec3(0.0, 1.0, 0.0), geom_normal, swatch_height_params[dom_idx].w);
			float dom_tz = sqrt(max(0.0, 1.0 - dot(dom_t, dom_t)));
			ground_ao = clamp((0.35 + 0.65 * dom_tz) * (0.55 + 0.45 * gn.b), 0.05, 1.0);
			ground_roughness = gn.a * swatch_albedo_params[dom_idx].w;
		}

		if (enable_macro_noise && macro_normal_strength > 0.0) {
			float n_noise_y_x = (macro_fbm((v_world_pos.xz + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_y_z = (macro_fbm((v_world_pos.xz + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_y = vec3(n_noise_y_x, 0.0, n_noise_y_z);
			ground_normal = normalize(ground_normal + noise_norm_y * macro_normal_strength);
		}

		terrain_color = ground_albedo;
		blended_world_normal = ground_normal;
		blended_ao = ground_ao;
		blended_roughness = ground_roughness;
	} else {
		vec3 cliff_geom_normal = geom_normal;

		vec3 triplanar_normal = abs(cliff_geom_normal);
		vec3 blend_weights = pow(triplanar_normal, vec3(4.0));
		float bw_sum = blend_weights.x + blend_weights.y + blend_weights.z;
		blend_weights = bw_sum > 0.0001 ? blend_weights / bw_sum : vec3(0.0, 1.0, 0.0);

		vec2 uv_x = v_world_pos.zy * texture_scale;
		vec2 uv_y = v_world_pos.xz * texture_scale;
		vec2 uv_z = v_world_pos.xy * texture_scale;

		if (enable_macro_noise && uv_warp_strength > 0.0) {
			float warp_factor = uv_warp_strength * 0.05 / max(0.001, macro_scale);
			vec2 warp_zy = vec2(macro_fbm(v_world_pos.zy * macro_scale), macro_fbm((v_world_pos.zy + vec2(13.1, 29.7)) * macro_scale)) - 0.5;
			vec2 warp_xz = vec2(macro_fbm(v_world_pos.xz * macro_scale), macro_fbm((v_world_pos.xz + vec2(17.3, 31.7)) * macro_scale)) - 0.5;
			vec2 warp_xy = vec2(macro_fbm(v_world_pos.xy * macro_scale), macro_fbm((v_world_pos.xy + vec2(37.3, 11.9)) * macro_scale)) - 0.5;

			uv_x = (v_world_pos.zy + warp_zy * warp_factor) * texture_scale;
			uv_y = (v_world_pos.xz + warp_xz * warp_factor) * texture_scale;
			uv_z = (v_world_pos.xy + warp_xy * warp_factor) * texture_scale;
		}

		vec2 dx_x = dFdx(uv_x);
		vec2 dy_x = dFdy(uv_x);
		vec2 dx_y = dFdx(uv_y);
		vec2 dy_y = dFdy(uv_y);
		vec2 dx_z = dFdx(uv_z);
		vec2 dy_z = dFdy(uv_z);

		vec4 raw_c_weights = v_cliff_tex_weights;
		float total_c_weight = raw_c_weights.x + raw_c_weights.y + raw_c_weights.z + raw_c_weights.w;
		vec4 norm_c_weights = total_c_weight > 0.0001 ? raw_c_weights / total_c_weight : vec4(1.0, 0.0, 0.0, 0.0);

		if (blend_noise_strength > 0.001) {
			vec2 blend_noise_uv = (v_world_pos.zy * blend_weights.x + v_world_pos.xz * blend_weights.y + v_world_pos.xy * blend_weights.z) * blend_noise_scale;
			float b_noise = (macro_fbm(blend_noise_uv) - 0.5) * 2.0 * blend_noise_strength;
			vec4 perturbed_c_weights = max(vec4(0.0), norm_c_weights + vec4(b_noise, -b_noise, b_noise * 0.5, -b_noise * 0.5));
			float perturbed_c_sum = perturbed_c_weights.x + perturbed_c_weights.y + perturbed_c_weights.z + perturbed_c_weights.w;
			norm_c_weights = perturbed_c_sum > 0.0001 ? perturbed_c_weights / perturbed_c_sum : norm_c_weights;
		}

		int c_idx0 = int(clamp(round(v_cliff_tex_indices.x), 0.0, 255.0));
		int c_idx1 = int(clamp(round(v_cliff_tex_indices.y), 0.0, 255.0));
		int c_idx2 = int(clamp(round(v_cliff_tex_indices.z), 0.0, 255.0));
		int c_idx3 = int(clamp(round(v_cliff_tex_indices.w), 0.0, 255.0));

		TriplanarPbrData cp0 = raw_c_weights.x > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.x), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx0].w, swatch_albedo_params[c_idx0].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);
		TriplanarPbrData cp1 = raw_c_weights.y > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.y), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx1].w, swatch_albedo_params[c_idx1].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);
		TriplanarPbrData cp2 = raw_c_weights.z > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.z), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx2].w, swatch_albedo_params[c_idx2].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);
		TriplanarPbrData cp3 = raw_c_weights.w > 0.001 ? sample_triplanar_pbr(terrain_normals_pbr, round(v_cliff_tex_indices.w), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, cliff_geom_normal, swatch_height_params[c_idx3].w, swatch_albedo_params[c_idx3].w) : TriplanarPbrData(cliff_geom_normal, 1.0, 0.85, 0.5);

		vec4 cliff0 = raw_c_weights.x > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.x), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff1 = raw_c_weights.y > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.y), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff2 = raw_c_weights.z > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.z), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff3 = raw_c_weights.w > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.w), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);

		vec3 c_col0 = (cliff0.rgb * cliff0.rgb) * swatch_albedo_params[c_idx0].rgb;
		vec3 c_col1 = (cliff1.rgb * cliff1.rgb) * swatch_albedo_params[c_idx1].rgb;
		vec3 c_col2 = (cliff2.rgb * cliff2.rgb) * swatch_albedo_params[c_idx2].rgb;
		vec3 c_col3 = (cliff3.rgb * cliff3.rgb) * swatch_albedo_params[c_idx3].rgb;

		vec4 cliff_blend_weights = norm_c_weights;
		vec3 cliff_albedo = vec3(0.0);

		if (enable_height_blend) {
			float cliff_edge_noise = 0.0;
			if (enable_macro_noise) {
				float max_w = max(max(norm_c_weights.x, norm_c_weights.y), max(norm_c_weights.z, norm_c_weights.w));
				float transition_factor = clamp((1.0 - max_w) * 2.0, 0.0, 1.0);
				cliff_edge_noise = (base_fbm_val - 0.5) * 0.12 * transition_factor;
			}

			vec4 c_heights = vec4(cp0.height, cp1.height, cp2.height, cp3.height);
			vec4 c_noise_offsets = vec4(cliff_edge_noise, -cliff_edge_noise, cliff_edge_noise * 0.75, -cliff_edge_noise * 0.75);

			vec4 c_hp0 = swatch_height_params[c_idx0];
			vec4 c_hp1 = swatch_height_params[c_idx1];
			vec4 c_hp2 = swatch_height_params[c_idx2];
			vec4 c_hp3 = swatch_height_params[c_idx3];

			vec4 c_h_scales = vec4(
				c_hp0.x > 0.001 ? c_hp0.x : 1.0,
				c_hp1.x > 0.001 ? c_hp1.x : 1.0,
				c_hp2.x > 0.001 ? c_hp2.x : 1.0,
				c_hp3.x > 0.001 ? c_hp3.x : 1.0
			);

			vec4 c_h_biases = vec4(c_hp0.y, c_hp1.y, c_hp2.y, c_hp3.y);

			vec4 c_h_powers = vec4(
				c_hp0.z > 0.001 ? c_hp0.z : 1.0,
				c_hp1.z > 0.001 ? c_hp1.z : 1.0,
				c_hp2.z > 0.001 ? c_hp2.z : 1.0,
				c_hp3.z > 0.001 ? c_hp3.z : 1.0
			);

			vec4 c_shaped_heights = vec4(
				pow(clamp(c_heights.x, 0.0, 1.0), c_h_powers.x),
				pow(clamp(c_heights.y, 0.0, 1.0), c_h_powers.y),
				pow(clamp(c_heights.z, 0.0, 1.0), c_h_powers.z),
				pow(clamp(c_heights.w, 0.0, 1.0), c_h_powers.w)
			);

			vec4 scaled_cliff_heights = vec4(
				c_shaped_heights.x * c_h_scales.x + c_h_biases.x,
				c_shaped_heights.y * c_h_scales.y + c_h_biases.y,
				c_shaped_heights.z * c_h_scales.z + c_h_biases.z,
				c_shaped_heights.w * c_h_scales.w + c_h_biases.w
			);

			vec4 c_height_scores = vec4(
				norm_c_weights.x > 0.001 ? (norm_c_weights.x + scaled_cliff_heights.x + c_noise_offsets.x) : -100.0,
				norm_c_weights.y > 0.001 ? (norm_c_weights.y + scaled_cliff_heights.y + c_noise_offsets.y) : -100.0,
				norm_c_weights.z > 0.001 ? (norm_c_weights.z + scaled_cliff_heights.z + c_noise_offsets.z) : -100.0,
				norm_c_weights.w > 0.001 ? (norm_c_weights.w + scaled_cliff_heights.w + c_noise_offsets.w) : -100.0
			);

			float max_c_score = max(max(c_height_scores.x, c_height_scores.y), max(c_height_scores.z, c_height_scores.w));
			float c_transition_depth = max(0.001, blend_softness);

			vec4 raw_c_blend = vec4(
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.x) * step(0.001, norm_c_weights.x),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.y) * step(0.001, norm_c_weights.y),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.z) * step(0.001, norm_c_weights.z),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.w) * step(0.001, norm_c_weights.w)
			);

			float c_blend_sum = raw_c_blend.x + raw_c_blend.y + raw_c_blend.z + raw_c_blend.w;
			cliff_blend_weights = c_blend_sum > 0.0001 ? raw_c_blend / c_blend_sum : norm_c_weights;

			cliff_albedo = (c_col0 * cliff_blend_weights.x +
			                c_col1 * cliff_blend_weights.y +
			                c_col2 * cliff_blend_weights.z +
			                c_col3 * cliff_blend_weights.w);
		} else {
			cliff_albedo = (c_col0 * norm_c_weights.x +
			                c_col1 * norm_c_weights.y +
			                c_col2 * norm_c_weights.z +
			                c_col3 * norm_c_weights.w);
		}

		vec3 cliff_normal = geom_normal;
		float cliff_ao = 1.0;
		float cliff_roughness = 0.85;

		float cw0 = cliff_blend_weights.x;
		float cw1 = cliff_blend_weights.y;
		float cw2 = cliff_blend_weights.z;
		float cw3 = cliff_blend_weights.w;

		if (enable_normal_mapping) {
			cliff_normal = normalize(cp0.normal * cw0 + cp1.normal * cw1 + cp2.normal * cw2 + cp3.normal * cw3);
			cliff_ao = (cp0.ao * cw0 + cp1.ao * cw1 + cp2.ao * cw2 + cp3.ao * cw3);
			cliff_roughness = (cp0.roughness * cw0 + cp1.roughness * cw1 + cp2.roughness * cw2 + cp3.roughness * cw3);
		} else {
			cliff_normal = cliff_geom_normal;
			cliff_ao = (cp0.ao * cw0 + cp1.ao * cw1 + cp2.ao * cw2 + cp3.ao * cw3);
			cliff_roughness = (cp0.roughness * cw0 + cp1.roughness * cw1 + cp2.roughness * cw2 + cp3.roughness * cw3);
		}

		if (enable_macro_noise && macro_normal_strength > 0.0) {
			float n_noise_y_x = (macro_fbm((v_world_pos.xz + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_y_z = (macro_fbm((v_world_pos.xz + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_y = vec3(n_noise_y_x, 0.0, n_noise_y_z);

			float n_noise_x_y = (macro_fbm((v_world_pos.zy + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.zy - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_x_z = (macro_fbm((v_world_pos.zy + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.zy - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_x = vec3(0.0, n_noise_x_y, n_noise_x_z);

			float n_noise_z_x = (macro_fbm((v_world_pos.xy + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xy - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_z_y = (macro_fbm((v_world_pos.xy + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xy - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_z = vec3(n_noise_z_x, n_noise_z_y, 0.0);

			vec3 triplanar_cliff_noise = noise_norm_x * blend_weights.x + noise_norm_y * blend_weights.y + noise_norm_z * blend_weights.z;
			cliff_normal = normalize(cliff_normal + triplanar_cliff_noise * macro_normal_strength);
		}

		terrain_color = cliff_albedo;
		blended_world_normal = cliff_normal;
		blended_ao = cliff_ao;
		blended_roughness = cliff_roughness;
	}

	float macro_var = 1.0;
	if (enable_macro_noise) {
		macro_var = mix(1.0 - macro_albedo_contrast, 1.0 + macro_albedo_contrast, base_fbm_val);
	}

	vec3 final_albedo = terrain_color * macro_var * v_color.rgb;
	float terrain_lum = dot(final_albedo, vec3(0.2126, 0.7152, 0.0722));
	final_albedo = mix(vec3(terrain_lum), final_albedo, 0.82);
	vec3 emission_color = vec3(0.0);
	
	if (pathing_visible) {
		vec2 pathing_uv = (v_world_pos.xz + terrain_size / 2.0) / terrain_size;
		int code = int(round(texture(pathing_texture, pathing_uv).r * 255.0));

		vec2 cell_frac = fract((v_world_pos.xz + terrain_size / 2.0) / grid_spacing);
		int sx = int(floor(cell_frac.x * 3.0));
		int sz = int(floor(cell_frac.y * 3.0));
		int box_idx = sz * 3 + sx;

		vec4 pathing_color = vec4(0.0);
		if (box_idx == 0) {
			if (code == 0) {
				pathing_color = vec4(0.9, 0.1, 0.1, 0.75);
			}
		} else if (box_idx == 1) {
			if ((code & 1) != 0) {
				pathing_color = vec4(0.2, 0.6, 1.0, 0.75);
			}
		} else if (box_idx == 2) {
			if ((code & 2) != 0) {
				pathing_color = vec4(0.0, 0.15, 0.7, 0.75);
			}
		} else if (box_idx == 3) {
			if ((code & 4) != 0) {
				pathing_color = vec4(0.85, 0.85, 0.0, 0.75);
			}
		} else if (box_idx == 4) {
			if ((code & 8) != 0) {
				pathing_color = vec4(0.2, 0.85, 0.2, 0.75);
			}
		} else if (box_idx == 5) {
			if ((code & 32) != 0) {
				pathing_color = vec4(0.6, 0.2, 0.8, 0.75);
			}
		}

		if (pathing_color.a > 0.0) {
			final_albedo = mix(final_albedo, pathing_color.rgb, pathing_color.a);
		}
	}
	
	float final_roughness = 0.9;

	if (enable_macro_noise) {
		float macro_roughness_var = mix(1.0 - macro_roughness_contrast, 1.0 + macro_roughness_contrast, base_fbm_val);
		final_roughness = clamp(blended_roughness * macro_roughness_var, 0.05, 1.0);
	} else {
		final_roughness = clamp(blended_roughness, 0.05, 1.0);
	}

	vec2 shroud_state = rts_shroud_sample(v_world_pos.xz);
	float shroud_light = rts_shroud_light_from_state(shroud_state);
	float shroud_explored_mask = rts_shroud_explored_mask(shroud_state);
	final_albedo *= shroud_light;
	emission_color *= shroud_light;

	ALBEDO = final_albedo;
	NORMAL = normalize((VIEW_MATRIX * vec4(blended_world_normal, 0.0)).xyz);
	AO = blended_ao * shroud_light;
	ROUGHNESS = mix(final_roughness, 1.0, shroud_explored_mask);
	METALLIC = 0.0;                 
	SPECULAR = specular_amt * shroud_light;
	EMISSION = emission_color;
}

