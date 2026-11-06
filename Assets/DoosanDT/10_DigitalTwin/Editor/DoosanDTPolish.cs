using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    /// <summary>
    /// 2026-11-03 视觉/性能整理（第三轮补强）。编辑器操作，不进 Play、不连 ROS、不采集。
    /// 每个菜单幂等；备份根通过命名识别（`~` 或 `-bkup113` 后缀）。
    /// </summary>
    public static class DoosanDTPolish
    {
        private const string MainScenePath = "Assets/Scenes/Doosan-DT.unity";
        private const string ArchiveScenePath = "Assets/Scenes/Doosan-DT_Archive.unity";
        private const string SkyMatPath = "Assets/DoosanDT/10_DigitalTwin/Art/Materials/DoosanDT_IndustrialSky.mat";
        private const string ProfilePath = "Assets/DoosanDT/10_DigitalTwin/Art/Config/DoosanDT_VisualProfile.asset";

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;
        private static string LogPath => Path.Combine(ProjectRoot, "Logs", "doosan_dt_polish.txt");

        private static void Say(string line)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
            Debug.Log("[DoosanDT polish] " + line);
        }

        private static void Dialog(string title, string msg, string ok)
        {
            Debug.Log("[DoosanDT polish] " + title + ": " + msg.Replace("\n", " | "));
        }

        private static GameObject Root(string name)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == name) return go;
            return null;
        }

        private static bool IsArchiveRootName(string n) => n == "~" || n.EndsWith("-bkup113", StringComparison.Ordinal);

        private static Bounds UnionRenderers(IEnumerable<GameObject> roots, bool includeInactive)
        {
            Bounds b = default; bool has = false;
            foreach (var r in roots)
            {
                if (r == null) continue;
                foreach (var rend in r.GetComponentsInChildren<Renderer>(includeInactive))
                {
                    if (rend == null) continue;
                    if (!has) { b = rend.bounds; has = true; } else b.Encapsulate(rend.bounds);
                }
            }
            if (!has) b = new Bounds(Vector3.zero, Vector3.one);
            return b;
        }

        // ---------------------------------------------------------------- stats
        [MenuItem("DigitalTwin/Doosan-DT/7 Scene Stats")]
        public static void SceneStats()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine("========== Scene Stats @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==========");
            sb.AppendLine("scene=" + scene.path + "  dirty=" + scene.isDirty);

            var allGo = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
            var activeGo = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(false)).Count();
            var meshRenderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true);
            var skinned = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>(true);
            sb.AppendLine("gameObjects(total)=" + allGo.Length + "  activeTransforms=" + activeGo);
            sb.AppendLine("meshRenderers=" + meshRenderers.Length + " (active=" + meshRenderers.Count(r => r.enabled && r.gameObject.activeInHierarchy) + ")"
                          + "  skinned=" + skinned.Length);
            sb.AppendLine("roots=" + scene.GetRootGameObjects().Length);
            sb.AppendLine("cameras=" + UnityEngine.Object.FindObjectsOfType<Camera>(true).Length
                          + "  lights=" + UnityEngine.Object.FindObjectsOfType<Light>(true).Length
                          + "  lightProbeGroups=" + UnityEngine.Object.FindObjectsOfType<LightProbeGroup>(true).Length
                          + "  reflectionProbes=" + UnityEngine.Object.FindObjectsOfType<ReflectionProbe>(true).Length);
            sb.AppendLine("volumes=" + UnityEngine.Object.FindObjectsOfType<Volume>(true).Length);

            sb.AppendLine("--- roots ---");
            foreach (var r in scene.GetRootGameObjects())
            {
                int cnt = r.GetComponentsInChildren<Transform>(true).Length;
                int act = r.GetComponentsInChildren<Transform>(false).Length;
                sb.AppendLine("  " + r.name + "  activeSelf=" + r.activeSelf + "  transforms=" + cnt + "  activeInHierarchy=" + act);
            }
            sb.AppendLine();

            long tris = 0, trisActive = 0;
            var mats = new HashSet<Material>();
            var meshes = new HashSet<Mesh>();
            foreach (var mr in meshRenderers)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var m = mf.sharedMesh;
                meshes.Add(m);
                tris += m.triangles.Length / 3;
                if (mr.enabled && mr.gameObject.activeInHierarchy) trisActive += m.triangles.Length / 3;
                foreach (var mat in mr.sharedMaterials) if (mat != null) mats.Add(mat);
            }
            foreach (var sm in skinned)
            {
                if (sm.sharedMesh == null) continue;
                meshes.Add(sm.sharedMesh);
                tris += sm.sharedMesh.triangles.Length / 3;
                if (sm.enabled && sm.gameObject.activeInHierarchy) trisActive += sm.sharedMesh.triangles.Length / 3;
                foreach (var mat in sm.sharedMaterials) if (mat != null) mats.Add(mat);
            }
            sb.AppendLine("triangles(total)=" + tris + "  triangles(active)=" + trisActive
                          + "  distinctMeshes=" + meshes.Count + "  distinctMaterials=" + mats.Count);

            var outlines = UnityEngine.Object.FindObjectsOfType<Outline>(true);
            sb.AppendLine("outlineComponents=" + outlines.Length
                          + "  enabled=" + outlines.Count(o => o.enabled)
                          + "  enabledActive=" + outlines.Count(o => o.enabled && o.gameObject.activeInHierarchy));

            long texBytes = 0; int texCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                texCount++;
                texBytes += (long)tex.width * tex.height * 4;
            }
            sb.AppendLine("textures(Assets)=" + texCount + "  estUncompressedMemoryMB=" + (texBytes / 1048576.0).ToString("F1"));
            var fi = new FileInfo(Path.Combine(ProjectRoot, MainScenePath));
            sb.AppendLine("sceneFileMB=" + (fi.Length / 1048576.0).ToString("F2"));
            sb.AppendLine();
            File.AppendAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[DoosanDT polish] stats -> " + LogPath);
        }

        // ---------------------------------------------------------------- outline
        [MenuItem("DigitalTwin/Doosan-DT/4 Optimize Outline (perf)")]
        public static void OptimizeOutline()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath) { Dialog("错误", "请在 Doosan-DT 场景中执行", "OK"); return; }

            var outlines = UnityEngine.Object.FindObjectsOfType<Outline>(true);
            int before = outlines.Count(o => o.enabled);
            int kept = 0, disabled = 0;
            foreach (var o in outlines)
            {
                bool keep = IsInteractivePart(o.transform);
                if (keep) { if (!o.enabled) o.enabled = true; kept++; continue; }
                if (o.enabled) { o.enabled = false; disabled++; }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Say("OUTLINE total=" + outlines.Length + " enabledBefore=" + before + " kept=" + kept
                + " disabled=" + disabled + " enabledAfter=" + outlines.Count(x => x.enabled));
            Dialog("Outline 收口", "总 " + outlines.Length + "，保留 " + kept + "，关闭 " + disabled, "OK");
        }

        /// <summary>交互件判定：自身或近层祖先带拆解组件（改名/换父级后仍成立）。</summary>
        private static bool IsInteractivePart(Transform t)
        {
            if (t == null) return false;
            // 只认对象自身：拆解零件 / 层组容器 / 可爆炸件；不向祖先扩散（否则整台机器人都会被算作交互件）
            if (t.GetComponent<DisassemblyPart>() != null) return true;
            if (t.GetComponent<LayerGroupConfig>() != null) return true;
            if (t.GetComponent<ExplodablePartXR>() != null) return true;
            return false;
        }

        // ---------------------------------------------------------------- archive
        [MenuItem("DigitalTwin/Doosan-DT/5 Extract Archive Group")]
        public static void ExtractArchive()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath) { Dialog("错误", "请在 Doosan-DT 场景中执行", "OK"); return; }

            string HolderName = "~_外部引用保留";
            var holder = scene.GetRootGameObjects().FirstOrDefault(r => r.name == HolderName);

            // 候选备份根：场景根 + 中转根的子对象（上次中断时可能嵌套）
            var candidates = new List<GameObject>(scene.GetRootGameObjects());
            if (holder != null) candidates.AddRange(holder.GetComponentsInChildren<Transform>(true).Where(t2 => t2 != holder.transform).Select(t2 => t2.gameObject));

            var archives = candidates
                .Where(r => r != null && r.transform.parent != holder?.transform ? IsArchiveRootName(r.name) && !r.activeSelf : IsArchiveRootName(r.name) && !r.activeSelf)
                .Distinct().ToList();
            if (archives.Count == 0) { Dialog("提示", "未找到备份根（~ 或 -bkup113）", "OK"); return; }

            var archiveSet = new HashSet<Transform>(archives.SelectMany(a => a.GetComponentsInChildren<Transform>(true)));

            // 扫描外部引用
            int refs = 0; string sample = "";
            var keep = new HashSet<Transform>();
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null || archiveSet.Contains(mb.transform)) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var obj = it.objectReferenceValue;
                    if (obj == null) continue;
                    var tr = obj as Transform ?? (obj as Component)?.transform ?? (obj as GameObject)?.transform;
                    if (tr != null && archiveSet.Contains(tr))
                    {
                        refs++;
                        keep.Add(tr);
                        if (sample.Length < 160) sample += mb.GetType().Name + "->" + tr.name + "; ";
                    }
                }
            }

            // 被引用对象（仅其自身子树）搬进中转根，保持世界变换与停用状态；不做祖先上溯
            if (keep.Count > 0)
            {
                if (holder == null)
                {
                    holder = new GameObject(HolderName);
                    holder.SetActive(false);
                }
                foreach (var k in keep)
                {
                    if (k == null || k.parent == holder.transform) continue; // 只看直接父级：祖先在中转根下不代表已搬出
                    k.SetParent(holder.transform, true);
                    k.gameObject.SetActive(false);
                }
                Say("EXTRACT keepReferenced refs=" + refs + " objects=[" + string.Join(",", keep.Where(k => k != null).Select(k => k.name)) + "] sample=" + sample);
            }

            Say("EXTRACT pre-move archives=[" + string.Join(",", archives.Select(a => a.name + ":" + a.GetComponentsInChildren<Transform>(true).Length)) + "] keep=[" + string.Join(",", keep.Select(k => k != null ? k.name : "null")) + "]");
            // 重新判定可搬走的备份根（不能包含被保留对象）
            archives = archives.Where(a => a != null && !keep.Any(k => k != null && k.IsChildOf(a.transform))).ToList();
            if (archives.Count == 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Dialog("提示", "备份根都被外部引用占用，未拆分", "OK");
                return;
            }

            var counts = archives.Select(a => a.name + "=" + a.GetComponentsInChildren<Transform>(true).Length).ToList();
            Scene archiveScene;
            var absArchive = Path.Combine(ProjectRoot, ArchiveScenePath);
            if (File.Exists(absArchive))
                archiveScene = EditorSceneManager.OpenScene(ArchiveScenePath, OpenSceneMode.Additive);
            else
                archiveScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            foreach (var a in archives)
            {
                if (a.transform.parent != null) a.transform.SetParent(null, true); // 先成为场景根，才能跨场景搬
                SceneManager.MoveGameObjectToScene(a, archiveScene);
            }
            EditorSceneManager.SaveScene(archiveScene);
            SceneManager.SetActiveScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.CloseScene(archiveScene, true);
            Say("EXTRACT roots=[" + string.Join(",", counts) + "] -> " + ArchiveScenePath
                + "  mainSceneMB=" + (new FileInfo(Path.Combine(ProjectRoot, MainScenePath)).Length / 1048576.0).ToString("F2"));
            Dialog("备份拆分", "已移动 " + string.Join(",", counts), "OK");
        }

        // ---------------------------------------------------------------- static flags
        [MenuItem("DigitalTwin/Doosan-DT/6 Mark Environment Static")]
        public static void MarkEnvironmentStatic()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath) { Dialog("错误", "请在 Doosan-DT 场景中执行", "OK"); return; }

            var env = Root("40_环境与灯光");
            if (env == null) { Dialog("提示", "未找到 40_环境与灯光", "OK"); return; }

            var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic
                        | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                        | StaticEditorFlags.ReflectionProbeStatic;
            int n = 0;
            foreach (var r in env.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null) continue;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                n++;
            }
            foreach (var r in env.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r == null) continue;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                n++;
            }
            foreach (var c in env.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (GameObjectUtility.GetStaticEditorFlags(c.gameObject) == 0)
                    GameObjectUtility.SetStaticEditorFlags(c.gameObject, StaticEditorFlags.BatchingStatic);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Say("STATIC marked renderers=" + n + " under 40_环境与灯光");
            Dialog("静态标记", "已标记 " + n + " 个渲染对象", "OK");
        }

        // ---------------------------------------------------------------- lighting
        [MenuItem("DigitalTwin/Doosan-DT/8 Apply Lighting Setup")]
        public static void ApplyLighting()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath) { Dialog("错误", "请在 Doosan-DT 场景中执行", "OK"); return; }

            Directory.CreateDirectory(Path.GetDirectoryName(SkyMatPath)!);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
            if (sky == null)
            {
                var sh = Shader.Find("Skybox/Procedural");
                if (sh == null) { Debug.LogError("Skybox/Procedural shader not found"); return; }
                sky = new Material(sh) { name = "DoosanDT_IndustrialSky" };
                AssetDatabase.CreateAsset(sky, SkyMatPath);
            }
            sky.SetFloat("_SunDisk", 1f);
            sky.SetFloat("_SunSize", 0.028f);
            sky.SetFloat("_SunSizeConvergence", 2.2f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.56f, 0.60f, 0.66f));
            sky.SetColor("_GroundColor", new Color(0.20f, 0.19f, 0.18f));
            sky.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.9f;
            RenderSettings.reflectionIntensity = 0.85f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var directionals = UnityEngine.Object.FindObjectsOfType<Light>(true)
                .Where(l => l.type == LightType.Directional && l.isActiveAndEnabled).ToList();
            Light main = directionals.FirstOrDefault(l => l.transform.parent != null && l.transform.parent.name == "40_环境与灯光")
                         ?? directionals.OrderByDescending(l => l.intensity).FirstOrDefault();
            foreach (var l in directionals)
            {
                if (l == main) continue;
                l.intensity = 0.25f;
                l.color = new Color(0.88f, 0.92f, 1.00f);
                l.shadows = LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Baked;
                EditorUtility.SetDirty(l);
            }
            if (main != null)
            {
                main.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                main.color = new Color(1.00f, 0.96f, 0.90f);
                main.intensity = 1.20f;
                main.shadows = LightShadows.Soft;
                main.shadowStrength = 0.85f;
                main.shadowBias = 0.05f;
                main.shadowNormalBias = 0.4f;
                main.lightmapBakeType = LightmapBakeType.Mixed;
                RenderSettings.sun = main;
                EditorUtility.SetDirty(main);
            }

            var settingsRoot = Root("90_全局设置");
            if (settingsRoot == null) settingsRoot = new GameObject("90_全局设置");
            var envRoot = Root("40_环境与灯光");
            var envB = envRoot != null
                ? UnionRenderers(new[] { envRoot }, true)
                : new Bounds(new Vector3(-5f, 2f, -8f), new Vector3(10f, 4f, 15f));
            float top = envB.max.y + 0.6f;
            CreateOrUpdateFillLight(settingsRoot.transform, "Fill_Light_A", new Vector3(envB.center.x - 2.0f, top, envB.center.z + 1.5f));
            CreateOrUpdateFillLight(settingsRoot.transform, "Fill_Light_B", new Vector3(envB.center.x + 2.5f, top, envB.center.z - 2.0f));

            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            ApplyProfile(profile);

            var vol = UnityEngine.Object.FindObjectsOfType<Volume>(true).FirstOrDefault(v => v.isGlobal);
            if (vol == null)
            {
                var go = new GameObject("Global Volume");
                go.transform.SetParent(settingsRoot.transform, false);
                vol = go.AddComponent<Volume>();
                vol.isGlobal = true;
            }
            vol.sharedProfile = profile;

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                var so = new SerializedObject(urp);
                var p = so.FindProperty("m_ColorGradingMode");
                if (p != null) { p.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); }
                Say("URP asset=" + AssetDatabase.GetAssetPath(urp) + " colorGrading=HDR");
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Say("LIGHTING applied sky=" + SkyMatPath + " profile=" + ProfilePath + " mainLight=" + (main != null ? main.name : "none")
                + " fillLights=2 directionalTotal=" + directionals.Count);
            Dialog("灯光配置", "天空 + 主光 + 补光 + ACES 已应用", "OK");
        }

        private static void CreateOrUpdateFillLight(Transform parent, string name, Vector3 pos)
        {
            var t = parent.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name);
            if (t == null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.GetComponent<Light>();
            if (l == null) l = go.AddComponent<Light>(); // Unity 假 null：不能用 ?? 运算符
            l.type = LightType.Point;
            l.color = new Color(0.85f, 0.90f, 1.00f);
            l.intensity = 6f;
            l.range = 9f;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Baked;
            EditorUtility.SetDirty(go);
        }

        private static void ApplyProfile(VolumeProfile profile)
        {
            if (!profile.TryGet(out Tonemapping tm)) { tm = profile.Add<Tonemapping>(true); AssetDatabase.AddObjectToAsset(tm, profile); }
            tm.active = true;
            tm.mode.Override(TonemappingMode.ACES);

            if (!profile.TryGet(out Bloom bloom)) { bloom = profile.Add<Bloom>(true); AssetDatabase.AddObjectToAsset(bloom, profile); }
            bloom.active = true;
            bloom.threshold.Override(1.10f);
            bloom.intensity.Override(0.08f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(true);

            if (!profile.TryGet(out ColorAdjustments ca)) { ca = profile.Add<ColorAdjustments>(true); AssetDatabase.AddObjectToAsset(ca, profile); }
            ca.active = true;
            ca.postExposure.Override(0f);
            ca.contrast.Override(8f);
            ca.saturation.Override(5f);

            if (!profile.TryGet(out Vignette vig)) { vig = profile.Add<Vignette>(true); AssetDatabase.AddObjectToAsset(vig, profile); }
            vig.active = true;
            vig.intensity.Override(0.12f);
            vig.smoothness.Override(0.35f);

            EditorUtility.SetDirty(profile);
        }

        // ---------------------------------------------------------------- snapshots
        [MenuItem("DigitalTwin/Doosan-DT/9 Snapshot Views")]
        public static void SnapshotViews()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath) { Dialog("错误", "请在 Doosan-DT 场景中执行", "OK"); return; }

            bool before = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) == null;
            string suffix = before ? "_before" : "_after";

            var robot = Root("00_数字孪生机械臂");
            var train = Root("20_漫游与训练");
            Bounds bRobot = UnionRenderers(new[] { robot }, false);
            Bounds bCell = UnionRenderers(new[] { robot, train }, false);
            Bounds bWide = UnionRenderers(scene.GetRootGameObjects().Where(r => r.activeSelf), false);

            SnapshotFromBounds(bRobot, 40f, "view_robot" + suffix);
            SnapshotFromBounds(bCell, 45f, "view_workcell" + suffix);
            SnapshotFromBounds(bWide, 55f, "view_wide" + suffix);
            Say("SNAPSHOT suffix=" + suffix + " robotCenter=" + bRobot.center + " robotSize=" + bRobot.size);
            Dialog("截图完成", "三张视图已写入 Logs/doosan_dt_view_*" + suffix + ".png", "OK");
        }

        private static void SnapshotFromBounds(Bounds b, float fov, string tag)
        {
            float radius = Mathf.Max(0.8f, b.extents.magnitude);
            Vector3 dir = new Vector3(0.9f, 0.45f, -1.4f).normalized;
            SnapshotPose(b.center + dir * radius * 2.6f, b.center, fov, tag);
        }

        private static void SnapshotPose(Vector3 pos, Vector3 look, float fov, string tag)
        {
            const int w = 1920, h = 1080;
            var camGo = new GameObject("__DoosanDT_SnapshotCam");
            var cam = camGo.AddComponent<Camera>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.transform.position = pos;
            camGo.transform.LookAt(look, Vector3.up);
            cam.clearFlags = CameraClearFlags.Skybox;

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(rt);

            var file = Path.Combine(ProjectRoot, "Logs", "doosan_dt_" + tag.Replace(" ", "_").Replace("/", "_") + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Say("SNAPSHOT " + tag + " -> " + file);
        }
    }
}