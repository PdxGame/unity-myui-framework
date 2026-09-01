namespace MyUI.Runtime
{
    /// <summary>
    /// 资源加载方式开关（UIManager.AssetMode）：
    /// - Resources：使用框架自带的 ResourcesPanelLoader（兼容模式）；
    /// - Addressables（默认）：使用可选程序集 MyUI.Loaders.Addressables 的 AddressablesPanelLoader
    ///   （未安装该程序集时自动回退 Resources 并警告）。
    /// 切换方式：窗口①（MyUI → Settings & Registration）勾选保存，或代码 UIManager.AssetMode = UIAssetMode.Resources;
    /// 也可用 UIManager.Init(loader) 显式指定加载器覆盖此开关。
    /// </summary>
    public enum UIAssetMode
    {
        /// <summary>Resources 加载（框架自带，零第三方依赖）。</summary>
        Resources,

        /// <summary>Addressables 加载（需要安装可选程序集 MyUI.Loaders.Addressables）。</summary>
        Addressables,
    }
}