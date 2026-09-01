using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MyUI.Runtime
{
    /// <summary>
    /// 面板测试器（QFramework UIPanelTester 思路）：编辑 / Play 模式下快速打开任意面板验证。
    /// 挂到场景任意 GameObject，填面板类型全名（如 MyUI.Examples.MainMenuPanel），
    /// Play 模式下点 Open（或勾选 OpenOnStart）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPanelTester : MonoBehaviour
    {
        [SerializeField] private string panelTypeName = "";
        [SerializeField] private bool openOnStart = false;

        private void Start()
        {
            if (openOnStart)
            {
                Open();
            }
        }

        /// <summary>按类型名打开面板（同 UiManager.OpenPanel&lt;T&gt;）。</summary>
        public void Open()
        {
            Type type = ResolvePanelType(panelTypeName);
            if (type == null)
            {
                Debug.LogError("[MyUI] UiPanelTester 找不到面板类型: " + panelTypeName);
                return;
            }

            MethodInfo openMethod = typeof(UiManager).GetMethod(nameof(UiManager.OpenPanel),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(object), typeof(Action<UiPanel>), typeof(Action<string>) },
                null);
            if (openMethod == null)
            {
                Debug.LogError("[MyUI] UiManager.OpenPanel 签名变化，UiPanelTester 需要更新");
                return;
            }

            MethodInfo generic = openMethod.MakeGenericMethod(type);
            generic.Invoke(null, new object[] { null, null, null });
        }

        /// <summary>按类型名关闭面板。</summary>
        public void Close()
        {
            Type type = ResolvePanelType(panelTypeName);
            if (type != null)
            {
                UiManager.ClosePanel(type.Name);
            }
        }

        private static Type ResolvePanelType(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // 1) 精确全名（含程序集限定也不怕）
            Type exact = Type.GetType(name);
            if (exact != null)
            {
                return exact;
            }

            // 2) 在已加载程序集里按全名 / 短名匹配
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name);
                if (type != null && typeof(UiPanel).IsAssignableFrom(type))
                {
                    return type;
                }
            }

            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(t => t.Name == name && typeof(UiPanel).IsAssignableFrom(t));
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).ToArray();
            }
        }
    }
}