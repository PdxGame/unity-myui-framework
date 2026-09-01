using MyUI.Runtime;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MyUI.Editor
{
    /// <summary>
    /// 面板注册助手（**手动**菜单：MyUI → Panels → Add to Addressables）：
    /// 把 Assets/MyUI/Panels 下所有带 UiPanel 组件的预制体加入 UIPanels 组，地址 = 类型名；
    /// 已存在的条目跳过（幂等）。不做删除/修改，只用不用由你决定。
    /// （尊重"Addressables 组由用户自己管理"的原则：不做任何自动修改。）
    /// </summary>
    public static class PanelAutoRegistrar
    {
        private const string PanelsFolder = "Assets/MyUI/Panels";
        private const string GroupName = "UIPanels";

        [MenuItem("MyUI/Panels/Add to Addressables")]
        public static void AddPanelsToAddressables()
        {
            if (!AssetDatabase.IsValidFolder(PanelsFolder))
            {
                Debug.LogWarning("[MyUI] 面板目录不存在: " + PanelsFolder);
                return;
            }

            try
            {
                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                {
                    Debug.LogWarning("[MyUI] Addressables 未初始化，请先打开 Window → Asset Management → Addressables → Groups");
                    return;
                }

                AddressableAssetGroup group = settings.FindGroup(GroupName);
                if (group == null)
                {
                    Debug.LogWarning("[MyUI] 组 '" + GroupName + "' 不存在，请在 Addressables Groups 窗口先创建");
                    return;
                }

                int added = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PanelsFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || prefab.GetComponentInChildren<UiPanel>(true) == null)
                    {
                        continue; // 只处理带 UiPanel 组件的面板
                    }

                    string typeName = path.Substring(path.LastIndexOf('/') + 1).Replace(".prefab", "");

                    bool exists = false;
                    foreach (AddressableAssetEntry e in group.entries)
                    {
                        if (e != null && e.address == typeName)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (exists)
                    {
                        continue;
                    }

                    AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
                    if (entry == null)
                    {
                        continue;
                    }

                    entry.address = typeName;
                    added++;
                }

                if (added > 0)
                {
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
                    AssetDatabase.SaveAssets();
                }

                Debug.Log($"[MyUI] 面板注册：新增 {added} 个条目到组 '{GroupName}'（地址=类型名；已存在的不动）");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MyUI] 面板注册失败: " + e.Message);
            }
        }
    }
}