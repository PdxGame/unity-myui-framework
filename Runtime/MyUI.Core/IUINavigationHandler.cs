namespace MyUI.Core
{
    /// <summary>
    /// 页面内部导航处理器。顶层 UIPanel 可实现它来接管 Back：
    /// 返回 true 表示页面内部已消费本次返回，UIManager 不再关闭顶层页面。
    /// </summary>
    public interface IUINavigationHandler
    {
        bool HandleBack();
    }
}
