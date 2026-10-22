uniform bool enable_procedural_motion = false;
uniform vec4 proc_anim_params1 = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 proc_anim_params2 = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 proc_anim_params3 = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 proc_anim_shake = vec4(0.0, 0.0, 0.0, 0.0);

float rts_procedural_motion_weight(vec3 local_position, vec3 local_normal) {
	float mask_mode = proc_anim_params1.y;
	float range_span = max(proc_anim_params1.w - proc_anim_params1.z, 0.001);
	float mask_power = proc_anim_params2.x;

	if (mask_mode < 0.5) {
		float norm_height = clamp((local_position.y - proc_anim_params1.z) / range_span, 0.0, 1.0);
		if (mask_power < 0.0) norm_height = 1.0 - norm_height;
		return pow(norm_height, max(abs(mask_power), 0.001));
	}

	if (mask_mode < 1.5) {
		float distance_from_axis = length(local_position.xz);
		float norm_distance = clamp((distance_from_axis - proc_anim_params1.z) / range_span, 0.0, 1.0);
		return pow(norm_distance, max(abs(mask_power), 0.001));
	}

	return clamp(dot(local_normal, vec3(0.0, 1.0, 0.0)), 0.0, 1.0);
}

vec3 rts_procedural_motion_offset(vec3 local_position, vec3 local_normal, vec3 world_position, float time) {
	if (!enable_procedural_motion || proc_anim_params1.x < 0.5) {
		return vec3(0.0);
	}

	float weight = rts_procedural_motion_weight(local_position, local_normal);
	if (weight <= 0.0001) {
		return vec3(0.0);
	}

	float motion_type = proc_anim_params1.x;
	float sway_frequency = proc_anim_params2.y;
	float sway_amplitude = proc_anim_params2.z;
	float flutter_frequency = proc_anim_params2.w;
	float flutter_amplitude = proc_anim_params3.x;
	float wind_influence = proc_anim_params3.y;
	float turbulence = proc_anim_params3.w;

	vec3 wind_direction = vec3(1.0, 0.0, 0.5);
	float sway_wave = sin(time * sway_frequency + (world_position.x + world_position.z) * turbulence);
	float flutter_wave = cos(time * flutter_frequency + local_position.y * 4.0);

	if (motion_type < 2.5) {
		vec3 sway_offset = wind_direction * (sway_wave * sway_amplitude) * wind_influence;
		vec3 flutter_axis = motion_type < 1.5 ? wind_direction : local_normal;
		return (sway_offset + flutter_axis * (flutter_wave * flutter_amplitude)) * weight;
	}

	if (motion_type < 3.5) {
		float flap_cycle = sin(time * sway_frequency);
		return vec3(0.0, flap_cycle * sway_amplitude * sign(local_position.x + 0.0001), 0.0) * weight;
	}

	float pulse_period = max(proc_anim_shake.z, 0.001);
	float pulse_progress = fract(time / pulse_period);
	float pulse_envelope = sin(pulse_progress * 3.14159265) * exp(-pulse_progress * max(proc_anim_shake.w, 0.0));
	float shake_wave = sin(time * proc_anim_shake.y * 6.28318531);
	return vec3(shake_wave, shake_wave * 0.5, -shake_wave * 0.7) * (proc_anim_shake.x * pulse_envelope * weight);
}
