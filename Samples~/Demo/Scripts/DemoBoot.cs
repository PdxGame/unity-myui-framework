using MyUI.Runtime;
using UnityEngine;

namespace MyUI.Examples
{
    /// <summary>
    /// Demo 启动器（示例）：不需要场景里放任何对象，任意场景 Play 即出主菜单。
    /// 启动 = 一行 UiManager.Init()（框架唯一启动方式；正式项目在自建入口里调用一次即可）。
    /// - Resources 模式：Sample 面板位于 Resources/UIPanel/，直接可跑；
    /// - Addressables 模式：先用窗口②把面板注册进组（地址=类型名）即可跑通。
    /// </summary>
    public static class DemoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootDemo()
        {
            UiManager.Init(); // 启动框架（幂等：重复调用直接返回）
            UiManager.OpenPanel<MainMenuPanel>();
        }
    }
}