// 实验上下文条：runId / profile / condition / payload_kg / TCP 定义 / session / Self-Check。
// 值来源只读（回放读 run 目录，运行时读 runtime/binder），缺失一律写"未提供/未校验"，不猜。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public sealed class TwinUiContextBarView : MonoBehaviour
    {
        private TMP_Text[] _values = new TMP_Text[7];
        private TMP_Text _sourceTag;
        private TMP_Text _watermark;
        private Image _selfCheckDot;
        private Image _sourceDot;
        private bool _resolved;

        public void Render(TwinUiSnapshot snapshot, TwinUiSettings settings)
        {
            Resolve();
            var s = snapshot ?? TwinUiSnapshot.Empty(TwinUiSourceKind.None, string.Empty);
            var ctx = s.Experiment ?? new TwinUiExperimentContext();

            Set(0, ctx.RunId);
            Set(1, ctx.Profile);
            Set(2, ctx.Condition);
            Set(3, ctx.PayloadKg);
            Set(4, ctx.TcpDefinition);
            Set(5, ctx.SessionId);
            Set(6, ctx.SelfCheckStatus);

            if (_selfCheckDot != null)
            {
                _selfCheckDot.color = ctx.SelfCheckPassed ? TwinUIStyle.OkColor : TwinUIStyle.WarnColor;
            }

            if (_sourceTag != null)
            {
                string label = s.SourceLabel;
                if (settings != null && settings.uiRefreshHz > 0f)
                {
                    label += " · UI " + settings.uiRefreshHz.ToString("F0") + " Hz";
                }

                _sourceTag.text = label;
                _sourceTag.color = s.IsMock ? TwinUIStyle.WarnColor : TwinUIStyle.AccentColor;
            }

            if (_sourceDot != null)
            {
                _sourceDot.color = s.IsMock ? TwinUIStyle.WarnColor : TwinUIStyle.AccentColor;
            }

            if (_watermark != null)
            {
                _watermark.text = s.IsMock ? TwinUITextR3.MockWatermark : string.Empty;
            }
        }

        private void Set(int index, string value)
        {
            if (_values[index] == null)
            {
                return;
            }

            _values[index].text = string.IsNullOrEmpty(value) ? TwinUITextR3.CtxNotProvided : value;
            _values[index].color = string.IsNullOrEmpty(value) || value == TwinUITextR3.CtxNotProvided
                ? TwinUIStyle.MutedColor : TwinUIStyle.TextColor;
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            string[] names = { "Run", "Profile", "Condition", "Payload", "Tcp", "Session", "SelfCheck" };
            for (int i = 0; i < names.Length; i++)
            {
                var t = transform.Find("Chip_" + names[i] + "/Value");
                _values[i] = t == null ? null : t.GetComponent<TMP_Text>();
            }

            var tag = transform.Find("SourceTag/Text");
            _sourceTag = tag == null ? null : tag.GetComponent<TMP_Text>();
            var dot = transform.Find("SourceTag/Dot");
            _sourceDot = dot == null ? null : dot.GetComponent<Image>();
            var wm = transform.Find("Watermark");
            _watermark = wm == null ? null : wm.GetComponent<TMP_Text>();
            var sc = transform.Find("Chip_SelfCheck/Dot");
            _selfCheckDot = sc == null ? null : sc.GetComponent<Image>();
        }
    }
}