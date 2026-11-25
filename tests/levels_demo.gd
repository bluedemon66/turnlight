extends SceneTree

## Play both levels through the visible controls and verify the HUD.
## Use --fixed-fps 60. Set ONE_BEAM_LEVELS_CAPTURE to save rendered PNGs.

var game: Control
var dish: Control
var clock: Label
var score: Label
var stash: Button
var result: Label
var next: Button
var capture_dir: String


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	capture_dir = OS.get_environment("ONE_BEAM_LEVELS_CAPTURE")
	var scene: String = ProjectSettings.get_setting("application/run/main_scene")
	if not _check(change_scene_to_file(scene) == OK, "open configured demo"):
		return
	await scene_changed
	await process_frame
	game = current_scene
	dish = game.get_node("Dish")
	clock = game.get_node("Timer")
	score = game.get_node("Score")
	stash = game.get_node("Seeds")
	result = game.get_node("Result")
	next = game.get_node("Next")
	if not _check(clock.text == "03:00" and score.text == "0 / 150" and stash.text.ends_with("×3"), "level 1 setup matches the workbook"):
		return
	await _capture("01-level-1-start.png")
	_click_control(stash)
	for slot in [0, 1, 2]:
		_click(_dish_point("CellCenter", slot))
	if not _check(stash.text.ends_with("×0"), "plant three seeds with the inventory and empty slots"):
		return
	_click_control(game.get_node("Speed"))
	if not await _until_elapsed(45):
		return
	_drag_lamp(0, 1)
	if not await _until_elapsed(48):
		return
	await _capture("02-level-1-shift-light.png")
	if not await _until_elapsed(100):
		return
	_drag_lamp(1, 0)
	if not await _until_elapsed(145):
		return
	if not _check(score.text == "150 / 150" and not next.visible and not result.visible, "reaching 150 early keeps the clock running"):
		return
	_drag_lamp(0, 1)
	if not await _until_elapsed(180):
		return
	if not _check(next.visible and result.visible and score.text == "150 / 150", "level 1 passes at the 180-second cutoff"):
		return
	await _capture("03-level-1-passed.png")

	_click_control(next)
	_click(_dish_point("CellCenter", 0))
	if not _check(clock.text == "03:00" and score.text == "150 / 160" and stash.text.ends_with("×1"), "level 2 retains the three plants and grants one seed"):
		return
	var dark := game.get_node("Darkness") as Label
	if not _check(dark.text.begins_with("黑暗 0 /"), "darkness resets at entry"):
		return
	await _capture("04-level-2-start.png")
	_click_control(stash)
	_click(_dish_point("CellCenter", 3))
	_drag_lamp(1, 2)
	_click_control(game.get_node("Speed"))
	if not await _until_elapsed(54):
		return
	_drag_lamp(2, 0)
	if not await _until_elapsed(60):
		return
	await _capture("05-level-2-relay.png")
	if not await _until_elapsed(102):
		return
	_drag_lamp(0, 2)
	if not await _until_elapsed(148):
		return
	_drag_lamp(2, 0)
	if not await _until_elapsed(180):
		return
	if not _check(result.visible and result.text == "两关完成" and not next.visible, "level 2 passes through the UI"):
		return
	await _capture("06-level-2-passed.png")
	print("LEVEL 2 FINAL ", score.text)
	if not capture_dir.is_empty():
		print("PASS one-beam levels demo UI")
		quit(0)
		return

	_click_control(game.get_node("Restart"))
	if not _check(clock.text == "03:00" and score.text == "150 / 160" and stash.text.ends_with("×1") and not result.visible, "restart returns to level 2 entry"):
		return
	_click_control(game.get_node("Speed"))
	_click_control(game.get_node("Pause"))
	if not await _until_elapsed(180):
		return
	if not _check(result.visible and result.text == "两关完成", "concentrating on two carried plants can also pass level 2"):
		return
	print("LEVEL 2 FIXED LAMP ", score.text)
	_click_control(game.get_node("Restart"))
	_drag_lamp(1, 4)
	_click_control(game.get_node("Speed"))
	_click_control(game.get_node("Pause"))
	if not await _until_elapsed(180):
		return
	if not _check(result.visible and result.text == "生机未达标", "lighting empty plots causes a failed round"):
		return
	_click_control(game.get_node("Restart"))
	if not _check(clock.text == "03:00" and score.text == "150 / 160" and stash.text.ends_with("×1"), "retry after failure restores level 2 entry"):
		return
	print("PASS one-beam levels demo UI")
	quit(0)


func _until_elapsed(seconds: int) -> bool:
	for frame in range(12500):
		var clock_parts := clock.text.split(":")
		if clock_parts.size() == 2:
			var remaining := int(clock_parts[0]) * 60 + int(clock_parts[1])
			if remaining <= 180 - seconds:
				return true
		await process_frame
	return _check(false, "clock reaches %s elapsed seconds" % seconds)


func _capture(name: String) -> void:
	if capture_dir.is_empty():
		return
	await RenderingServer.frame_post_draw
	var image := root.get_texture().get_image()
	assert(image.save_png(capture_dir.path_join(name)) == OK)


func _dish_point(method: String, slot: int) -> Vector2:
	var local: Vector2 = dish.call(method, slot)
	return dish.get_global_transform_with_canvas() * local


func _drag_lamp(from: int, to: int) -> void:
	var start := _dish_point("RailPoint", from)
	var end := _dish_point("RailPoint", to)
	_motion(start, 0)
	_button(start, true)
	_motion(end, MOUSE_BUTTON_MASK_LEFT)
	_button(end, false)


func _click_control(control: Control) -> void:
	_click(control.get_global_rect().get_center())


func _click(point: Vector2) -> void:
	_motion(point, 0)
	_button(point, true)
	_button(point, false)


func _motion(point: Vector2, mask: int) -> void:
	var e := InputEventMouseMotion.new()
	e.position = point
	e.global_position = point
	e.button_mask = mask
	root.push_input(e, true)


func _button(point: Vector2, down: bool) -> void:
	var e := InputEventMouseButton.new()
	e.position = point
	e.global_position = point
	e.button_index = MOUSE_BUTTON_LEFT
	e.button_mask = MOUSE_BUTTON_MASK_LEFT if down else 0
	e.pressed = down
	root.push_input(e, true)


func _check(ok: bool, step: String) -> bool:
	if not ok:
		push_error("FAIL levels demo: " + step + " (timer " + str(clock.text if clock else "") + ", score " + str(score.text if score else "") + ")")
		quit(1)
	return ok
