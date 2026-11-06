// TwinUiDockCardDrag.cs —— dock 卡片拖出/拖回（P6-1 / task-15）
// 复用 TwinUiInputArbiter（WindowDrag），不新起输入路径：按下卡片 Begin(WindowDrag) → 拖动显示拖拽幽灵 →
// 松手：落点仍在主窗内 → 保持 Docked；拖出主窗 → 转 Floating（最小 480×330）。
using DigitalTwin.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DigitalTwin.UI
{
    [DisallowMultipleComponent]
    public sealed class TwinUiDockCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private TwinUiModuleHost _host;
        private string _moduleId;
        private RectTransform _card;
        private bool _dragging;
        private Vector2 _startScreen;
        private Vector2 _accumCanvas;
        private Canvas _canvas;

        public bool IsDragging => _dragging;
        public string ModuleId => _moduleId;
        public Vector2 AccumCanvas => _accumCanvas;
        public bool GhostShown => _host != null && _host.DockDragGhostActive;

        public void Configure(TwinUiModuleHost host, string moduleId, RectTransform card)
        {
            _host = host;
            _moduleId = moduleId;
            _card = card;
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (_host == null || !_host.CanDragDockCard(_moduleId))
            {
                return;
            }

            if (!TwinUiInput.Begin(TwinUiInputOwner.WindowDrag))
            {
                return;
            }

            _dragging = true;
            _startScreen = eventData.position;
            _accumCanvas = Vector2.zero;
            _host.BeginDockCardDrag(_moduleId);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || eventData == null)
            {
                return;
            }

            float scale = _canvas != null ? Mathf.Max(0.01f, _canvas.scaleFactor) : 1f;
            _accumCanvas = (eventData.position - _startScreen) / scale;
            _host.UpdateDockCardDrag(_accumCanvas);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            TwinUiInput.End(TwinUiInputOwner.WindowDrag);
            _host.EndDockCardDrag();
        }

        // ------------------------------------------------------------- 自检注入（与真实指针同一代码路径）

        public bool BeginDragForCheck()
        {
            if (_host == null || !_host.CanDragDockCard(_moduleId))
            {
                return false;
            }

            if (!TwinUiInput.Begin(TwinUiInputOwner.WindowDrag))
            {
                return false;
            }

            _dragging = true;
            _startScreen = Vector2.zero;
            _accumCanvas = Vector2.zero;
            _host.BeginDockCardDrag(_moduleId);
            return true;
        }

        public void UpdateDragForCheck(Vector2 canvasDelta)
        {
            if (!_dragging)
            {
                return;
            }

            _accumCanvas = canvasDelta;
            _host.UpdateDockCardDrag(_accumCanvas);
        }

        public void EndDragForCheck()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            TwinUiInput.End(TwinUiInputOwner.WindowDrag);
            _host.EndDockCardDrag();
        }
    }
}