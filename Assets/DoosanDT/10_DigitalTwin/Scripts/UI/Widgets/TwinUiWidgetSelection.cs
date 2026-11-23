// TwinUiWidgetSelection.cs —— 选中对象卡片（P9）
// 把原先散落在左下角的"射线文字 HUD + 悬停信息面板"收进主面板：对象名 / 类型 / 世界坐标 / 屏幕坐标 / UI 遮挡。
// 数据源：TwinPlayRayVisual（只读当前命中，不主动射线、不改层）。
using DigitalTwin.Interaction;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiWidgetSelection : TwinUiModule
    {
        public const string ModuleId = "selection";

        private TwinPlayRayVisual _ray;
        private TwinUiCardParts.Badge _badge;
        private TwinUiCardParts.Kv _name;
        private TwinUiCardParts.Kv _world;
        private TwinUiCardParts.Kv _screen;
        private TwinUiCardParts.Kv _mode;
        private TMP_Text _hint;

        public override string Id => ModuleId;
        public override string Title => "选中对象";
        public override string Kicker => "SELECTION";
        public override string Subtitle => "TwinPlayRayVisual";
        public override string WorkspaceId => TwinUiWorkspaceRegistry.Overview;
        public override Vector2 CardSize => new Vector2(320f, 208f);
        public override Vector2 FloatingMinSize => new Vector2(280f, 180f);

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "WidgetSelection");
            View = root;
            float w = Mathf.Max(180f, CardSize.x - 24f);

            _badge = TwinUiCardParts.BadgeRow(root, 0f, 0f, w);
            _name = TwinUiCardParts.Row(root, 24f, "对象", w);
            _world = TwinUiCardParts.Row(root, 48f, "世界坐标", w);
            _screen = TwinUiCardParts.Row(root, 72f, "屏幕坐标", w);
            _mode = TwinUiCardParts.Row(root, 96f, "指针状态", w);
            _hint = TwinUiCardParts.Caption(root, "左键点选零件；F 聚焦到选中对象（漫游卡也有按钮）", 0f, 126f, w, 34f);
            _badge.Set(TwinUITone.Unknown, TwinUITextR3.NotConnected);
            return root;
        }

        public override void Refresh()
        {
            if (_ray == null)
            {
                _ray = Object.FindObjectOfType<TwinPlayRayVisual>();
            }

            if (_ray == null)
            {
                _badge.Set(TwinUITone.Unknown, "未挂载 TwinPlayRayVisual");
                _name.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _world.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _screen.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _mode.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                return;
            }

            bool overUi = _ray.IsPointerOverUi;
            bool hit = _ray.HasWorldHit;
            _badge.Set(overUi ? TwinUITone.Unknown : hit ? TwinUITone.Ok : TwinUITone.Warn,
                overUi ? "指针在 UI 上" : hit ? "已命中" : "未命中");

            if (hit)
            {
                RaycastHit info = _ray.CurrentHit;
                string name = info.collider != null ? info.collider.name : "(无 Collider)";
                _name.Set(name, TwinUITone.Ok);
                _world.Set("(" + info.point.x.ToString("F2") + ", " + info.point.y.ToString("F2") + ", "
                    + info.point.z.ToString("F2") + ")", TwinUITone.Ok);
            }
            else
            {
                _name.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
                _world.Set(TwinUITextR3.Placeholder, TwinUITone.Unknown);
            }

            Vector2 screen = _ray.PointerScreenPosition;
            _screen.Set(screen.x.ToString("F0") + " , " + screen.y.ToString("F0"), TwinUITone.Ok);
            _mode.Set(overUi ? "UI 遮挡（射线暂停）" : "3D 场景", overUi ? TwinUITone.Warn : TwinUITone.Ok);
        }
    }
}