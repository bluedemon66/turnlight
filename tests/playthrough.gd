extends SceneTree

## Play through the ecology sandbox using Godot's actual GUI/input.
## Run with --fixed-fps 60 to advance simulation time faster than wall time.

var test_checkpoint: String
var hud: Control
var world: Node2D
var plants: Node2D
var lamp: Node2D
var harvest: Button
var fruit: Button
var objective: Label


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	test_checkpoint = "user://playthrough-%s-%s.json" % [OS.get_process_id(), Time.get_ticks_usec()]
	ProjectSettings.set_setting("one_beam/checkpoint_file", test_checkpoint)
	if not await _load_scene():
		return

	_drag(_lamp_point(), _point(Vector2(176, 139)))
	_sow(0, Vector2(218, 174))
	var pause := hud.get_node("Session/Pause") as Button
	var growth := hud.get_node("Tools/GrowthBar") as ProgressBar
	_click_control(pause)
	var paused_growth := growth.value
	for frame in range(45):
		await process_frame
	if not _check(is_equal_approx(growth.value, paused_growth), "pause freezes plant growth"):
		return
	_click_control(pause)
	if not await _wait(func() -> bool: return not harvest.disabled, "sunflower becomes harvestable"):
		return
	_click_control(harvest)
	if not _check(objective.text.contains("光源母体") and plants.get_child_count() == 0, "harvest returns seeds and reveals mother objective"):
		return

	_sow(0, Vector2(218, 174))
	var sunflower := plants.get_child(0)
	_sow(1, Vector2(148, 132))
	if not await _wait(func() -> bool: return objective.text.contains("直到破顶"), "mother matures and locks lamp"):
		return
	var locked_lamp := lamp.position
	_drag(_lamp_point(), _point(Vector2(340, 170)))
	if not _check(lamp.position.is_equal_approx(locked_lamp), "lamp cannot move after the scientist leaves"):
		return

	_sow(2, Vector2(240, 120))
	if not _check(not fruit.disabled, "mother provides a usable fruit"):
		return
	_click_control(fruit)
	_click_control(hud.get_node("Tools/Actions/Aim"))
	_click(_point(Vector2(240, 1)))
	if not await _wait(func() -> bool: return objective.text.contains("度过首夜"), "bamboo breaks roof in the direction of growth"):
		return
	if not _check(is_instance_valid(sunflower) and plants.get_child_count() == 3, "all plants continue into the lab"):
		return
	if not _check(FileAccess.file_exists(test_checkpoint), "phase checkpoint saved to disk"):
		return

	# A fresh instance retrieves the checkpoint produced by gameplay.
	if not await _load_scene():
		return
	_click_control(hud.get_node("Session/Restore"))
	var dialog := hud.get_node("Confirm") as ConfirmationDialog
	if not _check(dialog.visible, "restore prompts before replacing the session"):
		return
	dialog.confirmed.emit()
	dialog.hide()
	if not _check(objective.text.contains("度过首夜") and plants.get_child_count() == 3, "restore repopulates the open laboratory"):
		return

	_sow(0, Vector2(510, 130))
	_sow(0, Vector2(780, 130))
	_sow(0, Vector2(120, 420))
	_sow(1, Vector2(390, 420))
	_sow(2, Vector2(590, 420))
	if not _check(plants.get_child_count() == 8, "new plants extend the ecosystem beyond the dish"):
		return
	if not await _wait(func() -> bool: return objective.text.contains("生态已稳定"), "regional coverage survives the first night", 2200):
		return

	_click_control(hud.get_node("Session/NewGame"))
	if not _check(dialog.visible, "new game prompts before resetting"):
		return
	dialog.confirmed.emit()
	dialog.hide()
	for x in [420, 480, 540]:
		_sow(0, Vector2(x, 130))
	if not _check(plants.get_child_count() == 3, "starting seeds spent on three dark plots"):
		return
	if not await _wait(func() -> bool: return objective.text.contains("已灭绝"), "losing all plants and seeds triggers extinction", 1900):
		return
	_click_control(hud.get_node("Session/Restore"))
	dialog.confirmed.emit()
	dialog.hide()
	if not _check(plants.get_child_count() == 0 and objective.text.contains("收割留种"), "restore recovers a playable world after extinction"):
		return

	DirAccess.remove_absolute(ProjectSettings.globalize_path(test_checkpoint))
	print("PASS one-beam full playthrough")
	quit(0)


func _load_scene() -> bool:
	if not _check(change_scene_to_file("res://scenes/Main.tscn") == OK, "load ecology sandbox"):
		return false
	await scene_changed
	await process_frame
	hud = current_scene.get_node("Interface/Hud")
	world = current_scene.get_node("WorldView")
	lamp = world.get_node("Lamp")
	plants = world.get_node("Plants")
	harvest = hud.get_node("Tools/Actions/Harvest")
	fruit = hud.get_node("Tools/Actions/Fruit")
	objective = hud.get_node("Objective")
	return true


func _sow(species_index: int, local: Vector2) -> void:
	var selector := hud.get_node("Tools/Species") as OptionButton
	selector.select(species_index)
	selector.item_selected.emit(species_index)
	_click_control(hud.get_node("Tools/PlantButton"))
	_click(_point(local))


func _point(local: Vector2) -> Vector2:
	return plants.get_global_transform_with_canvas() * local


func _lamp_point() -> Vector2:
	return lamp.get_global_transform_with_canvas().origin


func _click_control(control: Control) -> void:
	_click(control.get_global_rect().get_center())


func _click(at: Vector2) -> void:
	_move(at)
	_button(at, true)
	_button(at, false)


func _drag(from: Vector2, to: Vector2) -> void:
	_move(from)
	_button(from, true)
	_move(to, MOUSE_BUTTON_MASK_LEFT)
	_button(to, false)


func _button(at: Vector2, down: bool) -> void:
	var event := InputEventMouseButton.new()
	event.position = at
	event.global_position = at
	event.button_index = MOUSE_BUTTON_LEFT
	event.button_mask = MOUSE_BUTTON_MASK_LEFT if down else 0
	event.pressed = down
	root.push_input(event, true)


func _move(at: Vector2, mask: int = 0) -> void:
	var event := InputEventMouseMotion.new()
	event.position = at
	event.global_position = at
	event.button_mask = mask
	root.push_input(event, true)


func _wait(predicate: Callable, label: String, frames: int = 1500) -> bool:
	for unused in range(frames):
		if predicate.call():
			return true
		await process_frame
	return _check(false, label)


func _check(ok: bool, label: String) -> bool:
	if not ok:
		push_error("FAIL one-beam playthrough: " + label)
		if FileAccess.file_exists(test_checkpoint):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(test_checkpoint))
		quit(1)
	return ok
