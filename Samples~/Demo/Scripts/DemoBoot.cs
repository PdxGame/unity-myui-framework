using MyUI.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MyUI.Examples
{
    /// <summary>
    /// Demo 启动器（示例）：不需要场景里放任何对象，任意场景 Play 即出主菜单。
    /// 使用窗口①配置的加载模式（MyUI → Settings & Registration）：
    /// - Resources 模式：Sample 面板位于 Resources/UIPanel/，直接可跑；
    /// - Addressables 模式：需先用窗口②把面板注册进组（地址=类型名），本示例即可跑通 Addressables 链路。
    /// </summary>
    public static class DemoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootDemo()
        {
            EnsureEventSystem();
            if (UiManager.Instance == null)
            {
                UiManager.Bootstrap(); // 使用窗口配置的加载器（缺配置默认 Addressables）
            }

            UiManager.OpenPanel<MainMenuPanel>();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            Object.DontDestroyOnLoad(go);
        }
    }
}