// TwinUiShellPrefs.cs —— V2 工作台布局持久化（P8）
// 设计目标：页签 / 二级导航 / 预设 / 每张卡片的状态与浮窗矩形 / 浮窗叠放顺序，一次存一个 JSON。
// 兼容策略：
//   * 旧键 TwinUI.Window.Layout.v1（主窗矩形）与 TwinUI.Modules.v1（旧 dock/浮窗状态）保持只读不改；
//     首次运行 V2 时从 Modules.v1 迁移一次（Docked → Card、Floating 保留矩形、Hidden → Card）。
//   * 损坏/版本不符/超范围 → 回退默认布局并打日志，不阻塞启动。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.UI
{
    [Serializable]
    public class TwinUiWidgetPref
    {
        public string id;
        public int state;      // TwinUiModuleState
        public bool pinned;
        public bool minimized;
        public float x;
        public float y;
        public float w;
        public float h;
        public int order;
    }

    [Serializable]
    public class TwinUiShellLayoutData
    {
        public int version = 2;
        public string tab = TwinUiWorkspaceRegistry.Overview;
        public string sub = string.Empty;
        public string preset = TwinUiLayoutPresets.Default;
        public List<TwinUiWidgetPref> widgets = new List<TwinUiWidgetPref>();
        public List<string> stacking = new List<string>();
    }

    public static class TwinUiShellPrefs
    {
        public const string Key = "TwinUI.Shell.v2";
        public const string LegacyModulesKey = "TwinUI.Modules.v1";
        public const int Version = 2;

        public static bool LastLoadFailed { get; private set; }

        public static bool LastMigratedFromV1 { get; private set; }

        public static TwinUiShellLayoutData Load()
        {
            LastLoadFailed = false;
            LastMigratedFromV1 = false;
            string json = PlayerPrefs.GetString(Key, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JsonUtility.FromJson<TwinUiShellLayoutData>(json);
                    if (data != null && data.version == Version && data.widgets != null)
                    {
                        if (TwinUiWorkspaceRegistry.Find(data.tab) == null)
                        {
                            data.tab = TwinUiWorkspaceRegistry.Overview;
                        }

                        return data;
                    }

                    Debug.LogWarning("[SHELL] prefs version/format mismatch (version="
                        + (data != null ? data.version : -1) + ") → default layout");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SHELL] prefs parse failed: " + e.Message + " → default layout");
                }

                LastLoadFailed = true;
            }

            var fresh = new TwinUiShellLayoutData();
            MigrateLegacy(fresh);
            return fresh;
        }

        /// <summary>旧 TwinUI.Modules.v1 → v2（只读旧键，不删除，便于回退 Legacy 模式）。</summary>
        private static void MigrateLegacy(TwinUiShellLayoutData target)
        {
            string json = PlayerPrefs.GetString(LegacyModulesKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                var legacy = JsonUtility.FromJson<LegacyModulesState>(json);
                if (legacy?.modules == null)
                {
                    return;
                }

                for (int i = 0; i < legacy.modules.Count; i++)
                {
                    var m = legacy.modules[i];
                    if (m == null || string.IsNullOrEmpty(m.id))
                    {
                        continue;
                    }

                    var pref = new TwinUiWidgetPref
                    {
                        id = m.id,
                        // Docked(1) → Card(3)；Floating(2) 保留；Hidden(0) 在 V2 里默认显示为卡片。
                        state = m.state == (int)TwinUiModuleState.Floating
                            ? (int)TwinUiModuleState.Floating
                            : (int)TwinUiModuleState.Card,
                        x = m.x,
                        y = m.y,
                        w = m.w,
                        h = m.h,
                        order = m.order
                    };
                    target.widgets.Add(pref);
                }

                LastMigratedFromV1 = true;
                Debug.Log("[SHELL] migrated legacy modules prefs count=" + target.widgets.Count);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SHELL] legacy prefs migrate skipped: " + e.Message);
            }
        }

        public static void Save(TwinUiShellLayoutData data)
        {
            if (data == null)
            {
                return;
            }

            data.version = Version;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        [Serializable]
        private class LegacyModulesState
        {
            public List<LegacyModule> modules = new List<LegacyModule>();
            public int dockPage;
        }

        [Serializable]
        private class LegacyModule
        {
            public string id;
            public int state;
            public bool expanded;
            public int order;
            public float x;
            public float y;
            public float w;
            public float h;
        }
    }
}