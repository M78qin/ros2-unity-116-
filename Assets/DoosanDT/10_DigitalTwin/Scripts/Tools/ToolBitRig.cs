// ToolBitRig.cs -- keeps the visual bit extension and the Spindle_TCP on one coaxial axis.
// Lives on the "BitAxis" node, whose local +Z points DOWN the tool (outward from the flange).
// Children are laid out purely from bitExtensionLengthMm, so changing that one number in the
// Inspector stretches the bit and moves the TCP in a single step.
using UnityEngine;

namespace DigitalTwin.Tools
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ToolBitRig : MonoBehaviour
    {
        [Header("Bit / TCP")]
        [Tooltip("Bit extension below the spindle model tip, in millimetres. 0 = model bit only.")]
        public float bitExtensionLengthMm = 163.5133f;

        [Tooltip("Visual cylinder used as the extension. Its local +Y is mapped onto the tool axis.")]
        public Transform bitExtension;

        [Tooltip("TCP node placed at the resulting bit tip (local +Z points down the tool).")]
        public Transform spindleTcp;

        [Header("Geometry")]
        [Tooltip("Radius of the visual extension in millimetres (cosmetic only).")]
        public float bitRadiusMm = 4f;

        [Header("Reference (read-only)")]
        [Tooltip("Flange_TCP node, used to record the flange -> tip vector.")]
        public Transform flangeTcp;

        [SerializeField, Tooltip("Distance from the spindle model tip (BitAxis origin) to the flange, mm.")]
        private float modelTipToFlangeMm;

        [SerializeField, Tooltip("Live flange -> Spindle_TCP vector in world space (m).")]
        private Vector3 flangeToTcpWorld;

        [SerializeField] private float flangeToTcpDistanceMm;

        /// <summary>Flange -> Spindle_TCP vector in world space, refreshed by Apply().</summary>
        public Vector3 FlangeToTcpWorld => flangeToTcpWorld;

        /// <summary>|Flange_TCP - Spindle_TCP| in millimetres, refreshed by Apply().</summary>
        public float FlangeToTcpDistanceMm => flangeToTcpDistanceMm;

        /// <summary>Extension length actually applied, in metres.</summary>
        public float ExtensionMeters => Mathf.Max(0f, bitExtensionLengthMm) * 0.001f;

        private void OnEnable() { Apply(); }
        private void OnValidate() { Apply(); }
        private void Update() { if (!Application.isPlaying) Apply(); }

        public void Apply()
        {
            float length = ExtensionMeters;
            float radius = Mathf.Max(0.0005f, bitRadiusMm * 0.001f);

            if (bitExtension != null)
            {
                // Unity cylinder: 1 m diameter, 2 m tall, axis = local Y.
                // Rotate +90 about X so its local Y maps onto the rig's +Z (down the tool).
                bitExtension.localRotation = Quaternion.Euler(90f, 0f, 0f);
                bitExtension.localPosition = new Vector3(0f, 0f, length * 0.5f);
                bitExtension.localScale = new Vector3(radius * 2f, Mathf.Max(length * 0.5f, 0.0005f), radius * 2f);
                var r = bitExtension.GetComponent<Renderer>();
                if (r != null) r.enabled = length > 0.0005f;
            }

            if (spindleTcp != null)
            {
                // Lateral offset stays zero: the TCP sits on the rig's own Z axis.
                spindleTcp.localPosition = new Vector3(0f, 0f, length);
                spindleTcp.localRotation = Quaternion.identity;
            }

            if (flangeTcp != null && spindleTcp != null)
            {
                modelTipToFlangeMm = Vector3.Distance(transform.position, flangeTcp.position) * 1000f;
                flangeToTcpWorld = spindleTcp.position - flangeTcp.position;
                flangeToTcpDistanceMm = flangeToTcpWorld.magnitude * 1000f;
            }
        }
    }
}