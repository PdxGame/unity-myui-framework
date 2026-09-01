using System;
using MyUI.Core;
using UnityEngine;

namespace MyUI.Runtime
{
    /// <summary>
    /// 兼容回退加载器：按地址从 Resources 加载预制体并实例化（同步完成）。
    /// 注意：Addressables 是默认加载方式（见 MyUI → Settings），本加载器仅作
    /// 未安装 Addressables 程序集时的回退。地址约定 = 面板类型名（对应 Resources 根下同名预制体）。
    /// </summary>
    public sealed class ResourcesPanelLoader : IAssetLoader
    {
        public void LoadViewAsync(string address, Action<object, string> onDone)
        {
            var prefab = Resources.Load<GameObject>(address);
            if (prefab == null)
            {
                onDone?.Invoke(null, "Resources 中未找到面板预制体: " + address);
                return;
            }

            var instance = UnityEngine.Object.Instantiate(prefab);
            onDone?.Invoke(instance, null);
        }

        public void ReleaseView(object view)
        {
            if (view is GameObject go)
            {
                UnityEngine.Object.Destroy(go);
            }
        }

        public string DefaultAddress(Type panelType)
        {
            return panelType.Name;
        }
    }
}