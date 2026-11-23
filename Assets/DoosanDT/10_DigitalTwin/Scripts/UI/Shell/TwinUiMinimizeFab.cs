// TwinUiMinimizeFab.cs —— 主工作台"最小化悬浮图标"（P9，手机 App 悬浮球式）
// 行为：主窗最小化/隐藏时，在画布右侧浮出一个 56×56 圆角按钮（可拖动），点击即恢复到正常大小；
//       主窗可见时自动隐藏。位置存 PlayerPrefs（TwinUI.Fab.v1），分辨率变化时钳回可见区域。
// 约束：只调用 TwinUiWindowHost.Restore()，不新建任何页面/数据源。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiMinimizeFab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public const string PrefsKey = "TwinUI.Fab.v1";
        public const float Size = 56f;
        public const float Margin = 16f;
        /// <summary>边缘吸附阈值（距左/右边缘小于该值 → 平滑贴边）。</summary>
        public const float SnapThreshold = 24f;
        /// <summary>吸附动画时长（秒）。</summary>
        public const float SnapDuration = 0.18f;

        private TwinUiWindowHost _window;
        private RectTransform _canvasRect;
        private RectTransform _root;
        private Image _bg;
        private UnityEngine.UI.Outline _outline;
        private TMP_Text _icon;
        private TMP_Text _label;
        private RectTransform _hint;
        private TMP_Text _hintText;
        private CanvasGroup _group;
        private Vector2 _normalized = new Vector2(1f, 0.42f);
        private bool _dragging;
        private Vector2 _dragOffset;
        private int _restoreCount;
        private int _showCount;
        private bool _visible;
        private bool _snapping;
        private Vector2 _snapStartNormalized;
        private Vector2 _snapTargetNormalized;
        private float _snapElapsed;
        private int _snapCount;

        public bool IsVisible => _visible;
        public int RestoreCount => _restoreCount;
        public int ShowCount => _showCount;
        public RectTransform Root => _root;

        /// <summary>是否正在执行边缘吸附动画（动画期间不写 PlayerPrefs）。</summary>
        public bool Snapping => _snapping;

        /// <summary>完成吸附的次数（自增，自检用）。</summary>
        public int SnapCount => _snapCount;

        public static TwinUiMinimizeFab Create(Canvas canvas, TwinUiWindowHost window)
        {
            if (canvas == null || window == null)
            {
                return null;
            }

            var go = new GameObject("UiMinimizeFab", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            var fab = go.AddComponent<TwinUiMinimizeFab>();
            fab.Build(rt, window, canvas.transform as RectTransform);
            return fab;
        }

        private void Build(RectTransform root, TwinUiWindowHost window, RectTransform canvasRect)
        {
            _root = root;
            _window = window;
            _canvasRect = canvasRect;

            _root.anchorMin = new Vector2(1f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.sizeDelta = new Vector2(Size, Size);

            _bg = TwinUIStyle.Panel(_root, "FabBg", TwinUIStyle.CardColor, true);
            _bg.sprite = TwinUiRadii.CardSprite;
            _bg.type = Image.Type.Sliced;
            _bg.pixelsPerUnitMultiplier = 1f;
            TwinUIStyle.Stretch(_bg.rectTransform, 0f, 0f, 0f, 0f);
            TwinUIStyle.Outline(_bg.gameObject, TwinUIStyle.AccentColor);
            _outline = _bg.GetComponent<UnityEngine.UI.Outline>();

            var button = _root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = _bg;
            button.onClick.AddListener(RestoreWindow);

            _icon = TwinUIStyle.Text(_root, "Icon", "台", 20f, TwinUIStyle.TextColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(_icon.rectTransform, 0f, 4f, 0f, 18f);

            _label = TwinUIStyle.Text(_root, "Label", "工作台", TwinUIStyle.FontUnit, TwinUIStyle.MutedColor,
                TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(_label.rectTransform, 0f, 30f, 0f, 4f);

            _hint = TwinUIStyle.Frame(_root, "Hint");
            _hint.anchorMin = new Vector2(1f, 0.5f);
            _hint.anchorMax = new Vector2(1f, 0.5f);
            _hint.pivot = new Vector2(1f, 0.5f);
            _hint.anchoredPosition = new Vector2(-Size - 10f, 0f);
            _hint.sizeDelta = new Vector2(132f, 22f);
            _hint.gameObject.SetActive(false);
            var hintBg = TwinUIStyle.Panel(_hint, "HintBg", TwinUIStyle.Layer2Color, false);
            TwinUIStyle.Stretch(hintBg.rectTransform, 0f, 0f, 0f, 0f);
            _hintText = TwinUIStyle.Text(_hint, "HintText", "点击恢复主工作台", TwinUIStyle.FontUnit,
                TwinUIStyle.MutedColor, TextAlignmentOptions.Center);
            TwinUIStyle.Stretch(_hintText.rectTransform, 4f, 0f, 4f, 0f);

            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = true;

            TwinUiRadii.Apply(_root);
            LoadPosition();
            ApplyPosition();
            SetVisible(false);
            Debug.Log("[FAB] created prefabs=" + (_bg != null) + " canvas=" + (canvasRect != null));
        }

        private void Update()
        {
            if (_window == null)
            {
                return;
            }

            bool shouldShow = !_window.IsVisible;
            if (shouldShow != _visible)
            {
                SetVisible(shouldShow);
            }

            // 吸附动画期间只推进动画（不写 PlayerPrefs、不做常规钳位）。
            if (_visible && _snapping && !_dragging)
            {
                UpdateSnap();
                return;
            }

            if (_visible && !_dragging && _canvasRect != null)
            {
                // 分辨率变化后把悬浮球钳回可见区域（只在必要时动）。
                ClampToCanvas();
            }
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            // 关键：不要 SetActive(false)——那会让自身 Update 停止，悬浮球永远无法再出现。
            // 用 CanvasGroup 控制显隐与点击穿透，根节点保持激活。
            if (_group != null)
            {
                _group.alpha = visible ? 1f : 0f;
                _group.blocksRaycasts = visible;
                _group.interactable = visible;
            }

            if (visible)
            {
                _showCount++;
                ApplyPosition();
                if (_outline != null)
                {
                    _outline.enabled = true;
                }
            }
            else if (_outline != null)
            {
                _outline.enabled = false;
            }

            if (_hint != null && !visible)
            {
                _hint.gameObject.SetActive(false);
            }

            if (!visible)
            {
                _snapping = false;
            }
        }

        /// <summary>自检用：等价于点击悬浮球。</summary>
        public void RestoreForCheck()
        {
            RestoreWindow();
        }

        private void RestoreWindow()
        {
            if (_window == null)
            {
                return;
            }

            _restoreCount++;
            _window.Restore();
            SetVisible(false);
            Debug.Log("[FAB] restore main window count=" + _restoreCount);
        }

        private void LoadPosition()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                var data = JsonUtility.FromJson<FabPos>(json);
                if (data != null)
                {
                    _normalized = new Vector2(Mathf.Clamp01(data.x), Mathf.Clamp01(data.y));
                }
            }
            catch (Exception)
            {
                // 损坏的位置配置直接回默认，不阻塞。
            }
        }

        private void SavePosition()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new FabPos
            {
                x = _normalized.x,
                y = _normalized.y
            }));
            PlayerPrefs.Save();
        }

        private void ApplyPosition()
        {
            if (_root == null || _canvasRect == null)
            {
                return;
            }

            Rect canvas = _canvasRect.rect;
            float x = Mathf.Clamp(_normalized.x * canvas.width, Size + Margin, Mathf.Max(Size + Margin, canvas.width - Size - Margin));
            float y = Mathf.Clamp(_normalized.y * canvas.height, TwinUiTaskbar.Height + Margin,
                Mathf.Max(TwinUiTaskbar.Height + Margin, canvas.height - Size - Margin));
            _root.anchoredPosition = new Vector2(x - canvas.width, y);
        }

        private void ClampToCanvas()
        {
            Rect canvas = _canvasRect.rect;
            float x = _normalized.x * canvas.width;
            float y = _normalized.y * canvas.height;
            if (x < Size + Margin || x > canvas.width - Size - Margin || y < TwinUiTaskbar.Height + Margin
                || y > canvas.height - Size - Margin)
            {
                _normalized = new Vector2(Mathf.Clamp(x, Size + Margin, canvas.width - Size - Margin) / canvas.width,
                    Mathf.Clamp(y, TwinUiTaskbar.Height + Margin, canvas.height - Size - Margin) / canvas.height);
                ApplyPosition();
                SavePosition();
            }
        }

        /// <summary>分辨率/画布变化时调用：按当前画布重新钳位并刷新位置（供 TwinUiDisplayWatcher 使用）。</summary>
        public void RefreshLayout()
        {
            if (_canvasRect == null || _root == null)
            {
                return;
            }

            _normalized = ClampNormalized(_normalized);
            if (_snapping)
            {
                // 画布尺寸变化后重算吸附终点，并从当前位置重新推进（避免回跳）。
                _snapStartNormalized = _normalized;
                _snapTargetNormalized = ComputeSnapNormalized(_normalized);
                _snapElapsed = 0f;
            }

            ApplyPosition();
        }

        /// <summary>按画布（含 Taskbar 高度、Margin）把归一化坐标钳到可见区域。</summary>
        private Vector2 ClampNormalized(Vector2 normalized)
        {
            Rect canvas = _canvasRect.rect;
            float width = Mathf.Max(1f, canvas.width);
            float height = Mathf.Max(1f, canvas.height);
            float x = Mathf.Clamp(normalized.x * width, Size + Margin, Mathf.Max(Size + Margin, width - Size - Margin));
            float y = Mathf.Clamp(normalized.y * height, TwinUiTaskbar.Height + Margin,
                Mathf.Max(TwinUiTaskbar.Height + Margin, height - Size - Margin));
            return new Vector2(x / width, y / height);
        }

        /// <summary>计算吸附终点：距左/右边缘 &lt; SnapThreshold 时贴到 Margin；否则原样（仅钳位）。</summary>
        private Vector2 ComputeSnapNormalized(Vector2 normalized)
        {
            Rect canvas = _canvasRect.rect;
            float width = Mathf.Max(1f, canvas.width);
            float x = normalized.x * width;
            float leftEdge = x - Size;
            float distanceLeft = leftEdge;                    // 悬浮球左边缘到画布左边的距离
            float distanceRight = width - x;                  // 悬浮球右边缘到画布右边的距离
            Vector2 result = normalized;
            if (distanceLeft < SnapThreshold)
            {
                result.x = (Size + Margin) / width;           // 左吸附：左边缘保留 Margin
            }
            else if (distanceRight < SnapThreshold)
            {
                result.x = (width - Size - Margin) / width;   // 右吸附：右边缘保留 Margin
            }

            return ClampNormalized(result);
        }

        private void BeginSnap(Vector2 target)
        {
            _snapping = true;
            _snapElapsed = 0f;
            _snapStartNormalized = _normalized;
            _snapTargetNormalized = ClampNormalized(target);
        }

        /// <summary>0.18s 平滑贴边（SmoothStep）；期间不写 PlayerPrefs，结束后 SavePosition 一次。</summary>
        private void UpdateSnap()
        {
            if (_canvasRect == null)
            {
                _snapping = false;
                return;
            }

            _snapElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_snapElapsed / SnapDuration);
            float eased = t * t * (3f - 2f * t);
            _normalized = Vector2.LerpUnclamped(_snapStartNormalized, _snapTargetNormalized, eased);
            ApplyPosition();
            if (t < 1f)
            {
                return;
            }

            _snapping = false;
            _normalized = _snapTargetNormalized;
            ApplyPosition();
            _snapCount++;
            SavePosition();
            Debug.Log("[FAB] snap edge count=" + _snapCount
                + " x=" + (_normalized.x * Mathf.Max(1f, _canvasRect.rect.width)).ToString("F1"));
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_hint != null)
            {
                _hint.gameObject.SetActive(true);
            }

            if (_outline != null)
            {
                _outline.effectColor = TwinUIStyle.StrongColor;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_hint != null)
            {
                _hint.gameObject.SetActive(false);
            }

            if (_outline != null)
            {
                _outline.effectColor = TwinUIStyle.AccentColor;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            _snapping = false;
            if (_hint != null)
            {
                _hint.gameObject.SetActive(false);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_root == null || _canvasRect == null)
            {
                return;
            }

            _snapping = false;

            Rect canvas = _canvasRect.rect;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, eventData.position,
                    eventData.pressEventCamera, out local))
            {
                return;
            }

            // 画布坐标（左下角原点）。
            float x = Mathf.Clamp(local.x - Size * 0.5f + canvas.width * 0.5f, Size + Margin,
                Mathf.Max(Size + Margin, canvas.width - Size - Margin));
            float y = Mathf.Clamp(local.y - Size * 0.5f + canvas.height * 0.5f,
                TwinUiTaskbar.Height + Margin, Mathf.Max(TwinUiTaskbar.Height + Margin, canvas.height - Size - Margin));
            _normalized = new Vector2(x / Mathf.Max(1f, canvas.width), y / Mathf.Max(1f, canvas.height));
            ApplyPosition();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            if (_canvasRect == null)
            {
                SavePosition();
                return;
            }

            // P9.5：松手时距左/右边缘 < 24px → 0.18s 平滑贴边；否则直接保存。
            Vector2 target = ComputeSnapNormalized(_normalized);
            bool shouldSnap = Mathf.Abs(target.x - _normalized.x) > 0.0005f;
            if (shouldSnap)
            {
                BeginSnap(target);
                Debug.Log("[FAB] snap start from x=" + (_normalized.x * _canvasRect.rect.width).ToString("F1")
                    + " target=" + (target.x * _canvasRect.rect.width).ToString("F1")
                    + " duration=" + SnapDuration + "s");
            }
            else
            {
                SavePosition();
            }
        }

        public void ApplyTheme()
        {
            if (_bg != null)
            {
                _bg.color = TwinUIStyle.CardColor;
            }

            if (_outline != null)
            {
                _outline.effectColor = TwinUIStyle.AccentColor;
            }

            if (_icon != null)
            {
                _icon.color = TwinUIStyle.TextColor;
            }

            if (_label != null)
            {
                _label.color = TwinUIStyle.MutedColor;
            }

            if (_hintText != null)
            {
                _hintText.color = TwinUIStyle.MutedColor;
            }
        }

        [Serializable]
        private class FabPos
        {
            public float x;
            public float y;
        }
    }
}