// TwinUiShellMode.cs —— 主面板形态开关（P7）
// 设计目标：V2（浏览器式工作台 + 卡片网格）与 Legacy（P2–P6 三页签窗口）可在运行时二选一，
//           且切换**不需要改场景**：模式只存在 PlayerPrefs 里，场景哈希保持基线。
// 读取时机：必须在任何 UI 组件 Awake/Start 之前确定，因此提供 RuntimeInitializeOnLoadMethod 预热。
// 默认值：P7/P8 施工期为 Legacy；V2 通过全量自检后由 DefaultKind 翻到 V2（工程内一行可回滚）。
using UnityEngine;

namespace DigitalTwin.UI
{
    public enum TwinUiShellKind
    {
        Legacy = 0,
        V2 = 1
    }

    public static class TwinUiShellMode
    {
        public const string PrefsKey = "TwinUI.Shell.Mode";

        /// <summary>P8 起默认 V2（浏览器式工作台）。回退 Legacy：菜单 UI/46b；清空返回默认：UI/46c。</summary>
        public const TwinUiShellKind DefaultKind = TwinUiShellKind.V2;

        private static bool _loaded;

        public static TwinUiShellKind Kind
        {
            get
            {
                EnsureLoaded();
                return _kind;
            }
        }

        public static bool IsV2 => Kind == TwinUiShellKind.V2;

        private static TwinUiShellKind _kind = DefaultKind;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // 关闭 Domain Reload 时静态字段会残留；每次进 Play 重新读 Prefs。
            _loaded = false;
            _kind = DefaultKind;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void WarmUp()
        {
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            int raw = PlayerPrefs.GetInt(PrefsKey, (int)DefaultKind);
            _kind = raw == (int)TwinUiShellKind.V2 ? TwinUiShellKind.V2 : TwinUiShellKind.Legacy;
        }

        public static void Set(TwinUiShellKind kind, bool save = true)
        {
            _kind = kind;
            _loaded = true;
            if (save)
            {
                PlayerPrefs.SetInt(PrefsKey, (int)kind);
                PlayerPrefs.Save();
            }
        }

        public static void ClearSaved()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            _kind = DefaultKind;
            _loaded = true;
        }

        public static string Describe()
        {
            return "shellMode=" + Kind + " saved=" + PlayerPrefs.GetInt(PrefsKey, -1);
        }
    }
}