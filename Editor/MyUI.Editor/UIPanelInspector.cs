using System.Reflection;
using MyUI.Runtime;
using UnityEditor;
using UnityEngine;

namespace MyUI.Editor
{
    /// <summary>
    /// UIPanel 的 Inspector 扩展：选中面板时提供调试按钮 ——
    /// Play 模式下「打开 / 关闭」直接驱动框架；编辑模式提示先进入 Play。
    /// </summary>
    [CustomEditor(typeof(UIPanel), true)]
    public sealed class UIPanelInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var panel = (UIPanel)target;
            EditorGUILayout.Space();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("进入 Play 模式后可用按钮在场景中调试本面板。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开 / 聚焦"))
            {
                OpenReflectively(panel.GetType());
            }

            if (GUILayout.Button("关闭"))
            {
                UIManager.ClosePanel(panel);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>反射调用 UIManager.OpenPanel&lt;T&gt;（同 UIPanelTester 机制）。</summary>
        internal static void OpenReflectively(System.Type panelType)
        {
            MethodInfo openMethod = typeof(UIManager).GetMethod("OpenPanel",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(object), typeof(System.Action<UIPanel>), typeof(System.Action<string>) },
                null);
            MethodInfo generic = openMethod.MakeGenericMethod(panelType);
            generic.Invoke(null, new object[] { null, null, null });
        }
    }
}