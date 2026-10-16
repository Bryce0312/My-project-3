# E — 反馈与问题管理：技术路线与集成说明

> 分支：`feature/feedback-issue-management`（基于 `origin/main` @ `24286675`）
> 代码：`Assets/LivingRoom30/Scripts/Feedback/`（运行时）、`Assets/LivingRoom30/Editor/FeedbackSetup.cs`（编辑器）
> 需求：UN-03 / UN-04 / UN-05 / UN-06 / UN-07，目标 G4 / G5，PROJECT_BRIEF「反馈必须定位到空间问题」

---

## 1. 团队分工中 E 的职责

`XR Spatial Review — V1.md` 第 7 节：

> **E — Feedback & Issue Management**：负责用户创建 Marker 之后发生的事情。
> 流程：`Marker → Issue Category → Text Description → Submit`
> 负责：问题分类、文字输入、Submit、数据记录、最终 Issue List。
> 副任务：Main Scene Integration + Build。

所以 E 的**输入**是「一个被指向的空间位置 + 一个目标对象」，**输出**是「一条结构化、可定位、可导出、可回看的问题记录」。

---

## 2. 本次更新后的仓库实测（路线的事实依据）

这一版 main 相比上一次基线变化很大，以下为逐项实测结论，它们直接决定了 E 的技术选型。

| # | 实测事实 | 对 E 的约束 |
| --- | --- | --- |
| F1 | **D 已合并进 main**：`Assets/XR interaction/`（`AnnotationSpawner` / `AnnotationUI` / `HoverDwellHighlighter` / `Outline` / `Canvas.prefab`）；场景中有 **23 个对象**同时挂着 `XRSimpleInteractable` + `HoverDwellHighlighter` + `AnnotationSpawner` | E 不应另起一套点选/面板，而应**接入 D 的这条链路** |
| F2 | 场景中 23 条 `XRSimpleInteractable.selectEntered → AnnotationSpawner.OnSelectEntered` 绑定**齐全**；`hoverEntered/hoverExited → HoverDwellHighlighter` 同样齐全 | 标注面板的**弹出**是通的，E 可以直接依赖 |
| F3 | `Canvas.prefab` 里**只有** `Btn_Close → AnnotationUI.OnCancel` 一条持久绑定；**问题分类按钮与 Submit 按钮都没有 onClick**；全仓库无任何代码调用 `OnSelectCategory` / `OnSubmit` | 面板能弹出、能关闭，但**既不能选分类也不能提交**。这正是 E 要补的 `Issue Category → Submit` |
| F4 | `AnnotationUI.OnSubmit()` 只做 `Debug.Log`，既不落库也不生成空间标注 | 「数据记录」「最终 Issue List」「标注不消失」全在 E 的范围内 |
| F5 | `AnnotationUI.descriptionInput`（`TMP_InputField`）与 `submitButtonText`（`TextMeshProUGUI`）是 **public 字段**；`OnSelectCategory` / `CloseUI` 是 **public 方法**；面板内含 D 自建的**虚拟键盘** | E 无需反射、无需改 D 的文件即可读写描述、反查提交按钮、复用关闭逻辑；XR 内文字输入问题已由 D 解决 |
| F6 | 场景 `S06_XR_UIEventSystem` = `EventSystem` + `XRUIInputModule`，`enableXRInput = 1`、**`enableMouseInput = 0`** | **屏幕空间画布在本项目里点不到**（XRI 的 `TrackedDeviceGraphicRaycaster` 只支持 World Space / Camera）。E 的交互界面必须全部是**世界空间画布** |
| F7 | 两个 `XRRayInteractor`（左右手）均 `enableUIInteraction = 1` | 手柄射线点世界空间 UI 是通的；「用一只手点另一只手腕上的面板」这一交互模型成立 |
| F8 | `S05_ReviewHud` 已挂在**左手柄**：腕部偏移 `(0, 0.06, -0.11)`、画布 `520×230`、缩放 `0.00035`，含 `TrackedDeviceGraphicRaycaster` + `WristPanelFacing`，`SetLayerRecursively(...,"UI")` | E 的问题汇总入口沿用**完全相同**的挂载与朝向方案，改挂**右手柄**以避免与 S05 重叠 |
| F9 | `SceneNewBuilder` 已从「自动重建场景」改为「缺失告警」；场景由人工维护，且含 D 手工摆放的 23 个对象与手腕 HUD | E **绝不能依赖 `SceneNewBuilder`**，也**不往场景写任何序列化内容**（否则与 D 的场景改动冲突） |
| F10 | 包版本：XRI **2.6.4**、Input System **1.14.0**、OpenXR 1.14.3、TMP 3.0.7，且 **TMP Essential Resources 已导入** | TMP 可用（D 的面板依赖它）；但 TMP 默认字体 `LiberationSans SDF` **不含中文字形** |
| F11 | `primaryButton` / `secondaryButton` 在全仓库无任何绑定与使用 | 理论上可用于快捷键，但**XR Device Simulator 下 `InputDevices` 不可靠**，因此 E 不把关键交互压在按键上，只用射线可点的世界空间按钮 |
| F12 | 项目「无头显」路径 = **XR Device Simulator**（`XRDeviceSimulatorGate` 在 3 秒内没等到 XR 设备就启用模拟器）；`SpatialMeasureManager` 把模拟器计入 XR 模式 | 头显与模拟器下 E 的行为一致；`forceDesktop` 纯鼠标模式连 C 自己的面板都点不到，E 不为它单独做一套 UI |
| F13 | **C 的读数面板读数栏实际不可用**：`SpatialMeasureManager.Awake` 里 `ui.showSidePanel = false`，而 `MeasureUIPanel.Build()` 在创建 `readout` 之前就 `return`（源码第 47 行 vs 第 81 行）→ `readout` 恒为 null，`SetReadout` 是空操作。测量数值实际只以 `MeasurementVisualizer` 的 3D `TextMesh` 标签（`"0.72 m"`）呈现 | E 的测量软桥接必须以**标签池**为主来源（§5.2 来源 A），不能只盯 `readout` |
| F14 | `TutorialController` 的 Annotate 步骤判定条件是 `AnnotationSpawner.currentActiveUI != null`（面板出现即推进） | 与 E 的接管无冲突：E 不改 `AnnotationSpawner`，也不依赖教程状态 |
| F15 | 世界空间画布的"正面"口径由项目既有代码定义：`WristPanelFacing` 与 `HudLazyFollow` 都用 `LookRotation(面板位置 − 观察者位置)` | E 自建的画布必须沿用同一式；**叠加 `Rotate(0,180,0)` 会渲染成镜像文字**（D 的 `LookAt(cam)`+`Rotate(0,180,0)` 只是等价写法） |

---

## 3. 技术路线

### 3.1 架构

```
                        ┌── D 的标注链路（已合并进 main）────────────────┐
用户：手柄射线指向物体     │  HoverDwellHighlighter：dwell 1.5s → 黄色高亮   │
   + 按下 grip            │  XRSimpleInteractable.selectEntered（场景已绑定）│
                        │        ↓                                      │
                        │  AnnotationSpawner：在头显前方 1m 生成标注面板   │
                        │        （世界空间 Canvas + TrackedDeviceGraphic- │
                        │          Raycaster + 虚拟键盘）                 │
                        └───────────────┬──────────────────────────────┘
                                        │ 同一个 selectEntered 事件
                        ┌───────────────▼──────────────────────────────┐
                        │ E · AnnotationBridge                          │
                        │   捕获目标对象 + 按视线求表面锚点               │
                        │   轮询并接管 AnnotationUI 的生命周期            │
                        └───────────────┬──────────────────────────────┘
                        ┌───────────────▼──────────────────────────────┐
                        │ E · AnnotationBinding                         │
                        │   隐藏 D 那 3 个未接线的分类按钮                │
                        │   注入：5 类问题分类（2 行） + 3 档严重程度      │
                        │   接线 Submit → 提交；描述直接读 D 的输入框      │
                        └───────────────┬──────────────────────────────┘
                                        │
                        ┌───────────────▼──────────────────────────────┐
                        │ E · FeedbackService（运行时自举）              │
                        │  ┌────────────┐ ┌──────────────────────┐      │
                        │  │ IssueStore │ │ IssueMarkerManager   │      │
                        │  │ 记录/JSON/ │ │ 空间编号标注（不消失）│      │
                        │  │ CSV        │ └──────────────────────┘      │
                        │  └────────────┘ ┌──────────────────────┐      │
                        │                 │ IssueListHud         │      │
                        │                 │ 右腕 ISSUES(n) 入口  │      │
                        │                 │ + 前方汇总面板(分页) │      │
                        │                 └──────────────────────┘      │
                        └──────────────────────────────────────────────┘
```

### 3.2 关键决策与取舍

| 决策 | 理由 | 代价 |
| --- | --- | --- |
| **接入 D 的面板，而不是另做一套输入界面** | D 的面板已经解决了世界空间布局、射线避让、虚拟键盘（XR 内无物理键盘）三个最难的问题；重做一遍等于把 D 的工作作废，且必然出现两套并存的心智冲突 | 需要按 D 的版式注入控件；若 D 日后改版式，需同步 `AnnotationBinding` 顶部的行坐标常量 |
| **用 `selectEntered` 事件捕获目标，而不是自己再发一条射线** | 在遮挡、多物体重叠、dwell 高亮的语义下重新射线会猜错目标；复用 D 正在用的同一条事件，拿到的**就是**同一个对象，零歧义、零额外射线开销 | 与 D 的场景绑定强耦合（F2 已验证该绑定齐全） |
| **运行期补装，不修改 D 的任何文件与预制体** | D 的工作已合并进 main，改他的文件会在后续合并中产生冲突；运行期补装是纯增量 | 需要按 GameObject 名字/标签定位按钮（已做多重兜底） |
| **全部界面用世界空间画布** | F6：本项目 `XRUIInputModule` 关闭了鼠标输入，屏幕空间画布没有任何指针能点到 | 世界空间排版要手工算坐标（已按 D 的面板尺寸推导并注释） |
| **E 自建界面用内置 `LegacyRuntime.ttf` + UGUI `Text`** | 列表要显示用户输入的中文描述，而 TMP 默认字体不含中文字形（F10）；项目既有的 `S05_ReviewHud`、`MeasureUIPanel` 也都是这套 | 与 D 面板的 TMP 外观略有差异（仅限 E 注入的控件与汇总面板） |
| **汇总入口挂右手柄、由左手柄射线点击** | 完全复用 F8 的成熟挂载方案；左手腕已被 S05 占用，挂右侧既不重叠又符合「一只手点另一只手」的项目既有交互习惯 | 用户需要转头看右腕（与 S05 同一心智模型） |
| **汇总列表用分页而不是 ScrollRect** | 世界空间下射线拖拽滚动需要额外遮罩与拖拽调参，分页用两个按钮即可，行为在任何设备上完全确定 | 每页 6 条，条数多时需要翻页 |
| **不把关键交互绑定到手柄按键** | F11：模拟器下 `InputDevices` 不可靠；射线点按钮在头显与模拟器下都成立 | 需要用户抬手点腕部按钮 |

### 3.3 D 面板的实测版式与 E 的注入位置

`Canvas.prefab`：`Canvas` 为 WorldSpace，根 `sizeDelta = (400, 300)`、`localScale = 0.003`，子元素以**中心**为锚点。

实测占用区域（画布局部坐标，中心为原点）：

| 元素 | 位置与尺寸 | 占用范围 |
| --- | --- | --- |
| 标题 `Issue Marker` | (0, 111) 200×50 | y 86..136 |
| 关闭 `X` | (167.8, 119.6) 30×30 | x 152..183, y 104..135 |
| 分类 `size` | (95.8, 60.9) 160×30 | y 45.9..75.9 |
| 分类 `cramped` | (−77.3, 60.2) 160×30 | y 45.2..75.2 |
| 分类 `layout` | (−78.5, 11.3) 160×30 | y −3.7..26.3 |
| 描述 `DescriptionDisplay` | (0, −58.4) 300×80 | y −98..−18 |
| 提交 `Submit` | (−0.2, −121.7) 91×29 | y −137..−107 |

**隐藏 D 那三个未接线的分类按钮后**，`y −11..75` 的整幅宽度（x −188..188）成为可用区。E 按三行排布：

| 行 | 顶边距画布顶 | 局部 y | 内容 |
| --- | --- | --- | --- |
| 行 1 | 75 | 49..75 | `SPACE` / `LAYOUT` / `SIZE` |
| 行 2 | 105 | 19..45 | `CIRCULATION` / `ACCESSIBILITY` |
| 行 3 | 137 | −11..13 | `LOW` / `MEDIUM` / `HIGH` |

每行留 4 单位间隔，与上下相邻元素不重叠。**Submit 键只有 91×29 单位**，放不下完整标签，因此 E 把它改为两行、字号 14：`Submit` / `CIRCUL / MED` 形式，选择结果一眼可见。

若日后 D 调整面板版式，只需修改 `AnnotationBinding` 顶部的 `Row1Top / Row2Top / Row3Top` 等常量。

---

## 4. 模块清单

| 文件 | 职责 |
| --- | --- |
| `IssueModel.cs` | 5 类问题 / 3 档严重程度枚举与元数据（中英文标签、配色、短代码、标签→分类关键词映射）、`Vec3`、`MeasurementSnapshot`、`IssueRecord`、`IssueSession`、运行时 `IssueDraft` |
| `IssueStore.cs` | 唯一数据记录中心：增删查、按分类/严重程度统计、会话 JSON 持久化、CSV 导出、原子写入（UTF-8 BOM 保证表格软件中文不乱码） |
| `IssueTargetResolver.cs` | 命中 Transform → 家具根 / 家具 ID / 可读名称 的语义归一化（公开给 D 复用，保证命名口径一致） |
| `IssueMarker.cs` + `IssueMarkerManager.cs` | 空间编号标注：分类配色图钉 + `ISSUE #01` Billboard 标签、恒定屏幕尺寸、`ZTest Always` 不被家具遮挡、脉冲高亮、无碰撞体（避免被 C/D 的射线误拾取） |
| `FeedbackUi.cs` | 世界空间 UI 工厂：`CreateWorldSpaceCanvas`（含 `TrackedDeviceGraphicRaycaster` 与 UI 层）、手工定位辅助、`Text`/`Button` 构建、控制器与头显相机解析 |
| `AnnotationBridge.cs` | 订阅 23 个 `XRSimpleInteractable.selectEntered` 捕获目标与锚点；周期扫描新交互对象；轮询并接管 `AnnotationUI` 生命周期 |
| `AnnotationBinding.cs` | 单个标注面板的接管：隐藏 D 的分类按钮、注入 5 分类 2 行 + 3 档严重程度、接线 Submit、读描述、提交后交回 D 的 `CloseUI` |
| `IssueListHud.cs` | 右腕常驻 `ISSUES (n)` 入口（含状态行）+ 头显前方汇总面板（标题、严重程度摘要、每页 6 条、逐条 FOCUS 定位、分页、导出、关闭） |
| `FeedbackService.cs` | 总控与对外 API：运行时自举、`SubmitAnnotation`、`OfferMeasurement/Text`、汇总开关、导出、清空、`IssueCreated/Updated` 事件、测量软轮询 |
| `MeasurementBridge.cs` | 与 C（测量）的可选软桥接：优先扫 `MeasurementVisualizer` 的 3D 标签池（当前版本上唯一有效的来源，F13），退化到 `MeasureUIPanel.readout`；反射失败即静默降级 |
| `Editor/FeedbackSetup.cs` | 集成自检（逐项校验 D 标注链路、XR UI 输入链路、E 依赖项、自举约定）→ `Tools/xr-review/feedback-integration-report.txt`；导出目录、导出会话、汇总导出数据 |

---

## 5. 集成契约

### 5.1 E 与 D（主链路，已自动生效，无需双方改动）

E 通过两个**已存在**的接口接入，不需要 D 做任何事：

1. `XRSimpleInteractable.selectEntered`（场景已绑定给 D 的 `AnnotationSpawner`）——E 在运行期追加监听，用于捕获目标对象与锚点。
   采用闭包捕获对象引用、不读取 `SelectEnterEventArgs` 的任何成员，因此不随 XRI 版本对 args 命名的调整而失效。
2. `AnnotationUI` 的公开成员——`descriptionInput`（读描述）、`submitButtonText`（反查提交按钮）、`OnSelectCategory`（保持 D 的状态一致）、`CloseUI`（交还关闭与高亮复位）。

**运行顺序**：`UnityEvent` 先调用持久化监听（D 弹面板）、再调用运行期监听（E 捕获目标），同一帧完成；E 在 ≤0.25 秒后轮询到面板并接管。因此**不存在竞态**。

若 D 日后自行把分类/提交按钮接好，E 的接管仍成立：E 的监听器读取的是 E 自己缓存的分类与严重程度，不依赖 D 的私有状态（`AnnotationUI.OnSubmit` 若被触发，也只是多打印一行日志并关闭面板，而 E 的监听器已先行完成入库）。若要完全交还给 D，把 `FeedbackSystem` 上的 **`manageCategoryRow` 取消勾选**即可切换为「映射 D 原有 3 个分类按钮」的兼容模式。

### 5.2 E 与 C（测量）

三种方式任选，可共存：

1. **软桥接（默认开启，零改动）**：E 每 2 秒读取测量模块当前结果，结果**变化**即暂存为"待附加测量"，下一次提交自动附到反馈上。反射失败（C 未合并 / 结构变更）时静默降级。
   读取来源按可靠性排序：
   - **来源 A（当前版本上真正有效）**：`MeasurementSystem/Visualizer` 的 `MeasurementVisualizer.labelPool` —— 即场景中那些 `"0.72 m"` 的 3D 标签。
     实测依据：`SpatialMeasureManager.Awake` 执行了 `ui.showSidePanel = false`，而 `MeasureUIPanel.Build()` 在创建 `readout` 之前就 `return` 了，所以 **`readout` 恒为 null**，测量数值实际只以 3D 标签呈现。
   - **来源 B（兼容后续改动）**：`MeasureUIPanel.readout` —— 若日后有人启用侧边面板即可用，且自带"两点距离 / 表面间隙"这类语义前缀。
   - **判别规则**：只接受"标签池中**恰好 1 个** active 且形如 `<数字> m` 的标签"。两点/两物体距离只产生 1 个；而家具尺寸模式会一次画出 12 条包围盒棱线（12 个数值标签），因此不会被误当成"量了一段距离"。带 `路径` 后缀的路线总长标签也被排除。
2. **轻量契约**：`FeedbackService.Instance.OfferMeasurementText("表面间隙：0.72 m")`。
3. **显式类型契约**：`OfferMeasurement(new MeasurementSnapshot(label, meters, from, to))`。

> 说明：方式 1 是"零改动即可用"的便利路径；方式 2/3 是最干净的长期方案 ——
> 在 C 的 `ShowPointToPoint` / `ShowObjectToObject` 末尾各加一行即可完全绕开反射。

### 5.3 E 与 B（移动）／A（场景）

无需任何改动。E 不写场景、不接管移动输入、不修改任何既有组件。
E 的射线拾取（锚点计算）使用 `Physics.Raycast` 且只认目标自身子层级，不会误判。

### 5.4 参数调整（运行时自举组件的可配置性）

`FeedbackSystem` 默认在 Play 时自举，因此它的 Inspector 参数无法在运行前调整。若需要调参：

> 在场景里手动新建一个空 GameObject、命名为 `FeedbackSystem` 并挂上 `FeedbackService` 组件，
> 即可在 Inspector 上调整 `manageCategoryRow`（是否接管分类区）、`logBridgeActivity`（桥接日志）、
> `pullMeasurementFromModule`（是否软读取测量读数）。自举检测到场景实例后会直接跳过，
> 不会创建第二个实例。自检报告会把这种情形列为**有意预留的调参入口**，而不是异常。

### 5.5 未来 Designer Dashboard

```csharp
FeedbackService.Instance.IssueCreated += record => { /* 实时上报 */ };
FeedbackService.Instance.IssueUpdated += record => { /* 状态变更同步 */ };
var all = FeedbackService.Instance.Store.All;   // 只读列表
```

数据字段（`IssueRecord`）：`id / index / sessionId / origin / category / severity / description /`
`position / targetFurnitureId / targetName / targetObjectName / hasMeasurement / measurementMeters /`
`measurementText / createdAt` —— 完全满足 PROJECT_BRIEF「一条反馈至少应包含空间位置、问题类型、用户描述和创建时间」与「能关联具体家具时保存目标对象标识」。

---

## 6. 冲突与风险清单

| # | 风险 | 影响 | 处理 |
| --- | --- | --- | --- |
| R1 | 场景改为人工维护，功能若依赖场景序列化会与 D 的改动冲突 | 合并冲突 / 功能丢失 | E 全程运行期自举，零场景写入；自检会告警"场景里误存了 FeedbackSystem" |
| R2 | 屏幕空间画布在 `XRUIInputModule(enableMouseInput=0)` 下不可交互 | 界面点不动 | E 的所有界面均为世界空间画布 + `TrackedDeviceGraphicRaycaster`（F6/F7） |
| R3 | D 的分类/提交按钮未接线 | 面板无法选分类、无法提交 | E 运行期接管（F3）；并保留不接管 D 按钮的兼容开关 |
| R4 | 注入控件与 D 的版式重叠 | 视觉遮挡 | 注入位置按 `Canvas.prefab` 实测坐标推导（§3.3），只在隐藏 D 分类按钮后的空白区落位 |
| R5 | `AnnotationUI.descriptionInput` 在 D 的 `Start()` 里才创建，E 的 Update 可能更早 | 读到空描述 | `AnnotationBinding.TryBuild()` 分帧重试（最多 12 帧），超时后按"无描述输入"装配 |
| R6 | 接管面板后 D 的高亮"Cyan 选中态"不复位 | 物体一直高亮 | E 提交后调用 D 的 `CloseUI()`，复用其高亮复位逻辑，不重复实现 |
| R7 | 空间标注带碰撞体会被 C/D 的射线误拾取 | 测量/选择结果错误 | Marker 生成后立即移除所有 Collider，且不挂任何交互组件 |
| R8 | 3D 标签中文缺字 | 场景内乱码 | 3D 标签只用 ASCII（`ISSUE #01 / Circulation`）；中文只出现在 E 自建的 UGUI 与世界空间面板 |
| R9 | D 面板的 TMP 默认字体不含中文 | 注入按钮若用中文会缺字 | 注入到 D 面板的按钮一律用 ASCII（`SPACE` / `LOW` …）；中文只在 E 自建界面出现 |
| R10 | 汇总入口挂右手柄，若场景 rig 被替换 | 入口不生成 | `FindControllerTransform("Right")` 失败时给告警并**继续**运行：入库、空间标注、导出、`IssueCreated` 均不受影响，只是少一个入口 |
| R11 | 测量软桥接依赖 C 的私有字段名与标签池结构 | 桥接失效 | 三级来源 + 反射失败即静默降级（只是不再自动附加测量）；可改用 §5.2 方式 1/2 |
| R12 | 无 Unity 环境，改动未经编译验证 | 可能编译错误 | 已做逐 API 核查（含 XRI 2.6.4 的 `XRUIInputModule` / `TrackedDeviceGraphicRaycaster` / `XRRayInteractor.enableUIInteraction` / `XRSimpleInteractable.selectEntered`）、类型名冲突检查、括号平衡检查与 UI 元素越界核对；**首次在 Unity 内打开时必须确认零编译错误** |
| R13 | 世界空间画布朝向写反会渲染成**镜像文字** | 汇总面板完全不可读 | 项目既有 `WristPanelFacing` 与 `HudLazyFollow` 都用 `LookRotation(面板位置 − 观察者位置)`；E 统一沿用该式，**不得**再叠加 `Rotate(0,180,0)`（D 的 `LookAt(cam)` + `Rotate(0,180,0)` 只是等价写法，单独叠加会翻反） |
| R14 | 汇总面板生成点贴墙时会糊在脸上 | 无法阅读 | 生成距离下限 0.6 m，并在障碍物前留出 0.2 m；遮挡射线排除 `PlayerBody` |
| R15 | 每帧 `FindObjectOfType` 解析头显相机 | 无谓开销 | 腕部 HUD 的相机复检节流到 0.5 秒；标注面板轮询 0.25 秒、交互对象重扫 3 秒 |

---

## 7. 验收步骤

1. Unity 2022.3.62f3 打开工程，确认 `Assets/LivingRoom30/Scripts/Feedback/` 与 `Editor/FeedbackSetup.cs` 编译无错。
2. 打开 `Assets/Scenes/SceneNew.unity`（**不要**执行 `SceneNewBuilder` 的重建菜单，场景现在是手工维护的）。
3. 执行 `LivingRoom30 ▸ 反馈 ▸ 集成自检并生成报告`，确认「D 标注链路」与「XR UI 输入链路」两节全部为 `[通过]`。
4. Play（有头显走 PCVR；无头显等待约 3 秒让 XR Device Simulator 接管）。
5. 用手柄射线指向某件家具并保持约 1.5 秒 → 出现黄色描边；按下 grip → 头显前方弹出标注面板。
6. 确认面板上出现 E 注入的两行分类（`SPACE/LAYOUT/SIZE`、`CIRCULATION/ACCESSIBILITY`）与一行严重程度（`LOW/MEDIUM/HIGH`），且 Submit 键显示 `Submit` + 当前选择。
7. 选一个分类与严重程度 → 用面板自带虚拟键盘（或实体键盘）输入描述 → 点 Submit。
8. 预期：面板关闭、物体高亮复位、该锚点出现 `ISSUE #01` 组合标注并脉冲一次、右腕入口变为 `ISSUES (1)`、Console 打印记录详情与自动保存路径。
9. 用左手柄射线点右腕 `ISSUES (n)` → 前方出现汇总面板；核对标题计数、严重程度摘要、行内容（编号/分类/严重程度/目标/坐标/时间/描述）。
10. 点某行 `FOCUS` → 对应空间标注脉冲高亮。
11. 点 `EXPORT CSV+JSON` → 到 `LivingRoom30 ▸ 反馈 ▸ 打开反馈数据导出目录` 检查两个文件；用表格软件打开 CSV 确认中文不乱码、字段齐全。
12. 再提交 1~2 条（含一条在测量两点距离之后提交的）→ 确认反馈里带上了测量信息。
13. 点 `CLOSE`，走动、测量、再标注几次，确认与 A/B/C/D 均无输入冲突。

---

## 8. 已知限制与后续

**已知限制**
- 标注面板的**弹出**依赖 D 的 `XRSimpleInteractable` 与场景绑定（F2）；若场景被替换或重建，这条链路会断（自检会报 `[失败]`）。
- 汇总入口固定在右腕，用户需要抬手才能看到；未做「自动跟随视线」的浮窗。
- 汇总面板不跟随移动（与 D 的面板一致），走动后需要重新打开。
- 头显内仅能用 D 自带的 **A–Z 虚拟键盘**输入描述，无法输入中文；中文描述可在桌面端补录（数据层已完整支持）。
- `forceDesktop`（纯鼠标）模式下 UI 不可交互，这是项目 `XRUIInputModule` 的既有设定，非本模块引入。
- 反馈数据存于本机 `persistentDataPath`，未做多会话合并与云端同步。
- 测量自动附加依赖反射读取 C 的 3D 标签池（F13）：若 C 在标注**之后**又量了一次别的距离，暂存的测量会被新读数覆盖 —— 语义上"附加最近一次测量"，不做历史选择。
- 本机无 Unity，改动未经编译验证（见 R12）。

**V1+ 可扩展方向**
- 反馈状态流转（已解决 / 待确认）：`IssueRecord` 加 `status` 字段即可，汇总行已预留交互按钮位。
- 测量与反馈显式对接：在 C 的 `ShowPointToPoint` / `ShowObjectToObject` 末尾各加一行
  `FeedbackService.Instance?.OfferMeasurement(...)`，即可彻底去掉反射依赖（§5.2 方式 2/3）。
- 标注点选回看：给 Marker 加一个仅对特定层开放的碰撞体，即可实现"点标注打开详情"。
- 耳机内中文输入：接入系统输入法或改用 TMP 的中文动态字体资源。
- 汇总面板跟随视线（项目已有 `HudLazyFollow` 组件可直接复用）。
- Designer Dashboard：消费 `IssueCreated` 与 JSON 导出，做「发生了什么 + 发生在哪里」的可视化。
