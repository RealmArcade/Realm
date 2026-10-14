shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D texture_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_orm : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool has_orm_texture = false;
uniform bool has_player_mask = true;
uniform sampler2D texture_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool has_normal_texture = false;
uniform float normal_scale = 1.0;

uniform vec4 albedo_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float roughness_value : hint_range(0.0, 1.0) = 1.0;
uniform float metallic_value : hint_range(0.0, 1.0) = 0.0;

// Universal Transition / Dissolve Uniforms
uniform float progress : hint_range(0.0, 1.0) = 0.5;
uniform int transition_mode : hint_range(0, 6) = 1;
// 0: Vertical Slice / Wipe
// 1: Noise Burn & Dissolve
// 2: Hologram Scanlines
// 3: Ground Sink & Crumble
// 4: Radial Pulse / Burn
// 5: Glitch Pixelate
// 6: Alpha Fade & Fresnel Glow

uniform int direction : hint_range(0, 3) = 0;
// 0: Bottom to Top (Y+)
// 1: Top to Bottom (Y-)
// 2: Radial Outward (XZ)
// 3: Radial Inward (XZ)

uniform vec4 edge_color : source_color = vec4(1.0, 0.4, 0.1, 1.0);
uniform float edge_width : hint_range(0.001, 0.5) = 0.06;
uniform float edge_emission : hint_range(0.0, 20.0) = 5.0;
uniform float noise_scale : hint_range(0.5, 100.0) = 12.0;
uniform float noise_roughness : hint_range(0.0, 1.0) = 0.5;
uniform float fresnel_power : hint_range(0.1, 10.0) = 2.5;
uniform float vertex_displacement : hint_range(0.0, 1.0) = 0.0;
uniform float alpha_fade : hint_range(0.0, 1.0) = 1.0;

uniform vec3 model_bounds_min = vec3(-1.0, 0.0, -1.0);
uniform vec3 model_bounds_max = vec3(1.0, 2.0, 1.0);

varying vec3 v_world_pos;
varying vec3 v_local_pos;
varying vec3 v_normal;
varying vec2 v_uv;

// Procedural 3D Simplex-like Fast Hash Noise
float hash3D(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.2, 0.3));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise3D(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	
	return mix(
		mix(mix(hash3D(i + vec3(0.0, 0.0, 0.0)), hash3D(i + vec3(1.0, 0.0, 0.0)), f.x),
		    mix(hash3D(i + vec3(0.0, 1.0, 0.0)), hash3D(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
		mix(mix(hash3D(i + vec3(0.0, 0.0, 1.0)), hash3D(i + vec3(1.0, 0.0, 1.0)), f.x),
		    mix(hash3D(i + vec3(0.0, 1.0, 1.0)), hash3D(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
}

float fbm3D(vec3 p) {
	float val = 0.0;
	float amp = 0.5;
	for (int i = 0; i < 3; i++) {
		val += amp * noise3D(p);
		p *= 2.02;
		amp *= noise_roughness;
	}
	return val;
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_local_pos = VERTEX;
	v_normal = NORMAL;
	v_uv = UV;

	// Ground Sink & Crumble displacement
	if (vertex_displacement > 0.001) {
		float crumbleMask = clamp(1.0 - progress, 0.0, 1.0);
		float jitter = (hash3D(VERTEX * 20.0 + vec3(TIME * 5.0)) - 0.5) * 2.0;
		VERTEX += NORMAL * jitter * vertex_displacement * crumbleMask;
		if (transition_mode == 3) {
			VERTEX.y -= crumbleMask * vertex_displacement * 2.0;
		}
	}
}

void fragment() {
	vec4 albedo_tex = texture(texture_albedo, v_uv);
	vec3 base_color = albedo_tex.rgb * albedo_color.rgb;

	// Calculate Normalized Height / Position based on direction
	float heightRange = max(model_bounds_max.y - model_bounds_min.y, 0.001);
	float normY = clamp((v_local_pos.y - model_bounds_min.y) / heightRange, 0.0, 1.0);
	float maxRadius = max(max(abs(model_bounds_max.x), abs(model_bounds_max.z)), 0.001);
	float radDist = clamp(length(v_local_pos.xz) / maxRadius, 0.0, 1.0);

	float coord = normY;
	if (direction == 1) {
		coord = 1.0 - normY;
	} else if (direction == 2) {
		coord = radDist;
	} else if (direction == 3) {
		coord = 1.0 - radDist;
	}

	float noiseVal = fbm3D(v_local_pos * noise_scale);

	float cutoff = 0.0;
	float edge = 0.0;
	float alpha = 1.0;

	if (transition_mode == 0) {
		// Vertical Slice / Wipe
		cutoff = coord - progress;
		edge = 1.0 - smoothstep(0.0, edge_width, abs(cutoff));
	} else if (transition_mode == 1) {
		// Noise Burn & Dissolve
		float dissolveVal = mix(coord, noiseVal, 0.55);
		cutoff = dissolveVal - progress;
		edge = 1.0 - smoothstep(0.0, edge_width, abs(cutoff));
	} else if (transition_mode == 2) {
		// Hologram Scanlines
		float scanline = sin(v_world_pos.y * 30.0 + TIME * 10.0) * 0.5 + 0.5;
		cutoff = coord - progress;
		edge = (1.0 - smoothstep(0.0, edge_width, abs(cutoff))) + scanline * 0.4;
		alpha = alpha_fade * (0.4 + scanline * 0.6);
	} else if (transition_mode == 3) {
		// Ground Sink & Crumble
		float dissolveVal = coord + noiseVal * 0.4;
		cutoff = dissolveVal - (progress * 1.4);
		edge = 1.0 - smoothstep(0.0, edge_width, abs(cutoff));
	} else if (transition_mode == 4) {
		// Radial Pulse / Burn
		float pulse = radDist + noiseVal * 0.3;
		cutoff = pulse - progress;
		edge = 1.0 - smoothstep(0.0, edge_width, abs(cutoff));
	} else if (transition_mode == 5) {
		// Glitch Pixelate
		vec3 pixPos = floor(v_local_pos * 8.0) / 8.0;
		float pixNoise = hash3D(pixPos);
		cutoff = mix(coord, pixNoise, 0.7) - progress;
		edge = 1.0 - smoothstep(0.0, edge_width, abs(cutoff));
	} else if (transition_mode == 6) {
		// Alpha Fade & Fresnel Glow
		cutoff = 0.0;
		edge = 0.0;
		alpha = progress * alpha_fade;
	}

	// Discard pixel if dissolved
	if (transition_mode != 6 && cutoff > 0.0) {
		discard;
	}

	// Fresnel Rim
	float fresnel = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), fresnel_power);

	// Final Color & Emission
	vec3 finalColor = base_color;
	vec3 glowColor = edge_color.rgb * edge_emission;

	if (edge > 0.05) {
		finalColor = mix(finalColor, glowColor, clamp(edge, 0.0, 1.0));
		EMISSION = glowColor * edge;
	} else if (transition_mode == 2 || transition_mode == 6) {
		EMISSION = edge_color.rgb * fresnel * (1.0 - progress * 0.5) * edge_emission * 0.5;
	}

	ALBEDO = finalColor;
	ROUGHNESS = roughness_value;
	METALLIC = metallic_value;
	ALPHA = alpha;
}
