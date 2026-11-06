// 图元主题角色标签（P3 从 TwinUITheme.cs 拆出独立文件：类名与文件名一致，Unity 才能生成可引用的
// MonoScript 资产；内嵌 MonoScript 引用会导致进 Play 后组件不实例化——见 UI/36 修复）。
using UnityEngine;

namespace DigitalTwin.UI
{
    /// <summary>图元角色标签：主题切换时按角色重刷。</summary>
    public sealed class TwinUIToneTag : MonoBehaviour
    {
        public TwinUIToneRole Role = TwinUIToneRole.None;
    }
}