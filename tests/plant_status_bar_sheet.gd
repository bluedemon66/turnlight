extends SceneTree

## Close-up sheet of the plant status bar at 5x, on a fixed clock, to compare with
## docs/ui-mockup/hpbar/out/final_hpbar_states.png and final_hpbar_stageup_keys.png.
## Left: the states. Right: stage-up and new-seed keyframes.
## Set ONE_BEAM_HPBAR_CAPTURE to a folder to save the PNG (needs a window, not --headless).

const Bar := preload("res://src/Levels/Presentation/Chamber/PlantStatusBar.cs")
const S := 5

var capture_dir: String
var font: SystemFont


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	capture_dir = OS.get_environment("ONE_BEAM_HPBAR_CAPTURE")
	font = SystemFont.new()
	font.font_names = PackedStringArray(["Microsoft YaHei", "Noto Sans CJK SC", "sans-serif"])
	var ground := ColorRect.new()
	ground.color = Color("0e161d")
	ground.size = Vector2(1280, 720)
	root.add_child(ground)
	var zoom := Node2D.new()
	zoom.scale = Vector2(S, S)
	root.add_child(zoom)

	# label, stage, growth, darkness, wilted, seeds, lit   (normal stage needs 45, mother's first seed 15; dark 63 wilts, 90 dies)
	var rows := [
		["在光里，正在长", 2, 27.0, 0.0, false, 0, true],
		["不在光里", 2, 27.0, 0.0, false, 0, false],
		["生长 50%，黑暗 20%", 2, 22.5, 18.0, false, 0, false],
		["生长 80%，黑暗 40%", 2, 36.0, 36.0, false, 0, false],
		["黑暗 56：闪红（亮的一刻）", 2, 18.0, 56.0, false, 0, false],
		["蔫（黑暗 72）", 2, 20.25, 72.0, true, 0, false],
		["快死（黑暗 86）", 2, 11.25, 86.0, true, 0, false],
		["母体，算结籽", 3, 13.5, 27.0, false, 0, true],
	]
	for i in rows.size():
		var r: Array = rows[i]
		var b := _bar(zoom, Vector2(14, 8 + i * 16), 0.4)
		b.call("SyncForTest", r[1], r[2], r[3], r[4], r[5], r[6], true)
		_label(r[0], Vector2(64 * S, (8 + i * 16) * S - 6))

	# Stage-up with 30 % darkness, and a mother's second seed, at fixed times after the trigger.
	var keys := [0.03, 0.05, 0.09, 0.13, 0.2, 0.3, 0.44]
	for j in keys.size():
		var t: float = keys[j]
		var y := 8 + j * 16
		var up := _bar(zoom, Vector2(146, y), 10.0)
		up.call("SyncForTest", 1, 29.0, 27.0, false, 0, true, true)
		up.call("SyncForTest", 2, 4.5, 27.0, false, 0, true, false)
		up.set("TestClock", 10.0 + t)
		var seed := _bar(zoom, Vector2(206, y), 10.0)
		seed.call("SyncForTest", 3, 14.0, 9.0, false, 0, true, true)
		seed.call("SyncForTest", 3, 1.5, 9.0, false, 1, true, false)
		seed.set("TestClock", 10.0 + t)
		_label("%.2f 秒" % t, Vector2(124 * S, y * S - 6))
	_label("左：各状态　右：升阶段（30% 黑暗） / 母体结籽", Vector2(14 * S, 690))
	await _frames(4)
	if capture_dir.is_empty():
		print("PASS plant status bar sheet")
		quit(0)
		return
	await RenderingServer.frame_post_draw
	var image := root.get_texture().get_image()
	assert(image.save_png(capture_dir.path_join("10-sheet.png")) == OK)
	print("PASS plant status bar sheet")
	quit(0)


func _bar(parent: Node, top_left: Vector2, clock: float) -> Node2D:
	var b: Node2D = Bar.new()
	b.set("TestClock", clock)
	b.set("Anchor", top_left + Vector2(23, 16))
	parent.add_child(b)
	return b


func _label(text: String, at: Vector2) -> void:
	var l := Label.new()
	l.text = text
	l.position = at
	l.add_theme_font_override("font", font)
	l.add_theme_font_size_override("font_size", 22)
	l.add_theme_color_override("font_color", Color("efe6d2"))
	root.add_child(l)


func _frames(n: int) -> void:
	for i in n:
		await process_frame
