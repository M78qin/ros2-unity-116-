// 按钮悬停渐变：BaseMeshEffect 只改 uGUI 顶点色，不改材质、不改 Image.color。
//
// 设计约束（与 UI/12 审计兼容）：
//   - Image.color 仍然是 TwinUIButton 算出来的主题令牌色 → 对比度/令牌审计读到的值不变；
//   - 渐变只是渲染期顶点色插值（UI/Default 材质天然支持顶点色），因此不引入新材质、不破坏批处理；
//   - 关闭（enabledBySettings=false）时不改任何顶点，行为回退为"纯色"。
//
// 状态由 TwinUIButton.Apply() 驱动：
//   hover  → 基色 → 强调色线性渐变（强度 strength）；
//   press  → 两端色再向白色提亮（更亮），配合 TwinUIButton 的轻微缩放。
//
// 自检接口：LastTopColor / LastBottomColor / LastSpread 记录最近一次真正写进网格的顶点色，
// 供 UI/17 量测"hover 顶点色差 > 阈值"与"按下色不同"。
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin.UI
{
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class TwinUIGradientHover : BaseMeshEffect
    {
        [Header("渐变颜色")]
        [Tooltip("基色（非 hover 时的纯色；通常等于按钮当前填充色，由 TwinUIButton 每帧驱动）。")]
        [SerializeField] private Color baseColor = new Color(0.086f, 0.133f, 0.184f, 1f);
        [Tooltip("渐变目标色（通常为主题强调色）。")]
        [SerializeField] private Color accentColor = new Color(0.13f, 0.83f, 0.93f, 1f);

        [Header("开关与强度")]
        [Tooltip("设置资产开关：关闭时不改顶点色，按钮回退为纯色。")]
        [SerializeField] private bool enabledBySettings = true;
        [Tooltip("渐变强度：0=纯色，1=完全渐变到强调色。")]
        [SerializeField, Range(0f, 1f)] private float strength = 0.35f;

        [Header("方向与按下")]
        [Tooltip("沿垂直方向做渐变（上亮下暗）。")]
        [SerializeField] private bool verticalGradient = true;
        [Tooltip("沿水平方向做渐变（左暗右亮）。")]
        [SerializeField] private bool horizontalGradient;
        [Tooltip("按下时两端色向白色提亮的比例。")]
        [SerializeField, Range(0f, 1f)] private float pressBrighten = 0.18f;

        [Header("运行时状态（只读，最近一次写入网格）")]
        [Tooltip("最近一次写入的顶部顶点色。")]
        [SerializeField] private Color lastTopColor = Color.white;
        [Tooltip("最近一次写入的底部顶点色。")]
        [SerializeField] private Color lastBottomColor = Color.white;
        [Tooltip("最近一次写入的顶点色最大通道差（纯色=0）。")]
        [SerializeField] private float lastSpread;
        [Tooltip("最近一次是否处于渐变激活态（hover/press）。")]
        [SerializeField] private bool lastGradientActive;
        [Tooltip("最近一次处理的顶点数。")]
        [SerializeField] private int lastVertexCount;

        public bool EnabledBySettings
        {
            get { return enabledBySettings; }
            set
            {
                if (enabledBySettings == value)
                {
                    return;
                }

                enabledBySettings = value;
                Refresh();
            }
        }

        public float Strength
        {
            get { return strength; }
            set
            {
                strength = Mathf.Clamp01(value);
                Refresh();
            }
        }

        public Color BaseColor => baseColor;
        public Color AccentColor => accentColor;
        public Color LastTopColor => lastTopColor;
        public Color LastBottomColor => lastBottomColor;
        public float LastSpread => lastSpread;
        public bool LastGradientActive => lastGradientActive;
        public int LastVertexCount => lastVertexCount;
        public bool Hovered { get; private set; }
        public bool Pressed { get; private set; }

        /// <summary>TwinUIButton.Apply() 驱动入口：更新基色/强调色与 hover/press 状态。</summary>
        public void SetState(Color fillColor, Color accent, bool hovered, bool pressed)
        {
            bool changed = baseColor != fillColor || accentColor != accent || Hovered != hovered || Pressed != pressed;
            baseColor = fillColor;
            accentColor = accent;
            Hovered = hovered;
            Pressed = pressed;

            if (changed)
            {
                Refresh();
            }
        }

        /// <summary>不改变状态、只重刷顶点（主题切换/构建器写入后调用）。</summary>
        public void Refresh()
        {
            Graphic g = graphic;
            if (g != null)
            {
                g.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive())
            {
                return;
            }

            int count = vh.currentVertCount;
            if (count == 0)
            {
                return;
            }

            lastVertexCount = count;
            if (!enabledBySettings)
            {
                lastGradientActive = false;
                lastSpread = 0f;
                return;
            }

            bool active = Hovered || Pressed;
            Color top = active ? Color.Lerp(baseColor, accentColor, strength) : baseColor;
            Color bottom = baseColor;
            if (Pressed)
            {
                top = Color.Lerp(top, Color.white, pressBrighten);
                bottom = Color.Lerp(bottom, Color.white, pressBrighten * 0.6f);
            }

            float alpha = baseColor.a;
            top.a = alpha;
            bottom.a = alpha;

            float minY = float.MaxValue;
            float maxY = float.MinValue;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            var vertex = new UIVertex();
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Vector3 p = vertex.position;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
            }

            float rangeY = Mathf.Max(0.0001f, maxY - minY);
            float rangeX = Mathf.Max(0.0001f, maxX - minX);
            float maxSpread = 0f;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Vector3 p = vertex.position;
                float t = 0f;
                if (verticalGradient)
                {
                    t = Mathf.Clamp01((p.y - minY) / rangeY);
                }

                if (horizontalGradient)
                {
                    t = Mathf.Clamp01((p.x - minX) / rangeX);
                }

                Color c = active ? Color.Lerp(bottom, top, t) : bottom;
                c.a = alpha;
                vertex.color = c;
                vh.SetUIVertex(vertex, i);

                if (active)
                {
                    maxSpread = Mathf.Max(maxSpread, Mathf.Abs(c.r - bottom.r));
                    maxSpread = Mathf.Max(maxSpread, Mathf.Abs(c.g - bottom.g));
                    maxSpread = Mathf.Max(maxSpread, Mathf.Abs(c.b - bottom.b));
                }
            }

            lastTopColor = top;
            lastBottomColor = bottom;
            lastSpread = maxSpread;
            lastGradientActive = active;
        }
    }
}