// TwinUiCommandGate.cs —— 控制指令"硬锁"（P9.5）
// 目的：执行按钮默认不可点；必须①用户在本卡显式点【授权】②TwinRuntimeProfile.enableRealRobotCommand 为真
//       ③未急停 —— 三者同时满足才允许走既有 TwinCommandController 安全链（即便通过，dry-run 仍由 profile 决定）。
// 边界：本类只做 UI 门控，不替代 SafetyCheckService / TwinCommandController 的校验；不新增下发路径。
using DigitalTwin;
using UnityEngine;

namespace DigitalTwin.UI
{
    public static class TwinUiCommandGate
    {
        public const string Tag = "[GATE]";

        /// <summary>本 Play 会话内的显式授权（不持久化，重启 Play 自动锁回）。</summary>
        public static bool Authorized { get; private set; }

        public static int AuthorizeCount { get; private set; }
        public static int RevokeCount { get; private set; }

        public static void Authorize()
        {
            Authorized = true;
            AuthorizeCount++;
            Debug.Log(Tag + " authorized (session only) count=" + AuthorizeCount);
        }

        public static void Revoke()
        {
            Authorized = false;
            RevokeCount++;
            Debug.Log(Tag + " revoked count=" + RevokeCount);
        }

        public static DigitalTwinRuntime ResolveRuntime()
        {
            return Object.FindObjectOfType<DigitalTwinRuntime>();
        }

        public static bool ProfileAllowsRealCommand(DigitalTwinRuntime runtime)
        {
            return runtime != null && runtime.Profile != null && runtime.Profile.enableRealRobotCommand;
        }

        /// <summary>执行按钮是否可点（UI 门控；实际执行仍由安全链判定）。</summary>
        public static bool CanExecute(out string reason)
        {
            var runtime = ResolveRuntime();
            var controller = Object.FindObjectOfType<TwinCommandController>();
            if (controller == null)
            {
                reason = "未挂载 TwinCommandController";
                return false;
            }

            if (!Authorized)
            {
                reason = "未授权：先点【授权】（仅本次 Play 有效）";
                return false;
            }

            if (!ProfileAllowsRealCommand(runtime))
            {
                reason = "Profile.enableRealRobotCommand=false（真机下发未开启）";
                return false;
            }

            if (controller.IsEmergencyStopped)
            {
                reason = "急停中";
                return false;
            }

            reason = "已授权";
            return true;
        }

        public static string Describe()
        {
            var runtime = ResolveRuntime();
            var controller = Object.FindObjectOfType<TwinCommandController>();
            return "gate authorized=" + Authorized
                + " profileAllows=" + ProfileAllowsRealCommand(runtime)
                + " canSendRealMove=" + (controller != null && controller.CanSendRealMove)
                + " authorizeCount=" + AuthorizeCount + " revokeCount=" + RevokeCount;
        }
    }
}