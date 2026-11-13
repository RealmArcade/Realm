shader_type canvas_item;

// A dashed frame drawn over an ability card whose ability supports auto-cast. The dashes travel on their own, from the
// renderer's clock, so no script walks this overlay per frame, and they stand still while that auto-cast is off so the
// frame says "this card can cast itself" whether or not it currently is. A still frame is also drawn dimmer, so on and
// off are told apart at a glance rather than by noticing that nothing is moving.

uniform vec4 accent_color : source_color = vec4(1.0, 0.83, 0.32, 1.0);
uniform vec4 shade_color : source_color = vec4(0.05, 0.04, 0.02, 0.9);
uniform float border_fraction = 0.075;
uniform float dash_count = 7.0;
uniform float travel_speed = 0.6;
uniform float scroll_scale = 1.0;
uniform float border_smoothness = 0.012;
uniform float cast_enabled = 1.0;

void fragment() {
	vec2 uv = UV;
	float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
	float border = 1.0 - smoothstep(border_fraction - border_smoothness, border_fraction + border_smoothness, edge);

	float along = (uv.x - uv.y) * dash_count + TIME * travel_speed * scroll_scale;
	float light = step(0.5, fract(along));

	vec3 color = mix(shade_color.rgb, accent_color.rgb, light * mix(0.55, 1.0, cast_enabled));
	float alpha = border * mix(0.55, 1.0, light) * accent_color.a * mix(0.42, 1.0, cast_enabled);
	COLOR = vec4(color, alpha);
}
