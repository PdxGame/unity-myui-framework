using System;
using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MyUI.Loaders
{
    /// <summary>
    /// Addressables 加载器（可选扩展程序集 MyUI.Loaders.Addressables）。
    /// 框架默认使用本加载器；也可在 MyUI → Settings 配置中切换。
    /// 地址约定默认 "{TypeName}"（面板预制体的 Addressables 地址），可经
    /// [UIPanel(Address=...)]、OpenPanel&lt;T&gt;(address) 或配置表覆盖。
    /// </summary>
    public sealed class AddressablesPanelLoader : IAssetLoader
    {
        public void LoadViewAsync(string address, Action<object, string> onDone)
        {
            var operation = Addressables.InstantiateAsync(address);
            operation.Completed += handle =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    onDone?.Invoke(handle.Result, null);
                }
                else
                {
                    string error = handle.OperationException != null
                        ? handle.OperationException.Message
                        : "Addressables 加载失败: " + address;
                    onDone?.Invoke(null, error);
                }
            };
        }

        public void ReleaseView(object view)
        {
            if (view is GameObject go)
            {
                Addressables.ReleaseInstance(go);
            }
        }

        public string DefaultAddress(Type panelType)
        {
            return panelType.Name;
        }
    }
}