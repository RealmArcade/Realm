shader_type canvas_item;

uniform vec2 u_panel_size = vec2(840.0, 76.0);

void fragment() {
	float x = UV.x * u_panel_size.x;
	float h = u_panel_size.y;
	float s = h / 434.0;
	float w_left = 280.0 * s;
	float w_right = 280.0 * s;

	float tex_x = 0.0;
	if (x < w_left) {
		tex_x = x / s;
	} else if (x > u_panel_size.x - w_right) {
		float rx = x - (u_panel_size.x - w_right);
		tex_x = (2624.0 - 280.0) + (rx / s);
	} else {
		float center_w = max(u_panel_size.x - w_left - w_right, 1.0);
		float t = (x - w_left) / center_w;
		tex_x = 280.0 + t * (2344.0 - 280.0);
	}

	vec2 mapped_uv = vec2(clamp(tex_x / 2624.0, 0.0, 1.0), UV.y);
	COLOR = texture(TEXTURE, mapped_uv);
}
