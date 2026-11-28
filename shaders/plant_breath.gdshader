shader_type canvas_item;
// 植物呼吸。遮罩：R=茎弯曲权重(根0→顶1)  G=叶片权重(茎0→叶尖1)  B=每片叶的相位差
uniform sampler2D mask : filter_linear, repeat_disable;
uniform float period = 3.0;          // 一次呼吸几秒。取 3600 的约数，TIME 回绕时不跳
uniform float breath = 0.018;        // 茎上下起伏
uniform float sway = 0.010;          // 茎左右摆
uniform float leaf_lift = 0.020;     // 叶片上下开合
uniform float leaf_spread = 0.010;   // 叶片向外舒展
uniform float leaf_lag = 0.125;      // 叶片比茎慢多少拍(1=整个周期)
uniform float leaf_var = 0.12;       // 叶片之间的错拍
uniform float root_y = 0.9375;       // 根部底线(UV)
uniform float phase = 0.0;           // 每株错开，0~1

varying vec4 tint;

void vertex() {
	tint = COLOR;
}

void fragment() {
	vec3 m = texture(mask, UV).rgb;
	float t = TIME / period + phase;
	float s = sin(TAU * t);
	float s2 = sin(TAU * t - 1.2);
	float l = sin(TAU * (t - leaf_lag - (m.b - 0.5) * leaf_var));
	vec2 off = vec2(0.0);
	off.y -= breath * s * m.r * max(root_y - UV.y, 0.0);
	off.x += sway * s2 * m.r * m.r;
	off.y -= leaf_lift * l * m.g;
	off.x += leaf_spread * l * m.g * clamp((UV.x - 0.5) * 4.0, -1.0, 1.0);
	COLOR = texture(TEXTURE, UV - off) * tint;
}
