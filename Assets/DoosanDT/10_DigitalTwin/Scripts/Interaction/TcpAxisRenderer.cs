// TcpAxisRenderer.cs -- Game 视图可见的 TCP 三轴（不依赖 Scene Gizmo）。
// 跟随 ToolTcpSwitcher 当前激活的 TCP（Flange_TCP / Spindle_TCP），可运行时开关。
// 纯显示：不参与碰撞、不影响 IK、不发任何指令。
using UnityEngine;

namespace DigitalTwin.Interaction
{
    [DisallowMultipleComponent]
    public sealed class TcpAxisRenderer : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("双 TCP 切换器；为空时直接使用下面的 target。")]
        [SerializeField] private ToolTcpSwitcher switcher;

        [Tooltip("switcher 为空时使用的固定目标。")]
        [SerializeField] private Transform target;

        [Header("Look")]
        [Tooltip("轴长（米）。")]
        [SerializeField] private float axisLength = 0.10f;
        [Tooltip("线宽（米）。")]
        [SerializeField] private float width = 0.004f;
        [SerializeField] private bool visible = false;
        [Tooltip("线体始终渲染在物体前方（会被后处理忽略，仅视觉）。")]
        [SerializeField] private bool alwaysOnTop = true;

        private LineRenderer _x, _y, _z;

        private Transform Current =>
            switcher != null ? switcher.ActiveTcpTransform : target;

        private void OnEnable()
        {
            EnsureLines();
            ApplyVisibility();
        }

        private void Update()
        {
            Transform t = Current;
            if (t == null) return;
            UpdateLine(_x, t, t.right, Color.red);
            UpdateLine(_y, t, t.up, Color.green);
            UpdateLine(_z, t, t.forward, Color.blue);
        }

        public void SetVisible(bool value)
        {
            visible = value;
            ApplyVisibility();
        }

        public void ToggleVisible() => SetVisible(!visible);

        public bool Visible => visible;

        private void ApplyVisibility()
        {
            EnsureLines();
            if (_x != null) _x.enabled = visible;
            if (_y != null) _y.enabled = visible;
            if (_z != null) _z.enabled = visible;
        }

        private void EnsureLines()
        {
            _x = EnsureLine("TCP_Axis_X");
            _y = EnsureLine("TCP_Axis_Y");
            _z = EnsureLine("TCP_Axis_Z");
        }

        private LineRenderer EnsureLine(string name)
        {
            Transform holder = transform;
            Transform existing = holder.Find(name);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(holder, false);
            }
            var lr = go.GetComponent<LineRenderer>();
            if (lr == null) lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (lr.sharedMaterial == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit");
                if (s == null) s = Shader.Find("Sprites/Default");
                if (s != null)
                {
                    var m = new Material(s);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                    m.color = Color.white;
                    lr.sharedMaterial = m;
                }
            }
            return lr;
        }

        private void UpdateLine(LineRenderer lr, Transform t, Vector3 dir, Color c)
        {
            if (lr == null) return;
            lr.startColor = c;
            lr.endColor = c;
            lr.SetPosition(0, t.position);
            lr.SetPosition(1, t.position + dir.normalized * axisLength);
        }
    }
}