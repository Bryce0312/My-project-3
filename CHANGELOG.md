# 更新日志

本文件记录项目中值得关注的功能、资源和配置变更。

## [0.5.0] - 2026-10-16 — E 反馈与问题管理（结构化反馈闭环）

### 新增

- 新增反馈与问题管理模块 `Assets/LivingRoom30/Scripts/Feedback/`（**E 部分**），完成
  `标注面板 → 问题分类 → 严重程度 → 文字描述 → 提交 → 数据记录 → 空间标注 → 问题汇总 → 导出` 全流程。
- 新增结构化问题模型：5 类问题（空间/布局/动线/可达性/尺寸）+ 3 档严重程度（轻微/中等/严重）
  + 描述 + 世界坐标 + 目标家具标识 + 可选测量结果 + 创建时间。
- 新增空间持久标注 `IssueMarker`：分类配色图钉 + `ISSUE #NN` Billboard 标签，提交后持续保留在空间中，
  不被家具遮挡；标签保持恒定屏幕尺寸；无碰撞体以免被测量/选择射线误拾取。
- 新增数据持久化与导出 `IssueStore`：会话 JSON（`session_*.json`）与问题 CSV（`issues_*.csv`），
  输出到 `Application.persistentDataPath/xr-review/`，UTF-8 BOM 保证表格软件中文不乱码；提交后自动保存。
- 新增问题汇总 `IssueListHud`：右腕常驻 `ISSUES (n)` 世界空间入口（含状态行）+ 头显前方世界空间汇总面板
  （严重程度摘要、每页 6 条分页列表、逐条 FOCUS 定位高亮、导出、关闭）。
- 新增编辑器工具 `Assets/LivingRoom30/Editor/FeedbackSetup.cs`（菜单 `LivingRoom30 ▸ 反馈`）：
  集成自检并生成 `Tools/xr-review/feedback-integration-report.txt`、打开导出目录、导出当前会话、汇总已导出数据。
- 新增技术路线与集成说明文档 `Docs/E_反馈与问题管理_技术路线与集成说明.md`。

### 集成（接入 D 的标注链路与 C 的测量）

- 新增 `AnnotationBridge` / `AnnotationBinding`，在**运行期**接管 D 已合并的标注面板
  （`Assets/XR interaction/`）：订阅场景中已有的 `XRSimpleInteractable.selectEntered`
  捕获目标对象与空间锚点；隐藏 D 那三个未接线的分类按钮，注入 5 类问题分类（两行）与
  3 档严重程度（一行）；接线 Submit 按钮并读取 D 的公开字段 `descriptionInput`；
  提交后调用 D 的 `CloseUI()` 复用其高亮复位逻辑。**不修改 D 的任何文件与预制体。**
- 新增 `MeasurementBridge` 软桥接：反射读取测量模块读数面板并解析为测量快照，
  供反馈自动附加；C 未合并或字段改名时静默降级。同时提供
  `OfferMeasurement(...)` / `OfferMeasurementText(...)` 显式契约。
- 运行时自举生成 `FeedbackSystem`，**零场景序列化依赖**；场景现由人工维护
  （`SceneNewBuilder` 只做缺失告警），E 不参与场景生成，避免与 D 的场景改动冲突。

### 修复

- 修复 D 的标注面板中**问题分类按钮与 Submit 按钮未绑定任何事件**、`AnnotationUI.OnSubmit`
  仅打印日志导致"面板能弹出、能关闭，但既不能选分类也不能提交、提交后无任何记录"的问题。
- 补齐 D 面板缺失的**严重程度**维度（PROJECT_BRIEF 核心设计评审能力 4 要求）。

### 复查修正（第二轮静态核查）

- 修复**问题汇总面板会渲染成镜像文字**：世界空间画布的正面口径由项目既有的 `WristPanelFacing`
  与 `HudLazyFollow` 定义（`LookRotation(面板位置 − 观察者位置)`），E 的汇总面板在其之后又叠加了
  `Rotate(0,180,0)`，会把整块面板翻反。已去掉该旋转。
- 修复**测量软桥接连错了数据源**：`SpatialMeasureManager` 执行 `ui.showSidePanel = false`，
  而 `MeasureUIPanel.Build()` 在创建读数 `Text` 之前就 `return`（源码第 47 行 vs 第 81 行），
  因此 `readout` **恒为 null**、`SetReadout` 是空操作，原先的桥接永远读不到结果。
  现改为优先扫描 `MeasurementVisualizer` 的 3D 标签池（当前版本上唯一承载测量数值的地方），
  以「标签池中恰好 1 个 active 且形如 `<数字> m` 的标签」作为「这是一次两点/两物体距离测量」的判别，
  避免把家具尺寸模式的 12 条线框标签误当成距离；`readout` 保留为兼容来源。
- 修复**汇总面板逐次重建的节点未统一到 UI 层**（含空态文案分支）。
- 加固汇总面板生成：遮挡射线排除 `PlayerBody`，并设 0.6 m 下限距离，避免贴墙时面板糊在脸上。
- 性能：腕部 HUD 的头显相机复检由每帧改为 0.5 秒一次（`ResolveHeadCamera` 内含 `FindObjectOfType`）。
- 修正一个隐患：收集"面板按钮"时只取画布**直接子级**，避免把 D 自带虚拟键盘的约 40 个按键
  误判为分类按钮而连带隐藏（实测虚拟键盘挂在画布之下、初始 inactive）。
- 注入到 D 面板的按钮统一到 UI 层；内置字体缺失时给出一次明确报错，便于定位。

### 说明

- E 的自建界面全部为**世界空间画布**：本项目 `S06_XR_UIEventSystem` 的 `XRUIInputModule`
  刻意关闭了鼠标输入（避免与 XR Device Simulator 抢输入），屏幕空间画布在头显与模拟器下都无法被射线点击。
- E 自建界面使用内置 `LegacyRuntime.ttf` + UGUI `Text`（可显示中文）；
  注入到 D 面板的按钮改用 ASCII 标签，因为 TMP 默认字体不含中文字形。
- 问题汇总入口挂**右手柄**腕部（沿用 `S05_ReviewHud` 相同的偏移、缩放与朝向修正方案），
  由左手柄射线点击，避免与左手腕上的 `S05_ReviewHud` 重叠。
- 已知限制：头显内仅能用 D 自带的 A–Z 虚拟键盘输入描述；`forceDesktop` 纯鼠标模式下 UI 不可交互
  （项目既有设定）；反馈数据存于本机，未做云端同步。

## [0.4.0] - 2026-10-11

### 新增

- 新增空间测量工具集（`Assets/LivingRoom30/Scripts/Measurement/`）：家具尺寸、两点距离、两物体距离三种模式，
  XR 与桌面输入自动切换，可行走路线（NavMesh）预览，顶部 UI 面板与 Toast 引导。
- 新增桌面评审双模式：行走（WASD + 右键视角）/ 轨道相机，`F` 切换；无头显时自动回退桌面预览。
- 新增家具邻近尺寸显示（`FurnitureProximityDisplay`）：走近家具自动显示名称与真实长宽高并绘制线框。
- 新增编辑器工具 `MeasurementSetup`：一键为客厅地面烘焙测量用 NavMesh。
- 新增 `README.md`：需求到功能的映射、XR 交互与 UX 决策说明、构建运行指南、外部资源与已知限制。

### 修复

- 修复 Quest 3（Quest Link / OpenXR）下视角不跟随头转、场景"跟着头一起转"导致的眩晕：
  `TrackedPoseDriver` 的 `<XRHMD>` 单例动作路径在该环境不更新，已改为 `XRHeadPoseFollower`
  （旧版 `InputDevices` API，Update + onBeforeRender 双时机驱动相机），并保留手柄 45° Snap 转向。
- 修复 XR 出生高度塌地：XR Origin 改为 Floor 跟踪原点 + 1.6m 眼高 Camera Offset，并加追踪丢失防穿地守卫。

### 已知限制

- 空间标注、结构化反馈与 Feedback List 为 Phase 3 范围。
- 已验证环境为 Quest 3 + Quest Link 的 PCVR；一体机直连（Android 构建）尚未验证。
- 场景未包含烘焙后的 NavMesh 数据：首次使用「可行走路线」前需执行菜单
  `LivingRoom30 ▸ 烘焙测量用 NavMesh`（未烘焙时路线显示为"未烘焙/不可达"提示，其余功能不受影响）。

## [0.3.0] - 2026-10-04

### 新增

- 新增电视区域材质批量匹配工具 `ApplyTvAreaMaterials`。
- 新增 `Fabric042` 材质设置工具，用于统一处理指定布料材质参数。
- 新增暖棕色沙发布料材质 `Sofa_WarmBrown`。

### 修复

- 根据原作者留下的材质线索，对电视区域、墙面、柜体、玻璃、金属和 PVC 等相关材质进行重新匹配。
- 修复多处材质引用错误或缺失的问题，使场景中的材质对应关系更接近原始资产效果。
- 为 `Me/Materials` 下的木纹、卧室砖面和纹理材质补齐 Unity 材质资源。
- 更新示例场景中的材质绑定，并记录电视区域材质应用结果。

## [0.2.0] - 2026-10-03

### 新增

- 导入 `VLOGGER'S APARTMENT (FBX)` 公寓模型，以及配套材质和 62 张纹理图片。
- 新增电视区域的暖米色沙发布料材质 `Sofa_WarmIvory`。
- 为主沙发增加局部暖色柔光，改善室内预览效果。
- 新增材质检查、修复、验证和安全回滚工具。
- 为 FBX 大模型启用 Git LFS 管理。

### 修复

- 核验模型中的 116 个材质映射，确认它们均指向项目内现存材质。
- 根据 FBX 内保存的原始材质关系，恢复 40 个材质的 42 处纹理引用。
- 将 `Textile Normal N.jpg` 正确配置为法线贴图。
- 将主沙发的两个布料槽替换为暖米色织物材质，同时保留靠枕和框架原有材质。

### 移除

- 移除示例场景中已替换的 `CoffeeTable6.3ds` 资源。

### 已知限制

- 原始场景使用 Corona 材质和灯光，Unity Standard 材质无法直接复现 Corona 的程序化节点、混合贴图、反射、透光和曝光效果。
- 6 个复杂材质仍需要手动转换或烘焙，包括绒布混合材质、旋转 UV 材质和同名冲突材质。
- 当前画面只完成了基础纹理恢复和主沙发布料调整；电视墙、柜体、灯带、反射探针及全局照明仍待完善。

## [0.1.0] - 2026-10-03

### 新增

- 创建 Unity 2022.3.62f3 项目。
- 添加初始示例场景和基础项目配置。
