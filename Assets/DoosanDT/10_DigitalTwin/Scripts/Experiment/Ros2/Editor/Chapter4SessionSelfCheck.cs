// 会话自检：只读磁盘，不改任何运行状态、不发任何运动指令。
// 判据来自 E_TJ 库 agent_runs/dsh_2026-10-29/01_操作/操作卡_双端联调.md §5：
//   flush 后 samples.csv 数据行 > 0 是唯一能证明 Unity 真收到样本的判据。
// 新增于 2026-11-03（DSH dsh_2026-11-03 批次）；未改动任何既有文件。
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class Chapter4SessionSelfCheck
    {
        private const string MenuPath = "DigitalTwin/Chapter4/9 Session Self-Check (read disk)";
        private const string ProfileFolder = "Chapter4AutoPilot";

        [MenuItem(MenuPath, false, 900)]
        public static void Run()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string pointerFile = Path.Combine(projectRoot, ProfileFolder, "profile_path.txt");

            if (!File.Exists(pointerFile))
            {
                Debug.LogError("[CH4 SelfCheck] FAIL: 找不到 " + pointerFile +
                               "（还没 Load profile，或 profile_path.txt 未生成）");
                return;
            }

            string profilePath = File.ReadAllText(pointerFile).Trim().Trim('"');
            if (!File.Exists(profilePath))
            {
                Debug.LogError("[CH4 SelfCheck] FAIL: profile.json 不存在: " + profilePath);
                return;
            }

            string json = File.ReadAllText(profilePath);
            string outputDirectory = ExtractString(json, "outputDirectory");
            string runId = ExtractString(json, "runId");

            if (string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(runId))
            {
                Debug.LogError("[CH4 SelfCheck] FAIL: profile.json 缺少 outputDirectory 或 runId");
                return;
            }

            string sessionDir = Path.Combine(outputDirectory, runId);
            string samplesPath = Path.Combine(sessionDir, "samples.csv");
            string renderPath = Path.Combine(sessionDir, "render_frames.csv");
            string qualityPath = Path.Combine(sessionDir, "quality.json");

            int samplesRows = CountDataRows(samplesPath);
            int renderRows = CountDataRows(renderPath);
            bool qualityExists = File.Exists(qualityPath);

            bool pass = samplesRows > 0;   // 唯一判据
            string verdict = pass ? "PASS" : "FAIL";

            string line = string.Format(
                "[CH4 SelfCheck] verdict={0} runId={1} samples_rows={2} render_rows={3} quality.json={4} dir={5}",
                verdict, runId, samplesRows, renderRows, qualityExists ? "present" : "MISSING", sessionDir);

            if (pass) Debug.Log(line);
            else Debug.LogError(line + "  ← 该会话不可作为证据，按操作卡 §6 处置");

            if (!Directory.Exists(sessionDir)) Debug.LogError("[CH4 SelfCheck] 会话目录不存在: " + sessionDir);
            if (samplesRows > 0 && !qualityExists)
                Debug.LogWarning("[CH4 SelfCheck] samples.csv 有数据但 quality.json 缺失 → 记录可能未 Sealed");
        }

        // 与 ROS 侧无关的极简 JSON 取值：只处理本 profile.json 的两种键
        private static string ExtractString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        // 只数数据行：跳过表头与空行；文件缺失返回 0（不抛异常，便于自检报告缺什么）
        private static int CountDataRows(string path)
        {
            if (!File.Exists(path)) return 0;
            int rows = 0;
            bool headerSkipped = false;
            foreach (string raw in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!headerSkipped) { headerSkipped = true; continue; }
                rows++;
            }
            return rows;
        }
    }
}
#endif