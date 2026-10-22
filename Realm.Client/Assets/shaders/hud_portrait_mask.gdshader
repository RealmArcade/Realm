shader_type canvas_item;

void fragment() {
	vec2 center = UV - vec2(0.5);
	if (dot(center, center) > 0.25) {
		discard;
	}
	COLOR = texture(TEXTURE, UV);
}
