// GhostArm.cs —— MoveIt2 风格「半透明 Ghost 预演」骨架（第 4 章双向控制）
//
// 为什么运行时克隆而不是修旧模型：
//   场景里的 `H2516-Ghost` 是历史遗留（名字也不对），全场景只有 1 个 `Spindle_TCP` / `Flange_TCP`
//   （都在在役臂 `h2515-1 (4)` 下），Ghost 没有电主轴与 TCP，零点口径也未按 Step14/1 标定过。
//   所以这里从**校准后的在役臂**克隆一份纯视觉骨架：保留层级与网格（含电主轴），剥离
//   ArticulationBody / 碰撞体 / Rigidbody / 所有 MonoBehaviour，只留 Transform + Renderer。
//
// 边界：只写自己克隆体的 Transform 与材质，**从不触碰实体**；不发布任何 ROS 消息。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.Interaction
{
    public sealed class GhostArm : MonoBehaviour
    {
        public enum Tint
        {
            Normal = 0,
            Snap = 1,
            Blocked = 2
        }

        [Serializable]
        public sealed class MaterialConfig
        {
            [Tooltip("自由拖动时的高亮色（半透明本体蓝）。")]
            public Color ghostNormalColor = new Color(0.36f, 0.62f, 0.95f, 0.48f);

            [Tooltip("吸附到标定点时的青绿高亮。")]
            public Color ghostSnapColor = new Color(0.45f, 0.95f, 0.78f, 0.48f);

            [Tooltip("被工艺/工具门禁拦截时的红色警示。")]
            public Color ghostBlockedColor = new Color(0.95f, 0.36f, 0.32f, 0.48f);

            [Range(0.05f, 0.95f)] public float ghostAlpha = 0.48f;
        }

        public MaterialConfig Config = new MaterialConfig();

        public bool Visible => gameObject.activeSelf;
        public Transform SpindleTcp => _spindleTcp;
        public RobotModelController Source => _source;
        public int JointCount => _joints != null ? _joints.Length : 0;
        public Tint CurrentTint { get; private set; } = Tint.Normal;
        public float[] CurrentJoints { get; private set; } = new float[6];
        /// <summary>整体不透明度（0..1）。重合静止时淡出到 0，避免与实体表面 Z-fighting。</summary>
        public float Opacity { get; private set; } = 1f;

        private RobotModelController _source;
        private Transform[] _joints;
        private Quaternion[] _localRot0;
        private Vector3[] _axis;
        private float[] _sign;
        private float[] _q0;
        private Transform _spindleTcp;
        private Material _material;
        private readonly List<Renderer> _renderers = new List<Renderer>();

        /// <summary>从在役臂克隆一份纯视觉 Ghost（同一世界位姿），随后隐藏。</summary>
        public static GhostArm Create(RobotModelController source, MaterialConfig config)
        {
            if (source == null) return null;

            GameObject clone = Instantiate(source.gameObject);
            clone.name = "[GhostArm]";
            clone.SetActive(false);

            // 关键：Instantiate 只复制"本地"变换。source（base_link）自身 localPosition=(0,0,0)，
            // 世界偏移在祖先上 —— 不显式对齐的话 Ghost 会落在世界原点（离机器人 15 m，肉眼看不到）。
            clone.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            clone.transform.localScale = source.transform.lossyScale;

            // 立刻剥离逻辑；物理组件先"停用"（不等帧末，避免这一帧参与模拟），下一帧再真正销毁。
            StripScripts(clone);
            NeutralizePhysics(clone);
            SchedulePhysicsDestroy(clone);

            var ghost = clone.AddComponent<GhostArm>();
            ghost.Build(source, config);
            return ghost;
        }

        /// <summary>立即删除所有 MonoBehaviour / Behaviour（ArticulationBody 等物理组件先留着，帧末再删）。</summary>
        private static void StripScripts(GameObject root)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                var scripts = new List<Component>();
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    if (c is Transform) continue;
                    if (c is Renderer || c is MeshFilter) continue;
                    // 物理组件必须在 Behaviour 分支之前排除：ArticulationBody 也是 Behaviour，
                    // 若在这里 DestroyImmediate 会被 RequireComponent 依赖（UrdfJointFixed/UrdfInertial）
                    // 拦下并刷红字，且永远删不掉。物理组件留到帧末由 StripPhysicsDeferred 统一销毁。
                    if (c is ArticulationBody || c is Rigidbody || c is Collider || c is Joint) continue;
                    if (c is MonoBehaviour || c is Behaviour) { scripts.Add(c); continue; }
                    if (c is Camera || c is Light || c is AudioListener) { scripts.Add(c); continue; }
                }
                if (scripts.Count == 0) break;
                foreach (Component c in scripts)
                {
                    if (c == null) continue;
                    DestroyImmediate(c);
                    if (c != null) Destroy(c);   // DestroyImmediate 被拒时兜底延迟销毁
                }
            }
        }

        /// <summary>物理组件先停用：克隆体本来就是 inactive，这一步是双保险（残帧也不参与模拟）。</summary>
        private static void NeutralizePhysics(GameObject root)
        {
            foreach (var ab in root.GetComponentsInChildren<ArticulationBody>(true)) if (ab != null) ab.enabled = false;
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) if (rb != null) rb.isKinematic = true;
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) if (col != null) col.enabled = false;
            foreach (var j in root.GetComponentsInChildren<Joint>(true)) if (j != null) j.gameObject.SetActive(j.gameObject.activeSelf);
        }

        /// <summary>延迟销毁物理组件（Destroy 是帧末执行，克隆体已 inactive，这一帧不会参与模拟）。
        /// 用延迟销毁而不是 DestroyImmediate：脚本侧的 RequireComponent 依赖刚删，立即销毁会被拦截并刷红字。</summary>
        private static void SchedulePhysicsDestroy(GameObject root)
        {
            foreach (var ab in root.GetComponentsInChildren<ArticulationBody>(true)) if (ab != null) Destroy(ab);
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) if (rb != null) Destroy(rb);
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) if (col != null) Destroy(col);
            foreach (var j in root.GetComponentsInChildren<Joint>(true)) if (j != null) Destroy(j);
        }

        private void Build(RobotModelController source, MaterialConfig config)
        {
            _source = source;
            if (config != null) Config = config;

            var joints = source.Joints;
            int n = joints != null ? Mathf.Min(6, joints.Length) : 0;
            _joints = new Transform[n];
            _localRot0 = new Quaternion[n];
            _axis = new Vector3[n];
            _sign = new float[n];
            _q0 = new float[n];

            float[] q0 = source.ReadCurrentDisplayDegrees();
            for (int i = 0; i < n; i++)
            {
                var binding = joints[i];
                if (binding == null || binding.body == null) continue;

                int[] path = ChildIndexPath(source.transform, binding.body.transform);
                Transform ghostJoint = ResolvePath(transform, path);
                if (ghostJoint == null) continue;

                _joints[i] = ghostJoint;
                _localRot0[i] = ghostJoint.localRotation;
                Vector3 axis = binding.body.anchorRotation * Vector3.right;
                if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
                _axis[i] = axis.normalized;
                _sign[i] = binding.sign;
                _q0[i] = (q0 != null && i < q0.Length) ? q0[i] : 0f;
                CurrentJoints[i] = _q0[i];
            }

            _spindleTcp = FindChildByName(transform, "Spindle_TCP");

            _material = CreateGhostMaterial();
            _renderers.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                r.sharedMaterial = _material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                _renderers.Add(r);
            }

            SetTint(Tint.Normal);
            int residualAb = GetComponentsInChildren<ArticulationBody>(true).Length;
            int residualRb = GetComponentsInChildren<Rigidbody>(true).Length;
            int residualCol = GetComponentsInChildren<Collider>(true).Length;
            int mbLeft = GetComponentsInChildren<MonoBehaviour>(true).Length;
            Debug.Log("[GHOST] created from " + source.gameObject.name + " joints=" + n +
                      " spindle=" + (_spindleTcp != null) + " renderers=" + _renderers.Count +
                      " alpha=" + Config.ghostAlpha.ToString("F2") +
                      " worldPos=" + transform.position.ToString("F3") +
                      " pendingDestroy(AB=" + residualAb + ",RB=" + residualRb + ",Col=" + residualCol +
                      ",MB=" + mbLeft + "，均已禁用，帧末销毁)");
        }

        /// <summary>按绝对显示关节角（度）设置 Ghost 姿态；与实体同一套零点/符号/轴向数学。</summary>
        public void SetJoints(float[] displayDeg)
        {
            if (displayDeg == null || _joints == null) return;
            int n = Mathf.Min(displayDeg.Length, _joints.Length);
            for (int i = 0; i < n; i++)
            {
                Transform t = _joints[i];
                if (t == null) continue;
                float unityDeg = _source != null ? _source.ConvertDisplayDegToUnityDeg(i, displayDeg[i]) : displayDeg[i];
                float unity0 = _source != null ? _source.ConvertDisplayDegToUnityDeg(i, _q0[i]) : _q0[i];
                t.localRotation = _localRot0[i] * Quaternion.AngleAxis(unityDeg - unity0, _axis[i]);
                CurrentJoints[i] = displayDeg[i];
            }
        }

        public void Show()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void SetTint(Tint tint)
        {
            CurrentTint = tint;
            if (_material == null) return;
            Color c = tint == Tint.Snap ? Config.ghostSnapColor
                    : tint == Tint.Blocked ? Config.ghostBlockedColor
                    : Config.ghostNormalColor;
            c.a = Mathf.Clamp01(Config.ghostAlpha * Mathf.Clamp01(Opacity));
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", c);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", c);
        }

        /// <summary>0 = 完全淡出（重合静止态，消除 Z-fighting 斑纹）；1 = 正常半透明。</summary>
        public void SetOpacity(float value)
        {
            Opacity = Mathf.Clamp01(value);
            ApplyTintColor();
            // 完全透明时关掉 Renderer，连"透明但共面"的深度扰动都不留。
            bool on = Opacity > 0.01f;
            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = on;
            }
        }

        private void ApplyTintColor()
        {
            if (_material == null) return;
            Color c = CurrentTint == Tint.Snap ? Config.ghostSnapColor
                    : CurrentTint == Tint.Blocked ? Config.ghostBlockedColor
                    : Config.ghostNormalColor;
            c.a = Mathf.Clamp01(Config.ghostAlpha * Mathf.Clamp01(Opacity));
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", c);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", c);
        }

        public Color CurrentColor => _material != null && _material.HasProperty("_BaseColor")
            ? _material.GetColor("_BaseColor")
            : Color.white;

        private static Material CreateGhostMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var m = new Material(shader) { name = "GhostArmRuntime" };
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        private static int[] ChildIndexPath(Transform root, Transform target)
        {
            var list = new List<int>();
            Transform t = target;
            int guard = 0;
            while (t != null && t != root && guard++ < 64)
            {
                list.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            list.Reverse();
            return list.ToArray();
        }

        private static Transform ResolvePath(Transform root, int[] path)
        {
            Transform t = root;
            for (int i = 0; i < path.Length && t != null; i++)
            {
                if (path[i] < 0 || path[i] >= t.childCount) return null;
                t = t.GetChild(path[i]);
            }
            return t;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindChildByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}