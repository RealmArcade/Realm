shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert_wrap, specular_schlick_ggx;

uniform bool effect_enabled = true;
uniform vec4 base_color : source_color = vec4(0.15, 0.12, 0.1, 1.0);
uniform vec4 emission_color : source_color = vec4(1.0, 0.4, 0.05, 1.0);
uniform float emission_energy : hint_range(0.0, 20.0) = 4.0;
uniform float fresnel_power : hint_range(0.1, 10.0) = 3.0;
uniform vec4 fresnel_color : source_color = vec4(1.0, 0.6, 0.1, 1.0);
uniform float fresnel_factor : hint_range(0.0, 5.0) = 1.5;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;

uniform sampler2D noise_texture : filter_linear_mipmap, repeat_enable;
uniform bool use_procedural_noise = true;
uniform float noise_scale : hint_range(0.1, 20.0) = 3.0;
uniform vec2 uv_scroll_speed_1 = vec2(0.3, 0.15);
uniform vec2 uv_scroll_speed_2 = vec2(-0.2, 0.3);
uniform float threshold_cutoff : hint_range(0.0, 1.0) = 0.5;
uniform float threshold_smoothness : hint_range(0.001, 0.5) = 0.1;
uniform int emission_mask_source : hint_range(0, 3) = 0; // 0=Noise, 1=Vertex Color / Spikes, 2=Fresnel, 3=Alpha

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

void fragment() {
	vec4 tex_albedo = use_albedo_texture ? texture(albedo_texture, UV) : base_color;
	vec3 surface_base = tex_albedo.rgb;

	vec3 norm = (length(NORMAL) > 0.1) ? normalize(NORMAL) : normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	NORMAL = norm;

	if (!effect_enabled) {
		ALBEDO = surface_base;
		EMISSION = vec3(0.0);
		ROUGHNESS = 0.4;
		SPECULAR = 0.5;
		METALLIC = 0.1;
	} else {
		vec2 uv1 = UV * noise_scale + uv_scroll_speed_1 * TIME;
		vec2 uv2 = UV * noise_scale * 1.37 + uv_scroll_speed_2 * TIME;

		float n1 = use_procedural_noise ? procedural_noise(uv1) : texture(noise_texture, uv1).r;
		float n2 = use_procedural_noise ? procedural_noise(uv2) : texture(noise_texture, uv2).r;
		float combined_noise = (n1 * 0.6 + n2 * 0.4);

		float crack_mask = smoothstep(threshold_cutoff - threshold_smoothness, threshold_cutoff + threshold_smoothness, combined_noise);

		float NdotV = clamp(abs(dot(norm, VIEW)), 0.0, 1.0);
		float fresnel = pow(1.0 - NdotV, fresnel_power) * fresnel_factor;

		float mask = crack_mask;
		if (emission_mask_source == 1) {
			float vert_mask = 0.0;
			if (use_albedo_texture) {
				vec4 tex_raw = texture(albedo_texture, UV);
				if (tex_raw.r > 0.45 && tex_raw.b < 0.35) {
					vert_mask = tex_raw.r;
				}
			}
			mask = (vert_mask > 0.001) ? (vert_mask * crack_mask) : 0.0;
		} else if (emission_mask_source == 2) {
			mask = fresnel;
		} else if (emission_mask_source == 3) {
			float tex_a = use_albedo_texture ? texture(albedo_texture, UV).a : 1.0;
			mask = tex_a * crack_mask;
		}

		float crack_blend = clamp(mask * min(emission_energy, 1.0) * 0.75, 0.0, 1.0);
		vec3 albedo_out = mix(surface_base, emission_color.rgb, crack_blend);
		vec3 emission_out = (emission_color.rgb * mask * emission_energy);
		if (emission_mask_source == 1) {
			if (fresnel_factor > 0.001) {
				emission_out += (fresnel_color.rgb * (fresnel * mask * 0.5));
			}
		} else if (emission_mask_source == 2) {
			emission_out = (fresnel_color.rgb * fresnel * emission_energy);
		} else if (fresnel_factor > 0.001) {
			emission_out += (fresnel_color.rgb * (fresnel * 0.5));
		}

		ALBEDO = albedo_out;
		EMISSION = emission_out;
		ROUGHNESS = clamp(0.4 - mask * 0.2, 0.1, 0.8);
		SPECULAR = 0.5;
		METALLIC = 0.1;
	}
}
