// 事件/告警流视图：左列是总线最近事件（帧/命令/告警/实验），右列是告警统计与能力位。
// 事件来自 TwinUiEventBus（内存 + Logs/ui_events.jsonl），能力位来自快照的 Capabilities。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    public sealed class TwinUiEventStreamView : MonoBehaviour
    {
        private const int Rows = 14;
        private const int SummaryRows = 12;

        private TMP_Text[] _rowTime = new TMP_Text[Rows];
        private TMP_Text[] _rowLevel = new TMP_Text[Rows];
        private TMP_Text[] _rowCode = new TMP_Text[Rows];
        private TMP_Text[] _rowText = new TMP_Text[Rows];
        private TMP_Text[] _summaryLabel = new TMP_Text[SummaryRows];
        private TMP_Text[] _summaryValue = new TMP_Text[SummaryRows];
        private TMP_Text _note;
        private bool _resolved;

        public void Render(TwinUiPipeline pipeline, TwinUiEventBus bus, TwinUiSnapshotExport export, TwinUiSettings settings)
        {
            Resolve();
            var snapshot = pipeline != null ? pipeline.Snapshot : null;
            RenderRows(bus);
            RenderSummary(pipeline, bus, export, snapshot, settings);
        }

        private void RenderRows(TwinUiEventBus bus)
        {
            int count = bus == null ? 0 : bus.Count;
            for (int i = 0; i < Rows; i++)
            {
                if (_rowTime[i] == null)
                {
                    continue;
                }

                int index = count - 1 - i;
                if (index < 0)
                {
                    Set(_rowTime[i], string.Empty, TwinUIStyle.MutedColor);
                    Set(_rowLevel[i], string.Empty, TwinUIStyle.MutedColor);
                    Set(_rowCode[i], string.Empty, TwinUIStyle.MutedColor);
                    Set(_rowText[i], i == 0 ? "（暂无事件）" : string.Empty, TwinUIStyle.MutedColor);
                    continue;
                }

                var e = bus.Recent[index];
                Set(_rowTime[i], e.TimeLocal, TwinUIStyle.MutedColor);
                Set(_rowLevel[i], LevelText(e.Tone), TwinUiFormat.ToneTextColor(e.Tone));
                Set(_rowCode[i], e.Code, TwinUIStyle.MutedColor);
                Set(_rowText[i], e.Text, ToneOrText(e.Tone));
            }
        }

        private void RenderSummary(TwinUiPipeline pipeline, TwinUiEventBus bus, TwinUiSnapshotExport export,
            TwinUiSnapshot snapshot, TwinUiSettings settings)
        {
            int warn = 0;
            int crit = 0;
            int unknown = 0;
            if (pipeline != null && pipeline.Alerts != null)
            {
                for (int i = 0; i < pipeline.Alerts.Count; i++)
                {
                    var a = pipeline.Alerts[i];
                    if (a.Tone == (int)TwinUITone.Crit)
                    {
                        crit++;
                    }
                    else if (a.Tone == (int)TwinUITone.Warn)
                    {
                        warn++;
                    }
                    else
                    {
                        unknown++;
                    }
                }
            }

            int caps = snapshot == null ? 0 : snapshot.Capabilities;
            SetPair(0, TwinUITextR3.RowWarnCount, warn.ToString(), warn > 0 ? TwinUIStyle.WarnColor : TwinUIStyle.OkColor);
            SetPair(1, TwinUITextR3.RowCritCount, crit.ToString(), crit > 0 ? TwinUIStyle.CritColor : TwinUIStyle.OkColor);
            SetPair(2, TwinUITextR3.RowEventTotal, (bus == null ? 0 : bus.Total).ToString(), TwinUIStyle.TextColor);
            SetPair(3, TwinUITextR3.RowEventFile, bus == null ? TwinUiFormat.Dash : ShortPath(bus.FilePath), TwinUIStyle.MutedColor);
            SetPair(4, TwinUITextR3.RowSnapshotFile, export == null ? TwinUiFormat.Dash : ShortPath(export.FilePath),
                TwinUIStyle.MutedColor);
            SetPair(5, TwinUITextR3.RowSourceKind,
                snapshot == null ? TwinUITextR3.NoSource : snapshot.SourceLabel, TwinUIStyle.AccentColor);
            SetPair(6, TwinUITextR3.CapTcp, CapText(caps, TwinUiCapability.TcpPose), CapTone(caps, TwinUiCapability.TcpPose));
            SetPair(7, TwinUITextR3.CapTorque, CapText(caps, TwinUiCapability.JointTorque),
                CapTone(caps, TwinUiCapability.JointTorque));
            SetPair(8, TwinUITextR3.CapForce, CapText(caps, TwinUiCapability.Force), CapTone(caps, TwinUiCapability.Force));
            SetPair(9, TwinUITextR3.CapVelocity, CapText(caps, TwinUiCapability.JointVelocity),
                CapTone(caps, TwinUiCapability.JointVelocity));
            SetPair(10, TwinUITextR3.CtxSelfCheck,
                snapshot == null || snapshot.Experiment == null ? TwinUiFormat.Dash : snapshot.Experiment.SelfCheckStatus,
                TwinUIStyle.MutedColor);
            SetPair(11, "未订阅告警", unknown.ToString(), unknown > 0 ? TwinUIStyle.MutedColor : TwinUIStyle.OkColor);

            if (_note != null)
            {
                _note.text = "事件同时追加写 JSONL；本轮 UI " + (settings != null ? settings.uiRefreshHz.ToString("F0") : "?")
                    + " Hz，快照导出 " + (settings != null ? settings.snapshotExportHz.ToString("F0") : "?") + " Hz";
                _note.color = TwinUIStyle.MutedColor;
            }
        }

        private static string ShortPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return TwinUiFormat.Dash;
            }

            int i = path.LastIndexOf('/');
            int j = path.LastIndexOf('\\');
            int cut = Mathf.Max(i, j);
            return cut >= 0 ? path.Substring(cut + 1) : path;
        }

        private static string CapText(int caps, int bit)
        {
            return TwinUiCapability.Has(caps, bit) ? TwinUITextR3.CapOn : TwinUITextR3.CapOff;
        }

        private static Color CapTone(int caps, int bit)
        {
            return TwinUiCapability.Has(caps, bit) ? TwinUIStyle.OkColor : TwinUIStyle.WarnColor;
        }

        private static string LevelText(TwinUITone tone)
        {
            switch (tone)
            {
                case TwinUITone.Crit: return "CRIT";
                case TwinUITone.Warn: return "WARN";
                case TwinUITone.Ok: return "OK";
                default: return "INFO";
            }
        }

        private static Color ToneOrText(TwinUITone tone)
        {
            return tone == TwinUITone.Crit ? TwinUIStyle.CritTextColor
                : tone == TwinUITone.Warn ? TwinUIStyle.WarnColor
                : tone == TwinUITone.Ok ? TwinUIStyle.TextColor : TwinUIStyle.MutedColor;
        }

        private void SetPair(int i, string label, string value, Color color)
        {
            if (i < 0 || i >= SummaryRows)
            {
                return;
            }

            if (_summaryLabel[i] != null)
            {
                _summaryLabel[i].text = label;
                _summaryLabel[i].color = TwinUIStyle.MutedColor;
            }

            if (_summaryValue[i] != null)
            {
                _summaryValue[i].text = value;
                _summaryValue[i].color = color;
            }
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            for (int i = 0; i < Rows; i++)
            {
                string p = "EventList/Row" + i + "/";
                _rowTime[i] = T(p + "Time");
                _rowLevel[i] = T(p + "Level");
                _rowCode[i] = T(p + "Code");
                _rowText[i] = T(p + "Text");
            }

            for (int i = 0; i < SummaryRows; i++)
            {
                string p = "SummaryCard/Row" + i + "/";
                _summaryLabel[i] = T(p + "Label");
                _summaryValue[i] = T(p + "Value");
            }

            _note = T("SummaryCard/Note");
        }

        private static void Set(TMP_Text text, string value, Color color)
        {
            if (text == null)
            {
                return;
            }

            if (text.text != value)
            {
                text.text = value;
            }

            text.color = color;
        }

        private TMP_Text T(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<TMP_Text>();
        }
    }
}