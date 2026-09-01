using MyUI.Runtime;
using UnityEngine;

namespace MyUI.Examples
{
    /// <summary>
    /// Demo 启动器（示例）：不需要场景里放任何对象，任意场景 Play 即出主菜单。
    /// - 框架启动时自动创建 EventSystem（无需手动）；加载模式取窗口①配置（MyUI → Settings & Registration）：
    /// - Resources 模式：Sample 面板位于 Resources/UIPanel/，直接可跑；
    /// - Addressables 模式：先用窗口②把面板注册进组（地址=类型名）即可跑通。
    /// </summary>
    public static class DemoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootDemo()
        {
            if (UiManager.Instance == null)
            {
                UiManager.Bootstrap(); // 使用窗口配置的加载器（缺配置默认 Addressables）
            }

            UiManager.OpenPanel<MainMenuPanel>();
        }
    }
}