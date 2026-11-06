// 实验设置面板（只读 + 设置写回）：UI Hz / 导出 Hz / 主题 / 密度 / 数据源 / 阈值组。
// 点按钮 = 改 TwinUiSettings（编辑器内立即 SetDirty + SaveAssets，运行时只改内存），
// 所有改动都发一条 settings 事件到总线，便于答辩时回看"改了什么、什么时候改的"。
using TMPro;
using UnityEngine;

namespace DigitalTwin.UI
{
    public sealed class TwinUiSettingsView : MonoBehaviour
    {
        private TMP_Text[] _refreshInfo = new TMP_Text[3];
        private TMP_Text[] _themeInfo = new TMP_Text[2];
        private TMP_Text[] _sourceInfo = new TMP_Text[2];
        private TMP_Text _saveState;
        private TMP_Text[] _thresholdLabel = new TMP_Text[12];
        private TMP_Text[] _thresholdValue = new TMP_Text[12];
        private TMP_Text _thresholdNote;
        private TwinUIButton[] _hzButtons = new TwinUIButton[5];
        private TwinUIButton _darkButton;
        private TwinUIButton _lightButton;
        private TwinUIButton _comfortButton;
        private TwinUIButton _compactButton;
        private TwinUIButton _runtimeButton;
        private TwinUIButton _replayButton;
        private TwinUIButton _mockButton;
        private bool _resolved;

        public void Bind(TwinUiSettings settings, TwinUiIndustrialHost host)
        {
            Resolve();
            if (host == null)
            {
                return;
            }

            for (int i = 0; i < _hzButtons.Length; i++)
            {
                float hz = TwinUITextR3.UiHzOptions[i];
                Wire(_hzButtons[i], () => host.SetUiRefreshHz(hz));
            }

            Wire(_darkButton, () => host.SetTheme(TwinUIThemeMode.Dark));
            Wire(_lightButton, () => host.SetTheme(TwinUIThemeMode.Light));
            Wire(_comfortButton, () => host.SetDensity(TwinUIDensity.Comfortable));
            Wire(_compactButton, () => host.SetDensity(TwinUIDensity.Compact));
            Wire(_runtimeButton, () => host.SetSource(TwinUiSourceKind.Runtime));
            Wire(_replayButton, () => host.SetSource(TwinUiSourceKind.Replay));
            Wire(_mockButton, () => host.SetSource(TwinUiSourceKind.Mock));
        }

        public void Render(TwinUiSettings settings, TwinUiPipeline pipeline)
        {
            Resolve();
            if (settings == null)
            {
                return;
            }

            Set(_refreshInfo[0], settings.uiRefreshHz.ToString("F0") + " Hz");
            Set(_refreshInfo[1], settings.snapshotExportHz.ToString("F0") + " Hz");
            Set(_refreshInfo[2], settings.robotApplyRateHz.ToString("F0") + " Hz");
            Set(_themeInfo[0], TwinUITheme.ModeLabel);
            Set(_themeInfo[1], settings.density == TwinUIDensity.Compact ? "紧凑" : "标准");
            Set(_sourceInfo[0], settings.defaultSource == TwinUiSourceKind.Replay ? TwinUITextR3.CtxReplay
                : settings.defaultSource == TwinUiSourceKind.Mock ? TwinUITextR3.CtxMock
                : settings.defaultSource == TwinUiSourceKind.Runtime ? TwinUITextR3.CtxRuntime : TwinUITextR3.NoSource);
            Set(_sourceInfo[1], settings.replayCsvPath);

            for (int i = 0; i < _hzButtons.Length; i++)
            {
                if (_hzButtons[i] != null)
                {
                    _hzButtons[i].SetSelected(Mathf.Abs(settings.uiRefreshHz - TwinUITextR3.UiHzOptions[i]) < 0.01f);
                }
            }

            Select(_darkButton, TwinUITheme.Mode == TwinUIThemeMode.Dark);
            Select(_lightButton, TwinUITheme.Mode == TwinUIThemeMode.Light);
            Select(_comfortButton, settings.density == TwinUIDensity.Comfortable);
            Select(_compactButton, settings.density == TwinUIDensity.Compact);
            Select(_runtimeButton, settings.defaultSource == TwinUiSourceKind.Runtime);
            Select(_replayButton, settings.defaultSource == TwinUiSourceKind.Replay);
            Select(_mockButton, settings.defaultSource == TwinUiSourceKind.Mock);

            if (_saveState != null)
            {
                _saveState.text = "改动在编辑器内写回 TwinUiSettings.asset，并立即生效";
                _saveState.color = TwinUIStyle.MutedColor;
            }

            RenderThresholds(settings.thresholds, pipeline);
        }

        private void RenderThresholds(TwinUiThresholds t, TwinUiPipeline pipeline)
        {
            if (t == null)
            {
                return;
            }

            SetThreshold(0, "采样 Hz（黄/红）", "< " + t.sampleHzWarn.ToString("F0") + " / < " + t.sampleHzCrit.ToString("F0"));
            SetThreshold(1, "应用 Hz（黄/红）", "< " + t.applyHzWarn.ToString("F0") + " / < " + t.applyHzCrit.ToString("F0"));
            SetThreshold(2, "新鲜度（黄/红）", "> " + t.freshnessWarnMs.ToString("F0") + " / > " + t.freshnessCritMs.ToString("F0") + " ms");
            SetThreshold(3, "接收->应用（黄/红）", "> " + t.receiveToApplyWarnMs.ToString("F0") + " / > " + t.receiveToApplyCritMs.ToString("F0") + " ms");
            SetThreshold(4, "抖动 p99（黄/红）", "> " + t.jitterP99WarnMs.ToString("F1") + " / > " + t.jitterP99CritMs.ToString("F1") + " ms");
            SetThreshold(5, "位置误差（黄/红）", "> " + t.positionErrorWarnDeg.ToString("F2") + " / > " + t.positionErrorCritDeg.ToString("F2") + " °");
            SetThreshold(6, "力矩占比（黄/红）", "> " + (t.torqueWarnRatio * 100d).ToString("F0") + " / > " + (t.torqueCritRatio * 100d).ToString("F0") + " %");
            SetThreshold(7, "力矩额定值", t.torqueRatedNm.ToString("F0") + " N·m");
            SetThreshold(8, "六维力（黄/红）", "> " + t.forceWarnN.ToString("F0") + " / > " + t.forceCritN.ToString("F0") + " N");
            SetThreshold(9, "标定状态", t.calibrated ? "已标定" : "未标定（待校准）");
            SetThreshold(10, "当前告警数", pipeline == null ? "-" : pipeline.Alerts.Count.ToString());
            SetThreshold(11, "写入文件", "Logs/ui_snapshot.json / Logs/ui_events.jsonl");

            if (_thresholdNote != null)
            {
                _thresholdNote.text = TwinUITextR3.NoteCalibrated;
                _thresholdNote.color = TwinUIStyle.WarnColor;
            }
        }

        private void SetThreshold(int i, string label, string value)
        {
            if (i >= 0 && i < _thresholdLabel.Length && _thresholdLabel[i] != null)
            {
                _thresholdLabel[i].text = label;
                _thresholdLabel[i].color = TwinUIStyle.MutedColor;
            }

            if (i >= 0 && i < _thresholdValue.Length && _thresholdValue[i] != null)
            {
                _thresholdValue[i].text = value;
                _thresholdValue[i].color = TwinUIStyle.TextColor;
            }
        }

        private static void Select(TwinUIButton button, bool selected)
        {
            if (button != null)
            {
                button.SetSelected(selected);
            }
        }

        private static void Wire(TwinUIButton button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || button.Button == null)
            {
                return;
            }

            button.Button.onClick.RemoveAllListeners();
            button.Button.onClick.AddListener(action);
        }

        private static void Set(TMP_Text text, string value)
        {
            if (text == null)
            {
                return;
            }

            if (text.text != value)
            {
                text.text = value;
            }

            text.color = TwinUIStyle.TextColor;
        }

        private void Resolve()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            for (int i = 0; i < 3; i++)
            {
                _refreshInfo[i] = T("RefreshCard/Info" + i + "/Value");
            }

            for (int i = 0; i < 2; i++)
            {
                _themeInfo[i] = T("ThemeCard/Info" + i + "/Value");
            }

            _sourceInfo[0] = T("SourceCard/Info0/Value");
            _sourceInfo[1] = T("SourceCard/Info1/Value");
            _saveState = T("RefreshCard/SaveState");

            for (int i = 0; i < 12; i++)
            {
                _thresholdLabel[i] = T("ThresholdCard/Row" + i + "/Label");
                _thresholdValue[i] = T("ThresholdCard/Row" + i + "/Value");
            }

            _thresholdNote = T("ThresholdCard/Note");

            string[] hzNames = { "Hz5", "Hz10", "Hz20", "Hz30", "Hz60" };
            for (int i = 0; i < 5; i++)
            {
                _hzButtons[i] = FindButton("RefreshCard/" + hzNames[i]);
            }

            _darkButton = FindButton("ThemeCard/BtnThemeDark");
            _lightButton = FindButton("ThemeCard/BtnThemeLight");
            _comfortButton = FindButton("ThemeCard/BtnDensityComfort");
            _compactButton = FindButton("ThemeCard/BtnDensityCompact");
            _runtimeButton = FindButton("SourceCard/BtnSourceRuntime");
            _replayButton = FindButton("SourceCard/BtnSourceReplay");
            _mockButton = FindButton("SourceCard/BtnSourceMock");
        }

        private TMP_Text T(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<TMP_Text>();
        }

        private TwinUIButton FindButton(string path)
        {
            var t = transform.Find(path);
            return t == null ? null : t.GetComponent<TwinUIButton>();
        }
    }
}