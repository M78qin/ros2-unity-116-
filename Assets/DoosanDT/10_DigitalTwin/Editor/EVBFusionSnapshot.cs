using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    /// <summary>
    /// Fusion-session helper (2026-11-03): renders a still PNG of the current scene from a temporary
    /// camera framed on a target object. EDIT-MODE ONLY - it does not enter Play mode, does not
    /// start recording/streams and does not move the robot.
    /// </summary>
    public static class EVBFusionSnapshot
    {
        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("DigitalTwin/Fusion/9 Snapshot Target -> PNG (edit mode)")]
        public static void SnapshotTarget()
        {
            string[] candidates = { "EVB_Fusion", "00_数字孪生机械臂", "4.24-Try", "H2515_DigitalTwinRuntimeCanvas" };
            GameObject target = null;
            foreach (var c in candidates)
            {
                foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (go.name == c) { target = go; break; }
                }
                if (target != null) break;
            }
            if (target == null)
            {
                // fallback: frame every ACTIVE renderer in the scene (inactive backups excluded)
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                Bounds all = default; bool has = false;
                foreach (var r in active)
                    foreach (var rend in r.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!has) { all = rend.bounds; has = true; } else all.Encapsulate(rend.bounds);
                    }
                if (!has)
                {
                    Debug.LogError("[EVB Fusion] snapshot target not found and scene has no active renderer");
                    return;
                }
                SnapshotBounds(all, "Scene_" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                return;
            }

            Bounds b = GetBounds(target);
            if (b.size.sqrMagnitude < 0.0001f)
            {
                b = new Bounds(target.transform.position, Vector3.one);
            }
            SnapshotBounds(b, Sanitize(target.name));
        }

        /// <summary>Renders one still PNG framing the given world-space bounds.</summary>
        public static void SnapshotBounds(Bounds b, string tag)
        {
            // Frame from a 3/4 front-right view, distance based on bounds radius.
            float radius = Mathf.Max(0.6f, b.extents.magnitude);
            Vector3 dir = new Vector3(0.9f, 0.45f, -1.4f).normalized;
            Vector3 camPos = b.center + dir * radius * 2.6f;

            var camGo = new GameObject("__EVB_Fusion_SnapshotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            camGo.transform.position = camPos;
            camGo.transform.LookAt(b.center, Vector3.up);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.15f, 0.18f, 1f);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            var prevActive = RenderTexture.active;
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            cam.targetTexture = prevTarget;
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(rt);

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string outDir = Path.Combine(projectRoot, "Logs");
            Directory.CreateDirectory(outDir);
            string file = Path.Combine(outDir, "fusion_snapshot_" + tag + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("[EVB Fusion] snapshot tag=" + tag + " center=" + b.center + " size=" + b.size + " -> " + file);
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static Bounds GetBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = default;
            bool has = false;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!has) b = new Bounds(go.transform.position, Vector3.one);
            return b;
        }
    }
}