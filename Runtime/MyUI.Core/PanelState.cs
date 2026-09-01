namespace MyUI.Core
{
    /// <summary>
    /// 面板生命周期状态（UiManagerCore 内部状态机，GameFramework UIFormInfo.Status 思路）。
    /// 状态迁移只发生在 UiManagerCore 内；被遮挡/暂停是 Open 状态的子标记（见 PanelRecord.Covered / Paused）。
    /// </summary>
    public enum PanelState
    {
        Requested,  // 打开请求已登记，尚未开始加载
        Loading,    // 资源加载 / 实例化中
        Open,       // 已打开（可见或被遮挡都在此状态）
        Closing,    // 关闭流程中（含关闭动画的延迟销毁阶段）
        Closed,     // 已关闭（记录保留到当帧事件派发完成后移除）
    }
}