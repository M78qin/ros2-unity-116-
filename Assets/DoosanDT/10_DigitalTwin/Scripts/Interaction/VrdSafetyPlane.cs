// VrdSafetyPlane.cs —— VR_D 实训约束用的「电池包顶面安全网格面」（URP 半透明细网格 + 柔和脉动）。
//
// 位置：Dis_1 电池包包围盒顶面（实测 Y = 1.2631 m），X/Z 取 Dis_1 包围盒并外扩 0.05 m。
// 交互：默认隐藏（VR_D = OFF）；VR_D = ON 显示为青蓝细网；Ghost 穿透到网面以下时整面转红。
// 可上下微调：offsetMm（-5 / +5 mm 步进，Inspector 可精调）。
//
// 几何：单个 Mesh（细线用窄四边形拼，不依赖任何贴图/自定义 shader），材质用 URP/Unlit 透明模式，
// 保证 URP 14.0.12 下真实可见、不会品红、也不会因为缺贴图而全透明隐身。
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.Interaction
{
    public sealed class VrdSafetyPlane : MonoBehaviour
    {
        public const float DefaultPlaneY = 1.2631f;
        public const float DefaultMarginMeters = 0.05f;
        public static readonly Vector2 DefaultCenterXz = new Vector2(-6.79f, -13.20f); // Dis_1 包围盒中心
        public static readonly Vector2 DefaultSizeXz = new Vector2(1.36f, 0.78f);       // 外扩前 X/Z 跨度

        [Header("几何")]
        [Tooltip("网格线间距（米）。")]
        [SerializeField] private float cellMeters = 0.08f;
        [Tooltip("网格线宽（米）。")]
        [SerializeField] private float lineWidthMeters = 0.0016f;

        [Header("外观")]
        [SerializeField] private Color normalColor = new Color(0.22f, 0.80f, 1.00f, 0.30f);
        [SerializeField] private Color warnColor = new Color(0.95f, 0.32f, 0.30f, 0.42f);
        [SerializeField, Range(0f, 0.5f)] private float rippleAmplitude = 0.06f;
        [SerializeField, Range(0.1f, 4f)] private float rippleHz = 0.7f;

        public float PlaneY { get; private set; } = DefaultPlaneY;
        public float OffsetMm { get; private set; }
        public bool Warn { get; private set; }
        public bool Visible => gameObject.activeSelf;
        public Bounds SurfaceBounds { get; private set; }

        private Mesh _mesh;
        private Material _material;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private float _baseAlpha;

        /// <summary>创建（或复用）安全网格面。planeY = 包顶基准高度，centerXz/sizeXz = 覆盖范围。</summary>
        public static VrdSafetyPlane Create(float planeY, Vector2 centerXz, Vector2 sizeXz)
        {
            var go = new GameObject("[VRD_SafetyPlane]");
            go.SetActive(false);
            var plane = go.AddComponent<VrdSafetyPlane>();
            plane.Build(planeY, centerXz, sizeXz);
            return plane;
        }

        private void Build(float planeY, Vector2 centerXz, Vector2 sizeXz)
        {
            PlaneY = planeY;
            OffsetMm = 0f;
            transform.position = new Vector3(centerXz.x, planeY, centerXz.y);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            _material = CreatePlaneMaterial();
            _renderer.sharedMaterial = _material;

            BuildGrid(sizeXz);
            SurfaceBounds = new Bounds(new Vector3(centerXz.x, planeY, centerXz.y), new Vector3(sizeXz.x, 0f, sizeXz.y));
            SetWarn(false);
            Debug.Log("[VRD] safety plane built: y=" + planeY.ToString("F4") + " center=" +
                      centerXz.ToString("F3") + " size=" + sizeXz.ToString("F3") + " cell=" +
                      cellMeters.ToString("F3") + " verts=" + (_mesh != null ? _mesh.vertexCount : 0));
        }

        private void BuildGrid(Vector2 sizeXz)
        {
            float halfX = sizeXz.x * 0.5f;
            float halfZ = sizeXz.y * 0.5f;
            float y = 0f;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            void Line(Vector3 a, Vector3 b)
            {
                Vector3 dir = (b - a);
                if (dir.sqrMagnitude < 1e-8f) return;
                dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * (lineWidthMeters * 0.5f);
                int i0 = verts.Count;
                verts.Add(new Vector3(a.x - side.x, y, a.z - side.z));
                verts.Add(new Vector3(a.x + side.x, y, a.z + side.z));
                verts.Add(new Vector3(b.x + side.x, y, b.z + side.z));
                verts.Add(new Vector3(b.x - side.x, y, b.z - side.z));
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
            }

            float cell = Mathf.Max(0.02f, cellMeters);
            int nx = Mathf.Max(1, Mathf.CeilToInt(sizeXz.x / cell));
            int nz = Mathf.Max(1, Mathf.CeilToInt(sizeXz.y / cell));
            float stepX = sizeXz.x / nx;
            float stepZ = sizeXz.y / nz;

            for (int i = 0; i <= nx; i++)
            {
                float x = -halfX + stepX * i;
                Line(new Vector3(x, y, -halfZ), new Vector3(x, y, halfZ));
            }
            for (int j = 0; j <= nz; j++)
            {
                float z = -halfZ + stepZ * j;
                Line(new Vector3(-halfX, y, z), new Vector3(halfX, y, z));
            }

            _mesh = new Mesh { name = "VrdSafetyPlaneGrid" };
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
        }

        private static Material CreatePlaneMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var m = new Material(shader) { name = "VrdSafetyPlaneRuntime" };
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        private void Update()
        {
            if (_material == null) return;
            // 柔和脉动：整体透明度轻微呼吸，像"动态波纹"但不干扰读数
            float pulse = 1f - rippleAmplitude + rippleAmplitude * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * rippleHz * 6.2831853f));
            Color c = Warn ? warnColor : normalColor;
            c.a = Mathf.Clamp01(c.a * Mathf.Clamp01(pulse));
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", c);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", c);
        }

        /// <summary>高度微调（毫米，相对底面基准）。正 = 抬高。</summary>
        public void SetOffsetMm(float offsetMm)
        {
            OffsetMm = offsetMm;
            Vector3 p = transform.position;
            p.y = PlaneY + offsetMm * 0.001f;
            transform.position = p;
            SurfaceBounds = new Bounds(p, new Vector3(SurfaceBounds.size.x, 0f, SurfaceBounds.size.z));
        }

        public float CurrentWorldY => transform.position.y;

        public void Show()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>警示态：网面转红（Ghost 穿透到网面以下时由手柄调用）。</summary>
        public void SetWarn(bool warn)
        {
            Warn = warn;
            if (_material == null) return;
            Color c = warn ? warnColor : normalColor;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", c);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", c);
        }

        public Color CurrentColor => _material != null && _material.HasProperty("_BaseColor")
            ? _material.GetColor("_BaseColor") : Color.clear;
    }
}