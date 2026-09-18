using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MyUI.Core;
using UnityEngine;

namespace MyUI.Runtime
{
    /// <summary>
    /// UI 运行时门面（GameFramework UIComponent / QFramework UIKit 的对应物）。
    /// 使用方式：入口处调用一次 UIManager.Init()，之后用静态 API：
    ///   UIManager.OpenPanel&lt;SettingsPanel&gt;(data, onOpened, onFailed);
    /// 本类同时实现 IUIPanelFactory：把加载完成的视图挂到层 Canvas、注册运行时字段并执行激活/销毁。
    /// 启动时机是显式的：忘记 Init 时静态 API 会抛出明确异常提示先调用 Init。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIManager : MonoBehaviour, IUIPanelFactory
    {
        private static UIManager _instance;

        /// <summary>面板类型 → 解析后的声明（静态缓存，避免重复反射）。</summary>
        private static readonly Dictionary<Type, UIPanelAttribute> AttrCache = new Dictionary<Type, UIPanelAttribute>();

        public static UIManager Instance => _instance;

        private UIManagerCore _core;
        private IAssetLoader _loader;

        // ---- 面板状态事件（供游戏逻辑订阅；由 Core 事件转发） ----
        public event Action<PanelRecord> PanelOpened;
        public event Action<PanelRecord, bool> PanelClosed; // (record, pooled)
        public event Action<string, string> PanelLoadFailed; // (panelName, error)

        /// <summary>资源加载方式开关。默认值来自 MyUI 窗口配置（MyUI → Settings & Registration），
        /// 缺配置时按 Addressables；代码仍可随时覆盖本属性。</summary>
        public static UIAssetMode AssetMode { get; set; } = ResolveDefaultMode();

        /// <summary>
        /// 默认加载模式来源于工具生成的 Assets/MyUI/MyUiLoaderConfig.cs
        /// （窗口勾选保存；缺文件时按 Addressables）。反射读取，避免 Runtime 程序集
        /// 依赖用户的 Assembly-CSharp。
        /// </summary>
        private static UIAssetMode ResolveDefaultMode()
        {
            try
            {
                Type t = Type.GetType("MyUI.MyUiLoaderConfig, Assembly-CSharp");
                string mode = t?.GetField("Mode", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                if (mode == "Resources")
                {
                    return UIAssetMode.Resources;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MyUI] 读取加载模式配置失败（" + e.Message + "），使用 Addressables");
            }

            return UIAssetMode.Addressables;
        }

        /// <summary>
        /// 启动框架：确保 UIRoot 与 EventSystem 存在并绑定加载器（幂等，重复调用返回已有实例）。
        /// loader 为 null 时按 UIManager.AssetMode 选择：默认 Addressables（程序集缺失自动回退
        /// Resources 并警告）；也可显式 Init(loader) 指定任意实现。
        /// 所有静态 API 需在 Init() 之后调用，否则抛出明确异常。
        /// </summary>
        public static UIManager Init(IAssetLoader loader = null)
        {
            if (_instance != null)
            {
                return _instance;
            }

            UIRoot.EnsureCreated();
            EnsureEventSystem(); // UI 点击交互的刚需：场景已有 EventSystem 则不重复创建
            var go = new GameObject("MyUI Manager");
            var manager = go.AddComponent<UIManager>();
            _instance = manager; // 注意：AddComponent 后立刻登记单例（UIManager 无 Awake，必须在此赋值）
            DontDestroyOnLoad(go);
            manager.Initialize(loader ?? CreateDefaultLoader());
            return manager;
        }

        /// <summary>
        /// 确保场景存在 EventSystem（按钮点击依赖）：框架启动自动执行，用户无需手动创建。
        /// 场景已有则不创建；跨场景常驻；新输入系统项目自动使用 InputSystemUIInputModule。
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null ||
                UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Type moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                go.AddComponent(moduleType);
            }
            else
            {
                go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
#else
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        /// <summary>按 AssetMode 创建默认加载器（默认 Addressables；程序集缺失自动回退 Resources 并警告）。</summary>
        private static IAssetLoader CreateDefaultLoader()
        {
            if (AssetMode == UIAssetMode.Addressables)
            {
                try
                {
                    const string typeName = "MyUI.Loaders.AddressablesPanelLoader, MyUI.Loaders.Addressables";
                    Type loaderType = Type.GetType(typeName);
                    if (loaderType != null && typeof(IAssetLoader).IsAssignableFrom(loaderType))
                    {
                        var loader = (IAssetLoader)Activator.CreateInstance(loaderType);
                        Debug.Log("[MyUI] 加载器 = " + loader.GetType().Name + "（默认 Addressables）");
                        return loader;
                    }

                    Debug.LogWarning("[MyUI] 默认 Addressables 加载，但未找到程序集 MyUI.Loaders.Addressables，" +
                        "已回退 Resources 加载器（保持框架本体零第三方依赖）");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[MyUI] 创建 Addressables 加载器失败（" + e.Message + "），已回退 Resources 加载器");
                }
            }

            Debug.Log("[MyUI] 加载器 = ResourcesPanelLoader（兼容模式）");
            return new ResourcesPanelLoader();
        }

        private void Initialize(IAssetLoader loader)
        {
            _loader = loader;
            _core = new UIManagerCore(loader, this);
            _core.PanelOpened += args => PanelOpened?.Invoke(args.Record);
            _core.PanelClosed += args => PanelClosed?.Invoke(args.Record, args.Pooled);
            _core.PanelLoadFailed += args =>
            {
                PanelLoadFailed?.Invoke(args.PanelName, args.Error);
                // 任何加载失败都必须控制台可见（无订阅者也看得到），避免"没 UI 也没报错"的静默
                Debug.LogError("[MyUI] 面板加载失败：" + args.PanelName + " → " + args.Error);
            };
        }

        /// <summary>Release all panels and optionally destroy the persistent UI root.</summary>
        public static void Shutdown(bool destroyRoot = true)
        {
            UIManager instance = _instance;
            if (instance == null)
            {
                if (destroyRoot)
                {
                    UIRoot.DestroyInstance();
                }
                return;
            }

            _instance = null;
            instance._core?.Dispose();
            if (instance != null && instance.gameObject != null)
            {
                Destroy(instance.gameObject);
            }

            if (destroyRoot)
            {
                UIRoot.DestroyInstance();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            _core?.Dispose();
            _core = null;
            _loader = null;
        }

        private void Update()
        {
            if (_core != null)
            {
                _core.Tick(Time.deltaTime);
            }
        }

        // ================= IUIPanelFactory =================

        IUIPanelView IUIPanelFactory.AttachView(PanelRecord record, object viewInstance)
        {
            var go = viewInstance as GameObject;
            if (go == null)
            {
                throw new ArgumentException("加载器返回的视图实例必须是 GameObject", nameof(viewInstance));
            }

            var panel = go.GetComponent(record.PanelType) as UIPanel;
            if (panel == null)
            {
                panel = go.AddComponent(record.PanelType) as UIPanel;
            }

            if (panel == null)
            {
                throw new InvalidOperationException("面板视图缺少 UIPanel 组件: " + record.PanelName);
            }

            ApplyPanelConfiguration(record, panel);
            return panel;
        }

        private static void ApplyPanelConfiguration(PanelRecord record, UIPanel panel)
        {
            // 特性值作为基线；Inspector 的非默认选择可以覆盖 / 收紧它。
            // 这里不能把默认的 Normal/false/true 无条件写回，否则旧预制体会吞掉 [UIPanel] 声明。
            if (panel.inspectorLayer != UILayer.Normal || record.Layer == UILayer.Normal)
            {
                record.Layer = panel.inspectorLayer;
            }

            if (panel.inspectorInputMode != UIInputMode.Inherit)
            {
                record.InputMode = panel.inspectorInputMode;
            }

            if (panel.inspectorPauseBelow != UIPauseBelowMode.Inherit)
            {
                record.PauseBelow = panel.inspectorPauseBelow;
            }

            if (panel.inspectorOpenMode != UIOpenMode.Inherit)
            {
                record.OpenMode = panel.inspectorOpenMode;
            }

            record.Poolable = record.Poolable && panel.inspectorPoolable;
            record.Stackable = record.Stackable && panel.inspectorStackable;

            RectTransform layerRoot = UIRoot.Instance != null
                ? UIRoot.Instance.GetLayerRoot(record.Layer)
                : null;
            if (layerRoot != null && panel.transform.parent != layerRoot)
            {
                panel.transform.SetParent(layerRoot, false);
            }

            UIModalBlocker.Apply(panel.RectTransform, record.EffectiveInputMode == UIInputMode.Modal);

            panel.SerialId = record.SerialId;
            panel.PanelName = record.PanelName;
            panel.Layer = record.Layer;
            panel.Manager = _instance;
        }

        void IUIPanelFactory.OnViewReady(IUIPanelView view, PanelRecord record)
        {
            // Inspector 配置已在 ApplyPanelConfiguration 中写入记录；
            // 导航由 Core 在成功打开路径上统一登记，避免加载失败留下过期历史。
        }

        void IUIPanelFactory.DestroyView(IUIPanelView view)
        {
            if (view is UIPanel panel && panel.gameObject != null && _loader != null)
            {
                _loader.ReleaseView(panel.gameObject);
            }
        }

        void IUIPanelFactory.ReleaseViewInstance(object viewInstance)
        {
            if (viewInstance != null && _loader != null)
            {
                _loader.ReleaseView(viewInstance);
            }
        }

        void IUIPanelFactory.SetViewActive(IUIPanelView view, bool active)
        {
            if (view is UIPanel panel && panel.gameObject != null)
            {
                panel.gameObject.SetActive(active);
            }
        }

        void IUIPanelFactory.MoveToTop(IUIPanelView view)
        {
            if (view is UIPanel panel && panel.transform != null)
            {
                panel.transform.SetAsLastSibling();
            }
        }

        void IUIPanelFactory.RefreshViewContext(IUIPanelView view, PanelRecord record)
        {
            if (view is UIPanel panel)
            {
                ApplyPanelConfiguration(record, panel);
            }
        }

        // ================= 静态门面 API =================

        /// <summary>
        /// 打开面板（泛型版）。data 会原样传给 UIPanel.OnOpen。
        /// onOpened / onFailed 只回调一次；单实例重复打开以现有实例聚焦回调。
        /// 面板声明来自 [UIPanel] 特性（Layer / InputMode / PauseBelow / OpenMode 等），
        /// 未标注时按默认值（Normal 层、单实例、可入池）。
        /// </summary>
        public static void OpenPanel<T>(object data = null,
            Action<UIPanel> onOpened = null, Action<string> onFailed = null,
            UIInputMode? inputMode = null,
            UIPauseBelowMode? pauseBelow = null,
            UIOpenMode? openMode = null) where T : UIPanel
        {
            UIManager instance = RequireInstance();
            UIPanelAttribute attr = ResolveAttribute(typeof(T));
            UIInputMode resolvedInputMode = inputMode ?? attr.InputMode;
            UIPauseBelowMode resolvedPauseBelow = pauseBelow ?? attr.PauseBelow;
            UIOpenMode resolvedOpenMode = openMode ?? attr.OpenMode;
            instance._core.OpenPanel(typeof(T), typeof(T).Name,
                ResolveAddress(typeof(T), attr, instance._loader, null),
                attr.Layer, resolvedInputMode, resolvedPauseBelow, resolvedOpenMode,
                attr.AllowMulti, attr.Poolable, data,
                (view, error) =>
                {
                    if (error != null)
                    {
                        onFailed?.Invoke(error);
                    }
                    else
                    {
                        onOpened?.Invoke(view as UIPanel);
                    }
                },
                stackable: attr.Stackable);
        }

        /// <summary>保留旧三参数签名，兼容反射调用与既有编译代码。</summary>
        public static void OpenPanel<T>(object data, Action<UIPanel> onOpened, Action<string> onFailed)
            where T : UIPanel
        {
            OpenPanel<T>(data, onOpened, onFailed, null, null, null);
        }

        /// <summary>
        /// 打开面板（显式地址版）：自定义资源地址（如 Addressables 地址或其它路径），
        /// 不被默认约定 "UIPanel/{TypeName}" 限制。其余语义同泛型版。
        /// </summary>
        public static void OpenPanel<T>(string address, object data = null,
            Action<UIPanel> onOpened = null, Action<string> onFailed = null,
            UIInputMode? inputMode = null,
            UIPauseBelowMode? pauseBelow = null,
            UIOpenMode? openMode = null) where T : UIPanel
        {
            UIManager instance = RequireInstance();
            UIPanelAttribute attr = ResolveAttribute(typeof(T));
            UIInputMode resolvedInputMode = inputMode ?? attr.InputMode;
            UIPauseBelowMode resolvedPauseBelow = pauseBelow ?? attr.PauseBelow;
            UIOpenMode resolvedOpenMode = openMode ?? attr.OpenMode;
            instance._core.OpenPanel(typeof(T), typeof(T).Name,
                ResolveAddress(typeof(T), attr, instance._loader, address),
                attr.Layer, resolvedInputMode, resolvedPauseBelow, resolvedOpenMode,
                attr.AllowMulti, attr.Poolable, data,
                (view, error) =>
                {
                    if (error != null)
                    {
                        onFailed?.Invoke(error);
                    }
                    else
                    {
                        onOpened?.Invoke(view as UIPanel);
                    }
                },
                stackable: attr.Stackable);
        }

        /// <summary>保留旧四参数签名，兼容反射调用与既有编译代码。</summary>
        public static void OpenPanel<T>(string address, object data,
            Action<UIPanel> onOpened, Action<string> onFailed) where T : UIPanel
        {
            OpenPanel<T>(address, data, onOpened, onFailed, null, null, null);
        }

        /// <summary>地址解析优先级：显式地址 > 特性 Address > 加载器默认约定。</summary>
        private static string ResolveAddress(Type panelType, UIPanelAttribute attr,
            IAssetLoader loader, string explicitAddress)
        {
            if (!string.IsNullOrEmpty(explicitAddress))
            {
                return explicitAddress;
            }

            return attr.Address ?? loader.DefaultAddress(panelType);
        }

        /// <summary>打开面板的 Task 包装（回调版之上的语法糖；回调都发生在 Unity 主线程）。</summary>
        public static Task<UIPanel> OpenPanelAsync<T>(object data = null,
            UIInputMode? inputMode = null,
            UIPauseBelowMode? pauseBelow = null,
            UIOpenMode? openMode = null) where T : UIPanel
        {
            var tcs = new TaskCompletionSource<UIPanel>();
            OpenPanel<T>(data,
                panel => tcs.TrySetResult(panel),
                error => tcs.TrySetException(new InvalidOperationException("打开面板失败: " + error)),
                inputMode, pauseBelow, openMode);
            return tcs.Task;
        }

        /// <summary>按实例关闭面板（走标准关闭流程，含延迟销毁与池化）。</summary>
        public static void ClosePanel(UIPanel panel, bool immediate = false)
        {
            if (panel == null)
            {
                return;
            }

            if (panel.Manager != null)
            {
                panel.Manager.ClosePanel(panel.SerialId, immediate);
            }
            else
            {
                // 未接入管理器（异常路径）：直接销毁兜底
                Destroy(panel.gameObject);
            }
        }

        /// <summary>按面板名关闭（多实例时关闭最新打开的一个）。</summary>
        public static void ClosePanel(string panelName, bool immediate = false)
        {
            UIManager instance = RequireInstance();
            instance._core.ClosePanelByName(panelName, immediate);
        }

        /// <summary>按类型关闭面板（单实例语义；多实例时关闭最新打开的一个）。</summary>
        public static void ClosePanel<T>(bool immediate = false) where T : UIPanel
        {
            ClosePanel(typeof(T).Name, immediate);
        }

        /// <summary>关闭全部面板（场景切换时调用；immediate=true 跳过关闭动画延迟）。</summary>
        public static void CloseAll(bool immediate = false)
        {
            if (_instance == null)
            {
                return;
            }

            _instance._core.CloseAll(immediate);
        }

        /// <summary>取已打开面板（单实例语义；未打开返回 null）。</summary>
        public static UIPanel GetPanel<T>() where T : UIPanel
        {
            if (_instance == null)
            {
                return null;
            }

            return _instance._core.GetRecord(typeof(T).Name)?.View as UIPanel;
        }

        /// <summary>某类型面板是否已打开。</summary>
        public static bool IsOpen<T>() where T : UIPanel
        {
            if (_instance == null)
            {
                return false;
            }

            return _instance._core.IsOpen(typeof(T).Name);
        }

        /// <summary>把当前顶层面板压入导航栈（返回式导航用）。</summary>
        public static void Push()
        {
            UIManager instance = RequireInstance();
            int top = instance._core.GetTopOpenSerialId();
            if (top >= 0)
            {
                instance._core.Navigation.Push(top);
            }
        }

        /// <summary>
        /// 返回（Back）：消费一条返回历史并关闭当前顶层面板（Android 返回键语义）。
        /// 打开面板时框架已自动记录返回路径；没有任何返回历史时（如在第一页）无操作，
        /// 不会误关根页面。
        /// </summary>
        public static void Back()
        {
            _instance?._core?.Back();
        }

        /// <summary>由工厂注入（UIPanel.Close 转发到这里），内部用。</summary>
        internal void ClosePanel(int serialId, bool immediate)
        {
            if (_core != null)
            {
                _core.ClosePanel(serialId, immediate);
            }
        }

        // ================= 内部 =================

        private static UIManager RequireInstance()
        {
            if (_instance == null)
            {
                throw new InvalidOperationException(
                    "MyUI 未启动：请先调用 UIManager.Init()（入口处一行即可，加载模式按窗口①配置/AssetMode）。");
            }

            return _instance;
        }

        /// <summary>解析 [UIPanel] 特性；未标注返回默认值。</summary>
        private static UIPanelAttribute ResolveAttribute(Type panelType)
        {
            if (!AttrCache.TryGetValue(panelType, out UIPanelAttribute attr))
            {
                attr = (UIPanelAttribute)Attribute.GetCustomAttribute(panelType, typeof(UIPanelAttribute));
                if (attr == null)
                {
                    attr = new UIPanelAttribute();
                }

                AttrCache[panelType] = attr;
            }

            return attr;
        }
    }
}
