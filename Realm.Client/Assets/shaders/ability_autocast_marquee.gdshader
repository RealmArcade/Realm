shader_type canvas_item;

// A marching dashed frame drawn over an ability card whose auto-cast is on. The dashes travel on their own, from the
// renderer's clock, so no script walks this overlay per frame.

uniform vec4 accent_color : source_color = vec4(1.0, 0.83, 0.32, 1.0);
uniform vec4 shade_color : source_color = vec4(0.05, 0.04, 0.02, 0.9);
uniform float border_fraction = 0.075;
uniform float dash_count = 7.0;
uniform float travel_speed = 0.6;
uniform float border_smoothness = 0.012;

void fragment() {
	vec2 uv = UV;
	float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
	float border = 1.0 - smoothstep(border_fraction - border_smoothness, border_fraction + border_smoothness, edge);

	float along = (uv.x - uv.y) * dash_count + TIME * travel_speed;
	float light = step(0.5, fract(along));

	vec3 color = mix(shade_color.rgb, accent_color.rgb, light);
	float alpha = border * mix(0.55, 1.0, light) * accent_color.a;
	COLOR = vec4(color, alpha);
}
