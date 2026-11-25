# 一线光束 · Turnlight

TURN Game Jam 的植物生态游戏。玩家用一盏只能同时照到两株的灯培养植物，在限时内让培养槽达到足够的生机值。

## 开始试玩

用 **Godot 4.7 .NET** 和 **.NET 8 或更高版本的 SDK** 打开 [project.godot](project.godot)，按 **F5** 进入两关 Demo。点击种子库存，再点空槽播种。拖动灯头或按 **A/D、Q/E** 调光。右下角可以暂停、调整倍速和重开本关。

第一关在 180 秒时结算，目标为 **150**。过关后进入第二关，保留植株和生长进度，清空黑暗值并获得一颗种子。第二关同样在 180 秒时结算，目标为 **160**。[数值配置](data/levels-demo.json) · [规则与试玩结果](docs/levels-demo.md)

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
```

P.S. 前两关暂用程序绘制的植物和六槽单排布局。关卡数据可直接在 `data/levels-demo.json` 调整。
