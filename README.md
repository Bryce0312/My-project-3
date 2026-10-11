# XR Virtual Apartment / Living Room Design Review

面向非专业用户的 **1:1 XR 室内设计评审工具**：让未来住户与普通客户进入尚未定稿的起居室方案，
以真实尺度观察、移动、测量，发现平面图与效果图不易察觉的空间问题，并留下可定位的反馈。
项目定位、研究问题与完整需求见 [PROJECT_BRIEF.md](PROJECT_BRIEF.md)，版本变更见 [CHANGELOG.md](CHANGELOG.md)。

## 运行与构建

| 项 | 值 |
| --- | --- |
| Unity | 2022.3.62f3 |
| 渲染 | Built-in RP，PC 端（Quest 3 通过 Quest Link 的 PCVR） |
| XR 插件 | OpenXR 1.8.1 + XR Interaction Toolkit 2.3.2 + Input System 1.7.0（`activeInputHandler: Both`） |

1. Unity Hub 打开本仓库，使用 2022.3.62f3 打开（首次导入会重新生成 Library）。
2. 打开场景 `Assets/Scenes/SceneNew.unity`，直接 Play。
3. 有头显（Quest Link 已连接）：自动进入 XR 模式，rig 主相机跟随头动；
   无头显：自动回退桌面预览相机（鼠标键盘评审），两种模式互不抢输入。
4. 测量用可行走路线依赖 NavMesh：菜单 `LivingRoom30 ▸ 烘焙测量用 NavMesh`（提交的场景已含烘焙结果，重新改动家具后需重烘）。

### 操作说明

- XR 模式：物理转头自由视角；右手柄拨杆 45° Snap 转向；手柄射线选择家具/空间点；
  顶部 UI 面板切换测量模式（家具尺寸 / 两点距离 / 两物体距离 / 邻近自动测距）与清空。
- 桌面模式：`WASD` 行走、右键拖拽视角、`Shift` 加速、`F` 切换 行走/轨道；鼠标点击测量。
- 无头显但想强制桌面模式：顶部 UI 的「输入模式」按钮可手动切换。

## 需求 → 功能映射（可追溯性）

对应 PROJECT_BRIEF 的功能优先级；代码均位于 `Assets/LivingRoom30/`。

| PROJECT_BRIEF 需求 | 状态 | 实现 / 代码位置 |
| --- | --- | --- |
| 基础1 起居室真实尺度校验 | 已实现 | `1 unit = 1m`；出生眼高 1.6m（Floor 原点 + Camera Offset 1.6m）；`FurnitureProximityDisplay` 显示家具真实长宽高 |
| 基础2 地面/墙/门洞/玻璃/大家具碰撞代理 | 已实现 | v0.2.0 起建立的 XR 碰撞代理层级 + 玩家 `CharacterController`（`XR_Player`） |
| 基础3 XR Origin、玩家身高与出生点 | 已实现 | `LivingRoom30/Prefabs/XR_ReviewRig.prefab`（XROrigin，Floor 跟踪原点，`XRHeadPoseFollower` 头动跟随） |
| 基础4 连续移动/传送与动线通过测试 | 已实现 | rig `CharacterController` 连续移动；`Scripts/Measurement/NavMeshPathDrawer.cs` 可行走路线预览辅助动线评审 |
| 基础5 基础日间照明 | 已实现 | 场景内现有日间照明（v0.2–v0.3 完成材质与灯光修复） |
| 评审1 两点/表面间距离测量 | 已实现 | `Scripts/Measurement/SpatialMeasureManager.cs`（PointToPoint / ObjectToObject 模式） |
| 评审2 选择家具或建筑构件 | 已实现 | Dimensions 模式 + `FurnitureInfo`；XR 射线（`XRRayPointProvider`）/ 鼠标（`MousePointProvider`）双输入 |
| 评审3 指向空间位置创建标注 | 部分 | 测量线/包围盒/标签绘制（`MeasurementVisualizer`）；带描述的空间标注留待 Phase 3 |
| 评审4 问题类型、描述与严重程度输入 | 未实现 | Phase 3（结构化反馈） |
| 评审5 反馈保存、重定位与 Feedback List | 未实现 | Phase 3 |
| V1+ 净空阈值与通过性提示 | 部分 | `Scripts/Measurement/ProximityMeasurer.cs` 邻近自动测距（表面间隙/中心距实时提示） |
| V1+ 舒适移动、转向、防眩晕 | 已实现 | 见下节 XR 交互与 UX 决策 |
| V1+ 面向首次使用者的引导 | 已实现 | 顶部提示横幅 + Toast 引导（`MeasureUIPanel`），低学习成本交互 |

## XR 交互与用户体验决策

**舒适性（防眩晕）**

- 头动视角低延迟跟随：`XRHeadPoseFollower` 在 `Update` 与 `Application.onBeforeRender` 双时机把头显位姿写入相机，
  避免转视野只靠手柄、场景"跟着头一起转"的眩晕问题（Quest Link 下 `TrackedPoseDriver` 的 `<XRHMD>` 单例动作路径不更新，已弃用并记录于 CHANGELOG）。
- 转向采用 45° Snap Turn（XRI `ActionBasedSnapTurnProvider`）而非平滑转向，降低视觉前庭冲突；无强制的连续位移。
- 站立眼高 1.6m、Floor 跟踪原点，配合 `isTracked` 守卫防止追踪丢失时相机穿地。

**可访问性**

- 无头显时自动回退桌面预览（轨道/行走双模式），键鼠与手柄操作等价，评审不依赖拥有头显。
- 输入模式可手动强制切换，避免设备探测误判导致无法操作。

**安全**

- 玩家移动走 `CharacterController` 碰撞， furniture 与建筑有简化碰撞代理；桌面轨道相机被房间包围盒钳制（`clampToRoom`），不会穿出建筑。
- 拾取射线排除 `PlayerBody` 自身层，避免自碰撞挡射线。

**反馈与引导**

- 测量结果恒定屏幕尺寸的 billboard 标签 + UI 读数；走近家具自动显示名称与真实尺寸（青色线框高亮），无需学习成本。
- 测量结果可直接用于口头/书面反馈定位问题；结构化标注与 Feedback List 在 Phase 3 接入。

## 外部资源与依赖

| 资源 | 来源 / 版本 | 用途 |
| --- | --- | --- |
| VLOGGER'S APARTMENT (FBX) | 第三方公寓模型（v0.2.0 导入） | 建筑与室内基础网格、纹理 |
| ContemporaryFurnitureCollectionFree_3ds | 第三方家具包（`Assets/` 下随包） | 起居室家具模型 |
| XR Interaction Toolkit | Unity 官方包 2.3.2（含 Starter Assets 示例，`Assets/Samples/`） | XR Origin、交互器、Snap Turn、Device Simulator |
| Input System | Unity 官方包 1.7.0 | 输入后端（Both 模式兼容桌面键鼠） |
| OpenXR | Unity 官方包 1.8.1 | Quest Link PCVR 运行时 |

## 已知限制

- 目前以 **Quest 3 + Quest Link 的 PCVR** 为主验证环境；Quest/Pico 一体机直连（Android 构建）尚未验证。
- 空间标注、结构化反馈与 Feedback List 属 Phase 3 范围，当前测量结果以视觉化 + UI 读数呈现。
- 原始 FBX 的 Corona 材质无法在 Unity Standard 下完全复现，部分材质为手工恢复（详见 CHANGELOG 0.2.0「已知限制」）。
- 编辑器内的 XR Device Simulator 仅用于交互测试；检测到模拟器时系统按桌面模式处理（避免抢占键鼠）。
- 测量系统通过 `RuntimeInitializeOnLoadMethod` 运行时自举，无需在场景中手动挂载；如需停用见 `SpatialMeasureManager.cs` 头部说明。
