// 运行期输入仲裁器：解决"相机 / 世界射线 / IK 手柄 / UI 指针 / 窗口拖拽 / 模态层"同时
// 争夺同一份鼠标键盘状态的问题。
//
// 设计要点（P1b-2）：
//   - 栈式所有权（LIFO）：Begin(owner) 压栈，End(owner) 从栈顶开始移除最近的同 owner 记录；
//   - 优先级由枚举数值决定（数值越大优先级越高）：
//       None(-1) < Camera(0) < SceneRay(1) < IkHandle(2) < UiPointer(3)
//       < WindowDrag(4) < WindowResize(5) < Modal(6)
//   - Allows(owner) = 栈为空，或 owner 的优先级 >= 栈顶优先级；
//     → 低优先级消费者（Camera/SceneRay）被 IK/UI/窗口/模态层拥有时自动让路；
//     → 同优先级重入允许（例如相机在相机拖拽中再次 Begin 不会自锁）。
//   - Begin 只在"不低于当前栈顶"时入栈（低于栈顶的请求被忽略，且不产生任何分配）；
//   - 无 Update、无 LINQ、无字符串拼接；Begin/End/Allows 只做数组操作。
//   - Instance 静态入口：场景唯一；缺失时在 Play 中自动创建并 LogWarning（不抛异常）；
//     Editor（非 Play）不自动创建，返回 null，方便构建期菜单/审计只读判断。
//
// 使用方（P1b-2 接入）：
//   - TwinPlayCameraRig：右键/中键按住时 Begin(Camera)，松开 End(Camera)；
//     轮询输入前先 Allows(Camera)，指针在 UI 上时不进入相机所有权。
//   - TwinPlayIkHandle：左键开始拖动手柄时 Begin(IkHandle)，结束/取消/复位时 End(IkHandle)。
//   - TwinPlayRayVisual：只读查询 Allows(SceneRay)，不改命中逻辑。
using UnityEngine;

namespace DigitalTwin.Interaction
{
    /// <summary>输入所有权类型；枚举数值即优先级（越大越高）。</summary>
    public enum TwinUiInputOwner
    {
        /// <summary>没有所有者（栈为空时的 Current 值）。</summary>
        None = -1,
        /// <summary>桌面漫游相机（拖拽视角 / WASD 平移）。</summary>
        Camera = 0,
        /// <summary>世界鼠标射线（SceneRay）。</summary>
        SceneRay = 1,
        /// <summary>IK 末端手柄拖动。</summary>
        IkHandle = 2,
        /// <summary>UI 指针（uGUI 交互）。</summary>
        UiPointer = 3,
        /// <summary>窗口拖动。</summary>
        WindowDrag = 4,
        /// <summary>窗口缩放。</summary>
        WindowResize = 5,
        /// <summary>模态弹窗/遮罩（最高优先级）。</summary>
        Modal = 6
    }

    [DisallowMultipleComponent]
    public sealed class TwinUiInputArbiter : MonoBehaviour
    {
        private const int MaxDepth = 16;

        private readonly TwinUiInputOwner[] _stack = new TwinUiInputOwner[MaxDepth];
        private int _depth;

        private static TwinUiInputArbiter _instance;
        private static bool _missingWarningLogged;

        /// <summary>场景唯一实例。Play 中缺失时自动创建并 LogWarning；Editor 非 Play 时为 null。</summary>
        public static TwinUiInputArbiter Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _instance = FindObjectOfType<TwinUiInputArbiter>();
                if (_instance != null)
                {
                    return _instance;
                }

                if (!Application.isPlaying)
                {
                    return null;
                }

                var go = new GameObject("TwinUiInputArbiter (runtime)");
                _instance = go.AddComponent<TwinUiInputArbiter>();
                if (!_missingWarningLogged)
                {
                    _missingWarningLogged = true;
                    Debug.LogWarning("[INPUT] TwinUiInputArbiter missing in scene; created runtime instance. "
                        + "建议执行菜单 DigitalTwin/UI/26 Attach Input Arbiter 固化到场景。");
                }

                return _instance;
            }
        }

        /// <summary>便捷查询：相机当前是否可独占输入（无仲裁器时视为可用）。</summary>
        public static bool AllowsCamera
        {
            get
            {
                TwinUiInputArbiter arbiter = Instance;
                return arbiter == null || arbiter.Allows(TwinUiInputOwner.Camera);
            }
        }

        /// <summary>便捷查询：世界射线当前是否可用（无仲裁器时视为可用）。</summary>
        public static bool AllowsSceneRay
        {
            get
            {
                TwinUiInputArbiter arbiter = Instance;
                return arbiter == null || arbiter.Allows(TwinUiInputOwner.SceneRay);
            }
        }

        /// <summary>栈顶所有者；栈为空时为 None。</summary>
        public TwinUiInputOwner Current
        {
            get { return _depth > 0 ? _stack[_depth - 1] : TwinUiInputOwner.None; }
        }

        /// <summary>当前栈深度（0 = 无所有者）。</summary>
        public int Depth
        {
            get { return _depth; }
        }

        /// <summary>该 owner 是否被允许（栈为空或优先级不低于栈顶）。</summary>
        public bool Allows(TwinUiInputOwner owner)
        {
            if (owner == TwinUiInputOwner.None)
            {
                return _depth == 0;
            }

            if (_depth == 0)
            {
                return true;
            }

            return (int)owner >= (int)_stack[_depth - 1];
        }

        /// <summary>压栈申请所有权；低于当前栈顶的请求被忽略（返回 false）。</summary>
        public bool Begin(TwinUiInputOwner owner)
        {
            if (owner == TwinUiInputOwner.None)
            {
                return false;
            }

            if (_depth > 0 && (int)owner < (int)_stack[_depth - 1])
            {
                return false;
            }

            if (_depth >= MaxDepth)
            {
                Debug.LogWarning("[INPUT] arbiter stack overflow (" + MaxDepth + "); ignored owner=" + owner);
                return false;
            }

            _stack[_depth] = owner;
            _depth++;
            return true;
        }

        /// <summary>从栈顶向下移除最近的同 owner 记录；没有则返回 false。</summary>
        public bool End(TwinUiInputOwner owner)
        {
            if (_depth == 0)
            {
                return false;
            }

            for (int i = _depth - 1; i >= 0; i--)
            {
                if (_stack[i] != owner)
                {
                    continue;
                }

                for (int j = i; j < _depth - 1; j++)
                {
                    _stack[j] = _stack[j + 1];
                }

                _depth--;
                _stack[_depth] = TwinUiInputOwner.None;
                return true;
            }

            return false;
        }

        /// <summary>清空全部所有权（自检/退出 Play/异常恢复用）。</summary>
        public void EndAll()
        {
            for (int i = 0; i < _depth; i++)
            {
                _stack[i] = TwinUiInputOwner.None;
            }

            _depth = 0;
        }

        /// <summary>调试用栈快照（只在日志/自检里调用，避免热路径分配）。</summary>
        public string DescribeStack()
        {
            if (_depth == 0)
            {
                return "(empty)";
            }

            var sb = new System.Text.StringBuilder(64);
            for (int i = 0; i < _depth; i++)
            {
                if (i > 0)
                {
                    sb.Append(" > ");
                }

                sb.Append(_stack[i].ToString());
            }

            return sb.ToString();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[INPUT] duplicate TwinUiInputArbiter on '" + name + "'; disabled this copy. Keep one on the play camera.");
                enabled = false;
                return;
            }

            _instance = this;
            _depth = 0;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void ResetStaticOnLoad()
        {
            _instance = null;
            _missingWarningLogged = false;
        }
#endif
    }

    /// <summary>静态便捷门面，避免调用点到处写 Instance 判空。</summary>
    public static class TwinUiInput
    {
        /// <summary>相机是否可独占输入。</summary>
        public static bool AllowsCamera
        {
            get { return TwinUiInputArbiter.AllowsCamera; }
        }

        /// <summary>世界射线是否可独占输入。</summary>
        public static bool AllowsSceneRay
        {
            get { return TwinUiInputArbiter.AllowsSceneRay; }
        }

        /// <summary>当前栈顶所有者（无仲裁器时为 None）。</summary>
        public static TwinUiInputOwner CurrentOwner
        {
            get
            {
                TwinUiInputArbiter arbiter = TwinUiInputArbiter.Instance;
                return arbiter == null ? TwinUiInputOwner.None : arbiter.Current;
            }
        }

        /// <summary>开始占用（返回是否成功）。</summary>
        public static bool Begin(TwinUiInputOwner owner)
        {
            TwinUiInputArbiter arbiter = TwinUiInputArbiter.Instance;
            return arbiter != null && arbiter.Begin(owner);
        }

        /// <summary>释放占用。</summary>
        public static bool End(TwinUiInputOwner owner)
        {
            TwinUiInputArbiter arbiter = TwinUiInputArbiter.Instance;
            return arbiter != null && arbiter.End(owner);
        }

        /// <summary>该 owner 是否被允许。</summary>
        public static bool Allows(TwinUiInputOwner owner)
        {
            TwinUiInputArbiter arbiter = TwinUiInputArbiter.Instance;
            return arbiter == null || arbiter.Allows(owner);
        }
    }
}