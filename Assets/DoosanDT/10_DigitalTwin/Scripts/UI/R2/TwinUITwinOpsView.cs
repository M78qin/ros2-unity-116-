using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 主面板 B：孪生监控台。内部四个子页：概览 / 趋势 / 控制 / 记录。
    /// 数据全部只读自 TwinUIStatusBinder；控制子页只调用既有 TwinCommandController 入口，
    /// 不新增下发路径（真机开关打开时本面板拒绝执行）。
    /// </summary>
    public sealed class TwinUITwinOpsView : MonoBehaviour
    {
        [SerializeField] private TwinUIStatusBinder binder;
        [SerializeField] private float refreshHz = 5f;
        [SerializeField] private float chartHz = 20f;
        [SerializeField] private int speedPercent = 10;

        private readonly GameObject[] _pages = new GameObject[4];
        private readonly TwinUIButton[] _subButtons = new TwinUIButton[4];
        private int _sub;
        private bool _resolved;
        private float _nextRefresh;
        private float _nextChart;
        private float _chartTime;

        // 概览
        private TMP_Text _dartName, _dartValue; private Image _dartDot;
        private TMP_Text _rosName, _rosValue; private Image _rosDot;
        private readonly Image[] _chainBars = new Image[4];
        private readonly TMP_Text[] _chainTexts = new TMP_Text[4];
        private readonly TMP_Text[] _metricValues = new TMP_Text[9];
        private readonly TMP_Text[] _alarmTexts = new TMP_Text[6];
        private readonly Image[] _alarmDots = new Image[6];

        // 趋势
        private TwinTelemetryChart _chartPos, _chartTorque, _chartWrench;

        // 控制
        private readonly TMP_Text[] _checkTexts = new TMP_Text[9];
        private readonly Image[] _checkDots = new Image[9];
        private TMP_Text _modeValue, _logValue;
        private TwinUIButton _confirmButton, _haltButton;
        private bool _armed;
        private float _armedUntil;

        // 记录
        private readonly TMP_Text[] _recordValues = new TMP_Text[7];

        public void Bind(TwinUIStatusBinder value)
        {
            binder = value;
            _resolved = false;
            Resolve();
            SetSub(0);
            Refresh();
        }

        private void Awake()
        {
            Resolve();
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            if (binder == null)
            {
                binder = GetComponentInParent<TwinUIStatusBinder>();
            }

            string[] pageNames = { "PageOverview", "PageTrend", "PageControl", "PageRecord" };
            for (int i = 0; i < 4; i++)
            {
                var p = transform.Find("SubNav/Sub" + i);
                if (p != null)
                {
                    _subButtons[i] = p.GetComponent<TwinUIButton>();
                    int index = i;
                    if (_subButtons[i] != null && _subButtons[i].Button != null)
                    {
                        _subButtons[i].Button.onClick.RemoveAllListeners();
                        _subButtons[i].Button.onClick.AddListener(() => SetSub(index));
                    }
                }

                var page = transform.Find(pageNames[i]);
                _pages[i] = page == null ? null : page.gameObject;
            }

            _dartName = T("PageOverview/Source/Dart/Name");
            _dartValue = T("PageOverview/Source/Dart/Value");
            _dartDot = I("PageOverview/Source/Dart/Dot");
            _rosName = T("PageOverview/Source/Ros2/Name");
            _rosValue = T("PageOverview/Source/Ros2/Value");
            _rosDot = I("PageOverview/Source/Ros2/Dot");

            for (int i = 0; i < 4; i++)
            {
                _chainBars[i] = I("PageOverview/Chain/Step" + i + "/Bar");
                _chainTexts[i] = T("PageOverview/Chain/Step" + i + "/Value");
            }

            string[] metricNames =
            {
                "Rate", "Drop", "Reorder", "Queue", "Jitter", "Latency", "Quality", "Clock", "Mode"
            };
            for (int i = 0; i < metricNames.Length; i++)
            {
                _metricValues[i] = T("PageOverview/Metrics/" + metricNames[i] + "/Value");
            }

            for (int i = 0; i < 6; i++)
            {
                _alarmTexts[i] = T("PageOverview/Alarms/Row" + i + "/Text");
                _alarmDots[i] = I("PageOverview/Alarms/Row" + i + "/Dot");
            }

            _chartPos = GetChart("PageTrend/ChartJointPos");
            _chartTorque = GetChart("PageTrend/ChartJointTorque");
            _chartWrench = GetChart("PageTrend/ChartWrench");

            string[] checkNames =
            {
                "DryRun", "RealCmd", "Bidirectional", "CanSend", "Frame", "Arm", "Estop", "Safety", "Clock"
            };
            for (int i = 0; i < checkNames.Length; i++)
            {
                _checkTexts[i] = T("PageControl/Checks/" + checkNames[i] + "/Text");
                _checkDots[i] = I("PageControl/Checks/" + checkNames[i] + "/Dot");
            }

            _modeValue = T("PageControl/Mode/Value");
            _logValue = T("PageControl/Log/Value");
            _confirmButton = FindButton("PageControl/Actions/BtnConfirm");
            _haltButton = FindButton("PageControl/Actions/BtnHalt");

            Wire("PageControl/Actions/BtnPreview", Preview);
            Wire("PageControl/Actions/BtnCheck", Check);
            Wire("PageControl/Actions/BtnConfirm", Confirm);
            Wire("PageControl/Actions/BtnExecute", Execute);
            Wire("PageControl/Actions/BtnHalt", Halt);
            Wire("PageControl/Speed/0", () => SetSpeed(5));
            Wire("PageControl/Speed/1", () => SetSpeed(10));
            Wire("PageControl/Speed/2", () => SetSpeed(25));

            string[] recordNames = { "Recording", "Pending", "Dropped", "Csv", "Dir", "Session", "Experiment" };
            for (int i = 0; i < recordNames.Length; i++)
            {
                _recordValues[i] = T("PageRecord/Rows/" + recordNames[i] + "/Value");
            }
        }

        private TwinTelemetryChart GetChart(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<TwinTelemetryChart>();
        }

        private void Wire(string path, UnityEngine.Events.UnityAction action)
        {
            var t = transform.Find(path);
            if (t == null)
            {
                return;
            }

            var b = t.GetComponent<Button>();
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(action);
            }
        }

        private TwinUIButton FindButton(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<TwinUIButton>();
        }

        public void SetSub(int index)
        {
            if (!_resolved || _pages[0] == null || _pages[1] == null || _pages[2] == null || _pages[3] == null)
            {
                _resolved = false;
                Resolve();
            }

            _sub = Mathf.Clamp(index, 0, 3);
            for (int i = 0; i < 4; i++)
            {
                if (_pages[i] != null)
                {
                    _pages[i].SetActive(i == _sub);
                }

                if (_subButtons[i] != null)
                {
                    _subButtons[i].SetSelected(i == _sub);
                }
            }

            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _armedUntil && _armed)
            {
                _armed = false;
                if (_confirmButton != null)
                {
                    _confirmButton.SetArmed(false).SetLabel(TwinUITextR2.BtnConfirm);
                }
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 1f / Mathf.Max(0.5f, refreshHz);
                Refresh();
            }

            if (_sub == 1 && Time.unscaledTime >= _nextChart)
            {
                _nextChart = Time.unscaledTime + 1f / Mathf.Max(1f, chartHz);
                PushCharts();
            }
        }

        private void PushCharts()
        {
            if (binder == null)
            {
                return;
            }

            var s = binder.Current;
            _chartTime += 1f / chartHz;
            if (!s.HasFrame || s.JointPos == null)
            {
                return;
            }

            for (int i = 0; i < 6 && i < s.JointPos.Length; i++)
            {
                if (_chartPos != null)
                {
                    _chartPos.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.Joint1 + i),
                        _chartTime, s.JointPos[i] * Mathf.Rad2Deg);
                }
            }

            if (s.JointTorque != null)
            {
                for (int i = 0; i < 6 && i < s.JointTorque.Length; i++)
                {
                    if (_chartTorque != null)
                    {
                        _chartTorque.Push((TwinTelemetryChannel)((int)TwinTelemetryChannel.JointTorque1 + i),
                            _chartTime, s.JointTorque[i]);
                    }
                }
            }

            if (s.Force != null)
            {
                for (int i = 0; i < 6 && i < s.Force.Length; i++)
                {
                    if (_chartWrench != null)
                    {
                        var channel = i < 3
                            ? (TwinTelemetryChannel)((int)TwinTelemetryChannel.ForceX + i)
                            : (TwinTelemetryChannel)((int)TwinTelemetryChannel.TorqueX + (i - 3));
                        _chartWrench.Push(channel, _chartTime, s.Force[i]);
                    }
                }
            }

            if (_chartPos != null && !_chartPos.HasData)
            {
                _chartPos.SetHasData(true);
            }

            if (_chartTorque != null && !_chartTorque.HasData)
            {
                _chartTorque.SetHasData(true);
            }

            if (_chartWrench != null && !_chartWrench.HasData)
            {
                _chartWrench.SetHasData(true);
            }
        }

        public void Refresh()
        {
            if (binder == null)
            {
                return;
            }

            var s = binder.Current;

            // 概览：数据源
            SetText(_dartName, "DART");
            SetText(_dartValue, s.SourceKind == "Dart" || s.SourceKind == "DartStudio"
                ? (s.Connected ? "在线" : TwinUITextR2.NoConnection)
                : TwinUITextR2.NoConnection);
            DotColor(_dartDot, s.Connected && (s.SourceKind == "Dart" || s.SourceKind == "DartStudio"));

            SetText(_rosName, "ROS2");
            SetText(_rosValue, s.SourceKind == "Ros2"
                ? (s.Connected ? "在线" : TwinUITextR2.NoConnection)
                : TwinUITextR2.NoConnection);
            DotColor(_rosDot, s.Connected && s.SourceKind == "Ros2");

            // 数据链四段
            bool source = s.Connected;
            bool transport = s.Hz > 0.01;
            bool apply = s.HasFrame && s.LatencyMs < 50;
            bool model = s.HasFrame && s.AgeMs > 0 && s.AgeMs < 500;
            Chain(0, source, s.Hz > 0.01 ? s.Hz.ToString("F1") + " Hz" : "—");
            Chain(1, transport, s.LatencyMs > 0 ? s.LatencyMs.ToString("F1") + " ms" : "—");
            Chain(2, apply, s.LatencyMs.ToString("F1") + " ms");
            Chain(3, model, s.AgeMs.ToString("F0") + " ms");

            // 指标行
            SetText(_metricValues[0], s.Hz > 0.01 ? s.Hz.ToString("F1") + " Hz" : "—");
            SetText(_metricValues[1], s.Dropped.ToString());
            SetText(_metricValues[2], s.OutOfOrder.ToString());
            SetText(_metricValues[3], s.Queue.ToString());
            SetText(_metricValues[4], s.JitterMs.ToString("F1") + " ms");
            SetText(_metricValues[5], s.HasFrame ? s.LatencyMs.ToString("F1") + " ms" : "—");
            SetText(_metricValues[6], string.IsNullOrEmpty(s.Quality) ? "—" : s.Quality);
            SetText(_metricValues[7], string.IsNullOrEmpty(s.Clock) ? "—" : s.Clock);
            SetText(_metricValues[8], string.IsNullOrEmpty(s.Mode) ? "—" : s.Mode);

            // 告警
            int count = binder.Alarms == null ? 0 : binder.Alarms.Count;
            for (int i = 0; i < _alarmTexts.Length; i++)
            {
                if (i < count)
                {
                    var a = binder.Alarms[i];
                    SetText(_alarmTexts[i], a.Text);
                    if (_alarmTexts[i] != null)
                    {
                        _alarmTexts[i].color = a.Level == TwinUIStatusBinder.Severity.Crit ? TwinUIStyle.CritColor
                            : a.Level == TwinUIStatusBinder.Severity.Warn ? TwinUIStyle.WarnColor : TwinUIStyle.InfoColor;
                    }

                    DotColor(_alarmDots[i], a.Level == TwinUIStatusBinder.Severity.Crit ? TwinUIStyle.CritColor
                        : a.Level == TwinUIStatusBinder.Severity.Warn ? TwinUIStyle.WarnColor : TwinUIStyle.InfoColor);
                }
                else
                {
                    SetText(_alarmTexts[i], i == 0 ? TwinUITextR2.AlarmNone : string.Empty);
                    if (_alarmTexts[i] != null)
                    {
                        _alarmTexts[i].color = i == 0 ? TwinUIStyle.OkColor : TwinUIStyle.MutedColor;
                    }

                    DotColor(_alarmDots[i], i == 0 ? TwinUIStyle.OkColor : TwinUIStyle.Transparent);
                }
            }

            // 控制
            SetText(_modeValue, s.ControlMode);
            SetCheck(0, TwinUITextR2.ChkDryRun, s.DryRun, true);
            SetCheck(1, TwinUITextR2.ChkRealCmd, !s.RealCommand, !s.RealCommand);
            SetCheck(2, TwinUITextR2.ChkBidirectional, s.Bidirectional, true);
            SetCheck(3, TwinUITextR2.ChkCanSend, s.CanSend, s.CanSend);
            SetCheck(4, TwinUITextR2.ChkFrame, s.HasFrame, true);
            SetCheck(5, TwinUITextR2.ChkArm, _armed, true);
            SetCheck(6, TwinUITextR2.ChkEstop, !s.Estop, !s.Estop);
            SetCheck(7, TwinUITextR2.ChkSafetyChain, s.CanSend || !s.RealCommand, true);
            SetCheck(8, TwinUITextR2.ChkClock, s.Clock == "Synced", true);

            string log = "最近结果：" + s.LastStatus;
            if (!string.IsNullOrEmpty(s.LastCommandError))
            {
                log += "\n错误：" + s.LastCommandError;
            }

            if (_logValue != null)
            {
                _logValue.text = log;
            }

            if (_haltButton != null)
            {
                _haltButton.SetLabel(s.Estop ? "解除急停" : TwinUITextR2.BtnHalt)
                    .SetSelected(s.Estop);
            }

            // 记录
            SetText(_recordValues[0], s.Recording ? TwinUITextR2.RecOn : TwinUITextR2.RecOff);
            SetText(_recordValues[1], s.Pending.ToString());
            SetText(_recordValues[2], s.RecDropped.ToString());
            SetText(_recordValues[3], s.CsvReady ? "是" : "否");
            SetText(_recordValues[4], string.IsNullOrEmpty(s.CsvDir) ? "—" : Trim(s.CsvDir, 34));
            SetText(_recordValues[5], string.IsNullOrEmpty(s.SessionId) ? "—" : s.SessionId);
            SetText(_recordValues[6], Trim(s.SourceKind + " / " + (s.HasFrame ? s.Mode : "—"), 26));
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value;
            }

            return "…" + value.Substring(value.Length - max + 1);
        }

        private void Chain(int index, bool ok, string value)
        {
            if (_chainBars[index] != null)
            {
                _chainBars[index].color = ok ? TwinUIStyle.OkColor : TwinUIStyle.CritColor;
            }

            SetText(_chainTexts[index], value);
        }

        private void SetCheck(int index, string label, bool ok, bool neutral)
        {
            SetText(_checkTexts[index], label);
            Color c = neutral ? (ok ? TwinUIStyle.OkColor : TwinUIStyle.WarnColor) : TwinUIStyle.MutedColor;
            if (_checkTexts[index] != null)
            {
                _checkTexts[index].color = ok ? TwinUIStyle.TextColor : TwinUIStyle.MutedColor;
            }

            DotColor(_checkDots[index], c);
        }

        private void SetSpeed(int percent)
        {
            speedPercent = percent;
        }

        private TwinCommandController Controller
        {
            get { return binder != null ? binder.Controller : null; }
        }

        private DigitalTwinRuntime Runtime
        {
            get { return binder != null ? binder.Runtime : null; }
        }

        private void Preview()
        {
            var c = Controller;
            if (c == null)
            {
                Log("未找到控制组件");
                return;
            }

            c.EnterPlan();
            Log("已进入规划态");
        }

        private void Check()
        {
            var s = binder != null ? binder.Current : default(TwinUIStatusBinder.Snapshot);
            Log("干运行=" + (s.DryRun ? "开" : "关") + " 真机=" + (s.RealCommand ? "开" : "关")
                + " 可下发=" + (s.CanSend ? "是" : "否") + " 实时帧=" + (s.HasFrame ? "有" : "无"));
        }

        private void Confirm()
        {
            if (_armed)
            {
                _armed = false;
                _armedUntil = 0f;
                Log("已取消确认");
            }
            else
            {
                _armed = true;
                _armedUntil = Time.unscaledTime + 3f;
                Log("已武装：3 秒内点执行");
            }

            if (_confirmButton != null)
            {
                _confirmButton.SetArmed(_armed).SetLabel(_armed ? "已确认" : TwinUITextR2.BtnConfirm);
            }
        }

        private void Execute()
        {
            var c = Controller;
            if (c == null)
            {
                Log("未找到控制组件");
                return;
            }

            if (c.EnableRealRobotCommand && c.CanSendRealMove)
            {
                Log("拒绝执行：呈现层不下发真机指令");
                return;
            }

            if (!_armed)
            {
                Log("请先点确认（二次武装）");
                return;
            }

            _armed = false;
            if (_confirmButton != null)
            {
                _confirmButton.SetArmed(false).SetLabel(TwinUITextR2.BtnConfirm);
            }

            RobotStateFrame frame;
            var runtime = Runtime;
            if (runtime == null || !runtime.TryGetLatestFrame(out frame) || frame.JointPositionRad == null)
            {
                Log("没有实时帧，拒绝执行");
                return;
            }

            var result = c.SendMoveJoint(frame.JointPositionRad, speedPercent);
            Log(result.Status + (string.IsNullOrEmpty(result.ErrorMessage) ? string.Empty : " / " + result.ErrorMessage));
        }

        private void Halt()
        {
            var c = Controller;
            if (c == null)
            {
                Log("未找到控制组件");
                return;
            }

            bool next = !c.IsEmergencyStopped;
            c.SetEmergencyStopped(next);
            Log(next ? "已触发急停" : "已解除急停");
        }

        private void Log(string message)
        {
            if (_logValue != null)
            {
                _logValue.text = message;
            }
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static void DotColor(Image image, bool ok)
        {
            DotColor(image, ok ? TwinUIStyle.OkColor : TwinUIStyle.DisabledColor);
        }

        private static void DotColor(Image image, Color color)
        {
            if (image != null)
            {
                image.color = color;
            }
        }

        private TMP_Text T(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<TMP_Text>();
        }

        private Image I(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<Image>();
        }
    }
}