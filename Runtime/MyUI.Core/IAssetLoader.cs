using System;

namespace MyUI.Core
{
    /// <summary>
    /// 资源加载抽象（GameFramework IResourceManager 注入思路：UI 模块不依赖任何具体资源系统）。
    /// 框架本体自带 Resources 实现（默认兜底）；Addressables 实现位于可选程序集 MyUI.Loaders.Addressables。
    /// 视图对象对 Core 是不透明的 object，具体类型由 Runtime 端负责转换。
    /// </summary>
    public interface IAssetLoader
    {
        /// <summary>
        /// 异步加载并按地址实例化面板视图。
        /// 成功：onDone(view, null)；失败：onDone(null, errorMessage)。
        /// 返回的 view 必须是"已实例化、可挂接"的对象（如 GameObject）。
        /// </summary>
        void LoadViewAsync(string address, Action<object, string> onDone);

        /// <summary>销毁由本加载器产生的视图（同步或异步皆可，但必须最终销毁）。</summary>
        void ReleaseView(object view);

        /// <summary>面板类型 → 默认资源地址（约定 "UIPanel/{TypeName}"）。</summary>
        string DefaultAddress(Type panelType);
    }
}