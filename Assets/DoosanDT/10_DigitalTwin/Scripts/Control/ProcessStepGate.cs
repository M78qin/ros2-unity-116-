// ProcessStepGate.cs —— 第 3 章工艺/工具门禁 + 预标定 ROS2 点位绑定（第 4 章双向控制）
//
// 定位：Ghost IK 目标（世界坐标）→ 反查 Dis_1 的 DisassemblyPart → 工艺顺序 / 工具属性校验
//       → 放行并绑定预标定点位（Screw_10 / E5_SCREW_DISASSEMBLY），供 Ghost 预览与 ROS2 固定程序派发。
//
// 边界（重要）：
//   1. 纯只读判定，不发布任何 ROS 消息；真正的下发仍由 PointMotionSession / TwinCommandController 安全链决定。
//   2. 只调用第 3 章的只读接口：DisassemblySystem.CheckInteractability / GetCurrentInteractableParts /
//      DisassemblyPermissionEvents.NotifyDenied。**不修改** DB 目录下任何文件。
//   3. 不依赖物理射线：遍历 DisassemblySystem 管理的 DisassemblyPart 集合，用"目标点到各零件合并
//      Renderer.bounds 的最短距离"反查（默认容差 0.08 m）。原因：Lay1/pri1 未拆完时上盖 1-2 的碰撞体
//      处于关闭/遮挡态，射线必然打不到。
//   4. 严禁 GameObject.Find("Screw_10")：Screw_10 是标定路点，实体是 Dis_1/Lay1/pri1/screws。
using System;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>末端工具通道：电主轴（紧固件专用）或人工/通用工具。</summary>
    public enum ProcessToolChannel
    {
        Spindle = 0,
        ManualOrGeneric = 1
    }

    /// <summary>一次工艺门禁评估结果（值类型，便于日志/证据落盘）。</summary>
    [Serializable]
    public struct ProcessGateResult
    {
        public bool Allowed;
        public string ReasonCode;
        public string Message;
        public string TargetPartName;
        public float DistanceMeters;
        public ProcessToolChannel Channel;
        public string MatchedProgramId;
        public string MatchedWaypointName;

        public string ToLine()
        {
            return (Allowed ? "ALLOW" : "BLOCK") + " " + ReasonCode +
                   " part=" + (string.IsNullOrEmpty(TargetPartName) ? "-" : TargetPartName) +
                   " d=" + DistanceMeters.ToString("F3") + "m" +
                   " ch=" + Channel +
                   (string.IsNullOrEmpty(MatchedWaypointName)
                       ? string.Empty
                       : " wp=" + MatchedWaypointName + " prog=" + MatchedProgramId);
        }
    }

    /// <summary>
    /// 工艺/工具门禁。静态评估 + LastResult，供 IK 手柄、PointMotionSession、命令门控共用。
    /// </summary>
    public static class ProcessStepGate
    {
        public const string Tag = "[PROC-GATE]";

        // 预标定绑定：E5 拆螺钉固定程序（真机已核验的安全点位序列）
        public const string ScrewProgramId = "E5_SCREW_DISASSEMBLY";
        public const string ScrewWaypointName = "Screw_10";

        /// <summary>目标点到零件包围盒的容差（米）。超出即视为安全空域。</summary>
        public const float DefaultToleranceMeters = 0.08f;

        // 原因码
        public const string ReasonOk = "OK";
        public const string ReasonFreeSpace = "FREE_SPACE";
        public const string ReasonPrereqUnfinished = "PREREQ_UNFINISHED";
        public const string ReasonToolMismatch = "TOOL_MISMATCH";
        public const string ReasonGateUnavailable = "GATE_UNAVAILABLE";

        public static ProcessGateResult LastResult { get; private set; }
        public static bool HasResult { get; private set; }
        public static int EvaluateCount { get; private set; }
        public static int AllowedCount { get; private set; }
        public static int PrereqDeniedCount { get; private set; }
        public static int ToolDeniedCount { get; private set; }
        /// <summary>最近一次成功绑定的预标定点位（无绑定则为空）。</summary>
        public static string LastBoundWaypoint { get; private set; } = string.Empty;

        /// <summary>默认通道随 ToolTcpSwitcher 联动：Spindle_TCP → Spindle，Flange_TCP → ManualOrGeneric。</summary>
        public static ProcessToolChannel DefaultChannel
        {
            get
            {
                var switcher = UnityEngine.Object.FindObjectOfType<Interaction.ToolTcpSwitcher>();
                if (switcher == null) return ProcessToolChannel.Spindle;
                return switcher.ActiveTcp == Interaction.ToolTcpSwitcher.TcpKind.Spindle
                    ? ProcessToolChannel.Spindle
                    : ProcessToolChannel.ManualOrGeneric;
            }
        }

        public static void ResetCounters()
        {
            LastResult = default(ProcessGateResult);
            HasResult = false;
            EvaluateCount = 0;
            AllowedCount = 0;
            PrereqDeniedCount = 0;
            ToolDeniedCount = 0;
            LastBoundWaypoint = string.Empty;
        }

        /// <summary>按默认通道评估（IK 拖拽 / 会话内部用）。</summary>
        public static bool EvaluateTarget(Vector3 targetWorldPos, out ProcessGateResult result)
        {
            return EvaluateTarget(targetWorldPos, DefaultChannel, out result);
        }

        /// <summary>
        /// 评估 Ghost 目标世界坐标：工艺顺序 → 工具属性 → 放行并绑定预标定点位 / 安全空域。
        /// 返回 true 表示放行。
        /// </summary>
        public static bool EvaluateTarget(Vector3 targetWorldPos, ProcessToolChannel channel,
            out ProcessGateResult result)
        {
            result = new ProcessGateResult
            {
                Allowed = true,
                ReasonCode = ReasonFreeSpace,
                Message = "安全过渡空域：未命中任何零件",
                Channel = channel,
                DistanceMeters = float.PositiveInfinity,
                TargetPartName = string.Empty,
                MatchedProgramId = string.Empty,
                MatchedWaypointName = string.Empty
            };

            DisassemblySystem system = DisassemblySystem.Instance;
            if (system == null)
            {
                result.ReasonCode = ReasonGateUnavailable;
                result.Message = "第 3 章拆解系统未就绪：门禁放行但不做工艺判定";
                Commit(result);
                return true;
            }

            DisassemblyPart part;
            float distance;
            if (!TryFindNearestPart(system, targetWorldPos, out part, out distance))
            {
                // 没有任何可判定零件（或全部已拆完）→ 安全空域
                Commit(result);
                return true;
            }

            result.DistanceMeters = distance;
            result.TargetPartName = part.name;
            if (distance > DefaultToleranceMeters)
            {
                // 离最近零件仍超过容差 → 安全空域
                Commit(result);
                return true;
            }

            bool fastener = IsFastenerGroup(part);
            bool interactable = system.CheckInteractability(part);

            // ① 工艺顺序拦截：当前不可交互（前置紧固工步未完成 / 非当前层）
            if (!interactable)
            {
                result.Allowed = false;
                result.ReasonCode = ReasonPrereqUnfinished;
                result.Message = "前置紧固工步未完成（请先拆除 Lay1/pri1 螺钉），禁止操作上盖 1-2";
                NotifyDenied(part, result.Message);
                Commit(result);
                return false;
            }

            // ② 工具属性拦截：电主轴只能指向紧固件同步组
            if (channel == ProcessToolChannel.Spindle && !fastener)
            {
                result.Allowed = false;
                result.ReasonCode = ReasonToolMismatch;
                result.Message = "电主轴末端仅适用于紧固件工步（Lay1/pri1/screws），上盖拆解需人工或换工具执行";
                NotifyDenied(part, result.Message);
                Commit(result);
                return false;
            }

            // ③ 放行：命中螺钉组且为电主轴 → 绑定预标定固定点位/程序
            result.Allowed = true;
            result.ReasonCode = ReasonOk;
            if (fastener && channel == ProcessToolChannel.Spindle)
            {
                result.MatchedWaypointName = ScrewWaypointName;
                result.MatchedProgramId = ScrewProgramId;
                result.Message = "放行：绑定预标定点位 " + ScrewWaypointName + " / " + ScrewProgramId;
                LastBoundWaypoint = ScrewWaypointName;
            }
            else
            {
                result.Message = fastener
                    ? "放行：紧固件区域（人工/通用工具）"
                    : "放行：零件 " + part.name;
            }

            Commit(result);
            return true;
        }

        /// <summary>是否为紧固件同步组（screws / bolt / fastener）成员，或本身就是同步组根。</summary>
        public static bool IsFastenerGroup(DisassemblyPart part)
        {
            if (part == null) return false;
            Transform t = part.transform;
            int guard = 0;
            while (t != null && guard++ < 32)
            {
                string n = t.name;
                if (!string.IsNullOrEmpty(n))
                {
                    string lower = n.ToLowerInvariant();
                    if (lower.Contains("screw") || lower.Contains("bolt") ||
                        lower.Contains("fastener") || lower.Contains("nut"))
                    {
                        return true;
                    }
                }
                t = t.parent;
            }

            return false;
        }

        /// <summary>在 DisassemblySystem 管理的零件集合里，找距目标点最近且未完成的零件。</summary>
        private static bool TryFindNearestPart(DisassemblySystem system, Vector3 point,
            out DisassemblyPart nearest, out float nearestDistance)
        {
            nearest = null;
            nearestDistance = float.PositiveInfinity;
            var layers = system.layers;
            if (layers == null) return false;

            for (int li = 0; li < layers.Count; li++)
            {
                LayerGroupConfig layer = layers[li];
                if (layer == null || layer.allParts == null) continue;
                for (int pi = 0; pi < layer.allParts.Count; pi++)
                {
                    DisassemblyPart part = layer.allParts[pi];
                    if (part == null || part.isCompleted) continue;

                    Bounds bounds;
                    if (!TryGetPartBounds(part, out bounds)) continue;
                    float d = Vector3.Distance(point, bounds.ClosestPoint(point));
                    if (d < nearestDistance - 0.0005f)
                    {
                        nearestDistance = d;
                        nearest = part;
                    }
                    else if (nearest != null && Mathf.Abs(d - nearestDistance) <= 0.0005f)
                    {
                        // 距离相同（例如目标点同时落在上盖与螺钉的包围盒内）：取包围盒更小的那个，
                        // 让"具体零件"（螺钉）优先于"大件外壳"，避免工艺判定被外壳吞掉。
                        float a = bounds.size.x * bounds.size.y * bounds.size.z;
                        Bounds nb;
                        if (TryGetPartBounds(nearest, out nb))
                        {
                            float b = nb.size.x * nb.size.y * nb.size.z;
                            if (a < b)
                            {
                                nearestDistance = d;
                                nearest = part;
                            }
                        }
                    }
                }
            }

            return nearest != null;
        }

        /// <summary>零件子网格 Renderer.bounds 合并（忽略已禁用渲染器）。</summary>
        private static bool TryGetPartBounds(DisassemblyPart part, out Bounds bounds)
        {
            bounds = default(Bounds);
            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return any;
        }

        private static void NotifyDenied(DisassemblyPart part, string message)
        {
            try
            {
                DisassemblyPermissionEvents.NotifyDenied(part, message, "ik_process_gate");
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + " notify denied failed: " + e.Message);
            }
        }

        private static void Commit(ProcessGateResult result)
        {
            LastResult = result;
            HasResult = true;
            EvaluateCount++;
            if (result.Allowed)
            {
                AllowedCount++;
            }
            else if (result.ReasonCode == ReasonPrereqUnfinished)
            {
                PrereqDeniedCount++;
            }
            else if (result.ReasonCode == ReasonToolMismatch)
            {
                ToolDeniedCount++;
            }
        }
    }
}