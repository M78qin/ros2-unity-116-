using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    /// <summary>
    /// 常驻顶栏：数据源 / 帧率 / 时延 / 帧龄 四个 KPI + 告警 / 记录 / 急停 三个状态芯片 + 时钟。
    /// 子节点按名字解析（构建器负责结构），数值只读自 TwinUIStatusBinder。
    /// </summary>
    public sealed class TwinUIHeaderView : MonoBehaviour
    {
        [SerializeField] private TwinUIStatusBinder binder;
        [SerializeField] private float refreshHz = 2f;

        private TMP_Text _modeText;
        private TMP_Text _sourceValue;
        private TMP_Text _rateValue;
        private TMP_Text _latencyValue;
        private TMP_Text _ageValue;
        private TMP_Text _alarmText;
        private TMP_Text _recText;
        private TMP_Text _estopText;
        private TMP_Text _clockText;
        private Image _alarmDot;
        private Image _recDot;
        private Image _estopDot;
        private Image _modeBar;
        private bool _resolved;
        private float _next;

        public void Bind(TwinUIStatusBinder value, bool mission)
        {
            binder = value;
            _resolved = false;
            Resolve();
            SetMode(mission);
            Refresh();
        }

        public void SetMode(bool mission)
        {
            Resolve();
            if (_modeText != null)
            {
                _modeText.text = mission ? TwinUITextR2.ModeMission : TwinUITextR2.ModeTwinOps;
            }

            if (_modeBar != null)
            {
                _modeBar.color = mission ? TwinUIStyle.AccentColor : TwinUIStyle.InfoColor;
            }
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

            _modeText = T("ModeChip/Text");
            _sourceValue = T("KpiSource/Value");
            _rateValue = T("KpiRate/Value");
            _latencyValue = T("KpiLatency/Value");
            _ageValue = T("KpiAge/Value");
            _alarmText = T("AlarmBadge/Text");
            _recText = T("RecBadge/Text");
            _estopText = T("EstopChip/Text");
            _clockText = T("Clock");
            _alarmDot = I("AlarmBadge/Dot");
            _recDot = I("RecBadge/Dot");
            _estopDot = I("EstopChip/Dot");
            _modeBar = I("ModeChip/Bar");
        }

        private void Update()
        {
            if (Time.unscaledTime < _next)
            {
                return;
            }

            _next = Time.unscaledTime + 1f / Mathf.Max(0.5f, refreshHz);
            Refresh();
        }

        public void Refresh()
        {
            if (_modeText == null)
            {
                _resolved = false;
                Resolve();
            }

            if (binder == null)
            {
                return;
            }

            var s = binder.Current;

            Set(_sourceValue, s.Connected ? s.SourceName : TwinUITextR2.NoConnection);
            Set(_rateValue, s.Hz > 0.01 ? s.Hz.ToString("F1") + " Hz" : "—");
            Set(_latencyValue, s.HasFrame ? s.LatencyMs.ToString("F1") + " ms" : "—");
            Set(_ageValue, s.HasFrame ? s.AgeMs.ToString("F0") + " ms" : "—");

            if (_sourceValue != null)
            {
                _sourceValue.color = s.Connected ? TwinUIStyle.TextColor : TwinUIStyle.CritColor;
            }

            if (_ageValue != null)
            {
                _ageValue.color = !s.HasFrame ? TwinUIStyle.MutedColor
                    : s.AgeMs > 2000 ? TwinUIStyle.CritColor
                    : s.AgeMs > 500 ? TwinUIStyle.WarnColor : TwinUIStyle.TextColor;
            }

            var level = TwinUIStatusBinder.Severity.Info;
            if (binder.Alarms != null)
            {
                for (int i = 0; i < binder.Alarms.Count; i++)
                {
                    if (binder.Alarms[i].Level > level)
                    {
                        level = binder.Alarms[i].Level;
                    }
                }
            }

            Set(_alarmText, s.AlarmCount == 0 ? TwinUITextR2.AlarmNone : TwinUITextR2.AlarmPrefix + s.AlarmCount);
            Color alarmColor = s.AlarmCount == 0 ? TwinUIStyle.OkColor
                : level == TwinUIStatusBinder.Severity.Crit ? TwinUIStyle.CritColor
                : level == TwinUIStatusBinder.Severity.Warn ? TwinUIStyle.WarnColor : TwinUIStyle.InfoColor;
            if (_alarmText != null)
            {
                _alarmText.color = alarmColor;
            }

            SetColor(_alarmDot, alarmColor);

            Set(_recText, s.Recording ? TwinUITextR2.RecOn : TwinUITextR2.RecOff);
            SetColor(_recDot, s.Recording ? TwinUIStyle.CritColor : TwinUIStyle.DisabledColor);

            Set(_estopText, s.Estop ? TwinUITextR2.EstopSet : TwinUITextR2.EstopClear);
            if (_estopText != null)
            {
                _estopText.color = s.Estop ? TwinUIStyle.CritColor : TwinUIStyle.OkColor;
            }

            SetColor(_estopDot, s.Estop ? TwinUIStyle.CritColor : TwinUIStyle.OkColor);

            Set(_clockText, System.DateTime.Now.ToString("HH:mm:ss"));
        }

        private static void Set(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static void SetColor(Image image, Color color)
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