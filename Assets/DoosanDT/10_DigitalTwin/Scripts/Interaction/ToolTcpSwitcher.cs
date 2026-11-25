// ToolTcpSwitcher.cs -- 双 TCP 切换（法兰中心 Flange_TCP <-> 批头尖端 Spindle_TCP）。
// 只改 RobotIkController 的 TCP 参考节点，不碰运动控制路径：
//   allowRobotDispatch 恒为 false（与 TwinPlayIkHandle 的安全默认一致），
//   不调用 EnterPlan/EnterExecute，不向任何 ROS 话题发布。
using UnityEngine;
using DigitalTwin;

namespace DigitalTwin.Interaction
{
    [DisallowMultipleComponent]
    public sealed class ToolTcpSwitcher : MonoBehaviour
    {
        public enum TcpKind
        {
            Flange = 0,
            Spindle = 1
        }

        [Header("Active TCP")]
        [Tooltip("当前激活的 TCP。默认 Spindle（批头尖端）。")]
        [SerializeField] private TcpKind activeTcp = TcpKind.Spindle;

        [Header("References")]
        [Tooltip("法兰中心空节点。")]
        [SerializeField] private Transform flangeTcp;

        [Tooltip("批头尖端空节点（+Z 沿批头轴线向下）。")]
        [SerializeField] private Transform spindleTcp;

        [Tooltip("IK 控制器；切换时只更新它的 TCP 参考节点。")]
        [SerializeField] private RobotIkController ikController;

        [Tooltip("Play 模式拖拽手柄；切换时让它重新捕获当前 TCP（ghost 球与工作空间随之更新）。")]
        [SerializeField] private TwinPlayIkHandle ikHandle;

        [Header("Safety (read-only mirror)")]
        [Tooltip("与 TwinPlayIkHandle.allowRobotDispatch 对齐；本组件恒为 false，不做任何下发。")]
        [SerializeField] private bool allowRobotDispatch = false;

        public TcpKind ActiveTcp => activeTcp;
        public Transform FlangeTcp => flangeTcp;
        public Transform SpindleTcp => spindleTcp;
        public bool AllowRobotDispatch => allowRobotDispatch;

        public Transform ActiveTcpTransform =>
            activeTcp == TcpKind.Flange ? flangeTcp : spindleTcp;

        public RobotIkController IkController => ikController;
        public TwinPlayIkHandle IkHandle => ikHandle;

        public string ActiveTcpName =>
            activeTcp == TcpKind.Flange ? "Flange_TCP" : "Spindle_TCP";

        private void OnEnable() { Apply(); }
        private void OnValidate() { Apply(); }

        /// <summary>Inspector / 代码切换入口。</summary>
        public void SetActiveTcp(TcpKind kind)
        {
            activeTcp = kind;
            Apply();
        }

        public void SetActiveFlange() => SetActiveTcp(TcpKind.Flange);
        public void SetActiveSpindle() => SetActiveTcp(TcpKind.Spindle);

        public void Toggle() =>
            SetActiveTcp(activeTcp == TcpKind.Flange ? TcpKind.Spindle : TcpKind.Flange);

        /// <summary>把当前激活的 TCP 写进 RobotIkController 的参考节点（不改任何模型变换）。</summary>
        [ContextMenu("Apply Active TCP")]
        public void Apply()
        {
            var t = ActiveTcpTransform;
            if (t == null)
            {
                Debug.LogWarning("[ToolTcpSwitcher] active TCP reference is missing (" + ActiveTcpName + ")");
                return;
            }
            if (ikController != null) ikController.SetTcpTransform(t);
            // Play 模式下让拖拽手柄立即重新捕获 TCP；编辑模式手柄尚未初始化，跳过。
            if (ikHandle != null && Application.isPlaying) ikHandle.RecaptureTcp();
        }
    }
}