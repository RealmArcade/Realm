shader_type spatial;
render_mode world_vertex_coords, cull_disabled, diffuse_burley, specular_disabled;

#include "res://Assets/shaders/shroud.gdshaderinc"

global uniform vec4 rts_wind_state;
uniform sampler2DArray terrain_textures : source_color, filter_linear_mipmap;
uniform vec4 swatch_albedo_params[256];
varying vec2 grass_world_xz;
varying vec3 blade_albedo;

void vertex() {
	float tip = max((VERTEX.y - MODEL_MATRIX[3].y) / max(length(MODEL_MATRIX[1].xyz), 0.001), 0.0);
	vec3 world = VERTEX;
	float gust = sin(dot(world.xz, vec2(0.41, 0.27)) + TIME * 1.9);
	VERTEX += vec3(rts_wind_state.x, 0.0, rts_wind_state.y)
	          * (tip * tip * gust * clamp(rts_wind_state.z, 0.0, 2.0) * 0.055);
	grass_world_xz = world.xz;
	int slot = int(INSTANCE_CUSTOM.x + 0.5);
	vec3 pigment = textureLod(terrain_textures, vec3(INSTANCE_CUSTOM.yz, float(slot)), 5.5).rgb;
	blade_albedo = pigment * mix(vec3(0.18), pigment, 0.74) * swatch_albedo_params[slot].rgb * mix(0.78, 1.16, tip);
}

void fragment() {
	vec3 up = VIEW_MATRIX[1].xyz;
	NORMAL = normalize(NORMAL * sign(dot(NORMAL, up)) + 0.55 * up);
	ALBEDO = blade_albedo * rts_shroud_light(grass_world_xz);
	ROUGHNESS = 0.95;
}
