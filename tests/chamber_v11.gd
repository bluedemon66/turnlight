extends SceneTree

## Subsystem documents V1.1 in the camera-feed scene: day labels and the 24-hour clock,
## the stuck message, diagonal keys, several info cards and their rules, remains, the shovel,
## harvesting, the failed-day result and the sound events.
## Set ONE_BEAM_V11_CAPTURE to a folder to save PNGs (needs a window, not --headless).

var game: Node
var hud: Control
var capture_dir: String


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	capture_dir = OS.get_environment("ONE_BEAM_V11_CAPTURE")
	var scene: String = ProjectSettings.get_setting("application/run/main_scene")
	if not _check(change_scene_to_file(scene) == OK, "open main scene"):
		return
	await scene_changed
	await _frames(3)
	game = current_scene
	hud = game.get_node("Hud/Root")
	var clock := hud.get_node("Clock") as Label
	var hint := hud.get_node("Hint") as Label
	if not _check((hud.get_node("Level") as Label).text == "第 1 天" and clock.text == "第 1 天　00:00", "day label and the clock at 00:00"):
		return
	if not _check((hud.get_node("Journal/JournalText") as Label).text.contains("时限　24 小时"), "the journal gives the limit as 24 hours"):
		return

	# Q from A1 facing east would point the beam off the field.
	_key(KEY_Q)
	await _frames(2)
	if not _check(hint.text == "卡住了", "a refused turn says 卡住了"):
		return
	await _wait(1.0)
	if not _check(hint.text == "卡住了", "still shown after 1 s"):
		return
	await _wait(0.7)
	if not _check(hint.text != "卡住了", "gone after 1.5 s"):
		return

	# Hold S, then press D: one diagonal step on top of the S step.
	_hold(KEY_S, true)
	await _frames(2)
	if not _check(game.call("LampTargetSlot") == 5, "S alone moves the target down to A2"):
		return
	_key(KEY_D)
	await _frames(2)
	_hold(KEY_S, false)
	await _frames(1)
	if not _check(game.call("LampTargetSlot") == 11, "D with S held moves diagonally to B3"):
		return
	_key(KEY_A)
	_key(KEY_W)
	_key(KEY_W)
	await _frames(2)

	# A plant about to wither on C3.
	game.call("SetPlantForTest", 12, 2, 0.0, 89.6, true, 0)
	_click_control(hud.get_node("Pause"))   # 开始
	await _frames(2)
	game.call("AdvanceForTest", 30.0 / 4.0 - 0.2)   # dark plants lose some, C3 dies
	await _frames(3)
	if not _check(game.call("HasRemainsViewForTest", 12) and game.call("HasRemainsForTest", 12), "C3 withers to death and leaves remains"):
		return
	if not _check(clock.text.begins_with("第 1 天　01:"), "the clock runs with the day (1:xx after ~7 s)"):
		return
	game.call("AdvanceForTest", 30.0 - game.call("ElapsedSeconds"))
	await _frames(2)
	if not _check(clock.text == "第 1 天　06:00", "30 s of game time is 06:00"):
		return
	# The field: lit A1 and a lit mother on B1, two plants in the dark.
	for p in [[0, 2, 20.0, 0.0, false, 0], [1, 3, 6.0, 10.0, false, 0], [18, 2, 20.0, 30.0, false, 0], [16, 1, 12.0, 20.0, false, 0]]:
		game.call("SetPlantForTest", p[0], p[1], p[2], p[3], p[4], p[5])
	await _frames(3)
	var open: Array = game.call("OpenCardSlots")
	if not _check(open.has(0) and open.has(1) and not open.has(18), "lit plants show their cards while time runs"):
		return

	_click(game.call("CellScreen", 12))
	_click(game.call("CellScreen", 1))
	_mouse_to(Vector2(640, 700))
	await _frames(3)
	open = game.call("OpenCardSlots")
	if not _check(open.has(12) and open.has(1) and open[open.size() - 1] == 1, "two clicked cards stay open; the last one is on top"):
		return
	var remains_card := hud.get_node("Cards/Card12")
	if not _check((remains_card.find_child("CardTitle", true, false) as Label).text == "C3 · 残骸", "the remains card says 残骸"):
		return
	var harvest := hud.get_node("Cards/Card1").find_child("Harvest", true, false) as Button
	if not _check(harvest.visible and not harvest.disabled and harvest.text.contains("3"), "the mother card has a harvest button"):
		return
	await _capture("v11-overview.png")

	# Sowing on remains is refused; the shovel clears them.
	_click_control(hud.get_node("Seeds"))
	_click(game.call("CellScreen", 12))
	await _frames(2)
	if not _check(game.call("HasRemainsForTest", 12) and hint.text.contains("残骸"), "cannot sow on remains"):
		return
	_click_control(hud.get_node("Shovel"))
	await _frames(1)
	if not _check((hud.get_node("Shovel") as Button).text == "已选  铲子", "the shovel button arms the shovel"):
		return
	var seeds_before: int = game.call("SeedsForTest")
	_click(game.call("CellScreen", 12))
	_click(game.call("CellScreen", 18))
	await _frames(2)
	if not _check(game.call("IsPitEmptyForTest", 12) and game.call("IsPitEmptyForTest", 18) and not game.call("HasRemainsForTest", 18), "the shovel clears remains and a live plant, leaving no remains"):
		return
	if not _check(game.call("SeedsForTest") == seeds_before, "shovelling gives no seeds back"):
		return
	_click_control(hud.get_node("Shovel"))
	await _frames(1)

	_click_control(harvest)
	await _frames(2)
	if not _check(game.call("SeedsForTest") == seeds_before + 3 and game.call("IsPitEmptyForTest", 1), "harvest: +3 seeds, the mother is gone without remains"):
		return
	_click_control(hud.get_node("Cards/Card1").find_child("Close", true, false))
	_mouse_to(Vector2(640, 700))
	await _frames(2)
	if not _check(not game.call("OpenCardSlots").has(1), "the close button closes a card"):
		return

	# Speed change closes clicked cards except lit plants'; pause closes everything.
	_click(game.call("CellScreen", 16))
	_click(game.call("CellScreen", 0))
	_mouse_to(Vector2(640, 700))
	await _frames(2)
	_click_control(hud.get_node("Speed"))
	_mouse_to(Vector2(640, 700))
	await _frames(2)
	open = game.call("OpenCardSlots")
	if not _check(not open.has(16) and open.has(0), "changing speed closes cards except those of lit plants"):
		return
	_click_control(hud.get_node("Pause"))
	_mouse_to(Vector2(640, 700))
	await _frames(2)
	if not _check(game.call("OpenCardSlots").is_empty(), "pausing closes every card"):
		return
	_click_control(hud.get_node("Pause"))
	await _frames(1)

	var log: Array = game.call("SoundLog")
	for id in ["B3", "E1", "C5", "F1", "D1", "D4", "E3", "E2", "pause", "resume", "loop+A1", "loop+B4", "D6"]:
		if not _check(log.has(id), "sound event %s was requested" % id):
			return

	game.call("AdvanceForTest", 200.0)
	await _frames(3)
	var result := hud.get_node("Result") as Control
	if not _check(result.visible and (result.find_child("ResultTitle", true, false) as Label).text == "第 1 天　未达标", "the failed day says 第 1 天　未达标"):
		return
	if not _check((result.find_child("ResultNote", true, false) as Label).text == "第 1 天未达标，可以重开这一天" and (result.find_child("Retry", true, false) as Button).text == "重开这一天", "and offers to replay the day"):
		return
	if not _check(game.call("OpenCardSlots").is_empty(), "cards are hidden at the result"):
		return
	log = game.call("SoundLog")
	if not _check(log.has("E5") and log.has("settle"), "result sound and music fade-out"):
		return
	await _capture("v11-result.png")
	_click_control(result.find_child("Retry", true, false))
	await _frames(2)
	if not _check(not result.visible and clock.text == "第 1 天　00:00" and game.call("SoundLog").has("E6"), "replaying the day starts it again"):
		return
	print("PASS subsystem V1.1 interface")
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


## Press or release a key through Input, so Input.is_physical_key_pressed sees it held.
func _hold(code: Key, down: bool) -> void:
	var e := InputEventKey.new()
	e.physical_keycode = code
	e.pressed = down
	Input.parse_input_event(e)
	Input.flush_buffered_events()


func _mouse_to(point: Vector2) -> void:
	var motion := InputEventMouseMotion.new()
	motion.position = point
	motion.global_position = point
	root.push_input(motion, true)


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


func _check(ok: bool, what: String) -> bool:
	if not ok:
		push_error("FAIL: " + what)
		quit(1)
	return ok
