global uniform sampler2D rts_shroud_texture : filter_linear, repeat_disable, hint_default_black;
global uniform vec4 rts_shroud_world_bounds;
global uniform float rts_shroud_lerp;

vec2 rts_shroud_sample(vec2 world_xz) {
	if (rts_shroud_world_bounds.z <= 0.0 || rts_shroud_world_bounds.w <= 0.0) {
		return vec2(1.0, 1.0);
	}

	vec2 shroud_uv = (world_xz - rts_shroud_world_bounds.xy) * rts_shroud_world_bounds.zw;
	vec4 raw = texture(rts_shroud_texture, clamp(shroud_uv, vec2(0.0), vec2(1.0)));
	return mix(raw.ba, raw.rg, clamp(rts_shroud_lerp, 0.0, 1.0));
}

float rts_shroud_light_from_state(vec2 shroud_state) {
	float explored = smoothstep(0.0, 1.0, shroud_state.x);
	return explored * mix(0.45, 1.0, shroud_state.y);
}

float rts_shroud_light(vec2 world_xz) {
	return rts_shroud_light_from_state(rts_shroud_sample(world_xz));
}

float rts_shroud_explored_mask(vec2 shroud_state) {
	return 1.0 - step(0.5, shroud_state.x);
}

float rts_shroud_visible_alpha(vec2 world_xz) {
	return smoothstep(0.0, 1.0, rts_shroud_sample(world_xz).y);
}
