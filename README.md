# XR Virtual Apartment / Living Room Design Review

面向非专业用户的 **1:1 XR 起居室设计评审原型**。用户可以在客厅中观察、移动、查看家具尺寸与间距，沿引导路线评价空间，在模拟坐姿下观察电视和茶几、取放遥控器，并为具体物体填写分类反馈。结束时可查看点位评价和物体标注两个列表。

当前主场景为 `Assets/Scenes/SceneNew.unity`。项目目标与实现范围见 [PROJECT_BRIEF.md](PROJECT_BRIEF.md)，详细路线、交互与既有验证记录见 [评审引导说明](Tools/review-journey/README.md)，历史变更见 [CHANGELOG.md](CHANGELOG.md)。

## 小组成员 / Team Members

项目仓库 / Repository：[Bryce0312/My-project-3](https://github.com/Bryce0312/My-project-3)

| 姓名 / Name | 学生 ID / Student ID | GitHub 账号 / Account | 提交记录名称 / Commit author |
| --- | --- | --- | --- |
| 陈泓廷 | 26108188g | [tonyc6](https://github.com/tonyc6) | `tonyc6` |
| 丁书萱 | 26046154g | [Chrissy-dsx](https://github.com/Chrissy-dsx) | `Chrissy-dsx` |
| 迟浩洋 | 26121937g | [haoyang-chi](https://github.com/haoyang-chi) | `haoyang2023` |
| 雷清帆 | 26124196g | [Rayqfyx](https://github.com/Rayqfyx) | `XR Review E` |
| 刘士尧 | 26059381g | [Bryce0312](https://github.com/Bryce0312)（仓库创建者 / Repository owner） | `Bryce0312` |

注：Git 提交记录中的作者名称可能与 GitHub 登录账号不同；上表保留对应关系，便于核对成员贡献。

## 环境与运行

以下版本以仓库中的 `ProjectSettings/ProjectVersion.txt` 和 `Packages/manifest.json` 为准。

| 项目 | 当前配置 |
| --- | --- |
| Unity | 2022.3.62f3 |
| 渲染与主要体验环境 | Built-in Render Pipeline；Windows PCVR，Quest 3 通过 Quest Link 连接 |
| XR Interaction Toolkit | 2.6.4 |
| OpenXR Plugin | 1.14.3 |
| Input System | 1.14.0；Active Input Handling 为 Both |
| 其他已安装包 | Oculus XR Plugin 4.5.5、TextMesh Pro 3.0.7、UGUI 1.0.0；安装包不等于所有运行后端均已验收 |

### 首次打开

1. 克隆仓库并安装 Git LFS；大型 FBX 文件使用 LFS，克隆后执行 `git lfs pull`，确保取得实际资源而非指针文件。
2. 在 Unity Hub 中用 **2022.3.62f3** 打开，等待包解析与资源导入完成。
3. 打开 `Assets/Scenes/SceneNew.unity`，或使用菜单 `Tools > SceneNew > Open Main Scene`。编辑器不会自动切回客厅场景。
4. 使用头显时先完成 Quest Link 连接并确认 PC 端 XR 运行环境可用，再进入 Play。
5. 无头显时，场景中的 `XRDeviceSimulatorGate` 会先等待 XR 启动，约 3 秒后可启用 XR Device Simulator。当前测量系统将**真实 XR 或已启用的模拟器**都作为 XR 输入处理；并非所有无头显运行都会自动停留在普通桌面相机。
6. 如需普通键鼠预览，在编辑器中停用场景内的 XR Device Simulator 对象后运行。普通桌面模式支持行走/轨道查看及部分评审操作；坐姿与近手取放遥控器需要真实头显或 XR 模拟器。

### Windows 构建

在 `File > Build Settings` 选择 Windows Standalone，确认启用场景为 `Assets/Scenes/SceneNew.unity`，构建到独立输出目录。当前 Build Settings 已登记该场景。提交可执行文件时，将 `.exe`、同目录数据文件夹及运行依赖一起打包为 ZIP，并在目标 PC 和头显上完成运行检查。Android 一体机版本不属于当前已验收范围。

**GUIDE 路线不要求先烘焙 NavMesh。** 它使用本客厅专用点位图和运行时胶囊碰撞检查。另一个 `NavMeshPathDrawer` 测量路线模块依赖 NavMesh；当前场景的 `m_NavMeshData` 为空，若维护该模块，可使用 `LivingRoom30 > 烘焙测量用 NavMesh`，该操作会修改并保存场景。它不是首次体验 GUIDE 的必做步骤。

## 操作与体验流程

### 头显 / XR 操作

| 操作 | 作用 |
| --- | --- |
| 左手摇杆 | 连续移动；坐姿时暂停 |
| 右手摇杆 | 45° Snap Turn；坐姿时暂停 |
| 转头与移动手柄 | 观察环境、移动射线；坐姿仍保留头部与手柄追踪 |
| 射线指向可批注物体，按侧面 **Grip** | 选择该物体并打开批注卡 |
| **Trigger** 扳机 | 点击菜单、评价按钮、座椅图标及屏幕键盘 |
| 手靠近遥控器后，持续按 **Trigger 约 0.5 秒** | 进度圈完成后拿起；松开即放下。远处射线点击不会拿起 |

抓取只针对当前遥控器任务，不代表所有物品都可拿取。UI 点击优先于遥控器抓取；从 UI 开始的按压需要先松开。持有遥控器时先放下再起身或结束。

### 腕部菜单

左手腕附近提供以下入口，使用射线和扳机点击：

- `PAUSE / RESUME`：暂停或恢复**计时器**，不是冻结游戏或暂停 GUIDE 任务。
- `HELP`：基础操作教程，通过移动、转向、点击、靠近家具及批注等步骤学习控制。
- `GUIDE`：打开或恢复客厅评审引导。任务卡的 `X` 或 `FREE EXPLORE` 关闭引导、隐藏路线并暂停其进度。
- `END REVIEW / RESULTS`：结束本轮并进入总结；结束后可再次打开结果。
- `GAPS ON / OFF`：切换邻近间距提示，不负责关闭独立的家具尺寸显示。

GUIDE 卡片跟随右手腕附近位置，默认向前约 35 cm、向上约 19 cm、向右约 15 cm；卡片朝向使用者并保持直立。右手不可用或处于视线后方时回退到视野前下方。实际显示会避让障碍并保持观看距离，以上数值不是固定世界坐标。

### 一轮完整评审

`START REVIEW` → 入口整体观察 → 展示柜 → 书柜与通行 → 电视侧向视线 → 沙发与茶几 → 点击座椅图标进入坐姿 → 坐姿观察 → 取放遥控器 → 起身 → 总结。

- 六个评价点使用白色地面箭头、站位圈和目标图标引导；到位后按目标方向观察约 1.2 秒，再选择 `WORKS WELL`、`CONCERN` 或 `UNSURE`。
- 观察判定基于**头显朝向与停留时间**，不是眼动追踪，也不会代替用户判断空间是否满意。观察完成和评价保存时提供短震动确认。
- 坐下采用 XR Origin 对齐的模拟坐姿视角，保留头手追踪；`STAND UP` 返回指定安全站位。`RESET` 可找回遥控器，但不会代替实际取放来完成任务。
- 可随时自由探索、关闭后恢复引导；坐姿时关闭 GUIDE 仍保留起身及遥控器重置入口。

### 物体批注与结果

1. 指向具有批注配置的物体，按 Grip 打开卡片。
2. 选择 `inappropriate size`、`too cramped` 或 `strange layout`，填写原因（最多 500 字符）。原因输入框可显示英文屏幕键盘，支持字母、空格、退格和清空。
3. 提交前校验分类与原因；保存失败保留草稿。点位评价卡也会提示用 Grip 为具体物体补充反馈。
4. 结束后，`POINT REVIEWS` 展示六个点位评价，未回答项显示 `UNRATED`；`OBJECT NOTES` 按物体分组展示分类和原因，支持详情与翻页。
5. `RETURN TO REVIEW` 保留本轮数据并继续补充；`NEW REVIEW` 创建新一轮文件。结束时存在未提交批注，会提供 `SAVE & END` 和 `KEEP EDITING`，避免直接丢弃草稿。

进入大总结卡时会暂时隐藏间距及家具尺寸提示，关闭总结后恢复原显示状态。

### 普通桌面预览

`WASD` 行走、按住右键拖拽视角、`Left Shift` 加速，`F` 切换行走/轨道。鼠标用于 UI 与测量拾取。普通桌面预览可查看路线和提交观察，不等同于完整头显体验，也不提供模拟器的手部取放操作。

旧版测量侧边面板当前默认隐藏（`SpatialMeasureManager` 设置 `showSidePanel = false`），因此不要寻找 README 旧版所述的“顶部模式切换/输入模式按钮”。两点、两物体距离和 NavMesh 路线的代码仍保留供维护，当前用户主要通过邻近尺寸显示和腕部 `GAPS` 查看空间信息。

## 需求与当前实现

| 目标 | 当前实现与代码入口 | 范围说明 |
| --- | --- | --- |
| 真实尺度、空间移动与碰撞 | `Assets/Scenes/SceneNew.unity`；`Assets/LivingRoom30/Prefabs/XR_ReviewRig.prefab` | 1 unit = 1 m；连续移动、45° 转向、部件碰撞体与玩家胶囊；不将传送列为已完成体验 |
| 家具尺寸与间距 | `FurnitureProximityDisplay/FurnitureProximityDisplay.cs`；`Scripts/Measurement/ProximityMeasurer.cs`（均在 `Assets/LivingRoom30/` 下） | 靠近显示轮廓/尺寸，邻近间距用绿色标签；尺寸和间距以包围盒等几何估算为主 |
| 测量工具代码 | `Assets/LivingRoom30/Scripts/Measurement/SpatialMeasureManager.cs` | 保留尺寸、两点及两物体模式；旧侧栏默认关闭，不作为当前完整开放 UI 描述 |
| 对象选择与结构化反馈 | `Assets/XR interaction/AnnotationSpawner.cs`、`AnnotationUI.cs`；`Scripts/Review/ReviewAnnotationStore.cs` | 已配置物体的分类、原因、对象标识与位置；尚无独立严重程度字段或任意空间锚点批注流程 |
| 首次教学与空间引导 | `Assets/LivingRoom30/Scripts/UI/TutorialController.cs`；`Scripts/Review/ReviewJourneyController.cs` | HELP 教控制；GUIDE 组织九阶段活动、六个评价点 |
| 坐姿与日常使用任务 | `Assets/LivingRoom30/Scripts/Review/SeatViewController.cs`、`ReviewRemoteControl.cs`、`ReviewRemoteGrabInteractor.cs` | 模拟坐姿、近手长按扳机取放遥控器；未实现遥控器控制电视内容 |
| 保存与结果列表 | `Assets/LivingRoom30/Scripts/Review/ReviewJourneyController.cs`、`ReviewAnnotationStore.cs`、`ReviewSummaryPanel.cs` | 本轮点位评价、物体标注及操作记录持久化，双列表查看；不等于跨会话导入或自动传送到问题位置 |

## 数据保存

每轮保存到 `Application.persistentDataPath/ReviewSessions/living-room-*.json`，实际完整路径可查看运行日志。Windows 通常位于 `%USERPROFILE%/AppData/LocalLow/<CompanyName>/<ProductName>/ReviewSessions/`，目录名取决于 Player Settings。

当前 JSON `schemaVersion = 2`，包含点位观察/评价（`observations`）、任务操作（`actions`）及物体批注（`annotations`）。批注保存独立 ID、物体标识/名称/位置、分类、原因、UTC 时间及可选点位关联。自由探索时提交的标注也归入本轮；首次开始 GUIDE 不会清掉它们。先写同目录临时文件再替换正式文件，失败时保留草稿并提示。

当前界面用于查看本轮结果；保存了旧文件不代表界面已提供历史会话导入、云端同步或 CSV 导出。

## 维护约定

- **以现有场景为正式资产，增量修改。** 旧 `SceneNewBuilder` 默认通过 `ENABLE_LEGACY_LIVING_ROOM_BUILDER` 编译条件屏蔽；不要为添加物体或功能重新启用并重建房间，以免覆盖现有批注、引用及交互配置。
- `ReviewJourney` 是独立场景根节点；GUIDE 的提示、坐姿锚点和遥控器在其自己的运行时层级创建，不加入家具测量子树。新需求可以编写独立增量工具，不必依赖旧构建器。
- 调整家具布局后，重新检查碰撞、尺寸包围盒、`ReviewRouteGraph` 的点位/连线、可观察目标及坐站/伸手范围；GUIDE 不会自动为任意新布局生成完整任务。
- 测量系统与邻近尺寸显示有运行时初始化入口。新场景应检查这些模块的接入范围，不能假定所有逻辑都仅对当前客厅生效。
- 成员在各自功能分支完成 demo，讨论并验证后合并到 `main`。合并 Unity 场景、Prefab、`.meta` 或 XR 配置冲突时，核对对象引用与 GUID，再检查完整评审流程。

## 资源、验证与限制

当前客厅以项目内编写的组合几何及材质为基础；旧公寓/家具资源属于早期素材探索，不能将其直接当作当前主场景的构建来源。历史素材与一次性修复工具的归档位置见 [归档说明](_Archive/README.md)。运行依赖版本见上表与 `Packages/manifest.json`。

验证记录及适用范围见 [评审引导说明的验证部分](Tools/review-journey/README.md#验证)。这些记录对应生成时的版本；后续功能或布局变更需要重新执行相关检查，头显交互与舒适度需在目标设备上验证。

- 主要体验环境为 Quest Link PCVR；其他头显和 Android 独立构建仍需设备验证。
- 包围盒尺寸/间距不等于任意曲面上的精确最短距离，也不用于建筑规范认证。玩家胶囊有身体半径，因此可能在头部到达可见表面前停止移动。
- 坐姿是虚拟视点转换，不提供实体座椅接触；触及范围、阅读舒适度、震动感受和帧时间应在目标头显上检查。
- 屏幕键盘目前为基础英文字母键盘，不含完整中文输入法。
- 当前尚不提供方案自动对比、测量值直接附加批注、严重程度字段、历史会话导入或云端反馈管理。
