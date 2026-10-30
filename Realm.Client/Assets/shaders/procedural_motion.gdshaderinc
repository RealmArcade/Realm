global uniform vec4 rts_wind_state;
uniform bool enable_procedural_motion = false;
uniform vec4 proc_anim_params1 = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 proc_anim_params2 = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 proc_anim_params3 = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 proc_anim_shake = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec3 proc_anim_region_min = vec3(-1.0, -1.0, -1.0);
uniform vec3 proc_anim_region_max = vec3(1.0, 3.0, 1.0);

vec3 rts_wind_direction() {
	vec3 wind_vector = vec3(rts_wind_state.x, 0.0, rts_wind_state.y);
	float wind_length = length(wind_vector);
	return wind_length > 0.0001 ? wind_vector / wind_length : vec3(0.894427, 0.0, 0.447214);
}

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

	if (mask_mode < 2.5) {
		return clamp(dot(local_normal, vec3(0.0, 1.0, 0.0)), 0.0, 1.0);
	}

	vec3 region_inside = min(local_position - proc_anim_region_min, proc_anim_region_max - local_position);
	float inside_depth = min(min(region_inside.x, region_inside.y), region_inside.z);
	float region_feather = max(proc_anim_params1.z, 0.001);
	float region_weight = clamp(inside_depth / region_feather, 0.0, 1.0);
	region_weight = pow(region_weight, max(abs(mask_power), 0.001));
	if (mask_power < 0.0) region_weight = 1.0 - region_weight;
	return region_weight;
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

	vec3 wind_direction = rts_wind_direction();
	float wind_strength = clamp(rts_wind_state.z, 0.0, 3.0);
	float instance_phase = fract(sin(dot(world_position.xz, vec2(12.9898, 78.233))) * 43758.5453) * 6.28318531;
	float sway_wave = sin(time * sway_frequency + instance_phase + (world_position.x + world_position.z) * turbulence);
	float flutter_wave = cos(time * flutter_frequency + instance_phase + local_position.y * 4.0);
	float amplitude_limit = abs(sway_amplitude) + abs(flutter_amplitude);

	if (motion_type < 2.5) {
		vec3 sway_offset = wind_direction * (sway_wave * sway_amplitude) * wind_influence * wind_strength;
		vec3 flutter_axis = motion_type < 1.5 ? wind_direction : local_normal;
		vec3 displacement = (sway_offset + flutter_axis * (flutter_wave * flutter_amplitude * wind_strength)) * weight;
		float displacement_length = length(displacement);
		if (amplitude_limit > 0.0001 && displacement_length > amplitude_limit) {
			displacement *= amplitude_limit / displacement_length;
		}
		return displacement;
	}

	if (motion_type < 3.5) {
		float flap_cycle = sin(time * sway_frequency + instance_phase);
		return vec3(0.0, flap_cycle * sway_amplitude * sign(local_position.x + 0.0001), 0.0) * weight;
	}

	float pulse_period = max(proc_anim_shake.z, 0.001);
	float pulse_progress = fract(time / pulse_period);
	float pulse_envelope = sin(pulse_progress * 3.14159265) * exp(-pulse_progress * max(proc_anim_shake.w, 0.0));
	float shake_wave = sin(time * proc_anim_shake.y * 6.28318531);
	return vec3(shake_wave, shake_wave * 0.5, -shake_wave * 0.7) * (proc_anim_shake.x * pulse_envelope * weight);
}
