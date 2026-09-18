namespace MyUI.Core
{
    /// <summary>
    /// UI 层级（QFramework UILevel 思路的固定层方案）。
    /// 每个层在运行时是一个独立 Canvas，层内面板按打开顺序以 SiblingIndex 排序。
    /// 实际渲染顺序由 UILayerOrder 统一定义，避免枚举序列化值与视觉顺序绑定。
    /// </summary>
    public enum UILayer
    {
        Background = 0, // 背景 / 底层（全屏底图、场景 UI 载体）
        Normal = 1,     // 页面层（主菜单、装备页、背包页）
        Popup = 2,      // 弹窗层（设置、确认框、商店窗口）
        Guide = 3,      // 引导层
        System = 4,     // 系统模态层（加载遮罩、断线提示、强制更新）
        Toast = 5,      // 轻提示（多实例，如飘字）
        HUD = 6,        // 常驻 HUD（金币、血量、任务追踪；排在 Popup 下方）
    }
}// 
