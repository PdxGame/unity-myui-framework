namespace MyUI.Core
{
    /// <summary>
    /// UI 层级（QFramework UILevel 思路的固定层方案）。
    /// 每个层在运行时是一个独立 Canvas（sortingOrder = 层索引 x 100），
    /// 层内面板按打开顺序以 SiblingIndex 排序。
    /// 高层级的面板会遮挡低层级的所有面板（触发 OnCover / OnPause 回调）。
    /// </summary>
    public enum UILayer
    {
        Background = 0, // 背景 / 底层（全屏底图、场景 UI 载体）
        Normal = 1,     // 常规界面（主菜单、主界面）
        Popup = 2,      // 弹窗（设置、确认框、商店）
        Guide = 3,      // 引导层
        System = 4,     // 系统 UI（加载遮罩、断线提示）
        Toast = 5,      // 轻提示（多实例，如飘字）
    }
}// 
