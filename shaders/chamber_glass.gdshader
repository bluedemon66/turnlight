shader_type spatial;
// Triangular dome panels: dark glass with condensation. The panel the beam passes
// through glows around the entry point; the lamp itself stays out of view.
render_mode blend_mix, cull_disabled, depth_draw_never, unshaded, shadows_disabled;

uniform sampler2D droplets : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec3 glass_color : source_color = vec3(0.12, 0.17, 0.26);
uniform vec3 droplet_color : source_color = vec3(0.62, 0.7, 0.8);
uniform float base_alpha = 0.55;
uniform float droplet_scale = 1.6;
uniform vec3 beam_color : source_color = vec3(1.0, 0.86, 0.6);
uniform vec3 entry_point = vec3(0.0, 100.0, 0.0);
uniform float entry_radius = 1.1;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float fresnel = pow(1.0 - abs(dot(NORMAL, VIEW)), 2.0);
	float drop = texture(droplets, UV * droplet_scale).r;
	float glow = exp(-pow(distance(world_pos, entry_point) / entry_radius, 2.0));
	vec3 col = glass_color * (0.7 + 0.9 * fresnel);
	col += droplet_color * drop * 0.28;
	col += beam_color * glow * (1.1 + drop * 1.4);
	ALBEDO = col;
	ALPHA = clamp(base_alpha + fresnel * 0.25 + drop * 0.3 + glow * 0.35, 0.0, 0.96);
}
