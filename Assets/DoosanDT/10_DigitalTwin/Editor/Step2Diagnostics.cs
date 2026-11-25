// Step2Diagnostics.cs -- read-only inspection of the left-bay transform chain.
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.EditorTools
{
    public static class Step2Diagnostics
    {
        private static string V(Vector3 v) =>
            "(" + v.x.ToString("0.0000", CultureInfo.InvariantCulture) + ", " +
                  v.y.ToString("0.0000", CultureInfo.InvariantCulture) + ", " +
                  v.z.ToString("0.0000", CultureInfo.InvariantCulture) + ")";

        private static Transform FindIn(Transform root, params string[] names)
        {
            if (root == null) return null;
            var cur = root;
            foreach (var n in names)
            {
                Transform next = null;
                for (int i = 0; i < cur.childCount; i++)
                    if (cur.GetChild(i).name == n) { next = cur.GetChild(i); break; }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        private static Transform Root(string n)
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == n) return go.transform;
            return null;
        }

        [MenuItem("DigitalTwin/Step2/8 Dump Arm Transform Chain", false, 208)]
        public static void DumpChain()
        {
            var bay = Root("00_数字孪生机械臂");
            var tryNode = FindIn(bay, "4.24-Try");
            var arm = FindIn(tryNode, "h2515-1 (4)");
            var baseLink = FindIn(arm, "base_link");
            var link6 = baseLink;
            for (int i = 1; i <= 6 && link6 != null; i++) link6 = FindIn(link6, "link_" + i);

            var sb = new StringBuilder();
            sb.AppendLine("[Chain] bay  local=" + V(bay.localPosition) + " world=" + V(bay.position));
            sb.AppendLine("[Chain] try  local=" + V(tryNode.localPosition) + " world=" + V(tryNode.position) + " rot=" + tryNode.eulerAngles.ToString("F2"));
            sb.AppendLine("[Chain] arm  local=" + V(arm.localPosition) + " world=" + V(arm.position) + " parent=" + arm.parent.name);
            sb.AppendLine("[Chain] base local=" + V(baseLink.localPosition) + " world=" + V(baseLink.position));
            sb.AppendLine("[Chain] link6 world=" + V(link6.position));

            var model = baseLink.GetComponent<RobotModelController>();
            if (model != null)
            {
                var q = model.ReadCurrentDisplayDegrees();
                var qs = new StringBuilder();
                for (int i = 0; i < q.Length; i++) qs.Append(q[i].ToString("0.###", CultureInfo.InvariantCulture)).Append(i < q.Length - 1 ? ", " : "");
                sb.AppendLine("[Chain] displayDeg=[" + qs + "] authority=" + model.CurrentControlAuthority + " startup=" + model.CurrentStartupPoseMode);
            }

            var screws = FindIn(bay, "10_电池包拆解系统", "VR拆解", "Dis_1", "Lay1");
            if (screws != null)
            {
                var b = new Bounds();
                bool any = false;
                foreach (var r in screws.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (any) sb.AppendLine("[Chain] Lay1 bounds min=" + V(b.min) + " max=" + V(b.max));
            }

            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText(@"F:\Doosan_DT-VR_Lab\DoosanLab_VMware\run\step2_chain.txt", sb.ToString(), new UTF8Encoding(false));
        }
    }
}