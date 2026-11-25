// TcpAxisGizmo.cs -- lightweight Scene-view XYZ axes for TCP / flange anchor nodes.
// Draws in local space: X red, Y green, Z blue (Z = tool axis for the TCP nodes).
using UnityEngine;

namespace DigitalTwin.Tools
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TcpAxisGizmo : MonoBehaviour
    {
        [Tooltip("Axis length in metres (Scene view; grows when the object is selected).")]
        public float axisLength = 0.08f;

        [Tooltip("Draw a small sphere at the origin so the anchor is visible at a distance.")]
        public bool drawOriginDot = true;

        private void OnDrawGizmos()
        {
            Draw(1f);
        }

        private void OnDrawGizmosSelected()
        {
            Draw(2.5f);
        }

        private void Draw(float scale)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            float len = Mathf.Max(0.005f, axisLength) * scale;

            Gizmos.color = new Color(1f, 0.25f, 0.2f, 1f);
            Gizmos.DrawLine(Vector3.zero, Vector3.right * len);

            Gizmos.color = new Color(0.3f, 1f, 0.35f, 1f);
            Gizmos.DrawLine(Vector3.zero, Vector3.up * len);

            // tool axis: slightly longer and brighter so it reads as "outward"
            Gizmos.color = new Color(0.25f, 0.55f, 1f, 1f);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * (len * 1.4f));

            if (drawOriginDot)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
                Gizmos.DrawWireSphere(Vector3.zero, len * 0.08f);
            }
        }
    }
}