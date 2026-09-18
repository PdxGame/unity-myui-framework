using System;
using System.Collections.Generic;

namespace MyUI.Core
{
    /// <summary>
    /// 面板实例的运行时记录（纯数据，不持有任何 Unity 类型；GameFramework UIFormInfo 思路）。
    /// 由 UIManagerCore 分配并维护，其状态字段仅 Core 内部可写。
    /// </summary>
    public sealed class PanelRecord
    {
        public PanelRecord(int serialId, Type panelType, string panelName, string address,
            UILayer layer, bool blockInput, UIPauseBelowMode pauseBelow,
            UIOpenMode openMode, bool allowMulti, bool poolable, object userData)
        {
            SerialId = serialId;
            PanelType = panelType;
            PanelName = panelName;
            Address = address;
            Layer = layer;
            BlockInput = blockInput;
            PauseBelow = pauseBelow;
            OpenMode = openMode;
            AllowMulti = allowMulti;
            Poolable = poolable;
            UserData = userData;
        }

        /// <summary>实例唯一标识（全局自增；GameFramework serialId 思路，可定向关闭某个实例）。</summary>
        public int SerialId { get; }

        /// <summary>面板视图类型（Runtime 侧为 UIPanel 子类）。</summary>
        public Type PanelType { get; }

        /// <summary>面板名（默认 = 类型名，也是资源默认地址的一部分）。</summary>
        public string PanelName { get; }

        /// <summary>资源地址（默认约定 "UIPanel/{PanelName}"，可被 UIPanelAttribute.Address 覆盖）。</summary>
        public string Address { get; }

        /// <summary>所属层级（构造时按特性/参数注入；AttachView 后可按 Inspector 配置覆盖）。</summary>
        public UILayer Layer { get; internal set; }

        /// <summary>是否创建框架全屏输入阻断器。</summary>
        public bool BlockInput { get; internal set; }

        /// <summary>对下方面板的暂停策略；Inherit 等价于 Never。</summary>
        public UIPauseBelowMode PauseBelow { get; internal set; }

        /// <summary>打开策略。</summary>
        public UIOpenMode OpenMode { get; internal set; }

        /// <summary>是否允许多实例（Toast 类专用；默认 false 意味着重复打开只聚焦已有实例）。</summary>
        public bool AllowMulti { get; }

        /// <summary>关闭时是否入池复用（默认 true；可被 Inspector 配置覆盖）。</summary>
        public bool Poolable { get; internal set; }

        /// <summary>
        /// 本面板成功打开时登记的导航入口（其下方面板的 serialId）。
        /// -1 表示没有导航入口；关闭本面板时只清理这一条入口及其上方记录。
        /// </summary>
        public int NavigationEntrySerialId { get; internal set; } = -1;

        /// <summary>打开时传入的用户数据（OnOpen 的入参；聚焦 / 取消关闭重开时可更新）。</summary>
        public object UserData { get; internal set; }

        public PanelState State { get; internal set; } = PanelState.Requested;

        /// <summary>打开顺序号（全局自增，同层内排序键；保证同帧多次打开的次序稳定）。</summary>
        public int OpenOrder { get; internal set; }

        /// <summary>当前是否被上层面板遮挡（遮挡是几何事实，与暂停无关）。</summary>
        public bool Covered { get; internal set; }

        /// <summary>当前是否暂停（被声明了 PauseBelow 的遮挡源覆盖）。</summary>
        public bool Paused { get; internal set; }

        public bool EffectivePauseBelow => PauseBelow == UIPauseBelowMode.Always;

        /// <summary>该面板是否应对其下方形成逻辑遮挡。</summary>
        public bool CoversBelow =>
            BlockInput
            || EffectivePauseBelow;

        /// <summary>生命周期视图（Runtime 侧即 UIPanel 组件；Core 只通过 IUIPanelView 驱动它）。</summary>
        public IUIPanelView View { get; internal set; }

        /// <summary>打开请求的完成回调列表（加载中重复打开会合并进此列表，全部一次性回调）。</summary>
        internal List<Action<object, string>> OpenCallbacks { get; set; } = new List<Action<object, string>>();

        /// <summary>关闭请求时刻（Time.time 语义，Runtime 注入；用于延迟销毁与池淘汰）。</summary>
        public float CloseRequestedAt { get; internal set; }

        public bool IsOpen => State == PanelState.Open;

        /// <summary>是否处于未定流程中（加载中 / 关闭中，此时不应再接受打开请求）。</summary>
        public bool IsBusy => State == PanelState.Requested
            || State == PanelState.Loading
            || State == PanelState.Closing;
    }
}
