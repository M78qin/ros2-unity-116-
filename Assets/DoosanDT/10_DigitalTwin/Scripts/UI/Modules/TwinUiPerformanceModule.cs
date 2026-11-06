// TwinUiPerformanceModule.cs —— 性能监测模块（P4 / task-12）
// 数据源：旧拆解系统 PerformanceProfiler（只读 TryGetSnapshot，口径/CSV 格式不变）。
// 显示接管：模块激活时把旧 IMGUI 面板 showOnScreenUI=false，隐藏时还原用户原值并打日志。
// profiler 缺失：显示"未挂载"，不造假数据。
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiPerformanceModule : TwinUiModule
    {
        public const string ModuleId = "performance";
        public const float HistorySeconds = 60f;
        public const float HistoryInterval = 0.25f;

        private readonly PerformanceProfiler _profiler;
        private readonly List<float> _fpsHistory = new List<float>();
        private TwinUiPerformanceModuleView _view;
        private PerformanceProfiler.PerfSnapshot _snapshot;
        private bool _originalPanelVisible;
        private bool _hasOriginal;
        private float _lastHistoryAt = -1f;
        private int _attachCount;
        private int _detachCount;
        private int _exportCount;

        public TwinUiPerformanceModule(PerformanceProfiler profiler)
        {
            _profiler = profiler;
        }

        public override string Id => ModuleId;
        public override string Title => "性能监测";
        public override string Subtitle => _profiler != null ? "PerformanceProfiler" : "未挂载";
        public override Vector2 CompactSize => new Vector2(380f, 92f);
        public override Vector2 ExpandSize => new Vector2(480f, 340f);

        public PerformanceProfiler Profiler => _profiler;
        public bool ProfilerAvailable => _profiler != null;
        public PerformanceProfiler.PerfSnapshot Snapshot => _snapshot;
        public IReadOnlyList<float> FpsHistory => _fpsHistory;
        public int AttachCount => _attachCount;
        public int DetachCount => _detachCount;
        public int ExportCount => _exportCount;
        public bool OriginalPanelVisible => _hasOriginal ? _originalPanelVisible : true;
        public TwinUiPerformanceModuleView View2 => _view;

        public override RectTransform Build(Transform parent, bool floating)
        {
            var root = TwinUIStyle.Frame(parent, "PerfModule");
            View = root;
            _view = new TwinUiPerformanceModuleView();
            _view.Build(root, this, floating, IsExpanded);
            return root;
        }

        public override void OnAttach()
        {
            _attachCount++;
            if (_profiler == null)
            {
                return;
            }

            if (!_hasOriginal)
            {
                _originalPanelVisible = _profiler.IsOnScreenUIVisible();
                _hasOriginal = true;
            }

            _profiler.SetOnScreenUI(false, "module.attach state=" + State);
        }

        public override void OnDetach()
        {
            _detachCount++;
            if (_profiler == null || !_hasOriginal)
            {
                return;
            }

            _profiler.SetOnScreenUI(_originalPanelVisible, "module.detach state=" + State);
        }

        public override void OnExpandChanged(bool expanded)
        {
            if (_view != null)
            {
                _view.SetExpanded(expanded);
            }
        }

        public override void Refresh()
        {
            if (_profiler != null && _profiler.TryGetSnapshot(out var snapshot))
            {
                _snapshot = snapshot;
            }
            else
            {
                _snapshot = new PerformanceProfiler.PerfSnapshot { valid = false };
            }

            if (_lastHistoryAt < 0f || Time.unscaledTime - _lastHistoryAt >= HistoryInterval)
            {
                _lastHistoryAt = Time.unscaledTime;
                _fpsHistory.Add(_snapshot.valid ? _snapshot.fps : 0f);
                int max = Mathf.CeilToInt(HistorySeconds / HistoryInterval);
                while (_fpsHistory.Count > max)
                {
                    _fpsHistory.RemoveAt(0);
                }
            }

            if (_view != null)
            {
                _view.Refresh(this);
            }
        }

        public void ExportCsv()
        {
            if (_profiler == null)
            {
                Debug.LogWarning(TwinUiModuleHost.Tag + " perf export skipped: profiler 未挂载");
                return;
            }

            _exportCount++;
            bool flushed = _profiler.FlushCsvNow();
            Debug.Log(TwinUiModuleHost.Tag + " perf export csv path=" + _profiler.CsvFilePath
                + " flushed=" + flushed + " exportCount=" + _exportCount);
        }

        public override void DestroyView()
        {
            _view = null;
            base.DestroyView();
        }

        /// <summary>自检用：清空 FPS 历史。</summary>
        public void ClearHistory()
        {
            _fpsHistory.Clear();
            _lastHistoryAt = -1f;
        }
    }
}