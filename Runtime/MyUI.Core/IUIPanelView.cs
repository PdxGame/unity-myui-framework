namespace MyUI.Core
{
    /// <summary>
    /// 面板视图生命周期抽象。Core（UIManagerCore）通过本接口按固定次序驱动面板；
    /// Runtime 的 UIPanel（MonoBehaviour）实现本接口并把调用转发给用户覆写的 protected virtual 方法。
    /// 与 UI 框架解耦：单元测试可用假视图记录调用序列。
    /// </summary>
    public interface IUIPanelView
    {
        /// <summary>视图创建完成立即调用，整个生命周期仅一次（池复用不再调用）。</summary>
        void OnInit();

        /// <summary>每次打开都会调用（含池复用），用于按 userData 重置面板状态。</summary>
        void OnOpen(object userData);

        /// <summary>完全可见、可交互。</summary>
        void OnShow();

        /// <summary>被上层面板遮挡（但未进入暂停态）。</summary>
        void OnCover();

        /// <summary>重新露出。</summary>
        void OnReveal();

        /// <summary>被声明 PauseBelow 的覆盖型面板遮挡，进入暂停态（应停止计时器 / 动画 / 输入响应）。</summary>
        void OnPause();

        /// <summary>暂停解除。</summary>
        void OnResume();

        /// <summary>关闭流程开始（隐藏 / 播放关闭动画之前）。</summary>
        void OnHide();

        /// <summary>即将销毁或入池（pooled=true 表示入池复用，应释放临时资源但保留可重置状态）。</summary>
        void OnClose(bool pooled);

        /// <summary>实例真正销毁（立即关闭 / 池淘汰）时调用。</summary>
        void OnDestroyed();

        /// <summary>每帧驱动（仅 Open 状态且未暂停的面板收到）。</summary>
        void OnTick(float deltaTime);
    }
}
