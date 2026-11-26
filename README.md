# 一线光束 · Turnlight

TURN Game Jam 的植物生态游戏。玩家在一块 2.5D 田地里，用一盏同时照到两格的灯培养植物，在限时内积累足够的生机值。

## 开始试玩

用 **Godot 4.7 .NET** 和 **.NET 8 或更高版本的 SDK** 打开 [project.godot](project.godot)，按 **F5** 进入监控画面版两关（[ChamberLevels.tscn](scenes/ChamberLevels.tscn)）：5 × 5 田地，点种子再点空坑播种，**WASD** 移光、**Q/E** 转向，光只用键盘操作。配置在 [camera-levels.json](data/camera-levels.json)（每格 0.4 秒）。下面是原来的 Demo 说明，打开 [LevelsDemo.tscn](scenes/LevelsDemo.tscn) 后按 **F6** 进入。

原 Demo：点击种子库存，再点田块播种。拖动灯头到田块，或按 **WASD** 移灯、**Q/E** 旋转光区。右下角可以暂停、调整倍速和重开本关。

田地默认是 **4 × 4**。修改 `gridSize` 可设置 N × N 的边长。第一关在 180 秒时结算，目标为 **150**。过关后进入第二关，保留植株和生长进度，清空黑暗值并获得一颗种子。第二关同样在 180 秒时结算，目标为 **160**。[数值配置](data/levels-demo.json) · [规则与试玩结果](docs/levels-demo.md)

也可以打开 [Main.tscn](scenes/Main.tscn) 后按 **F6**，进入包含锁灯、破顶、昼夜和检查点的生态沙盒。

## 验证

在项目根目录执行以下命令。示例中的 `godot` 指你的 Godot .NET 可执行文件。

```sh
dotnet build OneBeam.csproj
godot --headless --path . --import
dotnet run --project tests/domain/DomainChecks.csproj
godot --headless --fixed-fps 60 --path . --script res://tests/levels_demo.gd
godot --headless --path . --script res://tests/smoke.gd
godot --headless --fixed-fps 60 --path . --script res://tests/playthrough.gd
godot --headless --path . --script res://tests/chamber_levels.gd
```

P.S. 田面、土层和植株使用等距投影与程序绘制。关卡配置同时决定两关的田地尺寸。
