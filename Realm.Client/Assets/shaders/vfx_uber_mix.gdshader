shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, unshaded;

uniform sampler2D base_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_base_texture = false;
uniform vec2 base_uv_scroll = vec2(0.0, 0.0);
uniform vec2 base_uv_scale = vec2(1.0, 1.0);
uniform bool use_flipbook = false;
uniform int flipbook_columns = 1;
uniform int flipbook_rows = 1;
uniform float flipbook_fps = 12.0;
uniform bool flipbook_subframe_blend = true;

uniform bool luminance_to_alpha = true;
uniform float luminance_threshold : hint_range(0.0, 1.0) = 0.0;
uniform float luminance_smoothness : hint_range(0.001, 0.5) = 0.1;
uniform bool use_grayscale = true;
uniform bool invert_mask = false;
uniform float high_pass_cutoff : hint_range(0.0, 1.0) = 0.0;

uniform sampler2D noise_texture : filter_linear_mipmap, repeat_enable;
uniform bool use_noise_texture = false;
uniform vec2 noise_uv_scroll = vec2(0.0, 0.0);
uniform vec2 noise_uv_scale = vec2(1.0, 1.0);
uniform float noise_distortion_strength : hint_range(0.0, 2.0) = 0.0;

uniform vec4 base_color : source_color = vec4(1.0, 0.45, 0.1, 1.0);
uniform vec4 secondary_color : source_color = vec4(0.8, 0.1, 0.0, 1.0);
uniform vec4 core_color : source_color = vec4(1.0, 0.95, 0.8, 1.0);
uniform float color_mix_ratio : hint_range(0.0, 1.0) = 1.0;
uniform float emission_boost : hint_range(0.0, 20.0) = 3.0;
uniform float core_threshold : hint_range(0.0, 1.0) = 0.7;

uniform bool enable_radial_falloff = false;
uniform float radial_falloff_start : hint_range(0.0, 1.0) = 0.6;
uniform float radial_falloff_end : hint_range(0.0, 1.0) = 1.0;

uniform bool enable_length_fade = false;
uniform float length_fade_start : hint_range(0.0, 1.0) = 0.0;
uniform float length_fade_end : hint_range(0.0, 1.0) = 1.0;
uniform float erosion_progress : hint_range(0.0, 1.0) = 0.0;

uniform bool enable_fresnel = false;
uniform float fresnel_power : hint_range(0.1, 10.0) = 2.5;
uniform float fresnel_intensity : hint_range(0.0, 10.0) = 1.5;

uniform bool enable_depth_fade = true;
uniform float depth_fade_distance : hint_range(0.0, 5.0) = 0.3;
uniform sampler2D depth_texture : hint_depth_texture, filter_linear;

uniform float surface_normal_offset : hint_range(-0.5, 0.5) = 0.01;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float procedural_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i + vec2(0.0, 0.0)), hash(i + vec2(1.0, 0.0)), u.x),
	           mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
	VERTEX += NORMAL * surface_normal_offset;
}

void fragment() {
	vec2 n_uv = UV * noise_uv_scale + noise_uv_scroll * TIME;
	vec2 noise_offset = vec2(0.0);
	if (use_noise_texture) {
		vec4 n_sample = texture(noise_texture, n_uv);
		noise_offset = (n_sample.rg - vec2(0.5)) * 2.0;
	} else {
		float n1 = procedural_noise(n_uv * 3.0);
		float n2 = procedural_noise((n_uv + vec2(17.3, 31.7)) * 3.0);
		noise_offset = vec2(n1 - 0.5, n2 - 0.5) * 2.0;
	}

	vec2 final_base_uv = UV * base_uv_scale + base_uv_scroll * TIME + noise_offset * (noise_distortion_strength * 0.5);

	float mask = 1.0;
	vec3 base_tex_rgb = vec3(1.0);
	if (use_base_texture) {
		vec4 base_sample;
		if (use_flipbook && flipbook_columns > 0 && flipbook_rows > 0 && (flipbook_columns > 1 || flipbook_rows > 1)) {
			int total_frames = flipbook_columns * flipbook_rows;
			float raw_frame = mod(TIME * flipbook_fps, float(total_frames));
			int frame_curr = int(floor(raw_frame)) % total_frames;
			int frame_next = (frame_curr + 1) % total_frames;
			float blend_weight = fract(raw_frame);

			vec2 sheet_size = vec2(float(flipbook_columns), float(flipbook_rows));
			vec2 wrapped_uv = fract(final_base_uv);

			vec2 col_row_curr = vec2(float(frame_curr % flipbook_columns), float(frame_curr / flipbook_columns));
			vec2 uv_curr = (wrapped_uv + col_row_curr) / sheet_size;

			vec4 sample_curr = texture(base_texture, uv_curr);
			if (flipbook_subframe_blend) {
				vec2 col_row_next = vec2(float(frame_next % flipbook_columns), float(frame_next / flipbook_columns));
				vec2 uv_next = (wrapped_uv + col_row_next) / sheet_size;
				vec4 sample_next = texture(base_texture, uv_next);
				base_sample = mix(sample_curr, sample_next, blend_weight);
			} else {
				base_sample = sample_curr;
			}
		} else {
			base_sample = texture(base_texture, final_base_uv);
		}

		base_tex_rgb = base_sample.rgb;

		if (luminance_to_alpha || use_grayscale) {
			float lum = dot(base_sample.rgb, vec3(0.299, 0.587, 0.114));
			mask = luminance_to_alpha ? lum : base_sample.a;
		} else {
			mask = base_sample.a;
		}
	}

	if (invert_mask) {
		mask = 1.0 - mask;
	}

	if (high_pass_cutoff > 0.001) {
		mask = max(0.0, mask - high_pass_cutoff) / max(0.001, 1.0 - high_pass_cutoff);
	}

	if (luminance_threshold > 0.0001) {
		mask = smoothstep(luminance_threshold - luminance_smoothness, luminance_threshold + luminance_smoothness, mask);
	}

	if (enable_radial_falloff) {
		float r = length(UV - vec2(0.5)) * 2.0;
		float rad_fade = 1.0 - smoothstep(radial_falloff_start, radial_falloff_end, r);
		mask *= rad_fade;
	}

	if (enable_length_fade) {
		float len_coord = clamp(UV.y + erosion_progress, 0.0, 1.0);
		float len_fade = 1.0 - smoothstep(length_fade_start, length_fade_end, len_coord);
		mask *= len_fade;
	}

	float fresnel_factor = 0.0;
	if (enable_fresnel) {
		float NdotV = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
		fresnel_factor = pow(1.0 - NdotV, fresnel_power) * fresnel_intensity;
	}

	if (enable_depth_fade && depth_fade_distance > 0.001) {
		float depth_raw = texture(depth_texture, SCREEN_UV).r;
		vec4 upos = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth_raw, 1.0);
		float pixel_z = -upos.z / upos.w;
		float surface_z = -VERTEX.z;
		float depth_diff = max(0.0, pixel_z - surface_z);
		float depth_fade = clamp(depth_diff / depth_fade_distance, 0.0, 1.0);
		mask *= depth_fade;
	}

	vec3 ramp_color = mix(secondary_color.rgb, base_color.rgb, clamp(mask * 1.4, 0.0, 1.0));
	if (mask > core_threshold) {
		float core_factor = clamp((mask - core_threshold) / max(0.001, 1.0 - core_threshold), 0.0, 1.0);
		ramp_color = mix(ramp_color, core_color.rgb, core_factor);
	}

	vec3 color = use_base_texture ? mix(base_tex_rgb, ramp_color, color_mix_ratio) : ramp_color;
	color *= (1.0 + (emission_boost - 1.0) * mask);

	if (enable_fresnel) {
		color += secondary_color.rgb * fresnel_factor * emission_boost;
		mask = max(mask, clamp(fresnel_factor, 0.0, 1.0));
	}

	float out_alpha = clamp(mask * base_color.a, 0.0, 1.0);
	ALBEDO = color;
	ALPHA = out_alpha;
}
