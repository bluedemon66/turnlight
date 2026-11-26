shader_type spatial;
// Chamber floor, pit walls and soil. The beam is not a Godot light: its footprint is
// drawn here so it matches the lit cells exactly, and dome struts throw shadows that
// the presentation projects from the (unseen) lamp onto the floor every frame.
render_mode cull_disabled, unshaded;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(1.0);
uniform float uv_scale = 0.35;
uniform float clip_radius = 100.0;
uniform float clip_xscale = 1.0;
uniform vec3 ambient : source_color = vec3(0.66, 0.67, 0.72);

uniform vec2 beam_center = vec2(0.0);
uniform vec2 beam_axis = vec2(1.0, 0.0);
uniform vec2 beam_half = vec2(1.0, 0.5);
uniform float beam_soft = 0.16;
uniform vec3 beam_color : source_color = vec3(1.0, 0.83, 0.54);
uniform float beam_energy = 1.9;
uniform vec3 lamp_position = vec3(0.0, 12.0, -1.0);
uniform float scatter_energy = 0.5;
uniform float scatter_radius = 3.2;

const int MAX_SEGMENTS = 48;
uniform vec4 shadow_segments[MAX_SEGMENTS];
uniform int shadow_count = 0;
uniform float shadow_width = 0.05;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float footprint(vec2 p) {
	vec2 d = p - beam_center;
	vec2 q = vec2(dot(d, beam_axis), dot(d, vec2(-beam_axis.y, beam_axis.x))) / beam_half;
	float r = pow(pow(abs(q.x), 3.0) + pow(abs(q.y), 3.0), 1.0 / 3.0);
	return 1.0 - smoothstep(1.0 - beam_soft, 1.0 + beam_soft, r);
}

float segment_distance(vec2 p, vec2 a, vec2 b) {
	vec2 ab = b - a;
	float t = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-5), 0.0, 1.0);
	return length(p - a - ab * t);
}

float strut_shadow(vec2 p) {
	float shade = 0.0;
	for (int i = 0; i < MAX_SEGMENTS; i++) {
		if (i >= shadow_count) break;
		vec4 s = shadow_segments[i];
		float d = segment_distance(p, s.xy, s.zw);
		shade = max(shade, 1.0 - smoothstep(shadow_width * 0.4, shadow_width, d));
	}
	return shade;
}

void fragment() {
	if (length(vec2(world_pos.x / clip_xscale, world_pos.z)) > clip_radius) discard;
	vec3 n = world_normal;
	vec2 uv = abs(n.y) > 0.5 ? world_pos.xz : (abs(n.x) > 0.5 ? world_pos.zy : world_pos.xy);
	vec3 base = texture(albedo_tex, uv * uv_scale).rgb * tint;

	vec3 to_lamp = normalize(lamp_position - world_pos);
	float facing = clamp(dot(n, to_lamp), 0.0, 1.0);
	float pool = footprint(world_pos.xz) * mix(0.35, 1.0, facing);
	float spread = exp(-pow(length(world_pos.xz - beam_center) / scatter_radius, 2.0));
	float shadow = strut_shadow(world_pos.xz);
	// Deeper inside a pit is darker; the soil sits about 0.22 below the plate.
	float depth_dark = clamp(1.0 + world_pos.y * 1.6, 0.55, 1.0);

	vec3 light = ambient * depth_dark;
	light += beam_color * scatter_energy * spread * (1.0 - 0.55 * shadow);
	light += beam_color * beam_energy * pool * (1.0 - 0.8 * shadow);
	ALBEDO = base * light;
}
