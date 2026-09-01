using System;
using MyUI.Core;

namespace MyUI.Runtime
{
    /// <summary>
    /// 面板声明特性：标注在 UIPanel 子类上，描述层级与行为。
    /// 未标注时按默认值（Normal 层、非全屏、单实例、可入池）。
    /// 用法示例：[UIPanel(UILayer.Popup, FullScreen = true)]
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class UIPanelAttribute : Attribute
    {
        public UIPanelAttribute() { }

        public UIPanelAttribute(UILayer layer)
        {
            Layer = layer;
        }

        /// <summary>所属层级（默认 Normal）。</summary>
        public UILayer Layer { get; set; } = UILayer.Normal;

        /// <summary>
        /// 是否全屏：作为遮挡链顶端时，会让它下方的所有面板进入暂停态 OnPause。
        /// 默认 false（弹窗类面板遮挡但不暂停下层）。
        /// </summary>
        public bool FullScreen { get; set; } = false;

        /// <summary>同类型是否允许多实例（Toast 类专用）。默认 false：重复打开只聚焦已有实例。</summary>
        public bool AllowMulti { get; set; } = false;

        /// <summary>关闭时是否入池复用。默认 true。</summary>
        public bool Poolable { get; set; } = true;

        /// <summary>资源地址覆盖；默认按约定 "UIPanel/{TypeName}"。</summary>
        public string Address { get; set; } = null;

        /// <summary>
        /// 打开时是否记录返回路径（参与导航栈）。默认 true。
        /// 飘字、过场加载条等临时 UI 应设为 false：它们不该污染返回历史。
        /// 注意：此开关必须在打开前判定，请用特性标注（Inspector 的实例配置来不及生效）。
        /// </summary>
        public bool Stackable { get; set; } = true;
    }
}