shader_type spatial;
// Visible shaft of the beam, from the glass it enters through down to the floor.
// UV.x runs around the shaft, UV.y from the glass (0) to the floor (1).
render_mode blend_add, cull_disabled, depth_draw_never, unshaded, shadows_disabled;

uniform vec3 beam_color : source_color = vec3(1.0, 0.82, 0.52);
uniform float energy = 0.3;
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform float dust_amount = 0.9;

void fragment() {
	float core = pow(abs(dot(NORMAL, VIEW)), 1.6);
	float fade = smoothstep(0.0, 0.12, UV.y) * mix(0.75, 1.0, UV.y) * (1.0 - smoothstep(0.93, 1.0, UV.y) * 0.4);
	vec2 drift = vec2(0.0, TIME * 0.018);
	float rays = texture(noise, vec2(UV.x * 4.0, UV.y * 0.35) + drift * 0.3).r;
	float dust = smoothstep(0.8, 0.97, texture(noise, vec2(UV.x * 9.0, UV.y * 6.0) - drift).r);
	vec3 col = beam_color * energy * core * fade * (0.55 + 0.8 * rays);
	col += beam_color * dust * dust_amount * core * fade * 0.5;
	ALBEDO = col;
}
