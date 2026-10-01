# Old Market Simulator · 第三人称相机 Mod (ThirdPersonMod)

为《Old Market Simulator》（菜市场模拟器）制作的第三人称视角 Mod，PUBG 式肩后视角，B 键随时切回第一人称。

## 功能特性

- **B 键切换** 第一人称 / 第三人称，切换时视角无跳变
- **偏移视角**：人物位于屏幕右下方，可调
- **准星零视差**：第三人称下捡东西、交互的射线与准星严格一致（汇聚式瞄准）
- **眼睛高度自动校准**：以第一人称相机位置为基准，适配不同角色模型
- **角色身体显示**：本地玩家默认被游戏隐藏（`ShadowsOnly` 只投影不渲染），第三人称下自动恢复显示
- **相机防穿墙**：背靠墙壁时自动拉近
- **BepInEx 配置文件**调参，改数值免重新编译

## 前置要求

- [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases)（已安装，游戏根目录有 `BepInEx` 文件夹即可）

## 安装

1. 从 [Releases](../../releases) 下载 `ThirdPersonMod.dll`
2. 放入游戏目录的 `BepInEx\plugins\` 文件夹
3. 启动游戏，按 **B** 切换第三人称

## 配置

首次运行后生成 `BepInEx\config\camera.patcher.mod.cfg`，改完保存、进游戏按 B 即生效：

| 配置项 | 默认值 | 说明 |
|---|---|---|
| 鼠标灵敏度 | 0.12 | 每像素转向速度 |
| 相机距离 | 2.2 | 相机与角色的距离（米） |
| 水平偏移 | -0.35 | 越负人物越靠屏幕右侧（PUBG 约 -0.4 ~ -0.5） |
| 垂直偏移 | 0.18 | 越大人物越靠屏幕下方 |
| 汇聚距离 | 40 | 准星汇聚距离，一般不用改 |
| 射线校正 | 0.3 | 近处拾取手感微调 |

## 自行编译

1. 安装 .NET SDK（支持 net472）
2. 从游戏 `Old Market Simulator_Data\Managed\`  与  `BepInEx\core\`  拷贝下列 DLL 到 `libs/`：

`BepInEx.dll`
`0Harmony.dll`
`Assembly-CSharp.dll`
`UnityEngine.dll`
`UnityEngine.CoreModule.dll`
`UnityEngine.InputLegacyModule.dll`
`UnityEngine.PhysicsModule.dll`
`UnityEngine.AnimationModule.dll`
`Unity.InputSystem.dll`

3. `dotnet build`，产物在 `bin\Debug\net472\`

> `libs/` 中的 DLL 为游戏版权文件，请勿上传/分发。

## 实现原理（供学习）

- Harmony 补丁 `ExampleCharacterCamera.UpdateWithInput`（KinematicCharacterController），在原相机逻辑执行后覆盖相机位姿
- `PlayerInteraction.InteractionRay` 执行瞬间把相机临时对准"汇聚视线"，使拾取射线与准星一致，结束后还原
- `RenderPipelineManager.beginCameraRendering` 时机把本地玩家渲染器的 `ShadowCastingMode` 从 `ShadowsOnly` 恢复为 `On`

## 免责声明

本 Mod 与游戏开发者无关，仅供学习交流。游戏更新后可能失效，届时请提 Issue。
