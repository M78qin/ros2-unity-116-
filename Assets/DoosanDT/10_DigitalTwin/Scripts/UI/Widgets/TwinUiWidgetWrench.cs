// TwinUiWidgetWrench.cs —— 六维力卡片（P8，数字孪生页）
// 数据源：场景内 DigitalTwinRuntime（只读）→ LatestFrame.ForceVector：[Fx,Fy,Fz,Mx,My,Mz]。
// 约定：ForceVector 不存在或长度 < 3 时只显示 未订阅 + 占位符，禁止补 0；不新增订阅、不下发命令。
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetWrench : TwinUiModule
    {
        public const string ModuleId = "wrench";

        private const int AxisCount = 6;
        private const float RowsTop = 24f;
        private const float RowStep = 24f;

        private static readonly string[] AxisLabels = { "Fx", "Fy", "Fz", "Mx", "My", "Mz" };

        private DigitalTwinRuntime _runtime;
        private TwinUIStatusBinder _binder;
        private TwinUiCardParts.Badge _badge;
        private readonly TwinUiCardParts.Kv[] _rows = new TwinUiCardParts.Kv[AxisCount];

        public override string Id => ModuleId;
        public override string Title => "六维力";
        public override string Kicker => "WRENCH";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Twin;
        public override string Group => "tcp";
        public override Vector2 CardSize => new Vector2(300f, 208f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetWrench");
            View = root;

            var content = TwinUIStyle.Frame(root, "Content");
            TwinUIStyle.Stretch(content, 0f, 0f, 0f, 0f);
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(content, 0f, 0f, w);

            for (int i = 0; i < AxisCount; i++)
            {
                _rows[i] = TwinUiCardParts.Row(content, RowsTop + RowStep * i, AxisLabels[i], w);
            }

            TwinUiCardParts.Caption(content, "力 N · 力矩 N·m",
                0f, RowsTop + RowStep * AxisCount, w, 16f);

            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);
            return root;
        }

        public override void Refresh()
        {
            if (_rows[0].Value == null)
            {
                return;
            }

            // A 支：回放态走同一份回放快照（六维力 + 力矩），实时路径原样保留。
            if (_binder == null)
            {
                _binder = Object.FindObjectOfType<TwinUIStatusBinder>();
            }

            if (_binder != null && _binder.ReplayOverrideActive && _binder.Current.IsReplay)
            {
                RefreshReplay(_binder.Current);
                return;
            }

            if (_runtime == null)
            {
                _runtime = Object.FindObjectOfType<DigitalTwinRuntime>();
            }

            if (_runtime == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 DigitalTwinRuntime");
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            if (!_runtime.HasLatestFrame)
            {
                _badge.Set(TwinUITone.Unknown, "无帧");
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            var frame = _runtime.LatestFrame;
            float[] force = frame == null ? null : frame.ForceVector;
            if (force == null || force.Length < 3)
            {
                _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotSubscribed);
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            if (force.Length < AxisCount)
            {
                _badge.Set(TwinUITone.Warn, "部分未接入");
                for (int i = 0; i < 3; i++)
                {
                    _rows[i].Set(force[i].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
                }

                for (int i = 3; i < AxisCount; i++)
                {
                    _rows[i].Set(TwinUITextR3.NotAvailable, TwinUITone.Unknown);
                }

                return;
            }

            _badge.Set(TwinUITone.Ok, "已连接");
            _rows[0].Set(force[0].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
            _rows[1].Set(force[1].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
            _rows[2].Set(force[2].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
            _rows[3].Set(force[3].ToString("F1") + TwinUITextR3.UnitNm, TwinUITone.Ok);
            _rows[4].Set(force[4].ToString("F1") + TwinUITextR3.UnitNm, TwinUITone.Ok);
            _rows[5].Set(force[5].ToString("F1") + TwinUITextR3.UnitNm, TwinUITone.Ok);
        }

        /// <summary>
        /// 回放路径：ext_force_fx/fy/fz（N）+ ext_force_tx/ty/tz（N·m）原样显示。
        /// 只有 3 个分量时显示"部分未接入"，力矩三行显示"未接入"，绝不补 0。
        /// </summary>
        private void RefreshReplay(TwinUIStatusBinder.Snapshot s)
        {
            if (!s.HasFrame)
            {
                _badge.Set(TwinUITone.Unknown, "无帧");
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            float[] w = s.Wrench;
            if (w == null || w.Length < 3)
            {
                _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotSubscribed);
                SetAll(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            if (w.Length < AxisCount)
            {
                _badge.Set(TwinUITone.Warn, "回放 · 部分未接入");
                for (int i = 0; i < 3; i++)
                {
                    _rows[i].Set(w[i].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
                }

                for (int i = 3; i < AxisCount; i++)
                {
                    _rows[i].Set(TwinUITextR3.NotAvailable, TwinUITone.Unknown);
                }

                return;
            }

            _badge.Set(TwinUITone.Ok, "回放 · 六维");
            _rows[0].Set(w[0].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
            _rows[1].Set(w[1].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
            _rows[2].Set(w[2].ToString("F1") + TwinUITextR3.UnitN, TwinUITone.Ok);
            _rows[3].Set(w[3].ToString("F1") + TwinUITextR3.UnitNm, TwinUITone.Ok);
            _rows[4].Set(w[4].ToString("F1") + TwinUITextR3.UnitNm, TwinUITone.Ok);
            _rows[5].Set(w[5].ToString("F1") + TwinUITextR3.UnitNm, TwinUITone.Ok);
        }

        private void SetAll(string value, TwinUITone tone)
        {
            for (int i = 0; i < AxisCount; i++)
            {
                if (_rows[i].Value != null)
                {
                    _rows[i].Set(value, tone);
                }
            }
        }
    }
}