// TwinUiTaskbar.cs —— 屏幕底部任务栏（P8）
// 设计目标：像浏览器/Windows 任务栏一样统一收口"主工作台 + 所有浮窗"，最小化/恢复/归位都从这里走，
//           用户永远不会因为关掉窗口而失去入口。
// 约束：只读 TwinUiSurfaceHost 的状态；不直接改卡片数据；不吞掉 3D 场景点击（背景 raycast=false）。
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiTaskbar : MonoBehaviour
    {
        public const float Height = TwinUIStyle.TaskbarV2Height;
        public const float ButtonHeight = 22f;
        public const float Gap = 6f;

        private TwinUiWindowHost _window;
        private TwinUiSurfaceHost _surface;
        private RectTransform _root;
        private RectTransform _entries;
        private TMP_Text _hint;
        private TwinUIButton _homeButton;
        private readonly List<TwinUIButton> _buttons = new List<TwinUIButton>();
        private readonly List<string> _buttonIds = new List<string>();
        private int _rebuildCount;

        public RectTransform Root => _root;
        public int EntryCount => _buttonIds.Count;
        public int RebuildCount => _rebuildCount;
        public bool HomeSelected => _homeButton != null && _window != null && _window.IsVisible;

        public static TwinUiTaskbar Create(Canvas canvas, TwinUiWindowHost window, TwinUiSurfaceHost surface)
        {
            if (canvas == null)
            {
                return null;
            }

            var go = new GameObject("UiTaskbar", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            var bar = go.AddComponent<TwinUiTaskbar>();
            bar.Build(rt, window, surface);
            return bar;
        }

        private void Build(RectTransform root, TwinUiWindowHost window, TwinUiSurfaceHost surface)
        {
            _root = root;
            _window = window;
            _surface = surface;

            var bg = TwinUIStyle.Panel(_root, "TaskbarBg", TwinUIStyle.Layer2Color, false);
            TwinUIStyle.Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

            var line = TwinUIStyle.Panel(_root, "TaskbarLine", TwinUIStyle.BorderColor, false);
            line.raycastTarget = false;
            var lrt = line.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, 1f);

            _homeButton = TwinUIStyle.Button(_root, "BtnHome", "主工作台", TwinUIButton.Kind.Plain,
                Vector2.zero, new Vector2(88f, ButtonHeight), ToggleHome);
            _homeButton.Label.fontSize = TwinUIStyle.FontUnit;
            TwinUIStyle.Row((RectTransform)_homeButton.transform, 8f, -Height / 2f + ButtonHeight / 2f, 88f,
                ButtonHeight);

            _entries = TwinUIStyle.Frame(_root, "TaskbarEntries");
            var ert = _entries;
            ert.anchorMin = new Vector2(0f, 0.5f);
            ert.anchorMax = new Vector2(0f, 0.5f);
            ert.pivot = new Vector2(0f, 0.5f);
            ert.anchoredPosition = new Vector2(104f, 0f);
            ert.sizeDelta = new Vector2(600f, ButtonHeight);

            _hint = TwinUIStyle.Text(_root, "Hint", "右键旋转 · WASD 漫游 · 拖标题栏移窗 · 拖边角缩放 · F11 最大化",
                TwinUIStyle.FontUnit, TwinUIStyle.QuietColor, TextAlignmentOptions.Right);
            _hint.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            _hint.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            _hint.rectTransform.pivot = new Vector2(1f, 0.5f);
            _hint.rectTransform.anchoredPosition = new Vector2(-10f, 0f);
            _hint.rectTransform.sizeDelta = new Vector2(520f, 16f);

            TwinUiRadii.Apply(_root);
            Rebuild();
        }

        private void ToggleHome()
        {
            if (_window == null)
            {
                return;
            }

            if (_window.IsVisible)
            {
                _window.Minimize();
            }
            else
            {
                _window.Restore();
            }

            Rebuild();
        }

        /// <summary>按 surface 状态重建条目（浮窗 / 最小化 / 图钉）。</summary>
        public void Rebuild()
        {
            if (_root == null)
            {
                return;
            }

            _rebuildCount++;
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i] != null)
                {
                    Destroy(_buttons[i].gameObject);
                }
            }

            _buttons.Clear();
            _buttonIds.Clear();

            if (_surface == null)
            {
                return;
            }

            var ids = _surface.FloatingIds;
            float x = 0f;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                var module = TwinUiSurfaceHost.FindModule(id);
                if (module == null)
                {
                    continue;
                }

                string label = module.Title;
                if (_surface.IsMinimizedFloating(id))
                {
                    label += " ·最小";
                }

                if (_surface.IsPinned(id))
                {
                    label += " ·钉";
                }

                string captured = id;
                var btn = TwinUIStyle.Button(_entries, "Task_" + id, label, TwinUIButton.Kind.Plain,
                    Vector2.zero, new Vector2(96f, ButtonHeight), () => OnEntryClicked(captured));
                if (btn.Label != null)
                {
                    btn.Label.fontSize = TwinUIStyle.FontUnit;
                }

                TwinUIStyle.Row((RectTransform)btn.transform, x, -ButtonHeight / 2f, 96f, ButtonHeight);
                _buttons.Add(btn);
                _buttonIds.Add(id);
                x += 96f + Gap;
            }

            if (_homeButton != null)
            {
                _homeButton.SetSelected(HomeSelected);
            }

            if (_hint != null)
            {
                _hint.text = _buttonIds.Count == 0
                    ? "右键转视角 · WASD 移动(无需右键) · Q/E 升降 · Shift 加速 · F 聚焦 · R 复位 · 卡片「拆出」可分离"
                    : "点击任务栏条目恢复/聚焦浮窗 · F 聚焦选中 · R 复位视角";
            }

            TwinUiRadii.Apply(_entries);
        }

        private void OnEntryClicked(string id)
        {
            if (_surface == null)
            {
                return;
            }

            if (_surface.IsMinimizedFloating(id))
            {
                _surface.RestoreFloating(id);
            }
            else
            {
                _surface.BringToFront(id);
            }

            if (_window != null && !_window.IsVisible)
            {
                _window.Restore();
            }

            Rebuild();
        }

        public void ApplyLayout(float canvasWidth)
        {
            if (_root == null)
            {
                return;
            }

            var rt = _root;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, Height);

            if (_hint != null)
            {
                _hint.rectTransform.sizeDelta = new Vector2(Mathf.Max(160f, Mathf.Min(520f, canvasWidth - 120f)), 16f);
            }
        }

        public void ApplyTheme()
        {
            if (_root == null)
            {
                return;
            }

            var bg = _root.Find("TaskbarBg");
            if (bg != null)
            {
                var img = bg.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.color = TwinUIStyle.Layer2Color;
                }
            }

            var line = _root.Find("TaskbarLine");
            if (line != null)
            {
                var img = line.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.color = TwinUIStyle.BorderColor;
                }
            }

            if (_hint != null)
            {
                _hint.color = TwinUIStyle.QuietColor;
            }

            if (_homeButton != null)
            {
                _homeButton.ApplyTheme();
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].ApplyTheme();
            }
        }
    }
}