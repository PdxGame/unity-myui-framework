namespace MyUI.Core
{
    /// <summary>
    /// 输入阻断方式。面板尺寸由 Prefab 锚点决定，本枚举是独立的输入行为配置。
    /// Inherit 使用 Self，即不创建框架自动阻断器。
    /// </summary>
    public enum UIInputMode
    {
        Inherit = 0,

        /// <summary>不添加框架输入阻断器，完全依赖 Prefab 自身射线设置。</summary>
        None = 1,

        /// <summary>保留 Prefab 自身命中区域，不创建全屏阻断器。</summary>
        Self = 2,

        /// <summary>自动添加全屏透明阻断器，阻止事件穿透到下层。</summary>
        Modal = 3,
    }

    /// <summary>
    /// 对下方面板的逻辑暂停策略。
    /// Inherit 使用 Never，即不因本面板自动暂停下层。
    /// </summary>
    public enum UIPauseBelowMode
    {
        Inherit = 0,
        Never = 1,
        Always = 2,
    }

    /// <summary>
    /// 打开策略。
    /// Inherit 的默认规则：Stackable=true 时 Push，否则 Overlay。
    /// </summary>
    public enum UIOpenMode
    {
        Inherit = 0,

        /// <summary>覆盖在当前内容之上，不登记返回路径。</summary>
        Overlay = 1,

        /// <summary>保留当前页面并登记返回路径，Back 时关闭当前页。</summary>
        Push = 2,

        /// <summary>新页面成功后关闭当前可返回页面，不增加返回层级。</summary>
        Replace = 3,
    }
}
