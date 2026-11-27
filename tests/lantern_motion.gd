extends SceneTree

## Lantern grass on a dark ground: the five pictures at rest, then each stage motion
## caught part way. Set ONE_BEAM_LANTERN_CAPTURE to a folder to save PNGs (needs a window).

const Plant := preload("res://src/Levels/Presentation/Chamber/LanternPlant.cs")

var capture_dir: String
var plants: Array = []


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	capture_dir = OS.get_environment("ONE_BEAM_LANTERN_CAPTURE")
	var stage := Node2D.new()
	root.add_child(stage)
	var ground := ColorRect.new()
	ground.color = Color("1c2230")
	ground.size = Vector2(1280, 720)
	stage.add_child(ground)
	for i in 5:
		var p: Node2D = Plant.new()
		p.set("PixelScale", 0.42)
		p.set("Phase", i * 0.7)
		p.position = Vector2(150 + i * 245, 560)
		stage.add_child(p)
		p.call("Show", i, false)
		plants.append(p)
	await _frames(3)
	await _capture("01-rest.png")

	# seed -> seedling, seedling -> normal, normal -> mother, normal -> wilted, wilted -> normal
	var moves := [[0, 1], [1, 2], [2, 3], [2, 4], [4, 2]]
	for k in moves.size():
		plants[k].call("Show", moves[k][0], false)
		plants[k].call("Show", moves[k][1], true)
	for at in [0.12, 0.3, 0.55]:
		await create_timer(at - (0.0 if at == 0.12 else 0.0)).timeout
		await _capture("02-motion-%d.png" % int(at * 100))
	await create_timer(1.0).timeout
	for p in plants:
		if not _check(p.get("Index") >= 0, "every plant ends on a picture"):
			return
	plants[3].call("Die")
	await create_timer(0.25).timeout
	await _capture("03-dying.png")
	await create_timer(0.6).timeout
	if not _check(plants[3].get("Finished"), "a dying plant finishes and hides"):
		return
	print("PASS lantern grass motion")
	quit(0)


func _frames(n: int) -> void:
	for i in n:
		await process_frame


func _capture(name: String) -> void:
	if capture_dir.is_empty():
		return
	await RenderingServer.frame_post_draw
	var image := root.get_texture().get_image()
	image.save_png(capture_dir.path_join(name))


func _check(ok: bool, step: String) -> bool:
	if not ok:
		push_error("FAIL lantern motion: " + step)
		quit(1)
	return ok
