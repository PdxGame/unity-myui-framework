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
    /// 使用方式：入口处调用一次 UiManager.Bootstrap(loader)，之后用静态 API：
    ///   UiManager.OpenPanel&lt;SettingsPanel&gt;(data, onOpened, onFailed);
    /// 本类同时实现 IUiPanelFactory：把加载完成的视图挂到层 Canvas、注册运行时字段并执行激活/销毁。
    /// 框架不强制任何加载器 —— 默认 ResourcesPanelLoader（框架自带），
    /// Addressables 版（MyUI.Loaders.Addressables 程序集）在 Bootstrap 时选择即可。
    /// 注意：所有静态 API 需在 Bootstrap 之后调用，否则抛出明确异常。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiManager : MonoBehaviour, IUiPanelFactory
    {
        private static UiManager _instance;

        /// <summary>面板类型 → 解析后的声明（静态缓存，避免重复反射）。</summary>
        private static readonly Dictionary<Type, UiPanelAttribute> AttrCache = new Dictionary<Type, UiPanelAttribute>();

        public static UiManager Instance => _instance;

        private UiManagerCore _core;
        private IAssetLoader _loader;

        // ---- 面板状态事件（供游戏逻辑订阅；由 Core 事件转发） ----
        public event Action<PanelRecord> PanelOpened;
        public event Action<PanelRecord, bool> PanelClosed; // (record, pooled)
        public event Action<string, string> PanelLoadFailed; // (panelName, error)

        /// <summary>
        /// 框架启动完成事件（Bootstrap 首次完成后触发一次）。
        /// 用于"在框架就绪后再执行打开等操作"，避免与 AutoBoot 的先后顺序产生竞态。
        /// </summary>
        public static event Action Started;

        /// <summary>资源加载方式开关。默认值来自 MyUI 窗口配置（MyUI → Settings & Registration），
        /// 缺配置时按 Addressables；代码仍可随时覆盖本属性。</summary>
        public static UiAssetMode AssetMode { get; set; } = ResolveDefaultMode();

        /// <summary>
        /// 默认加载模式来源于工具生成的 Assets/MyUI/MyUiLoaderConfig.cs
        /// （窗口勾选保存；缺文件时按 Addressables）。反射读取，避免 Runtime 程序集
        /// 依赖用户的 Assembly-CSharp。
        /// </summary>
        private static UiAssetMode ResolveDefaultMode()
        {
            try
            {
                Type t = Type.GetType("MyUI.MyUiLoaderConfig, Assembly-CSharp");
                string mode = t?.GetField("Mode", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                if (mode == "Resources")
                {
                    return UiAssetMode.Resources;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MyUI] 读取加载模式配置失败（" + e.Message + "），使用 Addressables");
            }

            return UiAssetMode.Addressables;
        }

        /// <summary>
        /// 是否自动启动框架（默认 true：游戏运行时自动 Bootstrap，无需写任何启动代码）。
        /// 想完全手动控制启动时机时设为 false，再自己调用 Bootstrap()。
        /// </summary>
        public static bool AutoBoot { get; set; } = true;

        /// <summary>游戏运行时自动启动框架（手动 Bootstrap 会被幂等跳过）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (AutoBoot && _instance == null)
            {
                Bootstrap();
            }
        }

        /// <summary>
        /// 启动框架：确保 UiRoot 存在并绑定加载器。
        /// loader 为 null 时按 UiManager.AssetMode 选择：默认 Addressables（程序集缺失自动回退
        /// Resources 并警告）；也可显式 Bootstrap(loader) 指定任意实现。
        /// </summary>
        public static UiManager Bootstrap(IAssetLoader loader = null)
        {
            if (_instance != null)
            {
                return _instance;
            }

            UiRoot.EnsureCreated();
            EnsureEventSystem(); // UI 点击交互的刚需：场景已有 EventSystem 则不重复创建
            var go = new GameObject("MyUI Manager");
            var manager = go.AddComponent<UiManager>();
            _instance = manager; // 注意：AddComponent 后立刻登记单例（UiManager 无 Awake，必须在此赋值）
            DontDestroyOnLoad(go);
            manager.Init(loader ?? CreateDefaultLoader());
            Started?.Invoke(); // 就绪通知（订阅方可在此时安全调用 OpenPanel）
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
            if (AssetMode == UiAssetMode.Addressables)
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

        private void Init(IAssetLoader loader)
        {
            _loader = loader;
            _core = new UiManagerCore(loader, this);
            _core.PanelOpened += args => PanelOpened?.Invoke(args.Record);
            _core.PanelClosed += args => PanelClosed?.Invoke(args.Record, args.Pooled);
            _core.PanelLoadFailed += args =>
            {
                PanelLoadFailed?.Invoke(args.PanelName, args.Error);
                // 任何加载失败都必须控制台可见（无订阅者也看得到），避免"没 UI 也没报错"的静默
                Debug.LogError("[MyUI] 面板加载失败：" + args.PanelName + " → " + args.Error);
            };
        }

        private void Update()
        {
            if (_core != null)
            {
                _core.Tick(Time.deltaTime);
            }
        }

        // ================= IUiPanelFactory =================

        IUiPanelView IUiPanelFactory.AttachView(PanelRecord record, object viewInstance)
        {
            var go = viewInstance as GameObject;
            if (go == null)
            {
                throw new ArgumentException("加载器返回的视图实例必须是 GameObject", nameof(viewInstance));
            }

            var panel = go.GetComponent(record.PanelType) as UiPanel;
            if (panel == null)
            {
                panel = go.AddComponent(record.PanelType) as UiPanel;
            }

            if (panel == null)
            {
                throw new InvalidOperationException("面板视图缺少 UiPanel 组件: " + record.PanelName);
            }

            // Inspector 可视化配置优先于代码特性/默认值（加载后立即生效）
            record.Layer = panel.inspectorLayer;
            record.FullScreen = panel.inspectorFullScreen;
            record.Poolable = panel.inspectorPoolable;

            // 按（可能被覆盖的）层级挂载到对应层 Canvas
            RectTransform layerRoot = UiRoot.Instance != null ? UiRoot.Instance.GetLayerRoot(record.Layer) : null;
            if (layerRoot != null)
            {
                go.transform.SetParent(layerRoot, false);
            }

            panel.SerialId = record.SerialId;
            panel.PanelName = record.PanelName;
            panel.Layer = record.Layer;
            panel.Manager = this;
            return panel;
        }

        void IUiPanelFactory.OnViewReady(IUiPanelView view, PanelRecord record)
        {
            // 视图就绪（新加载 / 池复用都经过这里）：Inspector 取消勾选「参与返回导航」→ 撤销本次入栈
            if (view is UiPanel panel)
            {
                ConfirmOrUndoPendingStack(_core, panel.inspectorStackable);
            }
        }

        void IUiPanelFactory.DestroyView(IUiPanelView view)
        {
            if (view is UiPanel panel && panel.gameObject != null && _loader != null)
            {
                _loader.ReleaseView(panel.gameObject);
            }
        }

        void IUiPanelFactory.SetViewActive(IUiPanelView view, bool active)
        {
            if (view is UiPanel panel && panel.gameObject != null)
            {
                panel.gameObject.SetActive(active);
            }
        }

        void IUiPanelFactory.MoveToTop(IUiPanelView view)
        {
            if (view is UiPanel panel && panel.transform != null)
            {
                panel.transform.SetAsLastSibling();
            }
        }

        void IUiPanelFactory.RefreshViewContext(IUiPanelView view, PanelRecord record)
        {
            if (view is UiPanel panel)
            {
                panel.SerialId = record.SerialId;
                panel.PanelName = record.PanelName;
                panel.Layer = record.Layer;
                panel.Manager = this;
            }
        }

        // ================= 静态门面 API =================

        /// <summary>
        /// 打开面板（泛型版）。data 会原样传给 UiPanel.OnOpen。
        /// onOpened / onFailed 只回调一次；单实例重复打开以现有实例聚焦回调。
        /// 面板声明来自 [UiPanel] 特性（Layer / FullScreen / AllowMulti / Poolable / Address），
        /// 未标注时按默认值（Normal 层、单实例、可入池）。
        /// </summary>
        public static void OpenPanel<T>(object data = null,
            Action<UiPanel> onOpened = null, Action<string> onFailed = null) where T : UiPanel
        {
            UiManager instance = RequireInstance();
            UiPanelAttribute attr = ResolveAttribute(typeof(T));
            RememberCurrentTop(instance, attr.Stackable); // 自动记返回路径（飘字/过场 UI 标记 Stackable=false 不入栈）
            instance._core.OpenPanel(typeof(T), typeof(T).Name,
                ResolveAddress(typeof(T), attr, instance._loader, null),
                attr.Layer, attr.FullScreen, attr.AllowMulti, attr.Poolable, data,
                (view, error) =>
                {
                    if (error != null)
                    {
                        onFailed?.Invoke(error);
                    }
                    else
                    {
                        onOpened?.Invoke(view as UiPanel);
                    }
                });
        }

        /// <summary>打开前自动把当前顶层面板记入导航栈（stackable=false 的面板不参与返回导航）。</summary>
        private static int _pendingStackSerial = -1;

        private static void RememberCurrentTop(UiManager instance, bool stackable)
        {
            _pendingStackSerial = -1;
            if (!stackable)
            {
                return; // 飘字/加载条等临时 UI：不记录返回路径
            }

            int top = instance._core.GetTopOpenSerialId();
            if (top >= 0)
            {
                instance._core.Navigation.Push(top);
                _pendingStackSerial = top; // 记录待确认入栈的实例（供 Inspector 撤销）
            }
        }

        /// <summary>AttachView 后调用：若 Inspector 取消勾选"参与返回导航"，撤销本次入栈。</summary>
        private static void ConfirmOrUndoPendingStack(UiManagerCore core, bool inspectorStackable)
        {
            if (inspectorStackable)
            {
                _pendingStackSerial = -1;
                return;
            }

            if (_pendingStackSerial >= 0)
            {
                core.Navigation.Remove(_pendingStackSerial); // Inspector 关闭：撤销本次记录
                _pendingStackSerial = -1;
            }
        }

        /// <summary>
        /// 打开面板（显式地址版）：自定义资源地址（如 Addressables 地址或其它路径），
        /// 不被默认约定 "UIPanel/{TypeName}" 限制。其余语义同泛型版。
        /// </summary>
        public static void OpenPanel<T>(string address, object data = null,
            Action<UiPanel> onOpened = null, Action<string> onFailed = null) where T : UiPanel
        {
            UiManager instance = RequireInstance();
            UiPanelAttribute attr = ResolveAttribute(typeof(T));
            RememberCurrentTop(instance, attr.Stackable); // 自动记返回路径（Stackable=false 不入栈）
            instance._core.OpenPanel(typeof(T), typeof(T).Name,
                ResolveAddress(typeof(T), attr, instance._loader, address),
                attr.Layer, attr.FullScreen, attr.AllowMulti, attr.Poolable, data,
                (view, error) =>
                {
                    if (error != null)
                    {
                        onFailed?.Invoke(error);
                    }
                    else
                    {
                        onOpened?.Invoke(view as UiPanel);
                    }
                });
        }

        /// <summary>地址解析优先级：显式地址 > 特性 Address > 加载器默认约定。</summary>
        private static string ResolveAddress(Type panelType, UiPanelAttribute attr,
            IAssetLoader loader, string explicitAddress)
        {
            if (!string.IsNullOrEmpty(explicitAddress))
            {
                return explicitAddress;
            }

            return attr.Address ?? loader.DefaultAddress(panelType);
        }

        /// <summary>打开面板的 Task 包装（回调版之上的语法糖；回调都发生在 Unity 主线程）。</summary>
        public static Task<UiPanel> OpenPanelAsync<T>(object data = null) where T : UiPanel
        {
            var tcs = new TaskCompletionSource<UiPanel>();
            OpenPanel<T>(data,
                panel => tcs.TrySetResult(panel),
                error => tcs.TrySetException(new InvalidOperationException("打开面板失败: " + error)));
            return tcs.Task;
        }

        /// <summary>按实例关闭面板（走标准关闭流程，含延迟销毁与池化）。</summary>
        public static void ClosePanel(UiPanel panel, bool immediate = false)
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
            UiManager instance = RequireInstance();
            instance._core.ClosePanelByName(panelName, immediate);
        }

        /// <summary>按类型关闭面板（单实例语义；多实例时关闭最新打开的一个）。</summary>
        public static void ClosePanel<T>(bool immediate = false) where T : UiPanel
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
        public static UiPanel GetPanel<T>() where T : UiPanel
        {
            if (_instance == null)
            {
                return null;
            }

            return _instance._core.GetRecord(typeof(T).Name)?.View as UiPanel;
        }

        /// <summary>某类型面板是否已打开。</summary>
        public static bool IsOpen<T>() where T : UiPanel
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
            UiManager instance = RequireInstance();
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
            if (_instance == null || _instance._core == null)
            {
                return;
            }

            // 无返回历史：不动作（防止在根页面误关）
            if (_instance._core.Navigation.Count == 0)
            {
                return;
            }

            _instance._core.Navigation.Pop(); // 消费一条历史
            int top = _instance._core.GetTopOpenSerialId();
            if (top >= 0)
            {
                _instance._core.ClosePanel(top, immediate: false);
            }
        }

        /// <summary>由工厂注入（UiPanel.Close 转发到这里），内部用。</summary>
        internal void ClosePanel(int serialId, bool immediate)
        {
            if (_core != null)
            {
                _core.ClosePanel(serialId, immediate);
            }
        }

        // ================= 内部 =================

        private static UiManager RequireInstance()
        {
            if (_instance == null)
            {
                throw new InvalidOperationException(
                    "MyUI 未启动：请在入口处先调用 UiManager.Bootstrap(loader)（loader 可省略，默认按配置 Addressables/Resources；运行时通常由 AutoBoot 自动启动）。");
            }

            return _instance;
        }

        /// <summary>解析 [UiPanel] 特性；未标注返回默认值。</summary>
        private static UiPanelAttribute ResolveAttribute(Type panelType)
        {
            if (!AttrCache.TryGetValue(panelType, out UiPanelAttribute attr))
            {
                attr = (UiPanelAttribute)Attribute.GetCustomAttribute(panelType, typeof(UiPanelAttribute));
                if (attr == null)
                {
                    attr = new UiPanelAttribute();
                }

                AttrCache[panelType] = attr;
            }

            return attr;
        }
    }
}