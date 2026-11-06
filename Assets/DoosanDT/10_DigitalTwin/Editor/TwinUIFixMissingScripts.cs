// Idempotent repair of the two lost MonoBehaviour shells plus the welcome page wiring.
//
// Menu: DigitalTwin/UI/2 Fix Missing Scripts
//
// What it does (each step is logged with the object path so the change is auditable):
//   1. 30_界面与交互/Canvas/登录界面        - remove the missing script, attach BatteryLoginGate
//   2. 50_玩家与相机/.../Gaze Interactor    - remove the missing script (it had no serialized
//      fields; the object already carries a stock XRGazeInteractor, so nothing is lost)
//   3. welcome page copy                    - title / enter / quit buttons, inputs hidden
//      (per this batch's decision the page is a WELCOME PAGE, it collects no credentials)
//   4. TMP fonts                            - every CJK text moved to the project SC atlas
//      (LiberationSans/simsun have no CJK glyphs; text rendered as tofu before this fix)
//
// Nothing here touches the data chain, the robot hierarchy or VR_EVB_DL.
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DigitalTwin.EditorTools
{
    public static class TwinUIFixMissingScripts
    {
        private const string LogPath = "Logs/ui_fix_missing.txt";

        private const string WelcomePath = "30_界面与交互/Canvas/登录界面";
        private const string LoginPanelPath = "30_界面与交互/Canvas/Canvas_I软著用标题/LoginPanel";
        private const string GazePath = "50_玩家与相机/player/XR Origin (XR Rig)/Camera Offset/Gaze Interactor";
        private const string ShellPath = "30_界面与交互/UI_统一入口面板";

        [MenuItem("DigitalTwin/UI/2 Fix Missing Scripts")]
        public static void Fix()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Twin UI missing-script repair  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                sb.AppendLine("RESULT: FAIL - no scene loaded");
                Write(sb);
                return;
            }

            sb.AppendLine("scene=" + scene.path);
            sb.AppendLine();

            int changes = 0;
            changes += FixWelcomeGate(sb);
            changes += FixGazeInteractor(sb);
            changes += FixWelcomeCopy(sb);
            changes += FixFonts(sb);

            if (changes > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                sb.AppendLine("scene saved, changes=" + changes);
            }
            else
            {
                sb.AppendLine("no change required (already fixed)");
            }

            int missing = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    missing += TwinSceneLookup.CountMissing(t.gameObject);
                }
            }

            sb.AppendLine("missingScriptInstances=" + missing);
            sb.AppendLine("RESULT: " + (missing == 0 ? "PASS" : "FAIL - " + missing + " remain"));
            Write(sb);
            Debug.Log("[UI Fix] " + (missing == 0 ? "PASS" : "FAIL") + " - see " + LogPath);
        }

        // ------------------------------------------------------------------ steps

        private static int FixWelcomeGate(StringBuilder sb)
        {
            int changes = 0;
            GameObject welcome = TwinSceneLookup.FindObject(WelcomePath);
            if (welcome == null)
            {
                sb.AppendLine("[gate] MISSING object " + WelcomePath);
                return 0;
            }

            int removed = TwinSceneLookup.RemoveMissingScripts(welcome);
            if (removed > 0)
            {
                sb.AppendLine("[gate] removed " + removed + " missing script component(s) from " + WelcomePath);
                changes++;
            }

            BatteryLoginGate gate = welcome.GetComponent<BatteryLoginGate>();
            if (gate == null)
            {
                gate = welcome.AddComponent<BatteryLoginGate>();
                sb.AppendLine("[gate] added BatteryLoginGate to " + WelcomePath);
                changes++;
            }

            GameObject loginPanel = TwinSceneLookup.FindObject(LoginPanelPath);
            GameObject shell = TwinSceneLookup.FindObject(ShellPath);
            Button enter = TwinSceneLookup.FindComponent<Button>(LoginPanelPath + "/LoginButton");
            Button quit = TwinSceneLookup.FindComponent<Button>(LoginPanelPath + "/CancelButton");
            TMP_Text title = TwinSceneLookup.FindComponent<TMP_Text>(LoginPanelPath + "/LoginTitle");
            GameObject account = TwinSceneLookup.FindObject(LoginPanelPath + "/AccountInput");
            GameObject password = TwinSceneLookup.FindObject(LoginPanelPath + "/PasswordInput");

            if (loginPanel == null)
            {
                sb.AppendLine("[gate] WARNING login panel not found at " + LoginPanelPath);
            }

            if (enter == null || quit == null)
            {
                sb.AppendLine("[gate] WARNING enter/quit buttons not found under " + LoginPanelPath);
            }

            gate.Configure(
                loginPanel,
                shell,
                enter,
                quit,
                title,
                null,
                new[] { account, password });
            EditorUtility.SetDirty(gate);
            sb.AppendLine("[gate] wired: loginPanel=" + (loginPanel != null) + " shell=" + (shell != null)
                + " enter=" + (enter != null) + " quit=" + (quit != null) + " title=" + (title != null));
            changes++;

            changes += AddPersistent(enter, gate, "Enter", sb);
            changes += AddPersistent(quit, gate, "Quit", sb);

            // the welcome page collects nothing: hide the credential inputs
            if (account != null && account.activeSelf)
            {
                account.SetActive(false);
                sb.AppendLine("[gate] hid AccountInput (welcome page collects no credentials)");
                changes++;
            }

            if (password != null && password.activeSelf)
            {
                password.SetActive(false);
                sb.AppendLine("[gate] hid PasswordInput (welcome page collects no credentials)");
                changes++;
            }

            return changes;
        }

        private static int AddPersistent(Button button, Object target, string method, StringBuilder sb)
        {
            if (button == null || target == null)
            {
                return 0;
            }

            if (button.onClick.GetPersistentEventCount() > 0)
            {
                sb.AppendLine("[gate] " + TwinSceneLookup.PathOf(button.transform)
                    + " already has " + button.onClick.GetPersistentEventCount() + " persistent call(s), left as is");
                return 0;
            }

            UnityEventTools.AddVoidPersistentListener(button.onClick, CreateAction(target, method));
            EditorUtility.SetDirty(button);
            sb.AppendLine("[gate] bound " + TwinSceneLookup.PathOf(button.transform) + " -> " + method + "()");
            return 1;
        }

        private static UnityEngine.Events.UnityAction CreateAction(Object target, string method)
        {
            var gate = (BatteryLoginGate)target;
            return method == "Quit" ? gate.Quit : (UnityEngine.Events.UnityAction)gate.Enter;
        }

        private static int FixGazeInteractor(StringBuilder sb)
        {
            GameObject gaze = TwinSceneLookup.FindObject(GazePath);
            if (gaze == null)
            {
                sb.AppendLine("[gaze] object not found: " + GazePath);
                return 0;
            }

            int removed = TwinSceneLookup.RemoveMissingScripts(gaze);
            if (removed == 0)
            {
                sb.AppendLine("[gaze] no missing script left on " + GazePath);
                return 0;
            }

            sb.AppendLine("[gaze] removed " + removed + " missing script component(s) from " + GazePath
                + " (the object keeps its stock XRGazeInteractor; the lost component had no serialized fields)");
            EditorUtility.SetDirty(gaze);
            return 1;
        }

        private static int FixWelcomeCopy(StringBuilder sb)
        {
            int changes = 0;
            if (!TwinUIFontCheck.IsInsideGlyphSet(TwinUIFontCheck.LoadScFont(), TwinUIShellText.ProductTitle))
            {
                sb.AppendLine("[copy] WARNING ProductTitle has characters outside the glyph set, copy not applied");
                return 0;
            }

            TMP_Text title = TwinSceneLookup.FindComponent<TMP_Text>(LoginPanelPath + "/LoginTitle");
            if (title != null && title.text != TwinUIShellText.ProductTitle)
            {
                title.text = TwinUIShellText.ProductTitle;
                title.enableAutoSizing = true;
                title.fontSizeMin = 12f;
                title.fontSizeMax = 32f;
                EditorUtility.SetDirty(title);
                sb.AppendLine("[copy] LoginTitle -> \"" + TwinUIShellText.ProductTitle + "\"");
                changes++;
            }

            Button enter = TwinSceneLookup.FindComponent<Button>(LoginPanelPath + "/LoginButton");
            TMP_Text enterLabel = enter == null ? null : enter.GetComponentInChildren<TMP_Text>(true);
            if (enterLabel != null && enterLabel.text != TwinUIShellText.Enter)
            {
                sb.AppendLine("[copy] LoginButton text \"" + enterLabel.text + "\" -> \"" + TwinUIShellText.Enter + "\"");
                enterLabel.text = TwinUIShellText.Enter;
                EditorUtility.SetDirty(enterLabel);
                changes++;
            }

            Button quit = TwinSceneLookup.FindComponent<Button>(LoginPanelPath + "/CancelButton");
            TMP_Text quitLabel = quit == null ? null : quit.GetComponentInChildren<TMP_Text>(true);
            if (quitLabel != null && quitLabel.text != TwinUIShellText.Quit)
            {
                sb.AppendLine("[copy] CancelButton text \"" + quitLabel.text + "\" -> \"" + TwinUIShellText.Quit + "\"");
                quitLabel.text = TwinUIShellText.Quit;
                EditorUtility.SetDirty(quitLabel);
                changes++;
            }

            return changes;
        }

        private static int FixFonts(StringBuilder sb)
        {
            TMP_FontAsset font = TwinUIFontCheck.LoadScFont();
            if (font == null)
            {
                sb.AppendLine("[font] WARNING SC font asset not found");
                return 0;
            }

            var targets = new List<TMP_Text>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                targets.AddRange(root.GetComponentsInChildren<TMP_Text>(true));
            }

            int changes = 0;
            foreach (TMP_Text text in targets)
            {
                if (text == null || text.font == font)
                {
                    continue;
                }

                if (!ContainsCjk(text.text))
                {
                    continue;
                }

                string before = text.font == null ? "<null>" : text.font.name;
                text.font = font;
                EditorUtility.SetDirty(text);
                sb.AppendLine("[font] " + TwinSceneLookup.PathOf(text.transform) + ": " + before + " -> " + font.name);
                changes++;
            }

            if (changes == 0)
            {
                sb.AppendLine("[font] all CJK texts already use " + font.name);
            }

            return changes;
        }

        private static bool ContainsCjk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}