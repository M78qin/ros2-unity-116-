// TwinUiWidgetWelcome.cs —— 系统入口卡（P9.5）
// 把原来的"欢迎/登录页"并进主面板：项目名、版本、当前模式、数据源与运行环境都在这一张卡里，
// 底部提供"退出系统"（编辑器内只隐藏主面板，构建里真正 Quit），不再需要单独一层欢迎页。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetWelcome : TwinUiModule
    {
        public const string ModuleId = "welcome";
        public const string ProjectTitle = "基于虚实联动的退役动力电池拆解实训系统";

        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Kv _version;
        private TwinUiCardParts.Kv _mode;
        private TwinUiCardParts.Kv _source;
        private TwinUiCardParts.Kv _workspace;
        private TMP_Text _note;
        private TwinUIButton _quitButton;

        public override string Id => ModuleId;
        public override string Title => "系统入口";
        public override string Kicker => "SYSTEM";
        public override string Subtitle => "welcome merged";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Overview;
        public override Vector2 CardSize => new Vector2(340f, 236f);
        public override Vector2 FloatingMinSize => new Vector2(300f, 210f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetWelcome");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);

            var title = TwinUIStyle.Text(root, "ProjectTitle", ProjectTitle, TwinUIStyle.FontBodySm,
                TwinUIStyle.TextColor);
            title.enableWordWrapping = true;
            TwinUIStyle.Row(title.rectTransform, 0f, 22f, w, 34f);

            _version = TwinUiCardParts.Row(root, 62f, "版本 / 引擎", w);
            _mode = TwinUiCardParts.Row(root, 86f, "界面模式", w);
            _source = TwinUiCardParts.Row(root, 110f, "数据源", w);
            _workspace = TwinUiCardParts.Row(root, 134f, "当前工作区", w);

            _note = TwinUiCardParts.Caption(root, "欢迎页已并入本卡（项目名/版本/模式），不再单独弹一层", 0f,
                160f, w, 26f);

            _quitButton = TwinUIStyle.Button(root, "BtnQuit", "退出系统", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(84f, 22f), Quit);
            TwinUIStyle.Row((RectTransform)_quitButton.transform, 0f, 190f, 84f, 22f);
            if (_quitButton.Label != null)
            {
                _quitButton.Label.fontSize = TwinUIStyle.FontUnit;
            }

            _badge.Set(TwinUITone.Ok, "运行中");
            return root;
        }

        public override void Refresh()
        {
            _badge.Set(TwinUITone.Ok, TwinUiShellMode.IsV2 ? "V2 工作台" : "Legacy 窗口");
            _version.Set("v" + Application.version + " / " + Application.unityVersion, TwinUITone.Ok);
            _mode.Set(TwinUiShellMode.IsV2 ? "浏览器式主面板" : "旧三页签窗口", TwinUITone.Ok);

            var binder = Object.FindObjectOfType<TwinUIStatusBinder>();
            string source = binder != null && !string.IsNullOrEmpty(binder.Current.SourceKind)
                ? binder.Current.SourceKind
                : TwinUITextR3.Placeholder;
            if (binder != null && !string.IsNullOrEmpty(binder.Current.SourceName))
            {
                source += " · " + binder.Current.SourceName;
            }

            _source.Set(source, binder != null && binder.Current.HasFrame ? TwinUITone.Ok : TwinUITone.Unknown);

            var host = TwinUiSurfaceHost.Instance;
            _workspace.Set(host != null && host.CurrentWorkspace != null ? host.CurrentWorkspace.Title
                : TwinUITextR3.Placeholder, TwinUITone.Ok);
        }

        private void Quit()
        {
            var window = Object.FindObjectOfType<TwinUiWindowHost>();
            if (window != null)
            {
                window.Hide();
            }

            Debug.Log("[WELCOME] quit requested (editor: main panel hidden, build: Application.Quit)");
#if !UNITY_EDITOR
            Application.Quit();
#endif
        }
    }
}