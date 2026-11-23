using System;
using System.Globalization;
using DigitalTwin;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    // Existing command module owns the point intent editor; no second control workspace.
    public sealed class TwinUiWidgetCommand : TwinUiModule
    {
        public const string ModuleId = "command";
        private PointMotionSession _session;
        private TwinCommandPanelView _panel;
        private readonly TMP_InputField[] _values = new TMP_InputField[6];
        private readonly TMP_Text[] _labels = new TMP_Text[6];
        private readonly TwinUIButton[] _modes = new TwinUIButton[3];
        private TwinUIButton _execute, _confirm, _authorize;
        private TMP_Text _status, _detail, _units, _parameters;
        private bool _loading;
        public override string Id => ModuleId;
        public override string Title => "点位控制";
        public override string Kicker => "POINT MOTION · SIMULATION";
        public override string Subtitle => "Doosan motion / 真机出口禁用";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Control;
        public override Vector2 CardSize => new Vector2(380f, 490f);
        public override Vector2 FloatingMinSize => new Vector2(360f, 490f);
        public TwinCommandPanelView Panel => _panel;
        public PointMotionSession Session => _session;
        public bool ExecuteInteractable => _execute != null && _execute.Button.interactable;

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetCommand"); View = root;
            float w = CardSize.x - 24f;
            var runtime = UnityEngine.Object.FindObjectOfType<DigitalTwinRuntime>();
            var owner = runtime != null ? runtime.gameObject : root.gameObject;
            _session = owner.GetComponent<PointMotionSession>() ?? owner.AddComponent<PointMotionSession>();
            _panel = root.gameObject.AddComponent<TwinCommandPanelView>();
            _panel.BindPointSession(_session);
            Label(root, "Source", "隔离模拟 · ROS2 · 硬件禁用", 0, 0, w, 22, TwinUIStyle.AccentColor);
            Button(root, "ConnectSim", "连接模拟", 0, 28, 112, () => _session.ConnectSimulation());
            Label(root, "Endpoint", "127.0.0.1:10003 / 域87", 122, 28, w - 122, 22, TwinUIStyle.MutedColor);
            string[] names = { "关节点位", "TCP点到点", "TCP直线" };
            for (int i = 0; i < 3; i++) {
                int mode = i;
                _modes[i] = Button(root, "Mode"+i, names[i], i * 120f, 60, 112, () => {
                    _session.SetMode((PointMotionSession.Motion)mode); LoadFields();
                });
            }
            _units = Label(root, "Units", "", 0, 90, w, 20, TwinUIStyle.MutedColor);
            for (int i = 0; i < 6; i++) {
                int index = i; float x = (i % 3) * 120f, y = 118 + (i / 3) * 55f;
                _labels[i] = Label(root, "TargetLabel"+i, "J"+(i+1), x, y, 110, 18, TwinUIStyle.MutedColor);
                _values[i] = Input(root, "Target"+i, x, y+20, 110);
                _values[i].onValueChanged.AddListener(_ => { if (!_loading) _session.Invalidate("目标正在编辑；请重新校验"); });
                _values[i].onEndEdit.AddListener(_ => CommitFields());
            }
            Button(root, "LoadCurrent", "锁定当前", 0, 238, 112, () => { _session.LoadCurrent(); LoadFields(); });
            Button(root, "LoadIk", "读取IK目标", 120, 238, 112, () => { _session.LoadIk(); LoadFields(); });
            Button(root, "Preview", "目标预览", 240, 238, 112, () => { if (CommitFields()) _session.Preview(); });
            _parameters = Label(root, "Parameters", "", 0, 270, w, 36, TwinUIStyle.QuietColor);
            _parameters.enableWordWrapping = true;
            Button(root, "Check", "安全校验", 0, 312, 112, () => { if (CommitFields()) _session.Prepare(); });
            _authorize = Button(root, "Authorize", "模拟授权", 120, 312, 112, () => _session.SetAuthorization(!_session.Authorized));
            _confirm = Button(root, "Confirm", "明确确认", 240, 312, 112, () => _session.Confirm());
            _execute = Button(root, "Execute", "执行模拟", 0, 346, 112, () => _session.Execute());
            Button(root, "Cancel", "取消/停止", 120, 346, 112, () => _session.Cancel());
            Button(root, "Estop", "软件急停", 240, 346, 112, () => _session.Cancel(true));
            _status = Label(root, "Status", "", 0, 382, w, 24, TwinUIStyle.AccentColor);
            _detail = Label(root, "Detail", "", 0, 410, w, 48, TwinUIStyle.MutedColor);
            _detail.enableWordWrapping = true;
            LoadFields(); Refresh(); return root;
        }
        public bool CommitFields()
        {
            var v = new double[6];
            for (int i = 0; i < 6; i++) {
                if (!double.TryParse(_values[i].text, NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])
                    || double.IsNaN(v[i]) || double.IsInfinity(v[i])) {
                    _session.Invalidate("输入错误：第"+(i+1)+"项需有限数值"); return false;
                }
            }
            bool same = true;
            for (int i = 0; i < 6; i++) same &= v[i] == _session.Target[i];
            return same || _session.SetTarget(v);
        }
        private void LoadFields()
        {
            _loading = true;
            for (int i = 0; i < 6; i++) _values[i].SetTextWithoutNotify(_session.Target[i].ToString("0.######", CultureInfo.InvariantCulture));
            _loading = false; Refresh();
        }
        public override void Refresh()
        {
            if (_session == null || _status == null) return;
            bool editing=false;
            for(int i=0;i<6;i++) editing|=_values[i].isFocused;
            if(!editing) for(int i=0;i<6;i++) _values[i].SetTextWithoutNotify(_session.Target[i].ToString("0.######",CultureInfo.InvariantCulture));
            int mode = (int)_session.Mode;
            for (int i = 0; i < 3; i++) _modes[i].SetSelected(mode == i);
            string[] tcp = { "X / mm", "Y / mm", "Z / mm", "A / deg", "B / deg", "C / deg" };
            for (int i = 0; i < 6; i++) _labels[i].text = mode == 0 ? "J"+(i+1)+" / deg" : tcp[i];
            _units.text = mode == 0 ? "六关节绝对点位 · 其他关节保持锁定值" : "模拟参考系 BASE(0) · 厂商姿态约定待现场核实";
            _parameters.text = mode == 2 ? "SIM_TCP / SIM_TOOL · v=[10,5] a=[20,10]\n路径未验证；模拟参数非真机安全批准" :
                "SIM_TCP / SIM_TOOL · v=5 deg/s a=10 deg/s²"+(mode == 1 ? " · sol=0" : "")+"\n路径未验证；末端不保证走直线";
            _authorize.SetSelected(_session.Authorized);
            _authorize.SetLabel(_session.Authorized ? "撤销授权" : "模拟授权");
            _confirm.SetInteractable(_session.State == "PLANNED" && _session.Authorized && _session.Connected);
            _execute.SetInteractable(_session.Confirmed);
            _status.text = (_session.Connected ? "SIM · " : "OFFLINE · ") + _session.State;
            _detail.text = _session.Reason + "\nv"+_session.Version+" · "+(_session.Digest.Length > 12 ? _session.Digest.Substring(0,12) : "未确认目标");
        }
        public override void OnDetach() { if (_session != null) { _session.SetAuthorization(false); } }
        private static TMP_Text Label(Transform p, string n, string text, float x, float y, float w, float h, Color c)
        {
            var t = TwinUIStyle.Text(p,n,text,TwinUIStyle.FontUnit,c); TwinUIStyle.Row(t.rectTransform,x,y,w,h); return t;
        }
        private static TwinUIButton Button(Transform p, string n, string text, float x, float y, float w, UnityEngine.Events.UnityAction action)
        {
            var b = TwinUIStyle.Button(p,n,text,TwinUIButton.Kind.Default,Vector2.zero,new Vector2(w,26),action);
            b.Label.fontSize = TwinUIStyle.FontUnit; TwinUIStyle.Row((RectTransform)b.transform,x,y,w,26); return b;
        }
        private static TMP_InputField Input(Transform p, string n, float x, float y, float w)
        {
            var r = TwinUIStyle.Frame(p,n); TwinUIStyle.Row(r,x,y,w,28);
            var image = r.gameObject.AddComponent<Image>(); image.color = TwinUIStyle.CardAltColor;
            var t = TwinUIStyle.Text(r,"Text","0",14,TwinUIStyle.TextColor);
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(7,2); t.rectTransform.offsetMax = new Vector2(-7,-2);
            var input = r.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic=image;
            input.textViewport=r; input.textComponent=t; input.contentType=TMP_InputField.ContentType.DecimalNumber;
            input.text="0"; return input;
        }
    }
}
