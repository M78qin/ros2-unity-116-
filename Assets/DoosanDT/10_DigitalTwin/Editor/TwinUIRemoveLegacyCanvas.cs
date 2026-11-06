using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin.EditorTools
{
    /// <summary>
    /// 删除历史遗留的 `30_界面与交互/Canvas/User_ui`（只含 1 个 Button，实际渲染约 18×9 px，
    /// 统一入口面板不引用它）。删除前先做引用扫描：只要场景里还有任何 SerializedProperty
    /// 指向它或其子对象，就中止并如实报出来，避免删出新的空引用。
    /// 菜单：DigitalTwin/UI/5 Remove Legacy User_ui Canvas (destructive)
    /// </summary>
    public static class TwinUIRemoveLegacyCanvas
    {
        private const string TargetPath = "30_界面与交互/Canvas/User_ui";
        private const string LogPath = "Logs/ui_remove_userui.txt";

        [MenuItem("DigitalTwin/UI/5 Remove Legacy User_ui Canvas (destructive)", false, 50)]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[TwinUIRemoveLegacyCanvas] " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("target=" + TargetPath);

            var targetT = TwinSceneLookup.FindByPath(TargetPath);
            var target = targetT == null ? null : targetT.gameObject;
            if (target == null)
            {
                sb.AppendLine("result=ALREADY_ABSENT");
                Write(sb);
                Debug.Log("[UI Remove] " + TargetPath + " already absent");
                return;
            }

            var scene = target.scene;
            sb.AppendLine("found guid=" + GlobalObjectId.GetGlobalObjectIdSlow(target));
            sb.AppendLine("activeSelf=" + target.activeSelf + " childCount=" + target.transform.childCount);

            // 1) 引用扫描（含非激活对象、含组件字段与 UnityEvent 调用）
            var referencedBy = new List<string>();
            var components = Object.FindObjectsOfType<Component>(true);
            foreach (var comp in components)
            {
                if (comp == null || comp.gameObject == target || comp.transform.IsChildOf(target.transform))
                {
                    continue;
                }

                // Parent -> child hierarchy links are Transform data, not functional references:
                // deleting the child removes it from the parent's m_Children automatically.
                if (comp is Transform && comp.transform == target.transform.parent)
                {
                    continue;
                }

                if (CompReferences(comp, target.transform))
                {
                    referencedBy.Add(TwinSceneLookup.PathOf(comp.transform) + " :: " + comp.GetType().Name);
                }
            }

            sb.AppendLine("referencesFound=" + referencedBy.Count);
            foreach (var r in referencedBy)
            {
                sb.AppendLine("  ref <- " + r);
            }

            if (referencedBy.Count > 0)
            {
                sb.AppendLine("result=ABORTED_STILL_REFERENCED");
                Write(sb);
                Debug.LogWarning("[UI Remove] aborted: " + referencedBy.Count + " scene object(s) still reference "
                                 + TargetPath + " (see " + LogPath + ")");
                return;
            }

            // 2) 备份文本化信息，便于事后复原结构
            sb.AppendLine("--- subtree ---");
            DumpTree(target.transform, sb, 1);

            // 3) 删除并保存
            Object.DestroyImmediate(target);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            bool stillThere = TwinSceneLookup.FindByPath(TargetPath) != null;
            sb.AppendLine("afterDeleteExists=" + stillThere);
            sb.AppendLine("scene=" + scene.path + " saved=True");
            sb.AppendLine("result=" + (stillThere ? "FAILED" : "PASS"));
            Write(sb);

            if (stillThere)
            {
                Debug.LogError("[UI Remove] " + TargetPath + " still exists after delete");
            }
            else
            {
                Debug.Log("[UI Remove] deleted " + TargetPath + " (log " + LogPath + ")");
            }
        }

        private static bool CompReferences(Component comp, Transform targetRoot)
        {
            var so = new SerializedObject(comp);
            var prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.Next(enterChildren))
            {
                enterChildren = true;
                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                var value = prop.objectReferenceValue;
                if (value == null)
                {
                    continue;
                }

                var t = value as Transform;
                if (t != null && (t == targetRoot || t.IsChildOf(targetRoot)))
                {
                    return true;
                }

                var go = value as GameObject;
                if (go != null && (go.transform == targetRoot || go.transform.IsChildOf(targetRoot)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DumpTree(Transform t, StringBuilder sb, int depth)
        {
            sb.AppendLine(new string(' ', depth * 2) + t.name
                          + " active=" + t.gameObject.activeSelf
                          + " comps=" + string.Join(",", System.Array.ConvertAll(t.GetComponents<Component>(),
                              c => c == null ? "<missing>" : c.GetType().Name)));
            for (int i = 0; i < t.childCount; i++)
            {
                DumpTree(t.GetChild(i), sb, depth + 1);
            }
        }

        private static void Write(StringBuilder sb)
        {
            var full = Path.Combine(Application.dataPath, "..", LogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, sb.ToString(), new UTF8Encoding(false));
        }
    }
}