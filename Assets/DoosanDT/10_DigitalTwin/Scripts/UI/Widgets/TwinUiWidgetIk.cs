// TwinUiWidgetIk.cs —— IK 预览手柄卡片（P9）
// 状态指标 + 6 个原生操作按钮（IK 拖拽 / 关节限幅 / TCP 切换 / TCP 坐标轴 / 姿态复位 / 点位标注）。
// 卡片由 TwinUiModuleHost 托管，自动获得窄栏/宽屏自适应、图钉悬浮/停靠、折叠、关闭。
// 只读指标；真机下发仍由 TwinPlayIkHandle.AllowRobotDispatch 与 TwinCommandController 安全链决定。
using DigitalTwin.Interaction;
using DoosanDT.UI.Runtime;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetIk : TwinUiModule
    {
        public const string ModuleId = "ik";

        private TwinPlayIkHandle _handle;
        private RobotIkController _ik;
        private ToolTcpSwitcher _tcp;
        private TcpAxisRenderer _axis;
        private E5SceneOverlay _overlay;

        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Kv _status;
        private TwinUiCardParts.Kv _reachable;
        private TwinUiCardParts.Kv _limits;
        private TwinUiCardParts.Kv _workspace;
        private TwinUiCardParts.Kv _posErr;
        private TwinUiCardParts.Kv _rotErr;
        private TwinUiCardParts.Kv _iterations;
        private TwinUiCardParts.Kv _gate;
        private TwinUiCardParts.Kv _ghost;
        private TwinUiCardParts.Kv _vrd;
        private TMP_Text _note;

        private TwinUIButton _btnDrag, _btnLimit, _btnTcp, _btnAxes, _btnRestore, _btnMarkers, _btnExecute;
        private TwinUIButton _btnVrd, _btnScrew, _btnPlaneDown, _btnPlaneUp;
        private bool _limitOn = true;
        private bool _markersOn;

        public override string Id => ModuleId;
        public override string Title => "末端 IK 微调";
        public override string Kicker => "IK CONTROL";
        public override string Subtitle => "TwinPlayIkHandle / ±30° 每轴";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Twin;
        public override Vector2 CardSize => new Vector2(340f, 624f);
        public override Vector2 FloatingMinSize => new Vector2(310f, 548f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetIk");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _status = TwinUiCardParts.Row(root, 24f, "手柄状态", w);
            _reachable = TwinUiCardParts.Row(root, 48f, "可达性", w);
            _limits = TwinUiCardParts.Row(root, 72f, "关节限位", w);
            _workspace = TwinUiCardParts.Row(root, 96f, "工作空间", w);
            _posErr = TwinUiCardParts.Row(root, 120f, "位置误差", w);
            _rotErr = TwinUiCardParts.Row(root, 144f, "旋转误差", w);
            _iterations = TwinUiCardParts.Row(root, 168f, "迭代次数", w);
            _gate = TwinUiCardParts.Row(root, 192f, "工艺门禁", w);
            _ghost = TwinUiCardParts.Row(root, 216f, "Ghost", w);
            _vrd = TwinUiCardParts.Row(root, 240f, "VR_D 平面", w);
            _note = TwinUiCardParts.Caption(root,
                "仅预览（Ghost）；真机下发锁定，执行需独立授权与安全评审", 0f, 266f, w, 30f);

            // ── 操作区：2 列 × 3 行原生按钮网格 ────────────────────────────
            TwinUiCardParts.Section(root, "操作", 304f, w);
            TwinUiCardParts.Divider(root, 320f, w);

            const float colW = 148f;
            const float rowH = 26f;
            float x0 = 0f, x1 = colW + 10f;
            _btnDrag = TwinUIStyle.Button(root, "BtnIkDrag", "IK 拖拽: OFF", TwinUIButton.Kind.Plain,
                new Vector2(x0, -334f), new Vector2(colW, rowH), OnToggleDrag);
            _btnLimit = TwinUIStyle.Button(root, "BtnJointLimit", "限幅 ±30°: ON", TwinUIButton.Kind.Plain,
                new Vector2(x1, -334f), new Vector2(colW, rowH), OnToggleLimit);
            _btnTcp = TwinUIStyle.Button(root, "BtnTcp", "TCP: S", TwinUIButton.Kind.Plain,
                new Vector2(x0, -366f), new Vector2(colW, rowH), OnToggleTcp);
            _btnAxes = TwinUIStyle.Button(root, "BtnTcpAxes", "坐标轴: OFF", TwinUIButton.Kind.Plain,
                new Vector2(x1, -366f), new Vector2(colW, rowH), OnToggleAxes);
            _btnRestore = TwinUIStyle.Button(root, "BtnRestorePose", "姿态复位", TwinUIButton.Kind.Default,
                new Vector2(x0, -398f), new Vector2(colW, rowH), OnRestorePose);
            _btnMarkers = TwinUIStyle.Button(root, "BtnMarkers", "点位标注: OFF", TwinUIButton.Kind.Plain,
                new Vector2(x1, -398f), new Vector2(colW, rowH), OnToggleMarkers);
            _btnExecute = TwinUIStyle.Button(root, "BtnGhostExecute", "执行 (Ghost→实体)", TwinUIButton.Kind.Default,
                new Vector2(x0, -430f), new Vector2(colW * 2f + 10f, rowH), OnExecuteGhost);
            _btnVrd = TwinUIStyle.Button(root, "BtnVrd", "VR_D: OFF", TwinUIButton.Kind.Plain,
                new Vector2(x0, -462f), new Vector2(colW, rowH), OnToggleVrd);
            _btnScrew = TwinUIStyle.Button(root, "BtnScrewReplay", "Screw 拆解", TwinUIButton.Kind.Default,
                new Vector2(x1, -462f), new Vector2(colW, rowH), OnStartScrewReplay);
            _btnPlaneDown = TwinUIStyle.Button(root, "BtnPlaneDown", "面高 -5mm", TwinUIButton.Kind.Plain,
                new Vector2(x0, -494f), new Vector2(colW, rowH), () => OnNudgePlane(-5));
            _btnPlaneUp = TwinUIStyle.Button(root, "BtnPlaneUp", "面高 +5mm", TwinUIButton.Kind.Plain,
                new Vector2(x1, -494f), new Vector2(colW, rowH), () => OnNudgePlane(5));

            if (_btnDrag != null) _btnDrag.Label.fontSize = TwinUIStyle.FontUnit;
            if (_btnLimit != null) _btnLimit.Label.fontSize = TwinUIStyle.FontUnit;
            if (_btnTcp != null) _btnTcp.Label.fontSize = TwinUIStyle.FontUnit;
            if (_btnAxes != null) _btnAxes.Label.fontSize = TwinUIStyle.FontUnit;
            if (_btnRestore != null) _btnRestore.Label.fontSize = TwinUIStyle.FontUnit;
            if (_btnMarkers != null) _btnMarkers.Label.fontSize = TwinUIStyle.FontUnit;
            if (_btnExecute != null && _btnExecute.Label != null) _btnExecute.Label.fontSize = TwinUIStyle.FontUnit;

            foreach (var b in new[]
                     {
                         _btnDrag, _btnLimit, _btnTcp, _btnAxes, _btnRestore, _btnMarkers, _btnExecute,
                         _btnVrd, _btnScrew, _btnPlaneDown, _btnPlaneUp
                     })
            {
                if (b == null || b.Label == null) continue;
                b.Label.enableAutoSizing = true;
                b.Label.fontSizeMin = 7f;
                b.Label.fontSizeMax = TwinUIStyle.FontUnit;
                b.Label.enableWordWrapping = false;
                b.Label.overflowMode = TextOverflowModes.Ellipsis;
            }

            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);            return root;
        }

        private void Resolve()
        {
            if (_handle == null) _handle = Object.FindObjectOfType<TwinPlayIkHandle>();
            if (_ik == null) _ik = Object.FindObjectOfType<RobotIkController>();
            if (_tcp == null) _tcp = Object.FindObjectOfType<ToolTcpSwitcher>();
            if (_axis == null) _axis = Object.FindObjectOfType<TcpAxisRenderer>();
            if (_overlay == null) _overlay = Object.FindObjectOfType<E5SceneOverlay>();
        }

        private void OnToggleDrag()
        {
            Resolve();
            if (_handle == null) return;
            bool on = !_handle.EnableHandle;
            _handle.SetIkDragEnabled(on);
            if (on && _ik != null) _ik.CaptureJointReference();
            Refresh();
        }

        private void OnToggleLimit()
        {
            Resolve();
            _limitOn = !_limitOn;
            if (_ik != null)
            {
                _ik.SetJointLimitEnabled(_limitOn);
                _ik.SetJointLimitDeg(30f);
            }
            Refresh();
        }

        private void OnToggleTcp()
        {
            Resolve();
            if (_tcp != null) _tcp.Toggle();
            if (_handle != null) _handle.RecaptureTcp();
            Refresh();
        }

        private void OnToggleAxes()
        {
            Resolve();
            if (_axis != null) _axis.ToggleVisible();
            Refresh();
        }

        private void OnRestorePose()
        {
            Resolve();
            if (_handle != null) _handle.RestoreSafePose();
            Refresh();
        }

        /// <summary>「执行 (Ghost→实体)」：实体按真机参考速度阻尼跟随到 Ghost 姿态（纯本地）。</summary>
        private void OnExecuteGhost()
        {
            Resolve();
            if (_handle == null) return;
            bool started = _handle.ExecuteGhostToEntity();
            if (!started) Debug.Log("[UI-IK] execute ignored: 需要先开 IK 拖拽并产生 Ghost 解或吸附");
            Refresh();
        }

        /// <summary>VR_D 开关：自由模式 ⇄ 实训约束（安全网格面 + 两级拦截）。</summary>
        private void OnToggleVrd()
        {
            Resolve();
            if (_handle == null) return;
            _handle.SetVrdEnabled(!_handle.VrdEnabled);
            Refresh();
        }

        /// <summary>网格面高度 ±5 mm 步进。</summary>
        private void OnNudgePlane(int deltaMm)
        {
            Resolve();
            if (_handle == null) return;
            _handle.NudgeVrdPlaneOffset(deltaMm);
            Refresh();
        }

        /// <summary>「Screw 拆解」：门槛不满足时只给中文提示，不启动。</summary>
        private void OnStartScrewReplay()
        {
            Resolve();
            if (_handle == null) return;
            if (_handle.ScrewReplayPlaying)
            {
                _handle.StopScrewReplay();
                Refresh();
                return;
            }
            string reason;
            if (!_handle.CanStartScrewReplay(out reason))
            {
                Debug.Log("[UI-IK] screw replay blocked: " + reason);
                Refresh();
                return;
            }
            _handle.StartScrewReplay();
            Refresh();
        }

        private void OnToggleMarkers()
        {
            Resolve();
            _markersOn = !_markersOn;
            if (_overlay != null) _overlay.SetMarkersVisible(_markersOn);
            Refresh();
        }

        private bool _tmpFitted;
        public override void Refresh()
        {
            Resolve();
            if (!_tmpFitted && View != null)
            {
                _tmpFitted = true;
                foreach (var tt in View.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (tt == null) continue;
                    tt.enableAutoSizing = true;
                    tt.fontSizeMin = 7f;
                    tt.fontSizeMax = Mathf.Max(8f, tt.fontSize);
                    tt.enableWordWrapping = false;
                    tt.overflowMode = TextOverflowModes.Ellipsis;
                }
            }

            if (_handle == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 TwinPlayIkHandle");
                _status.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _reachable.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _limits.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _workspace.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _posErr.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _rotErr.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _iterations.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                UpdateGateRow();
                UpdateGhostRow();
                UpdateVrdRow();
                UpdateButtonLabels();
                return;
            }

            bool dragging = _handle.Dragging;
            bool reachable = _handle.Reachable;
            bool limitsOk = _handle.JointLimitsOk;
            bool clamped = _handle.WorkspaceClamped;
            TwinUITone tone = !reachable || !limitsOk ? TwinUITone.Crit : clamped ? TwinUITone.Warn : TwinUITone.Ok;
            _badge.Set(dragging ? TwinUITone.Ok : TwinUITone.Unknown, dragging ? "拖动中" : "待机");

            _status.Set(string.IsNullOrEmpty(_handle.IkStatusText) ? TwinUITextR3.Placeholder : _handle.IkStatusText,
                tone);
            _reachable.Set(reachable ? "可达" : "不可达", reachable ? TwinUITone.Ok : TwinUITone.Crit);
            if (_handle.LimitStop)
                _limits.Set("顶到 ±30°/轴", TwinUITone.Warn);
            else
                _limits.Set(limitsOk ? "在限位内" : "超限位", limitsOk ? TwinUITone.Ok : TwinUITone.Crit);
            _workspace.Set(clamped ? "已钳制" : "在范围内", clamped ? TwinUITone.Warn : TwinUITone.Ok);
            _posErr.Set((_handle.PositionErrorMeters * 1000f).ToString("F1") + " mm",
                _handle.PositionErrorMeters > 0.02f ? TwinUITone.Warn : TwinUITone.Ok);
            _rotErr.Set(_handle.RotationErrorDeg.ToString("F2") + " °",
                _handle.RotationErrorDeg > 5f ? TwinUITone.Warn : TwinUITone.Ok);
            _iterations.Set(_handle.Iterations.ToString(), TwinUITone.Ok);
            UpdateGateRow();
            UpdateGhostRow();
            UpdateVrdRow();
            _note.color = _handle.AllowRobotDispatch ? TwinUITheme.ToneTextColor(TwinUITone.Crit) : TwinUIStyle.QuietColor;

            if (_btnExecute != null)
            {
                bool canExec = !_handle.GhostExecuting && _handle.GhostVisible &&
                               (_handle.GhostSnapEnabled || _handle.HasSolution) &&
                               !(_handle.VrdEnabled && _handle.VrdBlocked);
                _btnExecute.SetInteractable(canExec);
            }
            if (_btnScrew != null)
            {
                string reason;
                bool canReplay = _handle.CanStartScrewReplay(out reason);
                _btnScrew.SetInteractable(canReplay || _handle.ScrewReplayPlaying);
                _btnScrew.Label.text = _handle.ScrewReplayPlaying
                    ? ("拆解中 " + Mathf.RoundToInt(_handle.ScrewReplayProgress * 100f) + "%")
                    : "Screw 拆解";
            }

            UpdateButtonLabels();
        }

        /// <summary>第 4 章工艺/工具门禁只读行：放行显示绑定点位，拒绝显示中文原因（短标签，避免窄栏溢出）。</summary>
        /// <summary>Ghost 状态行：自由拖动 / 已吸附: X / 执行中 n% / 未启用。</summary>
        /// <summary>VR_D 行：OFF / ON · 面高 ±x.x mm / 拦截原因。</summary>
        private void UpdateVrdRow()
        {
            if (_vrd.Value == null) return;
            if (_handle == null || !_handle.VrdEnabled)
            {
                _vrd.Set("OFF", TwinUITone.Unknown);
                return;
            }
            string off = _handle.VrdPlaneOffsetMm.ToString("+0.0;-0.0;0.0") + " mm";
            if (_handle.VrdBlocked)
            {
                string code = _handle.VrdReasonCode == "BELOW_SAFETY_PLANE" ? "低于平面" : "门禁拦截";
                _vrd.Set(code + " · " + off, TwinUITone.Crit);
                return;
            }
            _vrd.Set("ON · " + off, TwinUITone.Ok);
        }

        private void UpdateGhostRow()
        {
            if (_ghost.Value == null) return;
            if (_handle == null || !_handle.GhostVisible)
            {
                _ghost.Set("未启用", TwinUITone.Unknown);
                return;
            }
            if (_handle.GhostExecuting)
            {
                _ghost.Set("执行中 " + Mathf.RoundToInt(_handle.GhostExecProgress * 100f) + "%", TwinUITone.Warn);
                return;
            }
            if (_handle.SnapActive && !string.IsNullOrEmpty(_handle.SnapWaypointName))
            {
                _ghost.Set("已吸附: " + _handle.SnapWaypointName, TwinUITone.Ok);
                return;
            }
            _ghost.Set("自由拖动", TwinUITone.Ok);
        }

        private void UpdateGateRow()
        {
            if (_gate.Value == null) return;

            if (!ProcessStepGate.HasResult)
            {
                _gate.Set("未评估", TwinUITone.Unknown);
                return;
            }

            ProcessGateResult r = ProcessStepGate.LastResult;
            if (r.Allowed)
            {
                if (r.ReasonCode == ProcessStepGate.ReasonOk && !string.IsNullOrEmpty(r.MatchedWaypointName))
                    _gate.Set("放行 · " + r.MatchedWaypointName, TwinUITone.Ok);
                else if (r.ReasonCode == ProcessStepGate.ReasonFreeSpace)
                    _gate.Set("安全空域", TwinUITone.Ok);
                else
                    _gate.Set("放行", TwinUITone.Ok);
                return;
            }

            string shortReason;
            if (r.ReasonCode == ProcessStepGate.ReasonPrereqUnfinished) shortReason = "前置工步未完成";
            else if (r.ReasonCode == ProcessStepGate.ReasonToolMismatch) shortReason = "工具不匹配";
            else if (string.IsNullOrEmpty(r.ReasonCode)) shortReason = "拒绝";
            else shortReason = r.ReasonCode;
            _gate.Set(shortReason, TwinUITone.Crit);
        }

        private void UpdateButtonLabels()
        {
            if (_btnDrag != null)
                _btnDrag.Label.text = ("IK 拖拽: " + (_handle != null && _handle.EnableHandle ? "ON" : "OFF"));
            if (_btnLimit != null)
                _btnLimit.Label.text = ("限幅 ±30°: " + (_limitOn ? "ON" : "OFF"));
            if (_btnTcp != null)
                _btnTcp.Label.text = ("TCP: " + (_tcp != null && !string.IsNullOrEmpty(_tcp.ActiveTcpName) ? (_tcp.ActiveTcpName.StartsWith("Flange") ? "F" : "S") : "S"));
            if (_btnAxes != null)
                _btnAxes.Label.text = ("坐标轴: " + (_axis != null && _axis.Visible ? "ON" : "OFF"));
            if (_btnMarkers != null)
                _btnMarkers.Label.text = ("点位标注: " + (_markersOn ? "ON" : "OFF"));
            if (_btnVrd != null)
                _btnVrd.Label.text = ("VR_D: " + (_handle != null && _handle.VrdEnabled ? "ON" : "OFF"));
        }
    }
}
