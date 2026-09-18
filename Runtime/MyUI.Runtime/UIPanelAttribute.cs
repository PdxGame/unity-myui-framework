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
        /// 是否全屏布局：Runtime 会把根 RectTransform 拉伸到所属层。
        /// 默认 false（窗口面板保留 Prefab 自身尺寸与锚点）。
        /// </summary>
        public bool FullScreen { get; set; } = false;

        /// <summary>输入阻断方式。默认 Inherit：全屏 Modal，非全屏 Self。</summary>
        public UIInputMode InputMode { get; set; } = UIInputMode.Inherit;

        /// <summary>对下方面板的暂停策略。默认 Inherit：全屏暂停下方，非全屏不暂停。</summary>
        public UIPauseBelowMode PauseBelow { get; set; } = UIPauseBelowMode.Inherit;

        /// <summary>打开策略。默认 Inherit：Stackable=true 时 Push，否则 Overlay。</summary>
        public UIOpenMode OpenMode { get; set; } = UIOpenMode.Inherit;

        /// <summary>同类型是否允许多实例（Toast 类专用）。默认 false：重复打开只聚焦已有实例。</summary>
        public bool AllowMulti { get; set; } = false;

        /// <summary>关闭时是否入池复用。默认 true。</summary>
        public bool Poolable { get; set; } = true;

        /// <summary>资源地址覆盖；默认按约定 "UIPanel/{TypeName}"。</summary>
        public string Address { get; set; } = null;

        /// <summary>
        /// 兼容旧配置的返回开关。默认 true。
        /// OpenMode=Push 时参与返回；Overlay / Replace 不新增返回层级。
        /// </summary>
        public bool Stackable { get; set; } = true;
    }
}
