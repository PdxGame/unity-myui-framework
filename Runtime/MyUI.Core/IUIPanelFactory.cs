namespace MyUI.Core
{
    /// <summary>
    /// 视图工厂：把加载器返回的视图实例挂接到管理层级 / 组件，并执行激活、置顶与销毁。
    /// 由 Runtime（UIManager）实现；Core 只通过本接口操作视图，
    /// 从而保持 Core 零 Unity 依赖（GameFramework Helper 分层思路）。
    /// 视图的最终销毁统一走 IAssetLoader.ReleaseView（本接口的 DestroyView 实现会转发），
    /// 避免实例与资源句柄被重复释放。
    /// </summary>
    public interface IUIPanelFactory
    {
        /// <summary>
        /// 把加载器已经实例化出的视图挂到其 PanelRecord.Layer 对应的层 Canvas 下，
        /// 完成组件注册后返回生命周期实现（Runtime 侧即 UIPanel 组件）。
        /// </summary>
        IUIPanelView AttachView(PanelRecord record, object viewInstance);

        /// <summary>销毁视图（实现应转发到 IAssetLoader.ReleaseView 以同时释放资源句柄）。</summary>
        void DestroyView(IUIPanelView view);

        /// <summary>设置视图激活状态（入池时 SetActive(false)，复用时 SetActive(true)）。</summary>
        void SetViewActive(IUIPanelView view, bool active);

        /// <summary>把视图置为所在层的顶层（SetAsLastSibling；打开 / 聚焦时由 Core 调用）。</summary>
        void MoveToTop(IUIPanelView view);

        /// <summary>
        /// 刷新视图的运行时上下文（SerialId / PanelName / Layer / Manager 等）。
        /// 池复用路径不经过 AttachView，但实例的 SerialId 已变化，必须刷新，
        /// 否则面板自带 Close()（按 SerialId 定向关闭）会失效。
        /// </summary>
        void RefreshViewContext(IUIPanelView view, PanelRecord record);

        /// <summary>
        /// 视图就绪通知：视图已挂接/激活完成、即将进入生命周期（新加载与池复用两条路径都会调用）。
        /// Runtime 侧用于执行"入栈后按 Inspector 配置撤销"等打开时检查。
        /// </summary>
        void OnViewReady(IUIPanelView view, PanelRecord record);
    }
}