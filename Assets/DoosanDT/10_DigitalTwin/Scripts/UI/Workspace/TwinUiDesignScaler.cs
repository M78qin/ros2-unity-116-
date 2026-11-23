// TwinUiDesignScaler.cs —— 把"设计尺寸"的内容等比缩放到任意父容器（P9.5）
// 用途：卡片内容在页内由 CardGrid 统一缩放；拆出到浮窗后，用本组件把同一份设计尺寸内容
//       等比放大/缩小并居中，保证浮窗变大时不会出现"右下角一片空白"，也不会拉变形。
using UnityEngine;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiDesignScaler : MonoBehaviour
    {
        public Vector2 DesignSize = new Vector2(TwinUiCardGrid.DesignWidth, 240f);
        public float MinScale = 0.5f;
        public float MaxScale = 1.6f;

        private RectTransform _rt;
        private RectTransform _parent;

        public float CurrentScale => _rt != null ? _rt.localScale.x : 1f;

        public void Configure(Vector2 designSize, RectTransform parent)
        {
            DesignSize = new Vector2(Mathf.Max(80f, designSize.x), Mathf.Max(80f, designSize.y));
            _parent = parent;
            _rt = transform as RectTransform;
            Apply();
        }

        private void OnEnable()
        {
            if (_rt == null)
            {
                _rt = transform as RectTransform;
            }

            if (_parent == null)
            {
                _parent = _rt != null ? _rt.parent as RectTransform : null;
            }

            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        private void Apply()
        {
            if (_rt == null)
            {
                _rt = transform as RectTransform;
            }

            if (_parent == null)
            {
                _parent = _rt != null ? _rt.parent as RectTransform : null;
            }

            if (_rt == null || _parent == null)
            {
                return;
            }

            float pw = _parent.rect.width;
            float ph = _parent.rect.height;
            if (pw < 4f || ph < 4f)
            {
                return;
            }

            float scale = Mathf.Min(pw / DesignSize.x, ph / DesignSize.y);
            scale = Mathf.Clamp(scale, MinScale, MaxScale);
            _rt.anchorMin = new Vector2(0.5f, 0.5f);
            _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.sizeDelta = DesignSize;
            _rt.anchoredPosition = Vector2.zero;
            _rt.localScale = new Vector3(scale, scale, 1f);
        }
    }
}