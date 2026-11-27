extends SceneTree

## Status bars above the plants: fill the field with plants in different states, then
## select one, play a stage-up and a new seed, and let one plant die.
## Set ONE_BEAM_HPBAR_CAPTURE to a folder to save PNGs (needs a window, not --headless).

var game: Node
var capture_dir: String


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	capture_dir = OS.get_environment("ONE_BEAM_HPBAR_CAPTURE")
	var scene: String = ProjectSettings.get_setting("application/run/main_scene")
	if not _check(change_scene_to_file(scene) == OK, "open main scene"):
		return
	await scene_changed
	await _frames(3)
	game = current_scene

	# slot = row * 5 + column; the light starts on A1-B1. Stages: 0 seed, 1 seedling, 2 normal, 3 mother.
	# (slot, stage, growth, darkness, wilted, seeds)
	var plants := [
		[0, 2, 27.0, 0.0, false, 0],    # A1 lit, 60 %
		[1, 3, 12.0, 27.0, false, 0],   # B1 lit mother, seed 80 %
		[2, 1, 15.0, 18.0, false, 0],   # C1 growth 50 %, dark 20 %
		[3, 2, 36.0, 36.0, false, 0],   # D1 growth 80 %, dark 40 %
		[6, 2, 18.0, 56.0, false, 0],   # B2 near the wilt line
		[7, 3, 27.0, 20.0, false, 1],   # C2 mother behind C3
		[8, 1, 13.5, 72.0, true, 0],    # D2 wilted
		[11, 2, 11.0, 86.0, true, 0],   # B3 about to die
		[12, 3, 20.0, 10.0, false, 1],  # C3 mother in front of C2
		[17, 0, 9.0, 0.0, false, 0],    # C4 seed
		[21, 2, 30.0, 58.0, false, 0],  # B5 near, front row
		[23, 1, 9.0, 10.0, false, 0],   # D5 seedling
	]
	for p in plants:
		game.call("SetPlantForTest", p[0], p[1], p[2], p[3], p[4], p[5])
	await _frames(4)
	for p in plants:
		if not _check(game.call("HasBarForTest", p[0]), "every plant has a bar (slot %d)" % p[0]):
			return
	var far: Vector2 = game.call("BarPositionForTest", 2)
	var near: Vector2 = game.call("BarPositionForTest", 21)
	if not _check(far == far.round() and near == near.round(), "bars sit on whole pixels"):
		return
	await _capture("01-field.png")

	game.call("SelectForTest", 6)
	await _frames(2)
	await _capture("02-selected.png")
	await _wait(0.4)
	await _capture("03-warn-other-phase.png")
	game.call("SelectForTest", -1)

	# C1 seedling -> normal with a little left over; C3 mother makes its second seed.
	game.call("SetPlantForTest", 2, 2, 4.0, 18.0, false, 0)
	game.call("SetPlantForTest", 12, 3, 3.0, 10.0, false, 2)
	await _frames(2)
	if not _check(game.call("BarCelebratingForTest", 2) and game.call("BarCelebratingForTest", 12), "stage-up and new seed start the bar animation"):
		return
	await _wait(0.04)
	await _capture("04-stageup-flash.png")
	await _wait(0.16)
	await _capture("05-stageup-rise.png")
	await _wait(0.4)
	if not _check(not game.call("BarCelebratingForTest", 2), "the animation ends after 0.45 s"):
		return
	await _capture("06-after.png")

	game.call("SetPlantForTest", 11, -1, 0.0, 0.0, false, 0)
	await _frames(2)
	if not _check(not game.call("HasBarForTest", 11), "a dead plant's bar leaves the field"):
		return
	await _wait(0.8)
	if not _check(game.get_node_or_null("Field/Overlay/Bars/Bar11") == null, "the fading bar is freed with the plant"):
		return
	print("PASS plant status bars")
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


func _check(ok: bool, what: String) -> bool:
	if not ok:
		push_error("FAIL: " + what)
		quit(1)
	return ok
