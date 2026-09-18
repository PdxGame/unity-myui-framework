using System;
using MyUI.Core;

namespace MyUI.Runtime
{
    /// <summary>
    /// 面板声明特性：标注在 UIPanel 子类上，描述层级与行为。
    /// 未标注时按默认值（Normal 层、非全屏、单实例、可入池）。
    /// 用法示例：[UIPanel(UILayer.Popup, BlockInput = true)]
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

        /// <summary>是否创建全屏输入阻断器。默认 false，依赖 Prefab 自身射线设置。</summary>
        public bool BlockInput { get; set; } = false;

        /// <summary>对下方面板的暂停策略。默认 Inherit：未显式配置时不暂停。</summary>
        public UIPauseBelowMode PauseBelow { get; set; } = UIPauseBelowMode.Inherit;

        /// <summary>打开策略。默认 Push。</summary>
        public UIOpenMode OpenMode { get; set; } = UIOpenMode.Push;

        /// <summary>同类型是否允许多实例（Toast 类专用）。默认 false：重复打开只聚焦已有实例。</summary>
        public bool AllowMulti { get; set; } = false;

        /// <summary>关闭时是否入池复用。默认 true。</summary>
        public bool Poolable { get; set; } = true;

        /// <summary>资源地址覆盖；默认按约定 "UIPanel/{TypeName}"。</summary>
        public string Address { get; set; } = null;

    }
}
