extends SceneTree

## Drive the camera-feed levels scene through its real controls and check the HUD.
## Set ONE_BEAM_CHAMBER_CAPTURE to a folder to save rendered PNGs (needs a window, not --headless).

var game: Node
var hud: Control
var capture_dir: String


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	capture_dir = OS.get_environment("ONE_BEAM_CHAMBER_CAPTURE")
	var scene: String = ProjectSettings.get_setting("application/run/main_scene")
	if not _check(change_scene_to_file(scene) == OK, "open main scene"):
		return
	await scene_changed
	await _frames(3)
	game = current_scene
	hud = game.get_node("Hud/Root")
	var timer := hud.get_node("Timer") as Label
	var score := hud.get_node("Score") as Label
	var seeds := hud.get_node("Seeds") as Button
	if not _check(timer.text == "2:00" and score.text == "生机  0 / 105" and seeds.text == "种子 ×3", "level 1 starts with full clock, no vitality and three seeds"):
		return
	if not _check((hud.get_node("Level") as Label).text == "第 1 关" and not (hud.get_node("Cam") as Label).visible, "level 1 shows few camera clues"):
		return
	await _capture("01-start.png")

	_click_control(seeds)
	for slot in [0, 1, 5]:
		_click(game.call("CellScreen", slot))
	await _frames(2)
	if not _check(seeds.text.ends_with("×0"), "sow A1, B1 and A2 by clicking pits"):
		return
	if not _check(game.call("IsSlotLit", 0) and game.call("IsSlotLit", 1) and not game.call("IsSlotLit", 5), "the light starts on A1-B1"):
		return
	_click(game.call("CellScreen", 5))
	var card := hud.get_node("Card") as Control
	await _frames(2)
	if not _check(card.visible and (hud.get_node("Card").find_child("CardTitle", true, false) as Label).text.begins_with("A2"), "clicking a plant opens its card"):
		return
	game.call("AdvanceForTest", 60.0)
	await _frames(4)
	await _capture("02-still.png")

	_key(KEY_D)
	_key(KEY_D)
	if not _check(game.call("LampTargetSlot") == 2, "two presses of D aim the light at C1"):
		return
	await _wait(0.42)
	await _capture("03-moving.png")
	await _wait(0.6)
	if not _check(game.call("IsSlotLit", 2) and game.call("IsSlotLit", 3) and not game.call("IsSlotLit", 0), "the light arrives on C1-D1"):
		return
	_key(KEY_E)
	await _wait(0.3)
	if not _check(game.call("IsSlotLit", 2) and game.call("IsSlotLit", 8) and not game.call("IsSlotLit", 3), "E turns the ellipse 45 degrees onto C1 and D2"):
		return
	await _capture("04-turned.png")

	game.call("AdvanceForTest", 200.0)
	await _frames(3)
	var result := hud.get_node("Result") as Control
	var title := result.find_child("ResultTitle", true, false) as Label
	if not _check(result.visible and title.text.begins_with("第 1 关"), "time up shows the result card"):
		return
	await _capture("05-result.png")
	print("PASS one-beam chamber levels UI")
	quit(0)


func _frames(count: int) -> void:
	for i in count:
		await process_frame


func _wait(seconds: float) -> void:
	await create_timer(seconds).timeout


func _capture(name: String) -> void:
	if capture_dir.is_empty():
		return
	await RenderingServer.frame_post_draw
	var image := root.get_texture().get_image()
	assert(image.save_png(capture_dir.path_join(name)) == OK)


func _key(code: Key) -> void:
	var press := InputEventKey.new()
	press.physical_keycode = code
	press.pressed = true
	root.push_input(press, true)
	var release := InputEventKey.new()
	release.physical_keycode = code
	root.push_input(release, true)


func _click_control(control: Control) -> void:
	_click(control.get_global_rect().get_center())


func _click(point: Vector2) -> void:
	for down in [true, false]:
		var motion := InputEventMouseMotion.new()
		motion.position = point
		motion.global_position = point
		root.push_input(motion, true)
		var e := InputEventMouseButton.new()
		e.position = point
		e.global_position = point
		e.button_index = MOUSE_BUTTON_LEFT
		e.button_mask = MOUSE_BUTTON_MASK_LEFT if down else 0
		e.pressed = down
		root.push_input(e, true)


func _check(ok: bool, step: String) -> bool:
	if not ok:
		push_error("FAIL chamber levels: " + step)
		quit(1)
	return ok
