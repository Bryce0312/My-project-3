using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using LivingRoom30;

namespace LivingRoom30.Measurement
{
    /// <summary>测量工具模式。</summary>
    public enum MeasureMode
    {
        Dimensions = 0,    // 家具尺寸（黄色包围盒线框已按需求移除，尺寸由读数与邻近标签承担）
        PointToPoint = 1, // 两点距离
        ObjectToObject = 2 // 两物体距离
    }

    /// <summary>
    /// 空间测量总控：管理工具模式状态机、XR/桌面双模式输入切换，并串联可视化/UI/路径模块。
    /// 说明：自举入口 Bootstrap 默认启用，Play 后自动生成 MeasurementSystem（侧边 UI + 3D 标签 + 邻近测距）。
    /// 如需停用（恢复场景原样、仅保留项目原本的 XR Device Simulator 界面），
    /// 把 Bootstrap 上那行 RuntimeInitializeOnLoadMethod 特性注释掉即可。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SpatialMeasureManager : MonoBehaviour
    {
        public MeasureMode mode = MeasureMode.Dimensions;

        [Header("调试")]
        [SerializeField] private bool m_EnableDiagnostics = false; // 关闭则不每 0.5s 打印 [XR诊断] 相机追踪日志（排查头转视角问题时在 Inspector 勾选）

        private MeasurementVisualizer visualizer;
        private MeasureUIPanel ui;

        private XRRayPointProvider xrProvider;
        private MousePointProvider mouseProvider;
        private DesktopOrbitCamera desktopOrbit;

        private Camera mainCam;
        private Camera desktopCam;
        private Camera xrCam;
        private bool forceDesktop;
        private bool xrActive;
        private bool simulatorActive;
        private float m_DiagTimer;

        // 邻近自动测距
        private ProximityMeasurer proximity;
        private MeasurementVisualizer proxViz;
        public bool proximityEnabled = true;

        // 桌面行走模式（与轨道相机互斥）
        [Header("桌面行走")]
        public KeyCode walkToggleKey = KeyCode.F;
        private DesktopWalkController desktopWalk;
        private bool desktopWalkMode = true;   // 是否处于行走模式（仅桌面模式可能为 true）；Play 后默认直接进入行走模式
        private bool useXrNow;                 // ConfigureInput 最近一次判定的输入模式
        private CharacterController rigCc;     // rig 的 CharacterController（桌面行走时必须禁用，防胶囊互卡）
        private ActionBasedContinuousMoveProvider rigMoveProvider;   // 必须与 rigCc 同步开关，见 SetRigLocomotionEnabled
        private ActionBasedSnapTurnProvider rigTurnProvider;
        private bool reviewLocomotionBlocked;

        // The review seat blocks locomotion, never headset/controller tracking or UI rays.
        public bool IsXrMode => useXrNow;
        public Camera ActiveCamera => mainCam;
        public Transform XrOriginTransform => rigCc != null ? rigCc.transform : null;

        public void SetReviewLocomotionBlocked(bool blocked)
        {
            reviewLocomotionBlocked = blocked;
            ApplyRigLocomotionState();
        }

        private void ApplyRigLocomotionState()
        {
            bool allow = useXrNow && !reviewLocomotionBlocked;
            if (rigCc != null) rigCc.enabled = allow;
            SetRigLocomotionEnabled(allow);
        }

        [Header("标签恒定屏幕尺寸")]
        public float labelScreenFraction = 0.02f;
        public float labelMinScale = 0.02f;
        public float labelMaxScale = 0.50f;

        // 交互状态
        private int pickCount;
        private Vector3 pendingA;
        private GameObject pendingRootA;
        private Bounds pendingBoundsA;

        // rig 内的 XR 射线交互器与其线可视化（桌面模式禁用，XR 模式启用）
        private readonly List<XRRayInteractor> xrInteractors = new List<XRRayInteractor>();
        private readonly List<XRInteractorLineVisual> xrRayVisuals = new List<XRInteractorLineVisual>();

        // 运行时自举：场景文件内无任何序列化残留，Play 时自动生成 MeasurementSystem。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindObjectOfType<SpatialMeasureManager>() != null) return;
            var go = new GameObject("MeasurementSystem");
            go.AddComponent<SpatialMeasureManager>();
        }

        private void Awake()
        {
            // 子模块
            var visGO = new GameObject("Visualizer");
            visGO.transform.SetParent(transform);
            visualizer = visGO.AddComponent<MeasurementVisualizer>();

            var uiGO = new GameObject("UIPanel");
            uiGO.transform.SetParent(transform);
            ui = uiGO.AddComponent<MeasureUIPanel>();
            ui.showSidePanel = false; // 用户要求：不显示侧边测量面板（顶部提示横幅保留）
            ui.Build();

            ui.OnModeSelected += OnModeSelected;
            ui.OnClear += OnClear;
            ui.OnToggleInputMode += OnToggleInputMode;
            ui.OnToggleWalkMode += ToggleWalkMode;

            // rig 的 CharacterController（XR_Player 子物体）：桌面行走时必须禁用，
            // 否则其胶囊（-2.7,0,-2.42）与行走体初始落点几乎重叠，第一次 Move 会被卡住/弹飞。
            var rigPlayer = GameObject.Find("S03_XR_ReviewRig/XR_Player");
            rigCc = rigPlayer != null ? rigPlayer.GetComponent<CharacterController>() : FindObjectOfType<CharacterController>();
            rigMoveProvider = rigPlayer != null ? rigPlayer.GetComponent<ActionBasedContinuousMoveProvider>() : null;
            rigTurnProvider = rigPlayer != null ? rigPlayer.GetComponent<ActionBasedSnapTurnProvider>() : null;

            // 相机：XR rig 主相机 与 专用桌面预览相机（S02_PreviewCamera）解耦，
            // 避免桌面模式下轨道相机与 rig 的位姿驱动组件争抢同一相机导致“啥也看不到”。
            xrCam = Camera.main; // 此刻 S02 仍禁用 -> 取到 rig 主相机
            desktopCam = GameObject.Find("S02_PreviewCamera")?.GetComponent<Camera>();

            // 头部追踪由现有 XR rig / TrackedPoseDriver 管理；测量工具只读取相机。

            // 桌面轨道相机挂在专用预览相机上
            if (desktopCam != null)
            {
                desktopOrbit = desktopCam.GetComponent<DesktopOrbitCamera>()
                    ?? desktopCam.gameObject.AddComponent<DesktopOrbitCamera>();

                // 房间包围盒注入：无论怎么旋转/平移/缩放，轨道相机都不会跑出建筑
                var roomRoot = GameObject.Find("SceneNew_40sqm_LivingRoom");
                if (roomRoot != null)
                {
                    desktopOrbit.roomBounds = GeometryUtils.GetWorldAABB(roomRoot);
                    desktopOrbit.clampToRoom = true;
                }

                // 初始位姿复刻 XR rig 主相机的起始视角（即项目“最开始的位置”）：
                // 相机停在 rig 相机处，焦点取其正前方 2.5m（反算逻辑统一在 SyncFromTransform）。
                if (xrCam != null)
                {
                    desktopOrbit.SyncFromTransform(xrCam.transform, 2.5f);
                }

                // 桌面行走控制器（与轨道相机互斥，初始关闭；RequireComponent 会自动带上 CharacterController）
                desktopWalk = desktopCam.GetComponent<DesktopWalkController>()
                    ?? desktopCam.gameObject.AddComponent<DesktopWalkController>();
                desktopWalk.orbit = desktopOrbit;
                desktopWalk.enabled = false;
            }

            // 复用现有 rig 射线；补齐缺失引用，实际输入模式在 Start 中统一配置。
            var xriManager = FindObjectOfType<XRInteractionManager>();
            xrInteractors.AddRange(FindObjectsOfType<XRRayInteractor>(true));
            foreach (var ri in xrInteractors)
            {
                if (ri.interactionManager == null && xriManager != null)
                    ri.interactionManager = xriManager;
                xrRayVisuals.AddRange(ri.GetComponentsInChildren<XRInteractorLineVisual>(true));
            }

            // XR 拾取（复用 rig 内 XR Ray Interactor）
            var interactor = FindObjectOfType<XRRayInteractor>();
            if (interactor != null)
            {
                xrProvider = new XRRayPointProvider();
                xrProvider.Bind(interactor);
                xrProvider.OnPick += HandlePick;
            }

            // 桌面鼠标拾取
            mouseProvider = new MousePointProvider { activeCamera = desktopCam ?? xrCam };
            mouseProvider.OnPick += HandlePick;

            // 邻近自动测距：独立可视化层（不挤占点击式测量的绘制池）
            var proxGO = new GameObject("ProximityVisualizer");
            proxGO.transform.SetParent(transform);
            proxViz = proxGO.AddComponent<MeasurementVisualizer>();
            proximity = proxGO.AddComponent<ProximityMeasurer>();
            proximity.Init(desktopCam ?? xrCam, desktopOrbit, proxViz, ui);
            proximity.enabled = proximityEnabled;
            ui.OnToggleProximity += OnToggleProximity;

            // 标签恒定屏幕尺寸：两套可视化（点击测量 + 邻近测距）统一生效
            visualizer?.SetLabelScaling(labelScreenFraction, labelMinScale, labelMaxScale, 1f);
            proxViz?.SetLabelScaling(labelScreenFraction, labelMinScale, labelMaxScale, 1f);
        }

        // UI 在 Awake 阶段尚未 Build 完成，初始状态推迟到 Start（所有 Awake 之后）再推送
        private void Start()
        {
            ConfigureInput();
            SetMode(mode);
        }

        private float m_XrRecheckTimer;

        private void Update()
        {
            if (mouseProvider != null) mouseProvider.Tick();
            HandleHotkeys();

            // 头显初始化和 XRDeviceSimulatorGate 均可能延迟完成；两者启动后恢复原 rig。
            m_XrRecheckTimer += Time.unscaledDeltaTime;
            if (m_XrRecheckTimer >= 0.5f)
            {
                m_XrRecheckTimer = 0f;
                if (!forceDesktop && (IsRealXrConnected() != xrActive || IsSimulatorActive() != simulatorActive))
                {
                    ConfigureInput();
                }
            }

            m_DiagTimer += Time.deltaTime;
            if (m_DiagTimer >= 0.5f)
            {
                m_DiagTimer = 0f;
                if (m_EnableDiagnostics) LogXrCameraDiagnostics();
            }
        }

        /// <summary>XR 相机追踪诊断：每 0.5s 打印相机世界高度、Camera Offset、HMD centerEye 与父节点欧拉角，
        /// 用于确认头转时是否只有相机本地旋转在变（正常第一人称），还是父节点被异常旋转（场景一起转）。</summary>
        private void LogXrCameraDiagnostics()
        {
            if (xrCam == null) return;
            try
            {
                var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                Vector3 centerEye = Vector3.zero;
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out centerEye);

                Vector3 worldPos = xrCam.transform.localToWorldMatrix.MultiplyPoint(Vector3.zero);
                Transform camOff = xrCam.transform.parent;
                float camOffY = camOff != null ? camOff.localPosition.y : 0f;
                Vector3 camOffEuler = camOff != null ? camOff.eulerAngles : Vector3.zero;
                Transform rigRoot = camOff != null ? camOff.parent : null;
                Vector3 rigEuler = rigRoot != null ? rigRoot.eulerAngles : Vector3.zero;

                Debug.Log($"[XR诊断] HMD.centerEye.y={centerEye.y:F2} | 相机世界y={worldPos.y:F2} | CameraOffset.y={camOffY:F2} | 相机世界欧拉={xrCam.transform.eulerAngles:F1} | CameraOffset欧拉={camOffEuler:F1} | rig根欧拉={rigEuler:F1}");
            }
            catch { /* 诊断失败忽略 */ }
        }

        /// <summary>
        /// 只把实际运行的 XR 显示或正在追踪的头显视为真实 XR；初始化结束本身不足以判定。
        /// </summary>
        private static bool IsRealXrConnected()
        {
            try
            {
                if (XRSettings.isDeviceActive) return true;
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetInstances(displays);
                foreach (var display in displays)
                    if (display.running) return true;

                var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                return head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSimulatorActive()
        {
            foreach (var simulator in FindObjectsOfType<XRDeviceSimulator>())
                if (simulator.isActiveAndEnabled) return true;
            return false;
        }

        /// <summary>桌面模式下的全局热键：F 切换 行走/轨道。</summary>
        private void HandleHotkeys()
        {
            if (useXrNow) return;
            if (Input.GetKeyDown(walkToggleKey)) ToggleWalkMode();
        }

        /// <summary>根据自动探测 + 手动开关配置当前输入模式与相机。</summary>
        private void ConfigureInput()
        {
            xrActive = IsRealXrConnected();
            simulatorActive = IsSimulatorActive();

            bool useXr = xrCam != null && !forceDesktop && (xrActive || simulatorActive);
            useXrNow = useXr;
            if (useXr)
            {
                xrProvider?.Enable();
                mouseProvider.Disable();
                if (desktopOrbit) desktopOrbit.enabled = false;
                if (xrCam) xrCam.enabled = true;
                if (desktopCam) desktopCam.enabled = false;
                mainCam = xrCam;
                proximity?.SetObserver(xrCam);

                // 回到 XR：恢复 rig 的行走能力与射线交互器，桌面行走强制退出
                desktopWalkMode = false;
                if (desktopWalk != null) desktopWalk.enabled = false;
                ApplyRigLocomotionState();
                SetXrRayActive(true);
                ui?.SetWalkModeActive(false);
                Debug.Log($"[SpatialMeasureManager] 模式=XR：启用 rig 相机（TrackedPoseDriver 跟随头转），xrCam.enabled={xrCam?.enabled}, desktopCam.enabled={desktopCam?.enabled}");
            }
            else
            {
                if (xrProvider != null) xrProvider.Disable();
                mouseProvider.Enable();
                if (desktopCam) desktopCam.enabled = true;
                mainCam = desktopCam ?? xrCam;
                if (xrCam) xrCam.enabled = mainCam == xrCam;
                proximity?.SetObserver(mainCam);

                // 桌面模式：禁用 rig 胶囊（与行走体落点重叠），排除自身层防自胶囊挡射线，
                // 关闭 XR 射线交互器（无 XR 设备时只会刷 NRE），并恢复用户上次的行走/轨道选择
                if (rigCc != null) rigCc.enabled = false;
                SetRigLocomotionEnabled(false);
                SetXrRayActive(false);
                int bodyLayer = LayerMask.NameToLayer("PlayerBody");
                mouseProvider.layerMask = bodyLayer >= 0 ? ~(1 << bodyLayer) : -1;
                if (desktopOrbit) desktopOrbit.enabled = !desktopWalkMode; // 行走时轨道让位
                SetDesktopWalk(desktopWalkMode);
                Debug.Log($"[SpatialMeasureManager] 模式=桌面预览：启用 S02 相机（固定朝向，不跟随头转），xrCam.enabled={xrCam?.enabled}, desktopCam.enabled={desktopCam?.enabled}");
            }

            mouseProvider.activeCamera = mainCam;
            visualizer?.SetFacingCamera(mainCam);
            proxViz?.SetFacingCamera(mainCam);

            string label = forceDesktop ? "输入: 桌面"
                : (xrActive ? "输入: XR" : simulatorActive ? "输入: XR 模拟器" : "输入: 桌面(无头显)");
            if (!useXrNow) label += desktopWalkMode ? " / 行走" : " / 轨道";
            ui?.SetInputModeLabel(label);
            ui?.SetProximityActive(proximityEnabled);
        }

        /// <summary>切换桌面「行走/轨道」模式；切换时相机位姿无缝衔接（原地出发/原地回轨道）。</summary>
        /// <summary>
        /// 与 rig 的 CharacterController 同进同出。
        ///
        /// 只禁用 CharacterController 是不够的：XRI 的 ContinuousMoveProviderBase 在 origin 上
        /// 找不到启用的 CharacterController 时会退化成 transform.position += motion 的裸位移，
        /// 完全不做碰撞检测——表现为用设备模拟器推摇杆时直接穿墙。两者必须一起开关。
        /// </summary>
        private void SetRigLocomotionEnabled(bool on)
        {
            if (rigMoveProvider != null) rigMoveProvider.enabled = on;
            if (rigTurnProvider != null) rigTurnProvider.enabled = on;
        }

        private void SetDesktopWalk(bool on)
        {
            if (desktopWalk == null || desktopOrbit == null || desktopCam == null) return;
            bool target = on && !useXrNow;
            if (target == desktopWalkMode && desktopWalk.enabled == target) return;

            desktopWalkMode = target;
            if (desktopWalkMode)
            {
                Vector3 rigSpawn = rigCc != null ? rigCc.transform.position : new Vector3(-2.7f, 0f, -2.42f);
                desktopWalk.BeginFrom(desktopCam.transform, desktopOrbit.target, rigSpawn);
                desktopOrbit.enabled = false;
                desktopWalk.enabled = true;
            }
            else
            {
                desktopWalk.enabled = false;
                desktopOrbit.enabled = true;
                desktopOrbit.SyncFromTransform(desktopCam.transform, 2.5f);
            }
            ui?.SetWalkModeActive(desktopWalkMode);
            string label = (forceDesktop ? "输入: 桌面" : (xrActive ? "输入: XR" : simulatorActive ? "输入: XR 模拟器" : "输入: 桌面(无头显)"))
                + (useXrNow ? "" : (desktopWalkMode ? " / 行走" : " / 轨道"));
            ui?.SetInputModeLabel(label);
        }

        private void ToggleWalkMode() => SetDesktopWalk(!desktopWalkMode);

        /// <summary>开关 rig 的 XR 射线交互器与线可视化（桌面模式关闭：无 XR 设备时它们只会每帧抛 NRE）。</summary>
        private void SetXrRayActive(bool on)
        {
            foreach (var v in xrRayVisuals)
                if (v != null) v.enabled = on;
            foreach (var ri in xrInteractors)
                if (ri != null) ri.enabled = on;
        }

        private void OnToggleProximity()
        {
            SetProximityEnabled(!proximityEnabled);
        }

        /// <summary>
        /// 供外部（手腕面板上的 GAPS 按钮）开关净空显示。
        ///
        /// 必须走这里,不能只写 <see cref="proximityEnabled"/> 字段:真正生效的是下面那行
        /// <c>proximity.enabled</c>,它只在 Awake 里被赋值过一次,之后没有任何地方监听字段变化。
        /// </summary>
        public void SetProximityEnabled(bool on)
        {
            proximityEnabled = on;
            if (proximity != null) proximity.enabled = proximityEnabled;
            ui?.SetProximityActive(proximityEnabled);
        }

        /// <summary>供外部按钮刷新自己的显示状态。</summary>
        public bool ProximityEnabled => proximityEnabled;

        private void OnModeSelected(MeasureMode m) => SetMode(m);
        private void OnClear()
        {
            visualizer?.Clear();
            pickCount = 0;
            ui?.SetReadout("已清空");
        }
        private void OnToggleInputMode()
        {
            forceDesktop = !forceDesktop;
            ConfigureInput();
        }

        private void SetMode(MeasureMode m)
        {
            mode = m;
            visualizer?.Clear();
            pickCount = 0;
            ui?.SetActiveMode(m);
            ui?.SetReadout(m == MeasureMode.Dimensions
                ? "点击家具查看尺寸"
                : m == MeasureMode.PointToPoint
                    ? "依次点击空间中的两点"
                    : "依次点击两个家具");
        }

        private void HandlePick(Vector3 point, GameObject root)
        {
            if (visualizer == null) return;
            switch (mode)
            {
                case MeasureMode.Dimensions:
                    ShowDimensions(root);
                    break;
                case MeasureMode.PointToPoint:
                    if (pickCount == 0) { pendingA = point; pickCount = 1; }
                    else { ShowPointToPoint(pendingA, point); pickCount = 0; }
                    break;
                case MeasureMode.ObjectToObject:
                    if (pickCount == 0)
                    {
                        pendingRootA = root;
                        pendingBoundsA = GeometryUtils.GetWorldAABB(root);
                        pickCount = 1;
                    }
                    else
                    {
                        ShowObjectToObject(pendingBoundsA, pendingRootA, GeometryUtils.GetWorldAABB(root), root);
                        pickCount = 0;
                    }
                    break;
            }
        }

        private void ShowDimensions(GameObject root)
        {
            // 家具尺寸模式只对家具生效：点中地板/墙等建筑构件时不再画出巨大包围盒
            if (!IsFurniture(root))
            {
                string what = IsUnderRoot(root, "A00_Architecture") ? "建筑构件" : "非家具对象";
                ui?.SetReadout($"请点击家具（{what}「{root.name}」不参与尺寸测量）\n"
                    + "· 量房间/门洞宽度请切「② 两点距离」\n"
                    + "· 查家具间隙请看「④ 邻近自动测距」或「③ 两物体距离」");
                return; // 不 Clear、不 DrawBox，保留上一次的测量结果
            }

            visualizer.Clear();
            var b = GeometryUtils.GetWorldAABB(root);
            // 黄色包围盒线框（DrawBox）已按需求移除：家具尺寸信息仍由下方 UI 读数与
            // FurnitureProximityDisplay 的邻近尺寸标签/贴合轮廓继续展示。
            var size = GeometryUtils.GetObjectAlignedSize(root);
            string name = root.GetComponent<FurnitureInfo>()?.EnglishName ?? root.name;
            ui.SetReadout($"家具：{name}\n长 {size.x:F2}  宽 {size.z:F2}  高 {size.y:F2} (m)\n整体 {b.size.x:F2}×{b.size.y:F2}×{b.size.z:F2} (m)");
        }

        /// <summary>判定命中根是否为家具：优先 FurnitureInfo，缺失时按 F00_Furniture 子树兜底。</summary>
        private static bool IsFurniture(GameObject root)
        {
            if (root == null) return false;
            if (root.GetComponentInParent<FurnitureInfo>(true) != null) return true;
            for (Transform p = root.transform; p != null; p = p.parent)
                if (p.name == "F00_Furniture") return true;
            return false;
        }

        /// <summary>判定物体是否位于指定根子树内（用于给出更精准的提示文案）。</summary>
        private static bool IsUnderRoot(GameObject go, string rootName)
        {
            for (Transform p = go.transform; p != null; p = p.parent)
                if (p.name == rootName) return true;
            return false;
        }

        private void ShowPointToPoint(Vector3 a, Vector3 b)
        {
            visualizer.Clear();
            float d = Vector3.Distance(a, b);
            visualizer.DrawLine(a, b, false, GeometryUtils.Meters(d));
            string pathStr = "无（未烘焙/不可达）";
            if (NavMeshPathDrawer.TryGetPath(a, b, out Vector3[] corners, out float len))
            {
                DrawPath(corners, len);
                pathStr = GeometryUtils.Meters(len);
            }
            ui.SetReadout($"两点距离：{GeometryUtils.Meters(d)}\n可行路线：{pathStr}");
        }

        private void ShowObjectToObject(Bounds bA, GameObject rootA, Bounds bB, GameObject rootB)
        {
            visualizer.Clear();
            float gap = GeometryUtils.SurfaceGap(bA, bB);
            float cd = GeometryUtils.CenterDistance(bA, bB);
            Vector3 pa = bA.ClosestPoint(bB.center);
            Vector3 pb = bB.ClosestPoint(bA.center);
            visualizer.DrawLine(pa, pb, false, GeometryUtils.Meters(gap));

            string na = rootA.GetComponent<FurnitureInfo>()?.EnglishName ?? rootA.name;
            string nb = rootB.GetComponent<FurnitureInfo>()?.EnglishName ?? rootB.name;

            string pathStr = "无（未烘焙/不可达）";
            if (NavMeshPathDrawer.TryGetPath(bA.center, bB.center, out Vector3[] corners, out float len))
            {
                DrawPath(corners, len);
                pathStr = GeometryUtils.Meters(len);
            }
            ui.SetReadout($"{na} ↔ {nb}\n表面间隙：{GeometryUtils.Meters(gap)}\n中心距：{GeometryUtils.Meters(cd)}\n可行路线：{pathStr}");
        }

        private void DrawPath(Vector3[] corners, float len)
        {
            for (int i = 1; i < corners.Length; i++)
                visualizer.DrawLine(corners[i - 1], corners[i], true, null);
            if (corners.Length > 0)
            {
                Vector3 mid = corners[corners.Length / 2];
                visualizer.PlaceLabel(mid + Vector3.up * 0.05f, GeometryUtils.Meters(len) + " 路径");
            }
        }
    }
}
