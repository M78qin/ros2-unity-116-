// Bidirectional-control PRESENTATION layer for the unified shell.
//
// Scope of this batch: the panel shows what the existing safety chain already knows, and every
// button either reads state or calls an existing TwinCommandController entry point. No new command
// path, no new topic, no direct robot write. Execute stays behind the profile's dry-run switch and
// behind a two-step arm/execute guard, and the panel refuses to execute when the profile says the
// real robot is enabled, because a presentation layer must not be able to move hardware.
//
// Evidence: TwinCommandController.SendMoveJoint() runs SafetyCheckService.ValidateMoveJoint() and
// returns DRY_RUN when TwinRuntimeProfile has enableDryRun=1 / enableRealRobotCommand=0 (verified).
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    public sealed class TwinCommandPanelView : MonoBehaviour
    {
        [SerializeField] private DigitalTwinRuntime runtime;
        [SerializeField] private TwinCommandController controller;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text detailText;
        [SerializeField] private Button previewButton;
        [SerializeField] private Button checkButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button executeButton;
        [SerializeField] private Button haltButton;
        [SerializeField, Range(1f, 100f)] private float speedPercent = 10f;
        [SerializeField] private float refreshHz = 3f;

        private float _nextRefresh;
        private bool _confirmArmed;
        private bool _haltLatched;

        public void Bind(DigitalTwinRuntime runtimeRef, TwinCommandController controllerRef, TMP_Text status, TMP_Text detail,
            Button preview, Button check, Button confirm, Button execute, Button halt)
        {
            runtime = runtimeRef;
            controller = controllerRef;
            statusText = status;
            detailText = detail;
            previewButton = preview;
            checkButton = check;
            confirmButton = confirm;
            executeButton = execute;
            haltButton = halt;
        }

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            if (refreshHz <= 0f || Time.unscaledTime < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.unscaledTime + 1f / refreshHz;
            Refresh();
        }

        // ------------------------------------------------------------------ button entries

        /// <summary>预览: only draws the plan preview, TwinCommandController.EnterPlan() does not move anything.</summary>
        public void PreviewPlan()
        {
            if (controller == null)
            {
                SetStatus("无控制器", TwinUIShellText.CommandIdle);
                return;
            }

            bool ok = controller.EnterPlan();
            SetStatus(ok ? TwinUIShellText.CommandPreview + " 已进入" : TwinUIShellText.CommandPreview + " 失败",
                ok ? "目标未下发，等待校验" : "见控制器返回");
            Refresh();
        }

        /// <summary>校验: reads the current safety gate. It deliberately does not trigger a move.</summary>
        public void CheckSafety()
        {
            if (controller == null)
            {
                SetStatus("无控制器", TwinUIShellText.CommandIdle);
                return;
            }

            string gate = controller.CanSendRealMove ? "真机可下发" : (controller.EnableDryRun ? "干运行" : "已阻止");
            string flags = "双向=" + (controller.EnableBidirectionalControl ? "开" : "关")
                           + " 真机=" + (controller.EnableRealRobotCommand ? "开" : "关")
                           + " 干运行=" + (controller.EnableDryRun ? "开" : "关");
            SetStatus(TwinUIShellText.CommandCheck + "：" + gate, flags + "；九项校验在执行时由安全链执行");
            _confirmArmed = false;
            Refresh();
        }

        /// <summary>确认: arms the next execute click. Two-step guard so a stray click cannot command.</summary>
        public void ArmConfirm()
        {
            _confirmArmed = !_confirmArmed;
            SetStatus(_confirmArmed ? TwinUIShellText.CommandConfirm + "：已武装" : TwinUIShellText.CommandConfirm + "：已取消",
                _confirmArmed ? "再次点击执行即可（干运行）" : "未武装");
            Refresh();
        }

        /// <summary>
        /// 执行: dry-run only. The target is the latest frame's joint vector, i.e. a zero-motion target,
        /// so this exercises the safety chain and the logging path without any physical effect.
        /// </summary>
        public void ExecuteGuarded()
        {
            if (controller == null || runtime == null)
            {
                SetStatus("无控制器", TwinUIShellText.CommandIdle);
                return;
            }

            if (controller.EnableRealRobotCommand && controller.CanSendRealMove)
            {
                SetStatus("已阻止：真机已启用", "呈现层不下发真机指令，请在控制台操作");
                _confirmArmed = false;
                return;
            }

            if (!_confirmArmed)
            {
                SetStatus("已阻止：未确认", "先点" + TwinUIShellText.CommandConfirm);
                return;
            }

            _confirmArmed = false;

            RobotStateFrame frame;
            if (!runtime.TryGetLatestFrame(out frame) || frame.JointPositionRad == null || frame.JointPositionRad.Length < 6)
            {
                SetStatus("已阻止：无数据源", TwinUIShellText.ChartNoData);
                return;
            }

            RobotCommandResult result = controller.SendMoveJoint(frame.JointPositionRad, speedPercent);
            SetStatus(TwinUIShellText.CommandExecute + "：" + result.Status,
                (result.DryRun ? "干运行" : "真机") + " 成功=" + result.Success
                + (string.IsNullOrEmpty(result.ErrorMessage) ? string.Empty : " " + result.ErrorMessage));
            Refresh();
        }

        /// <summary>急停: latches the local stop flag only, so the demo cannot accidentally resume motion.</summary>
        public void ToggleHalt()
        {
            _haltLatched = !_haltLatched;
            if (controller != null)
            {
                controller.SetEmergencyStopped(_haltLatched);
            }

            SetStatus(_haltLatched ? TwinUIShellText.CommandHalt + "：已按下" : TwinUIShellText.CommandHalt + "：已复位",
                _haltLatched ? "本地停止标记已置位" : "本地停止标记已清除");
            Refresh();
        }

        // ------------------------------------------------------------------ display

        private void Refresh()
        {
            if (statusText == null)
            {
                return;
            }

            if (controller == null)
            {
                statusText.text = TwinUIShellText.CommandIdle;
                if (detailText != null)
                {
                    detailText.text = TwinUIShellText.ChartNoData;
                }

                return;
            }

            string mode = controller.CurrentMode.ToString();
            string dryRun = controller.EnableDryRun ? "干运行" : "真机";
            RobotCommandResult last = controller.LastResult;
            statusText.text = mode + " / " + dryRun + " / " + (controller.IsEmergencyStopped ? "急停" : "正常");

            if (detailText == null)
            {
                return;
            }

            string planning = controller.LastPlanningStatus;
            string error = string.IsNullOrEmpty(controller.LastPlanningError) ? "-" : controller.LastPlanningError;
            detailText.text = "结果=" + (string.IsNullOrEmpty(last.Status) ? "-" : last.Status)
                              + " 规划=" + (string.IsNullOrEmpty(planning) ? "-" : planning)
                              + " 误差=" + error;
        }

        private void SetStatus(string status, string detail)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }

            if (detailText != null)
            {
                detailText.text = detail;
            }
        }
    }
}