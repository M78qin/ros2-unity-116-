// TwinUiLayoutContractStage.cs —— P10 自检（UI/54，auto-exit）
// 覆盖 2026-11-08 批准的响应式契约：1/2 列滞回、卡宽=列宽、localScale 恒 1（响应式卡）、
//   卡高 = 54 + 模块测量高、内容宽 = 卡宽 - 24、行内对齐、无横向越界、浮窗不加 DesignScaler。
// 边界：只读页面/卡片实际 Rect 与模块测量值；不改场景、不存场景、不改用户 Prefs（结束前 ResetForCheck）。
// 结果写 Logs/ui54_layout_contract.txt，并复制到 agent_runs 证据目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUiLayoutContractStage
    {
        public const string EvidenceRoot =
            @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-09_thesis_delivery\ui\testout\ui54";

        public const string LogPath = "Logs/ui54_layout_contract.txt";

        [MenuItem("DigitalTwin/UI/54 Layout Contract Check (auto-exit)", false, 540)]
        public static void Run()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[P10] UI/54 skipped: already in play mode");
                return;
            }

            TwinUiLayoutContractCheck.Reset();
            SessionState.SetBool(TwinUiLayoutContractCheck.PendingKey, true);
            Debug.Log("[P10] UI/54 armed; log=" + LogPath);
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// 恢复工具：清除场景 in-memory dirty 标记（不保存、不改内容）。
        /// 用于 Play 期间域重载导致 dirty 残留、而磁盘场景哈希仍与基线一致的场景。
        /// </summary>
        [MenuItem("DigitalTwin/UI/54b Clear Scene Dirty (recovery, no save)", false, 541)]
        public static void ClearSceneDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool ok = false;
            try
            {
                var method = typeof(UnityEditor.SceneManagement.EditorSceneManager).GetMethod(
                    "ClearSceneDirtiness",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(null, new object[] { scene });
                    ok = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P10] clear dirty failed: " + e.Message);
            }

            Debug.Log("[P10] clear scene dirty ok=" + ok + " scene=" + scene.name
                + " path=" + scene.path);
        }
    }

    public static class TwinUiLayoutContractCheck
    {
        public const string PendingKey = "TwinUiLayoutContractCheck.Pending";

        private const string EvidenceLogs = TwinUiLayoutContractStage.EvidenceRoot + @"\logs";
        private const string EvidenceShots = TwinUiLayoutContractStage.EvidenceRoot + @"\shots";
        private const float Tol = 1f;

        private struct Item
        {
            public string Label;
            public string Detail;
            public bool Ok;
        }

        private static readonly List<Item> Items = new List<Item>();
        private static readonly List<string> Shots = new List<string>();
        private static readonly Queue<Action> Steps = new Queue<Action>();
        private static readonly int[] Widths = { 720, 900, 1200, 1600 };

        private static TwinUiShellV2 _shell;
        private static TwinUiSurfaceHost _surface;
        private static int _logErrors;
        private static int _logExceptions;
        private static float _nextAt;
        private static bool _started;
        private static bool _finished;
        private static int _widthIndex;

        public static void Reset()
        {
            Items.Clear();
            Shots.Clear();
            Steps.Clear();
            _started = false;
            _finished = false;
            _widthIndex = 0;
            _logErrors = 0;
            _logExceptions = 0;
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying || _finished)
            {
                return;
            }

            // 看门狗：Play 已起但宿主 45s 内没就绪 → 记录并退出，避免把编辑器卡在 Play。
            if (!_started && Time.realtimeSinceStartup > 45f)
            {
                Debug.LogError("[P10] UI/54 watchdog: shell not ready within 45s, exiting play mode");
                SessionState.SetBool(PendingKey, false);
                _finished = true;
                EditorApplication.ExitPlaymode();
                return;
            }

            if (!_started)
            {
                if (Time.realtimeSinceStartup < 2f)
                {
                    return;
                }

                var window = UnityEngine.Object.FindObjectOfType<TwinUiWindowHost>();
                _shell = UnityEngine.Object.FindObjectOfType<TwinUiShellV2>();
                _surface = TwinUiSurfaceHost.Instance;
                if (window == null || _shell == null || _surface == null || _surface.Page == null
                    || _surface.Page.CardCount == 0)
                {
                    return;
                }

                _started = true;
                _nextAt = Time.realtimeSinceStartup + 0.5f;
                Application.logMessageReceived += OnLog;
                Enqueue();
                Debug.Log("[P10] UI/54 started surface=" + _surface.Describe());
                return;
            }

            if (Time.realtimeSinceStartup < _nextAt)
            {
                return;
            }

            if (Steps.Count == 0)
            {
                Finish();
                return;
            }

            var step = Steps.Dequeue();
            try
            {
                step();
            }
            catch (Exception e)
            {
                Add("step exception", e.GetType().Name + ": " + e.Message, false);
            }

            _nextAt = Time.realtimeSinceStartup + 0.25f;
        }

        private static void Enqueue()
        {
            Steps.Enqueue(Prepare);
            for (int i = 0; i < Widths.Length; i++)
            {
                int w = Widths[i];
                Steps.Enqueue(() => CheckWidth(w));
            }

            Steps.Enqueue(CheckFloating);
            Steps.Enqueue(Cleanup);
        }

        private static void Prepare()
        {
            _surface.ResetForCheck();
            _surface.SetTab(TwinUiWorkspaceRegistry.Twin);
            Add("shell mode", TwinUiShellMode.Kind.ToString(), true);
            Add("page cards", _surface.Page.CardCount.ToString(), _surface.Page.CardCount > 0);
        }

        private static void CheckWidth(int width)
        {
            _widthIndex++;
            float pageH = 900f;
            _surface.ApplyPageLayout(width, pageH);

            var page = _surface.Page;
            float c = TwinUiLayoutContract.C(width);
            float expectedColumns = ExpectedColumns(_widthIndex, c);
            float colW = TwinUiLayoutContract.ColumnWidth(c, (int)expectedColumns);

            Add("W" + width + " C", "c=" + c.ToString("F1") + " cols=" + page.Columns
                + " expected=" + expectedColumns, Mathf.Abs(page.Columns - expectedColumns) < 0.1f);
            Add("W" + width + " pageWidth", "page.C=" + page.ContentWidth.ToString("F1")
                + " contract.C=" + c.ToString("F1"), Mathf.Abs(page.ContentWidth - c) <= Tol);

            int checkedCards = 0;
            int scaleViolations = 0;
            int widthViolations = 0;
            int heightViolations = 0;
            int overflowViolations = 0;
            int legacy = 0;
            int cappedLegacy = 0;
            var rows = new Dictionary<int, float>();

            for (int i = 0; i < page.Cards.Count; i++)
            {
                var card = page.Cards[i];
                if (card == null || !card.gameObject.activeSelf)
                {
                    continue;
                }

                checkedCards++;
                var module = TwinUiSurfaceHost.FindModule(card.ModuleId);
                RectTransform rt = card.Root;
                Rect rect = rt.rect;
                float visualW = rect.width * rt.localScale.x;
                float expectedW = module != null && module.WideContent && page.Columns >= 2
                    ? TwinUiLayoutContract.WideWidth(c)
                    : colW;

                if (module != null && module.ResponsiveContent)
                {
                    if (Mathf.Abs(rt.localScale.x - 1f) > 0.001f || Mathf.Abs(rt.localScale.y - 1f) > 0.001f)
                    {
                        scaleViolations++;
                    }

                    // 响应式卡：视觉宽必须等于分配列宽（跨列卡 = C）
                    if (Mathf.Abs(visualW - expectedW) > Tol)
                    {
                        widthViolations++;
                    }

                    float contentW = TwinUiLayoutContract.ContentWidth(expectedW);
                    float measured = module.MeasureContentHeight(contentW, TwinUiModuleSurface.Card);
                    float expectedH = TwinUiLayoutContract.CardHeight(module, contentW, TwinUiModuleSurface.Card);
                    // Measurement is the minimum needed by this content; a row
                    // may allocate extra height to align adjacent card bottoms.
                    if (rect.height < expectedH - Tol
                        || card.Content.rect.height
                            < TwinUiLayoutContract.ClampContentHeight(measured) - Tol)
                    {
                        heightViolations++;
                    }
                }
                else
                {
                    // Legacy 卡（D4 决策）：设计尺寸 × clamp(列宽/设计宽, .62, 1.40)，铺不满列宽属预期。
                    legacy++;
                    float s = rt.localScale.x;
                    float designW = rect.width > 1f ? rect.width : colW;
                    float clamped = Mathf.Clamp(colW / designW, TwinUiCardGrid.MinScale, TwinUiCardGrid.MaxScale);
                    if (Mathf.Abs(s - clamped) > 0.002f)
                    {
                        scaleViolations++;
                    }

                    if (Mathf.Abs(visualW - designW * s) > Tol)
                    {
                        widthViolations++;
                    }

                    if (visualW < colW - Tol)
                    {
                        cappedLegacy++;
                    }
                }

                float centerX = rt.anchoredPosition.x;
                if (centerX - visualW * 0.5f < -Tol || centerX + visualW * 0.5f > c + Tol)
                {
                    overflowViolations++;
                }

                int rowKey = Mathf.RoundToInt(-rt.anchoredPosition.y);
                float top = -rt.anchoredPosition.y;
                if (!rows.ContainsKey(rowKey))
                {
                    rows[rowKey] = top;
                }
            }

            Add("W" + width + " cards", "visible=" + checkedCards, checkedCards > 0);
            Add("W" + width + " cardWidth", "violations=" + widthViolations, widthViolations == 0);
            Add("W" + width + " legacy mixed", "legacy=" + legacy + " capped(未铺满列)=" + cappedLegacy,
                legacy > 0);
            Add("W" + width + " localScale", "violations=" + scaleViolations, scaleViolations == 0);
            Add("W" + width + " cardHeight", "violations=" + heightViolations, heightViolations == 0);
            Add("W" + width + " overflow", "violations=" + overflowViolations, overflowViolations == 0);

            float pageH2 = page.ContentHeight;
            Add("W" + width + " pageHeight", "contentH=" + pageH2.ToString("F1"),
                pageH2 > 200f && pageH2 > page.CardArea.rect.height);
            Shot("ui54_" + width + ".png");
        }

        private static float ExpectedColumns(int index, float c)
        {
            // 序列 720→1, 900→2(从 1 列跨 820), 1200→2, 1600→2（滞回：2 列要 <780 才降）
            if (index <= 1)
            {
                return c >= TwinUiLayoutContract.GrowToTwoColumns ? 2f : 1f;
            }

            return c < TwinUiLayoutContract.ShrinkToOneColumn ? 1f : 2f;
        }

        private static void CheckFloating()
        {
            var module = TwinUiSurfaceHost.FindModule(TwinUiWidgetJoints.ModuleId);
            Add("joints responsive", module != null && module.ResponsiveContent ? "true" : "false",
                module != null && module.ResponsiveContent);

            _surface.Detach(TwinUiWidgetJoints.ModuleId);
            Add("floating open", "ids=" + _surface.FloatingIds.Count, _surface.FloatingIds.Count == 1);

            if (module == null || module.View == null)
            {
                Add("floating view", "view=null", false);
                return;
            }

            var scaler = module.View.GetComponent<TwinUiDesignScaler>();
            Add("floating no scaler", scaler == null ? "none" : "DesignScaler present", scaler == null);

            float w = Mathf.Max(80f, TwinUiCardView.ContentPadX);
            Rect vr = module.View.rect;
            Add("floating content", "w=" + vr.width.ToString("F1") + "h=" + vr.height.ToString("F1")
                + " pad=" + w.ToString("F0"), vr.width > 100f && vr.height > 60f);
            Add("floating scale", "scale=" + module.View.localScale.x.ToString("F3"),
                Mathf.Abs(module.View.localScale.x - 1f) < 0.001f);
            Add("floating mode", module is TwinUiWidgetJoints j ? j.LastMode.ToString() : "n/a", true);

            _surface.DockBack(TwinUiWidgetJoints.ModuleId);
        }

        private static void Cleanup()
        {
            _surface.ResetForCheck();
            Add("cleanup", "reset done", true);
        }

        private static void Shot(string name)
        {
            try
            {
                Directory.CreateDirectory(EvidenceShots);
                string path = Path.Combine(EvidenceShots, name);
                ScreenCapture.CaptureScreenshot(path);
                Shots.Add(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P10] shot failed: " + e.Message);
            }
        }

        private static void Add(string label, string detail, bool ok)
        {
            Items.Add(new Item { Label = label, Detail = detail, Ok = ok });
            Debug.Log("[P10] [item] " + label + " | " + detail + " | " + (ok ? "PASS" : "FAIL"));
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception)
            {
                _logExceptions++;
            }
            else if (type == LogType.Error)
            {
                _logErrors++;
            }
        }

        private static void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool(PendingKey, false);

            int fails = 0;
            var sb = new StringBuilder();
            sb.AppendLine("=== Layout Contract Check (UI/54)  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("shellMode=" + TwinUiShellMode.Kind
                + " columns=" + TwinUiLayoutContract.GrowToTwoColumns + "/" + TwinUiLayoutContract.ShrinkToOneColumn
                + " gap=" + TwinUiLayoutContract.Gap
                + " chrome=" + TwinUiCardView.ChromeHeight
                + " padX=" + TwinUiCardView.ContentPadX);
            foreach (var item in Items)
            {
                sb.AppendLine("[P10] [item] " + item.Label + " | " + item.Detail + " | result="
                    + (item.Ok ? "PASS" : "FAIL"));
                if (!item.Ok)
                {
                    fails++;
                }
            }

            sb.AppendLine("[P10] logErrors=" + _logErrors + " logExceptions=" + _logExceptions);
            sb.AppendLine("[P10] shots=" + Shots.Count);
            sb.AppendLine("[P10] RESULT: " + (fails == 0 ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TwinUiLayoutContractStage.LogPath));
                File.WriteAllText(TwinUiLayoutContractStage.LogPath, sb.ToString(), Encoding.UTF8);
                Directory.CreateDirectory(EvidenceLogs);
                File.Copy(TwinUiLayoutContractStage.LogPath,
                    Path.Combine(EvidenceLogs, "ui54_layout_contract.txt"), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P10] log write failed: " + e.Message);
            }

            Debug.Log("[P10] UI/54 RESULT " + (fails == 0 ? "PASS" : "FAIL") + " items=" + Items.Count
                + " fails=" + fails);
            EditorApplication.ExitPlaymode();
        }
    }
}
