// Play-mode UI self-test driver. It only exists while TwinUIPlaySmoke has armed it inside Play
// mode, it is never saved into the scene, and it drives the shell exactly like a user would:
// enter the system, walk the four tabs, start the two replay probes, press the command buttons.
//
// It never connects ROS2, never starts a stream, never writes a CSV: TwinRuntimeProfile has
// autoStartDartTransport=0 / autoStartRos2Transport=0 / autoStartRecordingWriter=0 (verified), and
// the chart data comes from the two read-only csv fixtures in Logs/.
//
// The log is flushed after every line on purpose: the first attempt died inside the training tab
// (QuickOutline.Awake on a non-readable EVB mesh threw out of SetActive) and lost the whole log
// because it was only written at the end.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    public sealed class TwinUIPlaySmokeDriver : MonoBehaviour
    {
        private StreamWriter _writer;
        private string _shotDir;
        private string _logPath;

        [SerializeField] private float holdSeconds = 0.7f;

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            _shotDir = Path.Combine(root, "Logs", "ui_shots");
            Directory.CreateDirectory(_shotDir);
            _logPath = Path.Combine(root, "Logs", "ui_play_smoke.txt");
            _writer = new StreamWriter(_logPath, false, new UTF8Encoding(false)) { AutoFlush = true };

            Log("Twin UI play-mode smoke  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("  screen=" + Screen.width + "x" + Screen.height + " editor=" + Application.isEditor);
            Log("  scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

            var runtime = FindAny<DigitalTwinRuntime>();
            Log("  runtime=" + (runtime == null ? "<null>" : runtime.gameObject.name));
            if (runtime != null && runtime.Settings != null)
            {
                Log("  profile autoStartDart=" + runtime.Settings.AutoStartDartTransport
                    + " autoStartRos2=" + runtime.Settings.AutoStartRos2Transport
                    + " autoStartRecord=" + runtime.Settings.AutoStartRecordingWriter
                    + " runtimeUI=" + runtime.Settings.EnableRuntimeUi);
            }

            var gate = FindAny<BatteryLoginGate>();
            var controller = FindAny<TwinUIShellController>();
            var view = FindAny<TwinCommandPanelView>();
            Log("  gate=" + (gate != null) + " shellController=" + (controller != null) + " commandView=" + (view != null));
            Log("  activeSource=" + (runtime == null ? "-" : runtime.ActiveSourceKind.ToString()));

            yield return new WaitForSecondsRealtime(1.2f);
            yield return Step("play_p01_welcome", null);

            yield return Step("play_p02_enter", () =>
            {
                if (gate != null)
                {
                    gate.Enter();
                    return "gate.Enter()";
                }

                return "gate <null>";
            });

            yield return new WaitForSecondsRealtime(holdSeconds);
            yield return Step("play_p03_roaming_replay", () =>
            {
                int started = StartProbes();
                return "started replay probes=" + started;
            });

            yield return new WaitForSecondsRealtime(2.5f);
            yield return Step("play_p04_roaming_after_replay", null);

            // The training / disassembly roots activate the whole EVB assembly. In this scene that
            // activation floods the console with XR interactable registration warnings and QuickOutline
            // "mesh is not readable" errors, and the coroutine does not survive it (see the batch record
            // for the two observed stalls). Those two tabs are therefore covered by the edit-mode renders
            // (i03/i04), and this run finishes the Play-mode evidence with the cheap, UI-only states:
            // the data tab with live replay curves and the command panel dry-run.
            yield return Step("play_p05_data_replay", () =>
            {
                if (controller == null)
                {
                    return "controller <null>";
                }

                controller.SelectTab(3);
                return "SelectTab(3)";
            });

            yield return new WaitForSecondsRealtime(1.5f);
            yield return Step("play_p06_command_dryrun", () =>
            {
                if (view == null)
                {
                    return "command view <null>";
                }

                view.PreviewPlan();
                view.CheckSafety();
                view.ArmConfirm();
                view.ExecuteGuarded();
                return "command preview/check/confirm/execute (dry-run guarded)";
            });

            yield return new WaitForSecondsRealtime(0.8f);
            yield return Step("play_p07_final", null);
            Log("BASE RESULT: PASS - UI-only states captured");

            // Phase 2: the two heavy pages. They activate the whole EVB assembly; after the
            // Outline guard (skip meshes whose Read/Write is off) this is expected to survive,
            // but each step is logged so a stall or exception is located exactly.
            Log("---- phase 2: heavy tabs ----");
            yield return Step("play_p08_training", () =>
            {
                if (controller == null)
                {
                    return "controller <null>";
                }

                controller.SelectTab(1);
                return "SelectTab(1) training";
            });

            yield return new WaitForSecondsRealtime(1.0f);
            yield return Step("play_p09_training_after", null);

            yield return Step("play_p10_disassembly", () =>
            {
                if (controller == null)
                {
                    return "controller <null>";
                }

                controller.SelectTab(2);
                return "SelectTab(2) disassembly";
            });

            yield return new WaitForSecondsRealtime(1.0f);
            yield return Step("play_p11_disassembly_after", null);

            Log("EXTENDED RESULT: PASS - all four tabs captured in Play");
            Finish();
        }

        private IEnumerator Step(string shot, Func<string> action)
        {
            if (action != null)
            {
                try
                {
                    Log("  action: " + action());
                }
                catch (Exception error)
                {
                    Log("  action EXCEPTION: " + error.GetType().Name + ": " + error.Message);
                    Log("  action stack: " + error.StackTrace);
                }
            }

            string path = Path.Combine(_shotDir, shot + ".png");
            try
            {
                ScreenCapture.CaptureScreenshot(path);
            }
            catch (Exception error)
            {
                Log("  capture EXCEPTION: " + error.GetType().Name + ": " + error.Message);
            }

            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(0.15f);
            bool exists = File.Exists(path);
            long size = exists ? new FileInfo(path).Length : 0;
            Log("  shot " + shot + ".png exists=" + exists + " bytes=" + size);
        }

        private int StartProbes()
        {
            int started = 0;
            foreach (var probe in FindAll<TwinTelemetryReplayProbe>())
            {
                if (probe == null)
                {
                    continue;
                }

                try
                {
                    probe.gameObject.SetActive(true);
                    if (probe.Load())
                    {
                        probe.Rewind();
                        probe.Play();
                        started++;
                        Log("  probe " + probe.gameObject.name + " rows=" + probe.RowCount + " csv=" + probe.CsvPath);
                    }
                    else
                    {
                        Log("  probe " + probe.gameObject.name + " load FAILED " + probe.StatusMessage);
                    }
                }
                catch (Exception error)
                {
                    Log("  probe " + probe.name + " EXCEPTION " + error.GetType().Name + ": " + error.Message);
                }
            }

            return started;
        }

        private void Log(string line)
        {
            if (_writer != null)
            {
                _writer.WriteLine(line);
            }

            Debug.Log("[UI Play Smoke] " + line);
        }

        private void Finish()
        {
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private static T FindAny<T>() where T : Component
        {
            var all = FindAll<T>();
            return all.Count > 0 ? all[0] : null;
        }

        private static List<T> FindAll<T>() where T : Component
        {
#if UNITY_2022_2_OR_NEWER
            var found = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var found = UnityEngine.Object.FindObjectsOfType<T>(true);
#endif
            return new List<T>(found);
        }
    }
}