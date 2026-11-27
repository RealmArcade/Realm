shader_type sky;

uniform sampler2D panorama : source_color, filter_linear, repeat_enable;
uniform bool use_panorama = false;

float cloud_hash(vec2 cell) {
	vec3 seed = fract(cell.xyx * vec3(0.1031, 0.1030, 0.0973));
	seed += dot(seed, seed.yzx + 33.33);
	return fract((seed.x + seed.y) * seed.z);
}

float cloud_noise(vec2 position) {
	vec2 cell = floor(position);
	vec2 local = fract(position);
	vec2 blend = local * local * (3.0 - 2.0 * local);
	return mix(mix(cloud_hash(cell), cloud_hash(cell + vec2(1.0, 0.0)), blend.x),
	           mix(cloud_hash(cell + vec2(0.0, 1.0)), cloud_hash(cell + vec2(1.0)), blend.x), blend.y);
}

void sky() {
	float elevation = EYEDIR.y;
	float below_board = smoothstep(0.0, 0.26, -elevation);
	float sky_height = mix(elevation, -elevation, below_board);
	float latitude = mix(SKY_COORDS.y, 1.0 - SKY_COORDS.y, below_board);
	latitude -= 0.15 * sin(PI * latitude);
	vec3 backdrop;
	if (use_panorama) {
		float drift = 0.026 * sin(TIME * 0.10 + latitude * 3.0);
		backdrop = texture(panorama, vec2(SKY_COORDS.x + drift, latitude)).rgb;
	} else {
		backdrop = mix(vec3(0.44, 0.56, 0.67), vec3(0.07, 0.21, 0.42), smoothstep(0.0, 0.94, sky_height));

	}

	vec2 projection = EYEDIR.xz / max(0.15, sky_height + 0.21);
	vec2 wind = vec2(0.066, 0.024) * TIME;
	vec2 cloud_uv = projection * 2.1;
	float density = cloud_noise(cloud_uv + wind) * 0.51
	              + cloud_noise(cloud_uv * 2.11 + wind * 1.56) * 0.29
	              + cloud_noise(cloud_uv * 4.37 + wind * 2.18) * 0.14
	              + cloud_noise(cloud_uv * 10.1 + wind * 3.4) * 0.06;
	float clouds = smoothstep(0.49, 0.69, density) * smoothstep(-0.02, 0.18, sky_height);
	vec3 color = backdrop;
	if (LIGHT0_ENABLED) {
		vec3 visible_sun = normalize(vec3(LIGHT0_DIRECTION.x, LIGHT0_DIRECTION.y * (1.0 - 1.57 * below_board), LIGHT0_DIRECTION.z));
		float alignment = clamp(dot(EYEDIR, visible_sun), 0.0, 1.0);
		float separation = length(EYEDIR - visible_sun);
		float radius = max(0.019, LIGHT0_SIZE);
		float disc = 1.0 - smoothstep(radius * 0.86, radius * 1.12, separation);
		vec3 sunlight = LIGHT0_COLOR * LIGHT0_ENERGY;
		float sunward_density = cloud_noise(cloud_uv + wind + LIGHT0_DIRECTION.xz * 0.35);
		float edge_light = smoothstep(-0.19, 0.16, density - sunward_density);
		color = mix(color, vec3(0.13, 0.30, 0.46), below_board * clamp(LIGHT0_ENERGY * 0.32, 0.0, 0.54));
		vec3 cloud_color = mix(vec3(0.22, 0.29, 0.37), vec3(0.57) + sunlight * 0.13, edge_light);
		color = mix(color, cloud_color, clouds * (use_panorama ? 0.74 : 0.87));
		color += sunlight * (0.13 * pow(alignment, 34.0) + 0.35 * pow(alignment, 410.0)
		                  + (AT_CUBEMAP_PASS ? 0.0 : 4.0 * disc)) * (1.0 - 0.88 * clouds);
	} else {
		color = mix(color, backdrop * 1.12, clouds);
	}
	COLOR = color;
}
