// 编辑态截图（不进入 Play）：R2 三段 + 三期工业数据台，全部由同一条回放数据链驱动。
//
// 关键点：
//   - 数据源固定为 Replay（读 PaperLogs/<runId>/samples.csv），不是编造的数字；
//   - R2 页的数字通过 TwinUIStatusBinder 的编辑态预览注入同一份快照，保证"截图与绑定代码一致"；
//   - 告警态 = 用真实回放数据 + 临时收紧阈值触发（截图名与 README 都会标注"阈值注入，非实测不合格"）；
//   - 未订阅态 = 切到 Runtime 且无帧；MOCK 态 = 切到 Mock 源并保留水印；
//   - 结束时恢复阈值、主题、页签与遮罩 Canvas，不保存场景。
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalTwin.UI;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIR2Snapshot
    {
        private const string RootPath = "30_界面与交互/UI_二期面板";
        private const string MainCameraPath = "50_玩家与相机/player/Main Camera";
        private const int Width = 1600;
        private const int Height = 900;
        private const string EvidenceDir =
            @"F:\new_2026-\zyq_ST_use\E_TJ\agent_runs\dsh_2026-11-04_ui_industrial\shots";

        private static readonly string[] OverlayPaths =
        {
            "30_界面与交互/Canvas/Canvas_I软著用标题",
            "30_界面与交互/Canvas/Canvas_I",
            "30_界面与交互/Canvas/Canvas_1_V",
            "30_界面与交互/Canvas/Canvas_1 P",
        };

        [MenuItem("DigitalTwin/UI/14 Snapshot R2+Industrial -> PNG (edit mode)", false, 14)]
        public static void Snapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI snapshots (R2 + R3 industrial, replay-driven)  "
                + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("resolution=" + Width + "x" + Height + " output=" + EvidenceDir);

            var camera = TwinSceneLookup.FindComponent<Camera>(MainCameraPath);
            var root = GameObject.Find(RootPath);
            if (camera == null || root == null)
            {
                sb.AppendLine("RESULT: FAIL - camera or UI root missing (run UI/1 Build Unified Shell (R2))");
                Write(sb);
                return;
            }

            var pageHost = root.GetComponent<TwinUIPageHost>();
            var binder = root.GetComponent<TwinUIStatusBinder>();
            var mission = root.transform.Find("MissionRoot");
            var ops = root.transform.Find("TwinOpsRoot");
            var opsView = ops == null ? null : ops.GetComponent<TwinUITwinOpsView>();
            var industrial = root.transform.Find(TwinUIR3Builder.IndustrialName);
            var industrialHost = industrial == null ? null : industrial.GetComponent<TwinUiIndustrialHost>();
            var settings = TwinUIR3Builder.LoadSettings(true);
            if (pageHost == null || binder == null || opsView == null || industrialHost == null || settings == null)
            {
                sb.AppendLine("RESULT: FAIL - pageHost/binder/opsView/industrial/settings missing (run UI/1 + UI/2)");
                Write(sb);
                return;
            }

            // 阈值/主题原值备份，结束时恢复
            var thresholdBackup = TwinUiThresholds.CreateDefault();
            CopyThresholds(settings.thresholds, thresholdBackup);
            var themeBackup = settings.themeMode;
            var sourceBackup = settings.defaultSource;
            var uiHzBackup = settings.uiRefreshHz;

            var replayPath = settings.replayCsvPath;
            if (!File.Exists(replayPath))
            {
                sb.AppendLine("RESULT: FAIL - replay csv missing: " + replayPath);
                Write(sb);
                return;
            }

            sb.AppendLine("replayCsv=" + replayPath);
            var original = new Dictionary<GameObject, bool>();
            foreach (string path in OverlayPaths)
            {
                var go = GameObject.Find(path);
                if (go != null && !original.ContainsKey(go))
                {
                    original[go] = go.activeSelf;
                    go.SetActive(false);
                }
            }

            Directory.CreateDirectory(EvidenceDir);
            RenderTexture previous = camera.targetTexture;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            int shots = 0;

            try
            {
                camera.targetTexture = rt;

                // ---- 1) 回放数据准备：同时喂给工业页与 R2 绑定 ----
                industrialHost.ConfigureForEditor(settings, TwinUiSourceKind.Replay, replayPath);
                var replay = industrialHost.Pipeline.Source as ReplayTwinUiDataSource;
                if (replay == null || !replay.Loaded)
                {
                    sb.AppendLine("RESULT: FAIL - replay load failed: "
                        + (replay == null ? "source null" : replay.LoadError));
                    Write(sb);
                    return;
                }

                sb.AppendLine("replayRows=" + replay.RowCount + " durationS=" + replay.TotalSeconds.ToString("F1"));

                // 1a) 工业数据台曲线：先显示数据页，用回放推进 200 个 UI tick（图表窗口 20 s @10 Hz）
                pageHost.Show(2);
                industrialHost.SetSubPage(0);
                for (int i = 0; i < 200; i++)
                {
                    industrialHost.StepReplayAndRender(0.15);
                }

                // 1b) R2 趋势页曲线：TwinOps 的 push 有真实时间节流，这里按 ~150 ms 节奏喂 80 个点
                for (int i = 0; i < 80; i++)
                {
                    industrialHost.StepReplayAndRender(0.25);
                    ApplyBinderPreview(binder, industrialHost.Pipeline.Snapshot);
                    System.Threading.Thread.Sleep(150);
                    opsView.SetSub(1);
                }

                var first = industrialHost.Pipeline.Snapshot;
                sb.AppendLine("snapshot sampleHz=" + first.Link.SampleHz.ToString("F1")
                    + " applyHz=" + first.Link.ApplyHz.ToString("F1")
                    + " freshnessMs=" + first.Link.FreshnessMs.ToString("F1")
                    + " recvToApplyMs=" + first.Link.ReceiveToApplyMs.ToString("F2")
                    + " jitterP99Ms=" + first.Link.JitterP99Ms.ToString("F2")
                    + " dropped=" + first.Link.DroppedFrames
                    + " caps=" + first.Capabilities);
                if (first.Experiment != null)
                {
                    sb.AppendLine("runId=" + first.Experiment.RunId + " condition=" + first.Experiment.Condition
                        + " session=" + first.Experiment.SessionId + " selfCheck=" + first.Experiment.SelfCheckStatus);
                }

                // ---- 2) R2 五态（深色，回放数据）----
                Capture(camera, rt, texture, "s00_mission_dark_replay", sb, () =>
                {
                    pageHost.Show(0);
                    var view = mission == null ? null : mission.GetComponent<TwinUIMissionView>();
                    if (view != null)
                    {
                        view.Refresh();
                    }
                });
                shots++;

                Capture(camera, rt, texture, "s01_twinops_overview_dark_replay", sb, () =>
                {
                    pageHost.Show(1);
                    opsView.SetSub(0);
                });
                shots++;

                Capture(camera, rt, texture, "s02_twinops_trend_dark_replay", sb, () =>
                {
                    pageHost.Show(1);
                    opsView.SetSub(1);
                });
                shots++;

                Capture(camera, rt, texture, "s03_twinops_control_dark_replay", sb, () =>
                {
                    pageHost.Show(1);
                    opsView.SetSub(2);
                });
                shots++;

                Capture(camera, rt, texture, "s04_twinops_record_dark_replay", sb, () =>
                {
                    pageHost.Show(1);
                    opsView.SetSub(3);
                });
                shots++;

                // ---- 3) 工业数据台：数据 / 事件 / 设置 ----
                pageHost.Show(2);
                industrialHost.SetSubPage(0);
                industrialHost.RefreshNow();
                Capture(camera, rt, texture, "s05_industrial_data_dark_replay", sb, () =>
                {
                    industrialHost.SetSubPage(0);
                    industrialHost.RefreshNow();
                });
                shots++;

                Capture(camera, rt, texture, "s06_industrial_events_dark_replay", sb, () =>
                {
                    industrialHost.SetSubPage(1);
                    industrialHost.RefreshNow();
                });
                shots++;

                Capture(camera, rt, texture, "s07_industrial_settings_dark", sb, () =>
                {
                    industrialHost.SetSubPage(2);
                    industrialHost.RefreshNow();
                });
                shots++;

                // ---- 4) 浅色主题（保留为可切换项）----
                settings.themeMode = TwinUIThemeMode.Light;
                industrialHost.SetTheme(TwinUIThemeMode.Light);
                industrialHost.ApplyThemeNow();
                industrialHost.SetSubPage(0);
                Capture(camera, rt, texture, "s08_industrial_data_light_replay", sb, () =>
                {
                    industrialHost.SetSubPage(0);
                    industrialHost.RefreshNow();
                });
                shots++;

                settings.themeMode = TwinUIThemeMode.Dark;
                industrialHost.SetTheme(TwinUIThemeMode.Dark);
                industrialHost.ApplyThemeNow();

                // ---- 5) 告警态：真实回放数据 + 临时收紧阈值（阈值注入，非实测不合格）----
                settings.thresholds.sampleHzWarn = 150d;
                settings.thresholds.sampleHzCrit = 120d;
                settings.thresholds.jitterP99WarnMs = 0.5d;
                settings.thresholds.jitterP99CritMs = 2d;
                settings.thresholds.freshnessWarnMs = 50d;
                settings.thresholds.freshnessCritMs = 200d;
                for (int i = 0; i < 6; i++)
                {
                    industrialHost.StepReplayAndRender(0.2);
                }

                industrialHost.SetSubPage(0);
                Capture(camera, rt, texture, "s09_industrial_data_alarm_threshold_injection", sb, () =>
                {
                    industrialHost.SetSubPage(0);
                    industrialHost.RefreshNow();
                });
                shots++;

                Capture(camera, rt, texture, "s10_industrial_events_alarm_threshold_injection", sb, () =>
                {
                    industrialHost.SetSubPage(1);
                    industrialHost.RefreshNow();
                });
                shots++;

                int alertCount = industrialHost.Pipeline.Alerts.Count;
                sb.AppendLine("alarmsAfterInjection=" + alertCount + " (threshold injection demo)");
                CopyThresholds(thresholdBackup, settings.thresholds);

                // ---- 6) 未订阅 / 无数据态（切换 Runtime 且无帧）----
                binder.ClearEditorPreview();
                industrialHost.SetSource(TwinUiSourceKind.Runtime);
                industrialHost.SetSubPage(0);
                Capture(camera, rt, texture, "s11_industrial_data_unsubscribed_no_source", sb, () =>
                {
                    industrialHost.SetSubPage(0);
                    industrialHost.RefreshNow();
                });
                shots++;

                // ---- 7) MOCK 水印态 ----
                industrialHost.SetSource(TwinUiSourceKind.Mock);
                industrialHost.SetSubPage(0);
                Capture(camera, rt, texture, "s12_industrial_data_mock_watermark", sb, () =>
                {
                    industrialHost.SetSubPage(0);
                    industrialHost.RefreshNow();
                });
                shots++;

                // ---- 8) 恢复现场 ----
                industrialHost.SetSource(sourceBackup);
                settings.themeMode = themeBackup;
                settings.uiRefreshHz = uiHzBackup;
                TwinUITheme.SetMode(themeBackup);
                industrialHost.ApplyThemeNow();
                binder.ClearEditorPreview();
                pageHost.Show(0);
                CopyThresholds(thresholdBackup, settings.thresholds);

                sb.AppendLine("shots=" + shots + " dir=" + EvidenceDir);
                sb.AppendLine("RESULT: PASS");
            }
            catch (System.Exception e)
            {
                sb.AppendLine("RESULT: FAIL - " + e.GetType().Name + ": " + e.Message);
                sb.AppendLine(e.StackTrace);
                Debug.LogException(e);
            }
            finally
            {
                camera.targetTexture = previous;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(texture);
                foreach (var pair in original)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.SetActive(pair.Value);
                    }
                }
            }

            Write(sb);
        }

        /// <summary>把工业快照转成 R2 绑定快照并注入编辑态预览（只影响显示，不写数据链）。</summary>
        private static void ApplyBinderPreview(TwinUIStatusBinder binder, TwinUiSnapshot s)
        {
            if (binder == null || s == null)
            {
                return;
            }

            var b = new TwinUIStatusBinder.Snapshot
            {
                HasFrame = s.Link.HasFrame,
                Connected = s.Link.Connected,
                SourceKind = s.SourceKind == TwinUiSourceKind.Replay ? "Replay" : s.SourceLabel,
                SourceName = s.SourceLabel,
                Hz = s.Link.SampleHz,
                AgeMs = s.Link.FreshnessMs,
                LatencyMs = s.Link.ReceiveToApplyMs,
                JitterMs = s.Link.JitterP99Ms,
                Dropped = s.Link.DroppedFrames,
                OutOfOrder = s.Link.OutOfOrderFrames,
                Queue = s.Link.QueueLength,
                Quality = s.Link.Quality,
                Clock = s.Link.ClockSync,
                Mode = "Twin",
                Motion = "Idle",
                JointPos = s.Joints.PositionDeg,
                JointVel = s.Joints.VelocityDegPerSec,
                JointTorque = s.Joints.TorqueNm,
                Force = s.Force.Force,
                ControlMode = "ROS2_EXCLUSIVE",
                DryRun = true,
                LastStatus = "replay preview (UI/14, not live hardware)",
                CsvReady = true,
                SessionId = s.Experiment != null ? s.Experiment.SessionId : string.Empty,
                CsvDir = string.Empty,
                Fps = 60f,
                AlarmCount = s.Alerts != null ? s.Alerts.Count : 0
            };

            var alarms = new List<TwinUIStatusBinder.Alarm>();
            if (s.Alerts != null)
            {
                foreach (var a in s.Alerts)
                {
                    if (a.Tone <= (int)TwinUITone.Ok)
                    {
                        continue;
                    }

                    alarms.Add(new TwinUIStatusBinder.Alarm
                    {
                        Text = a.Code + " " + a.Text,
                        Level = a.Tone >= (int)TwinUITone.Crit
                            ? TwinUIStatusBinder.Severity.Crit
                            : TwinUIStatusBinder.Severity.Warn
                    });
                }
            }

            binder.SetEditorPreview(b, alarms);
        }

        private static void CopyThresholds(TwinUiThresholds from, TwinUiThresholds to)
        {
            to.sampleHzWarn = from.sampleHzWarn;
            to.sampleHzCrit = from.sampleHzCrit;
            to.applyHzWarn = from.applyHzWarn;
            to.applyHzCrit = from.applyHzCrit;
            to.freshnessWarnMs = from.freshnessWarnMs;
            to.freshnessCritMs = from.freshnessCritMs;
            to.receiveToApplyWarnMs = from.receiveToApplyWarnMs;
            to.receiveToApplyCritMs = from.receiveToApplyCritMs;
            to.jitterP99WarnMs = from.jitterP99WarnMs;
            to.jitterP99CritMs = from.jitterP99CritMs;
            to.positionErrorWarnDeg = from.positionErrorWarnDeg;
            to.positionErrorCritDeg = from.positionErrorCritDeg;
            to.torqueWarnRatio = from.torqueWarnRatio;
            to.torqueCritRatio = from.torqueCritRatio;
            to.torqueRatedNm = from.torqueRatedNm;
            to.forceWarnN = from.forceWarnN;
            to.forceCritN = from.forceCritN;
            to.calibrated = from.calibrated;
            to.calibrationNote = from.calibrationNote;
        }

        private static void Capture(Camera camera, RenderTexture rt, Texture2D texture, string name,
            StringBuilder sb, System.Action action)
        {
            action();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            byte[] png = texture.EncodeToPNG();
            string path = Path.Combine(EvidenceDir, name + ".png");
            File.WriteAllBytes(path, png);
            sb.AppendLine(name + ".png  bytes=" + png.Length);
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ui_r2_snapshot.txt", sb.ToString());
            File.WriteAllText("Logs/ui_r3_snapshot.txt", sb.ToString());
            try
            {
                Directory.CreateDirectory(EvidenceDir);
                File.WriteAllText(Path.Combine(EvidenceDir, "_snapshot_log.txt"), sb.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UI snapshot] evidence dir copy failed: " + e.Message);
            }

            Debug.Log("[UI snapshot] " + sb.ToString().Replace("\n", " | "));
        }
    }
}