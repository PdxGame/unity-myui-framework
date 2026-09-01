using MyUI.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MyUI.Examples
{
    /// <summary>
    /// Demo 启动器（静态自举版：不需要场景里放任何对象）。
    /// 打开任意场景直接 Play 即出主菜单：
    /// 游戏运行时自动执行 → 设置加载方式（Resources 兼容模式，本 Demo 零配置即跑）
    /// → 启动框架 → 打开主菜单。
    /// 生产项目请用默认 Addressables 模式（什么都不用写，AutoBoot 自动启动）。
    /// </summary>
    public static class DemoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootDemo()
        {
            UiManager.AutoBoot = false;                              // 本 Demo 自行控制启动
            UiManager.Bootstrap(new ResourcesPanelLoader());         // 兼容模式：克隆即跑
            EnsureEventSystem();
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