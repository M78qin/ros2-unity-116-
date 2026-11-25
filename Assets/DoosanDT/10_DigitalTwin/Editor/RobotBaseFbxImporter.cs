// RobotBaseFbxImporter.cs -- builds the vendor mobile-base mesh from the ASCII FBX.
// Unity 2022.3 silently imports Doosan_H2515_MobileBase.fbx as an empty GameObject
// (hand-generated ASCII FBX 7.4, no mesh sub-assets), so we parse the geometry arrays
// and rebuild an equivalent Mesh asset with the original 5 material slots.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class RobotBaseFbxImporter
    {
        public const string BaseDirAsset = "Assets/DoosanDT/Models/RobotBase";
        public const string DesktopPack = @"C:\Users\Administrator\Desktop\Doosan_H2515_MobileBase_UnityPack-v2";
        public const string SourceFbxPath = DesktopPack + @"\Doosan_H2515_MobileBase.fbx";
        public const string MeshAssetPath = BaseDirAsset + "/Doosan_H2515_MobileBase_Mesh.asset";
        private const string Root = @"E:\new_2026-\unity\project\Unity_Ros_Try1\";

        private static float[] ParseArray(string text, string key)
        {
            int idx = text.IndexOf(key + ":", StringComparison.Ordinal);
            if (idx < 0) throw new Exception("missing array " + key);
            int a = text.IndexOf("a:", idx, StringComparison.Ordinal);
            int end = text.IndexOf('}', a);
            string body = text.Substring(a + 2, end - a - 2);
            var parts = body.Split(',');
            var list = new List<float>(parts.Length);
            foreach (var p in parts)
            {
                string t = p.Trim();
                if (t.Length == 0) continue;
                list.Add(float.Parse(t, CultureInfo.InvariantCulture));
            }
            return list.ToArray();
        }

        private static List<string> ParseMaterialNames(string text)
        {
            var names = new List<string>();
            var ms = Regex.Matches(text, "Material:\\s*\\d+,\\s*\"Material::([^\"]+)\"");
            foreach (Match m in ms) names.Add(m.Groups[1].Value);
            return names;
        }

        [MenuItem("DigitalTwin/Step1/6 Build Mobile Base Mesh From FBX", false, 196)]
        public static void BuildMeshAsset()
        {
            string text = File.ReadAllText(SourceFbxPath);
            float[] vRaw = ParseArray(text, "Vertices");
            float[] pvRaw = ParseArray(text, "PolygonVertexIndex");
            float[] nRaw = ParseArray(text, "Normals");
            float[] uvRaw = ParseArray(text, "UV");
            float[] matRaw = ParseArray(text, "Materials");
            var matNames = ParseMaterialNames(text);

            int vCount = vRaw.Length / 3;
            int pvCount = pvRaw.Length;
            int triCount = 0;
            // polygon split (negative index marks the last vertex of a polygon)
            var polys = new List<List<int>>();
            var cur = new List<int>();
            foreach (float f in pvRaw)
            {
                int i = Mathf.RoundToInt(f);
                if (i < 0) { cur.Add(-i - 1); polys.Add(cur); cur = new List<int>(); }
                else cur.Add(i);
            }
            if (cur.Count > 0) polys.Add(cur);
            foreach (var p in polys) triCount += Mathf.Max(0, p.Count - 2);

            if (nRaw.Length / 3 != pvCount) throw new Exception("normal stream length mismatch");
            if (uvRaw.Length / 2 != pvCount) throw new Exception("uv stream mismatch");

            // one vertex per polygon-vertex (keeps hard edges / per-corner uv)
            var verts = new Vector3[pvCount];
            var norms = new Vector3[pvCount];
            var uvs = new Vector2[pvCount];
            int k = 0;
            foreach (float f in pvRaw)
            {
                int vi = f < 0 ? Mathf.RoundToInt(-f) - 1 : Mathf.RoundToInt(f);
                verts[k] = new Vector3(vRaw[vi * 3], vRaw[vi * 3 + 1], vRaw[vi * 3 + 2]);
                norms[k] = new Vector3(nRaw[k * 3], nRaw[k * 3 + 1], nRaw[k * 3 + 2]);
                uvs[k] = new Vector2(uvRaw[k * 2], uvRaw[k * 2 + 1]);
                k++;
            }

            int[] triangles = new int[triCount * 3];
            int[] triMat = new int[triCount];
            int t = 0, pos = 0;
            for (int pi = 0; pi < polys.Count; pi++)
            {
                var p = polys[pi];
                int mat = pi < matRaw.Length ? Mathf.Clamp(Mathf.RoundToInt(matRaw[pi]), 0, 4) : 0;
                for (int j = 1; j + 1 < p.Count; j++)
                {
                    triangles[t * 3] = pos;
                    triangles[t * 3 + 1] = pos + j;
                    triangles[t * 3 + 2] = pos + j + 1;
                    triMat[t] = mat;
                    t++;
                }
                pos += p.Count;
            }

            var mesh = new Mesh { name = "Doosan_H2515_MobileBase_Mesh" };
            mesh.indexFormat = pvCount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.subMeshCount = 5;
            var perSub = new List<int>[5];
            for (int s = 0; s < 5; s++) perSub[s] = new List<int>(triCount);
            for (int i = 0; i < triCount; i++)
            {
                perSub[triMat[i]].Add(triangles[i * 3]);
                perSub[triMat[i]].Add(triangles[i * 3 + 1]);
                perSub[triMat[i]].Add(triangles[i * 3 + 2]);
            }
            for (int s = 0; s < 5; s++) mesh.SetTriangles(perSub[s], s, true);
            mesh.RecalculateBounds();

            if (!AssetDatabase.IsValidFolder(BaseDirAsset)) AssetDatabase.CreateFolder("Assets/DoosanDT/Models", "RobotBase");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (existing != null) AssetDatabase.DeleteAsset(MeshAssetPath);
            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[Step1] base mesh built: verts=" + pvCount + " polys=" + polys.Count +
                      " tris=" + triCount + " materials=" + matNames.Count + " bounds=" + mesh.bounds);
        }

        /// <summary>Creates a scene GameObject with the rebuilt mesh and the 5 textured materials.</summary>
        public static GameObject BuildInstance(out string info)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (mesh == null) { BuildMeshAsset(); mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath); }
            if (mesh == null) { info = "mesh asset missing"; return null; }

            string text = File.ReadAllText(SourceFbxPath);
            var names = ParseMaterialNames(text);
            var mats = new Material[5];
            string matDir = BaseDirAsset + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(BaseDirAsset, "Materials");

            int texBound = 0;
            for (int i = 0; i < 5; i++)
            {
                string mname = i < names.Count ? names[i] : ("Mat_Slot" + i);
                string texAsset = BaseDirAsset + "/textures/" + mname + "_Albedo.png";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texAsset);
                if (tex != null) texBound++;
                string path = matDir + "/M_" + mname + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    mat = new Material(shader) { name = "M_" + mname };
                    AssetDatabase.CreateAsset(mat, path);
                }
                if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(mat);
                mats[i] = mat;
            }
            AssetDatabase.SaveAssets();

            var go = new GameObject("MobileBase_H2515");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            info = "verts=" + mesh.vertexCount + " mats=" + mats.Length + " texturesBound=" + texBound;
            return go;
        }
    }
}