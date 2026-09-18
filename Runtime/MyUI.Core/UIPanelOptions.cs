namespace MyUI.Core
{
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
    /// </summary>
    public enum UIOpenMode
    {
        /// <summary>覆盖在当前内容之上，不登记返回路径。</summary>
        Overlay = 1,

        /// <summary>保留当前页面并登记返回路径，Back 时关闭当前页。</summary>
        Push = 2,

        /// <summary>新页面成功后关闭当前可返回页面，不增加返回层级。</summary>
        Replace = 3,
    }
}
