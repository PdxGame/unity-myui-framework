using MyUI.Runtime;
using UnityEngine;

namespace MyUI.Examples
{
    /// <summary>
    /// Demo 启动器（示例）：不需要场景里放任何对象，任意场景 Play 即出主菜单。
    /// 框架启动完全由 AutoBoot 自动完成（无需任何启动代码）；本示例只负责
    /// "框架就绪后打开主菜单"，通过 Started 事件等待就绪，与 AutoBoot 顺序无关。
    /// - Resources 模式：Sample 面板位于 Resources/UIPanel/，直接可跑；
    /// - Addressables 模式：先用窗口②把面板注册进组（地址=类型名）即可跑通。
    /// 注意：不要在这里调用 Bootstrap()——那会与 AutoBoot 产生先后竞态（真报错来源）；
    /// 若你关闭了 AutoBoot（AutoBoot=false），由你的游戏逻辑自行启动并打开页面。
    /// </summary>
    public static class DemoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootDemo()
        {
            if (UiManager.Instance != null)
            {
                UiManager.OpenPanel<MainMenuPanel>();
                return;
            }

            // 框架尚未就绪（AutoBoot 还没执行）：等待就绪事件，顺序无关
            UiManager.Started += OnFrameworkStarted;
        }

        private static void OnFrameworkStarted()
        {
            UiManager.Started -= OnFrameworkStarted; // 只等一次
            UiManager.OpenPanel<MainMenuPanel>();
        }
    }
}