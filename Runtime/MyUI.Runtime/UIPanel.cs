using MyUI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI.Runtime
{
    /// <summary>
    /// 用户面板基类：继承后覆写生命周期虚方法即可（GameFramework UIFormLogic 思路）。
    /// 生命周期由框架（UIManagerCore）按固定次序驱动，顺序见各方法注释；
    /// 本类实现 Core 的 IUIPanelView 并把调用转发到 protected virtual 方法。
    /// 面板应通过 [UIPanel] 特性声明层级与行为，未标注时按默认值。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIPanel : MonoBehaviour, IUIPanelView
    {
        /// <summary>框架分配的实例唯一标识（仅打开期间有效；GameFramework serialId 思路）。</summary>
        public int SerialId { get; internal set; }

        /// <summary>面板名（默认 = 类型名，也是资源地址的一部分）。</summary>
        public string PanelName { get; internal set; }

        /// <summary>所属层级（由框架按特性 / 参数注入）。</summary>
        public UILayer Layer { get; internal set; }

        /// <summary>所属运行时管理器（由工厂注入；Close 用它发关闭请求）。</summary>
        public UIManager Manager { get; internal set; }

        // ---- Inspector 可视化配置（优先于代码 [UIPanel] 特性）----
        // 预制体上选中面板根节点即可在 Inspector 中直接勾选，无需为每个面板写特性。

        /// <summary>所属层级（Inspector 选择；不勾选时按 [UIPanel] 特性或默认值 Normal）。</summary>
        [Tooltip("面板所属层级（示例：主菜单 Normal 全屏、窗口 Popup、飘字 Toast）")]
        [SerializeField] internal UILayer inspectorLayer = UILayer.Normal;

        /// <summary>是否全屏：跨层遮挡源，全屏页面打开时其下层面板会暂停逻辑（Inspector 勾选）。</summary>
        [Tooltip("全屏：作为跨层遮挡源，打开时其下层面板收到 OnCover/OnPause")]
        [SerializeField] internal bool inspectorFullScreen = false;

        /// <summary>关闭时是否入池复用（Inspector 勾选；默认勾选）。</summary>
        [Tooltip("关闭时入池复用，下次打开不重建实例")]
        [SerializeField] internal bool inspectorPoolable = true;

        /// <summary>
        /// 参与返回导航（Inspector 勾选；默认勾选）。飘字/过场加载条等临时 UI 取消勾选，
        /// 打开时不会记录返回路径；也可用特性 [UIPanel(Stackable = false)] 声明，任一为否即不入栈。
        /// </summary>
        [Tooltip("参与返回导航：取消勾选则打开时不影响返回历史（飘字/过场提示用）")]
        [SerializeField] internal bool inspectorStackable = true;

        private RectTransform _rect;

        /// <summary>本面板的 RectTransform（缓存，避免每帧 GetComponent）。
        /// 面板根节点必须是 RectTransform；若预制体根误用了普通 Transform 会在此自动替换。</summary>
        public RectTransform RectTransform
        {
            get
            {
                if (_rect == null)
                {
                    _rect = transform as RectTransform;
                    if (_rect == null)
                    {
                        // UI 布局前提：把普通 Transform 替换成 RectTransform（保留位置/旋转/缩放）
                        _rect = gameObject.AddComponent<RectTransform>();
                    }
                }

                return _rect;
            }
        }

        /// <summary>请求关闭本面板（走标准关闭流程）。</summary>
        public void Close(bool immediate = false)
        {
            if (Manager != null)
            {
                Manager.ClosePanel(SerialId, immediate);
            }
            else
            {
                // 兜底：未接入管理器时直接销毁
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 便捷查找：按节点名取子物体组件（BindButton 的底层）。
        /// 找不到返回 null（不抛异常），适合在 OnInit 里一次绑完所有控件。
        /// </summary>
        protected T Find<T>(string nodeName) where T : Component
        {
            Transform t = RectTransform.Find(nodeName);
            return t != null ? t.GetComponent<T>() : null;
        }

        /// <summary>
        /// 绑定按钮点击（自动 Find + 获取 Button + AddListener）。
        /// 找不到节点/组件时打警告，不影响其它绑定。在 OnInit 中调用即可。
        /// </summary>
        protected void BindButton(string nodeName, UnityEngine.Events.UnityAction onClick)
        {
            var btn = Find<Button>(nodeName);
            if (btn == null)
            {
                Debug.LogWarning($"[UIPanel] 未找到按钮节点或缺失 Button 组件: {nodeName}", this);
                return;
            }

            btn.onClick.AddListener(onClick);
        }

        // ---------- IUIPanelView 显式实现：转发给可覆写虚方法 ----------
        void IUIPanelView.OnInit() => OnInit();
        void IUIPanelView.OnOpen(object userData) => OnOpen(userData);
        void IUIPanelView.OnShow() => OnShow();
        void IUIPanelView.OnCover() => OnCover();
        void IUIPanelView.OnReveal() => OnReveal();
        void IUIPanelView.OnPause() => OnPause();
        void IUIPanelView.OnResume() => OnResume();
        void IUIPanelView.OnHide() => OnHide();
        void IUIPanelView.OnClose(bool pooled) => OnClose(pooled);
        void IUIPanelView.OnDestroyed() => OnDestroyed();
        void IUIPanelView.OnTick(float deltaTime) => OnTick(deltaTime);

        /// <summary>视图创建完成立即调用，整个生命周期仅一次（池复用不再调用）。</summary>
        protected virtual void OnInit() { }

        /// <summary>每次打开都会调用（含池复用），用于按 userData 重置面板状态。</summary>
        protected virtual void OnOpen(object userData) { }

        /// <summary>完全可见、可交互。</summary>
        protected virtual void OnShow() { }

        /// <summary>被上层面板遮挡（但未进入暂停态）。</summary>
        protected virtual void OnCover() { }

        /// <summary>重新露出。</summary>
        protected virtual void OnReveal() { }

        /// <summary>被全屏面板遮挡链覆盖，进入暂停态（应停止计时器 / 动画 / 输入响应）。</summary>
        protected virtual void OnPause() { }

        /// <summary>暂停解除。</summary>
        protected virtual void OnResume() { }

        /// <summary>关闭流程开始（隐藏 / 播放关闭动画之前）。</summary>
        protected virtual void OnHide() { }

        /// <summary>即将销毁或入池（pooled=true 表示入池复用，应释放临时资源但保留可重置状态）。</summary>
        protected virtual void OnClose(bool pooled) { }

        /// <summary>实例真正销毁（立即关闭 / 池淘汰）。</summary>
        protected virtual void OnDestroyed() { }

        /// <summary>每帧驱动（仅 Open 状态且未暂停的面板收到）。</summary>
        protected virtual void OnTick(float deltaTime) { }
    }
}