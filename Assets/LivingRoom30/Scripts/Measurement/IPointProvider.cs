using UnityEngine;
using System;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 拾取抽象：屏蔽 XR 控制器射线 与 桌面鼠标射线 的差异。
    /// 当发生一次“拾取”（点选/确认）时抛出 OnPick，携带命中世界点与对应家具根物体。
    /// </summary>
    public interface IPointProvider
    {
        /// <summary>一次拾取事件：worldPoint 为命中表面点，root 为家具根（若命中的不是家具则为命中物体本身）。</summary>
        event Action<Vector3, GameObject> OnPick;

        /// <summary>用于标签朝向相机时的参考相机（XR 模式为射线起点，桌面模式为活动相机）。</summary>
        Transform CameraTransform { get; }

        /// <summary>启用手柄（订阅输入事件）。</summary>
        void Enable();

        /// <summary>停用手柄（取消订阅，避免泄漏）。</summary>
        void Disable();

        /// <summary>每帧轮询（鼠标 Provider 需要；XR Provider 提供空实现）。</summary>
        void Tick();
    }
}
