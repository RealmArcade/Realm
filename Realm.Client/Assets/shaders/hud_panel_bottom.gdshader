shader_type canvas_item;

uniform vec2 u_console_size = vec2(1920.0, 240.0);

void fragment() {
	float x = UV.x * u_console_size.x;
	float h = u_console_size.y;
	float s = h / 433.0;
	float w_left = 935.0 * s;
	float w_right = 912.0 * s;

	float tex_x = 0.0;
	if (x < w_left) {
		tex_x = x / s;
	} else if (x > u_console_size.x - w_right) {
		float rx = x - (u_console_size.x - w_right);
		tex_x = (2752.0 - 912.0) + (rx / s);
	} else {
		float center_w = max(u_console_size.x - w_left - w_right, 1.0);
		float t = (x - w_left) / center_w;
		tex_x = 935.0 + t * (1840.0 - 935.0);
	}

	vec2 mapped_uv = vec2(clamp(tex_x / 2752.0, 0.0, 1.0), UV.y);
	COLOR = texture(TEXTURE, mapped_uv);
}
