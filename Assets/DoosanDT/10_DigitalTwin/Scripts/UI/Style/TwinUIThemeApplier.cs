// 主题重刷器（P3 从 TwinUITheme.cs 拆出独立文件：类名与文件名一致，Unity 才能生成可引用的
// MonoScript 资产，UI/36 可把历史场景里的内嵌 MonoScript 引用改回资产引用）。
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DigitalTwin.UI
{
    /// <summary>挂在 Canvas 根上：切换主题后把所有带标签的图元/按钮/图表重刷一遍。</summary>
    public sealed class TwinUIThemeApplier : MonoBehaviour
    {
        [SerializeField] private bool applyOnStart = true;

        [Header("P2.5 主题启动引导：Play 开始时强制切换到该主题（仅 UI/31 勾选，旧场景保持 false 不变）")]
        [SerializeField] private bool applyBootMode;

        [SerializeField] private TwinUIThemeMode bootMode = TwinUIThemeMode.Dark;

        /// <summary>UI/31 / UI/32 用：设置 Play 启动时的主题引导。</summary>
        public void SetBootMode(TwinUIThemeMode mode, bool enable)
        {
            bootMode = mode;
            applyBootMode = enable;
        }

        public TwinUIThemeMode BootMode => bootMode;
        public bool ApplyBootMode => applyBootMode;

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                Debug.Log("[THEME] applier OnEnable enabled=" + enabled + " go=" + gameObject.name);
            }

            TwinUITheme.Register(this);
            TwinUITheme.Changed += OnThemeChanged;
        }

        private void OnDisable()
        {
            TwinUITheme.Unregister(this);
            TwinUITheme.Changed -= OnThemeChanged;
        }

        private void Start()
        {
            // Play 启动引导：静态主题在域重载后会回到 Dark，这里按场景里保存的 bootMode 恢复，
            // 保证 UI/31 换肤后进 Play 看到的就是 Ocean。
            if (Application.isPlaying)
            {
                Debug.Log("[THEME] applier start applyBootMode=" + applyBootMode + " bootMode=" + bootMode
                    + " current=" + TwinUITheme.Mode);
                if (applyBootMode && TwinUITheme.Mode != bootMode)
                {
                    TwinUITheme.SetMode(bootMode);
                    Debug.Log("[THEME] applier boot applied=" + bootMode);
                }
            }

            if (applyOnStart)
            {
                Apply();
            }
        }

        private void OnThemeChanged(TwinUIThemeMode mode)
        {
            Apply();
        }

        /// <summary>重刷本 Canvas 下所有注册图元，返回数量。</summary>
        public int Apply()
        {
            int count = 0;
            var tags = GetComponentsInChildren<TwinUIToneTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];
                if (tag == null || tag.Role == TwinUIToneRole.None)
                {
                    continue;
                }

                Color c = TwinUITheme.ColorForRole(tag.Role);
                var img = tag.GetComponent<Graphic>();
                if (img != null)
                {
                    img.color = c;
                    count++;
                    continue;
                }

                var text = tag.GetComponent<TMP_Text>();
                if (text != null)
                {
                    text.color = c;
                    count++;
                }
            }

            var buttons = GetComponentsInChildren<TwinUIButton>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                {
                    buttons[i].ApplyTheme();
                    count++;
                }
            }

            var charts = GetComponentsInChildren<TwinTelemetryChart>(true);
            for (int i = 0; i < charts.Length; i++)
            {
                if (charts[i] != null)
                {
                    charts[i].ApplyTheme();
                    count++;
                }
            }

            return count;
        }
    }
}