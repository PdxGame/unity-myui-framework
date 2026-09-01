namespace MyUI.Runtime
{
    /// <summary>
    /// 开发期资源加载方式开关（UiManager.AssetMode）：
    /// - Resources：默认，使用框架自带的 ResourcesPanelLoader；
    /// - Addressables：使用可选程序集 MyUI.Loaders.Addressables 的 AddressablesPanelLoader
    ///   （未安装该程序集时自动回退 Resources 并警告）。
    /// 切换方式：UiManager.AssetMode = UiAssetMode.Addressables;（业务代码只需调用 UiManager.Bootstrap()）。
    /// 也可用 UiManager.Bootstrap(loader) 显式指定加载器覆盖此开关。
    /// </summary>
    public enum UiAssetMode
    {
        /// <summary>Resources 加载（框架自带，零第三方依赖）。</summary>
        Resources,

        /// <summary>Addressables 加载（需要安装可选程序集 MyUI.Loaders.Addressables）。</summary>
        Addressables,
    }
}