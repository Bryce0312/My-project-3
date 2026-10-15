using UnityEngine;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 桌面行走模式：WASD 移动 + 按住右键拖拽视角，CharacterController 提供与家具/墙体的真实碰撞与重力贴地。
    /// 由 SpatialMeasureManager 运行时挂到 S02_PreviewCamera 上，与 DesktopOrbitCamera 严格互斥（同一时刻只有一个 enabled）。
    /// 模式状态归 Manager 所有，本组件不监听切换热键。
    /// 注意：本体 GameObject 会被移到 PlayerBody 层，拾取射线需排除该层（Manager 已配置 layerMask）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DesktopWalkController : MonoBehaviour
    {
        [Header("移动")]
        public float walkSpeed = 1.4f;
        public float sprintSpeed = 2.6f;      // 按住 LeftShift 加速

        [Header("身体")]
        public float eyeHeight = 1.6f;        // 相机即眼睛，胶囊底 = 相机下方 eyeHeight
        public float headRoom = 0.10f;        // 胶囊顶高出眼睛的距离，防止贴墙时近裁剪面穿墙
        public float gravity = -9.81f;

        [Header("视角")]
        public float lookSpeed = 130f;
        public bool pointerLockLook = false;  // 默认按住右键拖拽（不锁光标，UGUI 面板可点、左键拾取不受影响）

        [Header("依赖（Manager 注入）")]
        public DesktopOrbitCamera orbit;      // 互斥伙伴；BeginFrom 用其 target 作第二落点候选

        private CharacterController cc;
        private float yaw, pitch, vSpeed;
        private int bodyMask = -1;            // 排除自身层的掩码（落地/穿插检测用）

        private void OnEnable()
        {
            EnsureBody();
            cc.enabled = true;
            Debug.Log($"[行走模式] 已启用：WASD/方向键移动、按住右键拖拽视角、Shift 加速、按 F 返回轨道（出生点 {transform.position:F2}）");
        }

        private void OnDisable()
        {
            if (cc != null) cc.enabled = false;
        }

        private void Update()
        {
            if (cc == null || !cc.enabled) return;
            Look();
            Step();
        }

        /// <summary>运行时确保 CharacterController 存在并参数化，同时把本体挪到 PlayerBody 层（缺失则退 Ignore Raycast）。</summary>
        private void EnsureBody()
        {
            if (cc == null)
            {
                cc = GetComponent<CharacterController>();
                if (cc == null) cc = gameObject.AddComponent<CharacterController>();

                cc.radius = 0.25f;
                cc.skinWidth = 0.025f;
                cc.height = eyeHeight + headRoom;
                cc.center = new Vector3(0f, cc.height * 0.5f - eyeHeight, 0f); // 胶囊底 = 相机下方 eyeHeight
                cc.slopeLimit = 45f;
                cc.stepOffset = 0.3f;
                cc.minMoveDistance = 0f;

                int playerLayer = LayerMask.NameToLayer("PlayerBody");
                int selfLayer = playerLayer >= 0 ? playerLayer : 2;
                gameObject.layer = selfLayer;
                bodyMask = ~(1 << selfLayer);
            }
        }

        private void Look()
        {
            float dx = 0f, dy = 0f;
            if (pointerLockLook || Cursor.lockState == CursorLockMode.Locked)
            {
                dx = Input.GetAxis("Mouse X");
                dy = Input.GetAxis("Mouse Y");
            }
            else if (Input.GetMouseButton(1))
            {
                dx = Input.GetAxis("Mouse X");
                dy = Input.GetAxis("Mouse Y");
            }
            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f)) return;

            yaw += dx * lookSpeed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch - dy * lookSpeed * Time.deltaTime, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void Step()
        {
            Vector3 wish = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (wish.sqrMagnitude > 1f) wish.Normalize();                 // 斜向不加速

            Quaternion yawQ = Quaternion.Euler(0f, yaw, 0f);              // 只用 yaw 计算水平速度，低头不影响走
            float spd = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
            Vector3 v = yawQ * wish * spd;

            if (cc.isGrounded && vSpeed < 0f) vSpeed = -2f;               // 贴地微压
            else vSpeed += gravity * Time.deltaTime;
            v.y = vSpeed;
            cc.Move(v * Time.deltaTime);

            if (!cc.isGrounded && vSpeed <= 0f) TrySnapDown();            // 离地 0.35m 内向下回吸（下台阶不悬空）
        }

        private void TrySnapDown()
        {
            Vector3 origin = transform.position;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, eyeHeight + 0.35f, bodyMask, QueryTriggerInteraction.Ignore))
            {
                float targetY = hit.point.y + eyeHeight;
                if (transform.position.y > targetY)
                {
                    Vector3 p = transform.position;
                    p.y = targetY;
                    transform.position = p;
                    vSpeed = -2f;
                }
            }
        }

        /// <summary>
        /// 从轨道相机切入行走：相机落到当前视角正下方地面，三级候选落点
        /// （当前相机 XZ → orbit.target XZ → rig 出生点 XZ），每级做 CheckCapsule 穿插检测，
        /// 避免出生在沙发内部第一帧被 CharacterController 解穿插弹飞；朝向继承当前相机。
        /// </summary>
        public void BeginFrom(Transform pose, Vector3 fallbackXZ, Vector3 rigSpawnXZ)
        {
            EnsureBody();
            cc.enabled = true;

            Vector3[] candidates =
            {
                new Vector3(pose.position.x, 0f, pose.position.z),
                new Vector3(fallbackXZ.x, 0f, fallbackXZ.z),
                new Vector3(rigSpawnXZ.x, 0f, rigSpawnXZ.z),
            };

            Vector3 pos = new Vector3(candidates[0].x, eyeHeight, candidates[0].z);
            for (int i = 0; i < candidates.Length; i++)
            {
                Vector3 p = new Vector3(candidates[i].x, eyeHeight, candidates[i].z);
                if (!IsBlocked(p)) { pos = p; break; }
                if (i == candidates.Length - 1) pos = p; // 全被挡：交给 CC 自行解穿插
            }

            float ground = GroundYAt(pos);
            transform.position = new Vector3(pos.x, ground + eyeHeight, pos.z);

            Vector3 fwdFlat = Vector3.ProjectOnPlane(pose.forward, Vector3.up);
            yaw = fwdFlat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwdFlat).eulerAngles.y : 0f;
            pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(pose.forward.y, -1f, 1f)) * Mathf.Rad2Deg, -85f, 85f);
            vSpeed = 0f;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>候选落点处胶囊是否与家具/墙体穿插（排除自身层）。</summary>
        private bool IsBlocked(Vector3 eyePos)
        {
            if (cc == null) return false;
            float halfLen = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            Vector3 c = eyePos + cc.center;
            return Physics.CheckCapsule(c + Vector3.up * halfLen, c - Vector3.up * halfLen, cc.radius * 0.9f, bodyMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>某 XZ 处的地面高度：从眼部稍上方（低于天花）向下探测，避免从高处打射线
        /// 先命中天花顶面而把人生成到屋顶；打不到则回退 y=0。</summary>
        private float GroundYAt(Vector3 xz)
        {
            Vector3 origin = new Vector3(xz.x, eyeHeight + 0.2f, xz.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, eyeHeight + 5f, bodyMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }
    }
}
