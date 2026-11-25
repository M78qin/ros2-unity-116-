using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    /// <summary>
    /// Clears QuickOutline console spam by enabling ModelImporter Read/Write on meshes
    /// referenced by active Outline components (battery pack screws etc.).
    /// </summary>
    public static class OutlineReadWriteFixStage
    {
        [MenuItem("DigitalTwin/Maintenance/1 Enable ReadWrite for Outline Meshes")]
        public static void EnableReadWriteForOutlineMeshes()
        {
            var outlines = Object.FindObjectsOfType<Outline>(true);
            var meshPaths = new HashSet<string>();
            int meshCount = 0;

            foreach (var outline in outlines)
            {
                if (outline == null) continue;
                foreach (var mf in outline.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    meshCount++;
                    string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (!string.IsNullOrEmpty(path)) meshPaths.Add(path);
                }
                foreach (var smr in outline.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null || smr.sharedMesh == null) continue;
                    meshCount++;
                    string path = AssetDatabase.GetAssetPath(smr.sharedMesh);
                    if (!string.IsNullOrEmpty(path)) meshPaths.Add(path);
                }
            }

            int changed = 0;
            int already = 0;
            int skipped = 0;
            var sb = new StringBuilder();
            sb.AppendLine("[OutlineFix] outlines=" + outlines.Length + " meshRefs=" + meshCount
                          + " uniqueAssets=" + meshPaths.Count);

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in meshPaths)
                {
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null)
                    {
                        skipped++;
                        continue;
                    }
                    if (importer.isReadable)
                    {
                        already++;
                        continue;
                    }
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    changed++;
                    sb.AppendLine("  RW=ON  " + path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            sb.AppendLine("[OutlineFix] RESULT changed=" + changed + " already=" + already
                          + " skippedNonModel=" + skipped);
            Debug.Log(sb.ToString());
            // No DisplayDialog — blocks MCP / headless menu execution.
        }

        [MenuItem("DigitalTwin/Maintenance/2 Clear Console")]
        public static void ClearConsole()
        {
            // Reflective clear — works without referencing UnityEditor.LogEntries assembly name churn.
            var logEntries = System.Type.GetType("UnityEditor.LogEntries,UnityEditor.dll");
            var clear = logEntries?.GetMethod("Clear", System.Reflection.BindingFlags.Static
                                                      | System.Reflection.BindingFlags.Public);
            clear?.Invoke(null, null);
            Debug.Log("[OutlineFix] Console cleared.");
        }
    }
}
