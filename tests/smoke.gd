extends SceneTree

## Exercise the ecology sandbox through Godot's viewport input pipeline.
## Use the real controls, world scene and HUD; run against a built .NET project.

func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	if not _check(change_scene_to_file("res://scenes/Main.tscn") == OK, "open ecology sandbox"):
		return
	await scene_changed
	await process_frame

	var tools := current_scene.get_node_or_null("Interface/Hud/Tools") as Control
	var world := current_scene.get_node_or_null("WorldView") as Node2D
	if not _check(tools != null and world != null, "locate HUD and world"):
		return
	var button := tools.get_node_or_null("PlantButton") as Button
	var inventory := tools.get_node_or_null("Inventory") as Label
	var selection := tools.get_node_or_null("Selection") as Label
	var plants := world.get_node_or_null("Plants")
	var lamp := world.get_node_or_null("Lamp") as Node2D
	if not _check(button and inventory and selection and plants and lamp, "locate controls and lamp"):
		return

	var starting_seeds: int = current_scene.get("StartingSeeds")
	if not _check(starting_seeds > 0 and inventory.text.ends_with(str(starting_seeds)), "render starting inventory"):
		return
	var button_center := button.get_global_rect().get_center()
	_click(button_center)
	if not _check(button.text == "取消播种", "arm planting via the HUD button"):
		return

	# Place the existing panel over a valid empty plot. With planting armed,
	# clicking Cancel would also plant a seed here if UI input leaked through.
	var original_position := tools.position
	var plot_local := Vector2(420, 210)
	var plot := world.get_global_transform_with_canvas() * plot_local
	tools.position += plot - button_center
	_click(button.get_global_rect().get_center())
	if not _check(button.text != "取消播种" and plants.get_child_count() == 0 and inventory.text.ends_with(str(starting_seeds)), "overlay handles click without planting behind it"):
		return
	tools.position = original_position

	_click(button.get_global_rect().get_center())
	_click(plot)
	if not _check(plants.get_child_count() == 1 and inventory.text.ends_with(str(starting_seeds - 1)), "plant one seed and decrement inventory once"):
		return
	var plant := plants.get_child(0) as Node2D
	if not _check(plant and plant.position.is_equal_approx(plot_local) and selection.text.ends_with("未受光"), "plant appears at the selected dark plot"):
		return

	var lamp_start := lamp.get_global_transform_with_canvas().origin
	_drag(lamp_start, plot)
	if not _check(lamp.position.is_equal_approx(plot_local) and selection.text.ends_with("　受光"), "dragging the lamp onto the plant updates the HUD"):
		return
	_drag(plot, lamp_start)
	if not _check(selection.text.ends_with("未受光"), "moving the lamp away restores the dark state"):
		return
	_click(button.get_global_rect().get_center())
	_click(world.get_global_transform_with_canvas() * Vector2(470, 210))
	if not _check(selection.text.contains("#2"), "second seed becomes selected"):
		return
	_drag(lamp_start, plot)
	_click(plot)
	if not _check(selection.text.contains("#1") and selection.text.ends_with("　受光"),
		"clicking a plant under the lamp selects it without moving the lamp"):
		return

	print("PASS one-beam scene smoke")
	quit(0)


func _click(at: Vector2) -> void:
	_motion(at, 0)
	_button(at, true)
	_button(at, false)


func _drag(from: Vector2, to: Vector2) -> void:
	_motion(from, 0)
	_button(from, true)
	_motion(to, MOUSE_BUTTON_MASK_LEFT)
	_button(to, false)


func _motion(at: Vector2, buttons: int) -> void:
	var event := InputEventMouseMotion.new()
	event.position = at
	event.global_position = at
	event.button_mask = buttons
	root.push_input(event, true)


func _button(at: Vector2, pressed: bool) -> void:
	var event := InputEventMouseButton.new()
	event.position = at
	event.global_position = at
	event.button_index = MOUSE_BUTTON_LEFT
	event.button_mask = MOUSE_BUTTON_MASK_LEFT if pressed else 0
	event.pressed = pressed
	root.push_input(event, true)


func _check(ok: bool, step: String) -> bool:
	if not ok:
		push_error("FAIL one-beam scene smoke: " + step)
		quit(1)
	return ok
