// TwinUiWidgetAlarm.cs —— 告警摘要卡片（P8）
// 数据源：TwinUIStatusBinder.Alarms（只读）；无告警显示"无告警"，不显示假告警。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetAlarm : TwinUiModule
    {
        public const string ModuleId = "alarm";
        public const int MaxRows = 4;

        private TwinUIStatusBinder _binder;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Stat _count;
        private readonly TMP_Text[] _rows = new TMP_Text[MaxRows];
        private TMP_Text _empty;
        private int _visibleRows = -1;

        public override string Id => ModuleId;
        public override string Title => "告警";
        public override string Kicker => "ALERTS";
        public override string Subtitle => "StatusBinder";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Overview;
        public override Vector2 CardSize => new Vector2(340f, 226f);
        public override Vector2 FloatingMinSize => new Vector2(300f, 200f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetAlarm");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _count = TwinUiCardParts.StatBlock(root, "活跃告警", 0f, 22f, w);
            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = TwinUIStyle.Text(root, "AlarmRow" + i, string.Empty, TwinUIStyle.FontBodySm,
                    TwinUIStyle.MutedColor);
                TwinUIStyle.Row(_rows[i].rectTransform, 0f, 82f + i * 22f, w, 20f);
                _rows[i].enableWordWrapping = false;
                _rows[i].overflowMode = TextOverflowModes.Ellipsis;
            }

            _empty = TwinUiCardParts.Caption(root, TwinUITextR3.NoData, 0f, 82f, w, 20f);
            return root;
        }

        public override void Refresh()
        {
            if (_binder == null)
            {
                _binder = Object.FindObjectOfType<TwinUIStatusBinder>();
            }

            if (_binder == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 StatusBinder");
                _count.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _empty.gameObject.SetActive(true);
                _empty.text = TwinUITextR3.NotAvailable;
                for (int i = 0; i < MaxRows; i++)
                {
                    _rows[i].gameObject.SetActive(false);
                }

                SyncVisibleRows(0);
                return;
            }

            var alarms = _binder.Alarms;
            int n = alarms != null ? alarms.Count : 0;
            bool hasEvidence = n > 0;
            TwinUITone tone = TwinUITone.Ok;
            for (int i = 0; i < n; i++)
            {
                string level = alarms[i].Level.ToString();
                if (level.IndexOf("Warn", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || level.IndexOf("预警", System.StringComparison.Ordinal) >= 0)
                {
                    tone = TwinUITone.Warn;
                }

                if (level.IndexOf("Crit", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || level.IndexOf("超限", System.StringComparison.Ordinal) >= 0)
                {
                    tone = TwinUITone.Crit;
                    break;
                }
            }

            _badge.Set(hasEvidence ? tone : TwinUITone.Ok, hasEvidence ? "有告警" : "无告警");
            _count.Set(n.ToString(), hasEvidence ? tone : TwinUITone.Ok);

            _empty.gameObject.SetActive(n == 0);
            _empty.text = "当前没有活跃告警（AlarmCount=0）";
            for (int i = 0; i < MaxRows; i++)
            {
                bool show = i < n;
                _rows[i].gameObject.SetActive(show);
                if (!show)
                {
                    continue;
                }

                var a = alarms[i];
                string text = a.Text;
                _rows[i].text = "· " + (string.IsNullOrEmpty(text) ? TwinUITextR3.Placeholder : text);
                _rows[i].color = TwinUITheme.ToneTextColor(
                    a.Level.ToString().StartsWith("Crit") ? TwinUITone.Crit : TwinUITone.Warn);
            }

            SyncVisibleRows(Mathf.Min(n, MaxRows));
        }

        /// <summary>
        /// P11 测量失效：告警行数变化 = 内容结构变化（同宽下卡片高度也要跟着变）。
        /// 只在这里递增一次版本，避免每 4 Hz 刷新都触发整页重排。
        /// </summary>
        private void SyncVisibleRows(int rows)
        {
            if (_visibleRows == rows)
            {
                return;
            }

            _visibleRows = rows;
            InvalidateContentLayout();
        }
    }
}