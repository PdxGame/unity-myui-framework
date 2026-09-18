using System;
using System.Collections.Generic;

namespace MyUI.Core
{
    /// <summary>
    /// UI 核心状态机（纯 C#，零 Unity 依赖；GameFramework UIManager 思路的轻量实现）。
    ///
    /// 职责：面板打开 / 关闭流程编排、重复打开（单实例）合并、加载中取消、遮挡 / 暂停状态计算、
    /// 导航栈、实例池记账与淘汰决策。不直接操作任何视图对象，
    /// 全部通过 IAssetLoader / IUIPanelFactory / IUIPanelView 接口驱动，
    /// 因此可在 EditMode 中用假实现做单元测试（无需 Play 模式与 Unity 资源）。
    ///
    /// 生命周期调用顺序（文档化契约，测试保证）：
    ///   打开：OnInit（仅首次）→ OnOpen(data) → OnShow
    ///   遮挡：OnCover →（若遮挡源声明暂停下方）OnPause
    ///   露出：OnReveal → OnResume
    ///   关闭：OnHide →（延迟销毁可选）→ OnClose(pooled) → 入池或 OnDestroyed + 销毁
    ///
    /// 遮挡 / 暂停判定规则：
    ///   遮挡（Covered）：同层更晚打开的覆盖型面板，或更高层的覆盖型面板；
    ///   暂停（Paused）：被遮挡 且 遮挡源声明 PauseBelow。
    ///   （即：InputMode/PauseBelow 决定行为；Toast 不参与遮挡。面板尺寸由 Prefab 决定。）
    ///
    /// 单实例语义：未标注 AllowMulti 的面板重复打开 = 聚焦（重新 OnOpen/OnShow + 置顶）；
    /// 加载中的重复打开会合并，只触发一次加载。
    /// </summary>
    public sealed class UIManagerCore
    {
        private readonly IAssetLoader _loader;
        private readonly IUIPanelFactory _factory;

        /// <summary>全部激活记录（含等待加载完成的已取消记录，用于资源释放）。</summary>
        private readonly List<PanelRecord> _all = new List<PanelRecord>();

        /// <summary>单实例面板名索引（AllowMulti=false 时记录也在此）。</summary>
        private readonly Dictionary<string, PanelRecord> _singles = new Dictionary<string, PanelRecord>();

        /// <summary>serialId 索引（定向关闭 / 导航栈用）。</summary>
        private readonly Dictionary<int, PanelRecord> _bySerial = new Dictionary<int, PanelRecord>();

        /// <summary>实例池：address -> 空闲视图（FIFO，优先复用最早入池的）。</summary>
        private readonly Dictionary<string, Queue<object>> _pool = new Dictionary<string, Queue<object>>();

        /// <summary>视图入池时刻（淘汰判定用）。</summary>
        private readonly Dictionary<object, float> _pooledAt = new Dictionary<object, float>();

        /// <summary>延迟关闭队列（Closing 状态、等待销毁）。</summary>
        private readonly List<PanelRecord> _closing = new List<PanelRecord>();

        /// <summary>Shutdown 时仍在加载的 serialId；迟到实例到达后只释放，不再挂接。</summary>
        private readonly HashSet<int> _discardedLoads = new HashSet<int>();

        private int _nextSerialId = 1;
        private int _openOrder;
        private float _time;
        private bool _disposed;

        /// <summary>关闭动画延迟销毁时长（秒）；0 = 立即销毁。</summary>
        public float CloseDelaySeconds { get; set; } = 0f;

        /// <summary>每个资源地址的池容量上限（超出即销毁，不淘汰旧实例）。</summary>
        public int PoolCapacityPerAddress { get; set; } = 3;

        /// <summary>池中实例闲置超过该秒数即淘汰销毁；0 表示不按时间淘汰。</summary>
        public float PoolExpireSeconds { get; set; } = 60f;

        /// <summary>导航栈（调用方显式 Push / Back，见 GetTopOpenSerialId）。</summary>
        public NavigationStack Navigation { get; } = new NavigationStack();

        /// <summary>虚拟时钟（Tick 累加；测试可控）。</summary>
        public float Time => _time;

        // ---- 事件（Runtime 门面订阅后转发给业务层） ----
        public event Action<PanelOpenedEventArgs> PanelOpened;
        public event Action<PanelClosedEventArgs> PanelClosed;
        public event Action<PanelLoadFailedEventArgs> PanelLoadFailed;

        public UIManagerCore(IAssetLoader loader, IUIPanelFactory factory)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        // ================= 打开 =================

        /// <summary>
        /// 请求打开面板。
        /// onDone(view, error)：打开完成回调（error 为 null 表示成功），只回调一次；
        /// 单实例已打开 → 立即以现有视图回调（聚焦语义）；加载中 → 合并等待一次加载。
        /// 注：被取消的打开请求会收到 (null, "cancelled")。
        /// </summary>
        public void OpenPanel(Type panelType, string panelName, string address, UILayer layer,
            UIInputMode inputMode, UIPauseBelowMode pauseBelow, UIOpenMode openMode,
            bool allowMulti, bool poolable, object userData,
            Action<object, string> onDone, bool stackable = true)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UIManagerCore));
            }

            if (panelType == null)
            {
                throw new ArgumentNullException(nameof(panelType));
            }

            if (string.IsNullOrEmpty(panelName))
            {
                panelName = panelType.Name;
            }

            if (string.IsNullOrEmpty(address))
            {
                address = _loader.DefaultAddress(panelType);
            }

            if (!allowMulti)
            {
                PanelRecord existing = GetRecord(panelName);
                if (existing != null && TryMergeOrRefocus(existing, userData, onDone))
                {
                    return;
                }
            }

            var record = new PanelRecord(_nextSerialId++, panelType, panelName, address, layer,
                inputMode, pauseBelow, openMode, allowMulti, poolable, userData, stackable);
            if (!allowMulti)
            {
                _singles[panelName] = record;
            }

            _all.Add(record);
            _bySerial[record.SerialId] = record;
            record.OpenCallbacks.Add(onDone);
            BeginLoad(record);
        }

        /// <summary>单实例已存在时的合并 / 聚焦语义。返回 true 表示已处理（调用方不再新建）。</summary>
        private bool TryMergeOrRefocus(PanelRecord existing, object userData, Action<object, string> onDone)
        {
            if (existing.State == PanelState.Open)
            {
                // 聚焦：更新数据、置顶、重新走打开数据注入与显示
                existing.UserData = userData;
                existing.OpenOrder = ++_openOrder;
                _factory.MoveToTop(existing.View);
                existing.View.OnOpen(userData);
                existing.View.OnShow();
                RefreshCoverage();
                onDone?.Invoke(existing.View, null);
                return true;
            }

            if (existing.State == PanelState.Requested || existing.State == PanelState.Loading)
            {
                // 加载中：合并等待（以最后一次 userData 为准，一次性回调所有人）
                existing.UserData = userData;
                existing.OpenCallbacks.Add(onDone);
                return true;
            }

            if (existing.State == PanelState.Closing)
            {
                // 取消关闭，重新打开
                _closing.Remove(existing);
                existing.UserData = userData;
                existing.State = PanelState.Open;
                existing.OpenOrder = ++_openOrder;
                _factory.SetViewActive(existing.View, true);
                _factory.MoveToTop(existing.View);
                existing.View.OnOpen(userData);
                existing.View.OnShow();
                RefreshCoverage();
                onDone?.Invoke(existing.View, null);
                return true;
            }

            return false; // Closed（已取消等待释放的记录）：允许新建
        }

        private void BeginLoad(PanelRecord record)
        {
            record.State = PanelState.Loading;

            // 池复用优先：命中则跳过加载，直接进入打开序列
            if (record.Poolable
                && _pool.TryGetValue(record.Address, out Queue<object> queue)
                && queue.Count > 0
                && record.View == null)
            {
                object pooled = queue.Dequeue();
                _pooledAt.Remove(pooled);
                var view = pooled as IUIPanelView;
                if (view == null)
                {
                    // 池中对象异常（理论上不会发生）：销毁重建
                    _factory.DestroyView(pooled as IUIPanelView);
                }
                else
                {
                    record.View = view;
                    record.State = PanelState.Open;
                    record.OpenOrder = ++_openOrder;
                    _factory.RefreshViewContext(view, record); // 池复用：刷新 SerialId 等运行时字段
                    _factory.SetViewActive(view, true);
                    _factory.MoveToTop(view);
                    _factory.OnViewReady(view, record); // 视图就绪（池复用路径统一钩子）
                    RegisterNavigation(record);
                    view.OnOpen(record.UserData);
                    view.OnShow();
                    ApplyReplace(record);
                    CompleteOpen(record);
                    return;
                }
            }

            try
            {
                _loader.LoadViewAsync(record.Address, (viewInstance, error) =>
                    OnViewLoaded(record.SerialId, viewInstance, error));
            }
            catch (Exception e)
            {
                FinishWithError(record, "发起面板加载异常：" + e.Message);
            }
        }

        /// <summary>加载完成（同步 / 异步加载器都会走到这里）。</summary>
        private void OnViewLoaded(int serialId, object viewInstance, string error)
        {
            if (!_bySerial.TryGetValue(serialId, out PanelRecord record))
            {
                // Dispose 期间尚未完成的加载：迟到实例到这里统一释放，避免资源泄漏。
                if (_discardedLoads.Remove(serialId) && viewInstance != null)
                {
                    try
                    {
                        _factory.ReleaseViewInstance(viewInstance);
                    }
                    catch (Exception releaseError)
                    {
                        System.Diagnostics.Debug.WriteLine(releaseError);
                    }
                }

                return;
            }

            if (!string.IsNullOrEmpty(error))
            {
                FinishWithError(record, error);
                return;
            }

            if (viewInstance == null)
            {
                FinishWithError(record, "加载器未返回视图实例");
                return;
            }

            IUIPanelView view;
            try
            {
                view = _factory.AttachView(record, viewInstance);
            }
            catch (Exception e)
            {
                try
                {
                    _factory.ReleaseViewInstance(viewInstance);
                }
                catch (Exception releaseError)
                {
                    // Keep the original initialization failure as the user-facing error.
                    System.Diagnostics.Debug.WriteLine(releaseError);
                }
                FinishWithError(record, "面板视图初始化失败：" + e.Message);
                return;
            }

            record.View = view;

            if (record.State == PanelState.Closing || record.State == PanelState.Closed)
            {
                // 加载期间被取消：挂接后直接释放
                _factory.DestroyView(view);
                RemoveRecord(record, notify: false);
                return;
            }

            record.State = PanelState.Open;
            record.OpenOrder = ++_openOrder;
            _factory.MoveToTop(view);
            _factory.OnViewReady(view, record); // 视图就绪（新建路径统一钩子）
            RegisterNavigation(record);
            view.OnInit();
            view.OnOpen(record.UserData);
            view.OnShow();
            ApplyReplace(record);
            CompleteOpen(record);
        }

        /// <summary>打开完成收尾：遮挡刷新 + 事件 + 一次性回调所有请求者。</summary>
        private void CompleteOpen(PanelRecord record)
        {
            RefreshCoverage();
            PanelOpened?.Invoke(new PanelOpenedEventArgs(record));
            List<Action<object, string>> callbacks = record.OpenCallbacks;
            record.OpenCallbacks = null;
            if (callbacks != null)
            {
                foreach (Action<object, string> callback in callbacks)
                {
                    callback?.Invoke(record.View, null);
                }
            }
        }

        // ================= 关闭 =================

        /// <summary>按 serialId 关闭（UI 面板实例定向关闭）。</summary>
        public void ClosePanel(int serialId, bool immediate)
        {
            if (_bySerial.TryGetValue(serialId, out PanelRecord record))
            {
                BeginClose(record, immediate);
            }
        }

        /// <summary>按面板名关闭（多实例时关闭最新打开的一个）。</summary>
        public void ClosePanelByName(string panelName, bool immediate)
        {
            PanelRecord record = GetRecord(panelName);
            if (record != null)
            {
                BeginClose(record, immediate);
            }
        }

        /// <summary>关闭全部面板（场景切换时调用）。</summary>
        public void CloseAll(bool immediate)
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                BeginClose(_all[i], immediate);
            }
        }

        private void BeginClose(PanelRecord record, bool immediate)
        {
            switch (record.State)
            {
                case PanelState.Closed:
                case PanelState.Closing:
                    return;

                case PanelState.Requested:
                case PanelState.Loading:
                    // 打开尚未完成：取消（等加载回调到达后释放实例）
                    CancelOpen(record);
                    return;

                case PanelState.Open:
                    record.State = PanelState.Closing;
                    record.CloseRequestedAt = _time;
                    record.View.OnHide();
                    if (CloseDelaySeconds > 0 && !immediate)
                    {
                        _closing.Add(record);
                        return;
                    }

                    DoFinishClose(record);
                    return;
            }
        }

        private void CancelOpen(PanelRecord record)
        {
            // 记录保留（State=Closed）直到加载回调到达，以便释放实例；期间不可再合并
            record.State = PanelState.Closed;
            var callbacks = record.OpenCallbacks;
            record.OpenCallbacks = null;
            if (callbacks != null)
            {
                foreach (Action<object, string> callback in callbacks)
                {
                    callback?.Invoke(null, "cancelled");
                }
            }
        }

        private void DoFinishClose(PanelRecord record)
        {
            bool pooled = record.Poolable && PushToPool(record);
            record.View.OnClose(pooled);
            if (!pooled)
            {
                record.View.OnDestroyed();
                _factory.DestroyView(record.View);
            }
            else
            {
                _factory.SetViewActive(record.View, false);
            }

            if (record.NavigationEntrySerialId >= 0)
            {
                Navigation.Remove(record.NavigationEntrySerialId);
                record.NavigationEntrySerialId = -1;
            }

            RemoveRecord(record, notify: true, pooled: pooled);
        }

        /// <summary>入池；容量已满返回 false（调用方负责销毁）。</summary>
        private bool PushToPool(PanelRecord record)
        {
            if (!record.Poolable || record.View == null)
            {
                return false;
            }

            if (!_pool.TryGetValue(record.Address, out Queue<object> queue))
            {
                queue = new Queue<object>();
                _pool[record.Address] = queue;
            }

            if (queue.Count >= PoolCapacityPerAddress)
            {
                return false;
            }

            queue.Enqueue(record.View);
            _pooledAt[record.View] = _time;
            return true;
        }

        private void RemoveRecord(PanelRecord record, bool notify, bool pooled = false)
        {
            record.State = PanelState.Closed;
            record.NavigationEntrySerialId = -1;
            _all.Remove(record);
            _bySerial.Remove(record.SerialId);
            if (!record.AllowMulti
                && _singles.TryGetValue(record.PanelName, out PanelRecord current)
                && current == record)
            {
                _singles.Remove(record.PanelName);
            }

            if (notify)
            {
                RefreshCoverage();
                PanelClosed?.Invoke(new PanelClosedEventArgs(record, pooled));
            }
        }

        private void FinishWithError(PanelRecord record, string error)
        {
            RemoveRecord(record, notify: false);
            PanelLoadFailed?.Invoke(new PanelLoadFailedEventArgs(record.PanelName, error));
            var callbacks = record.OpenCallbacks;
            record.OpenCallbacks = null;
            if (callbacks != null)
            {
                foreach (Action<object, string> callback in callbacks)
                {
                    callback?.Invoke(null, error);
                }
            }
        }

        // ================= 每帧驱动 =================

        /// <summary>每帧驱动：推进延迟关闭、池淘汰、遮挡 / 暂停刷新、活跃面板 OnTick。</summary>
        public void Tick(float deltaTime)
        {
            if (_disposed)
            {
                return;
            }

            _time += deltaTime;

            // 延迟关闭到点
            for (int i = _closing.Count - 1; i >= 0; i--)
            {
                PanelRecord record = _closing[i];
                if (_time - record.CloseRequestedAt >= CloseDelaySeconds)
                {
                    _closing.RemoveAt(i);
                    DoFinishClose(record);
                }
            }

            // 池过期淘汰（FIFO 队列时间有序，队头未过期则整队未过期）
            if (PoolExpireSeconds > 0)
            {
                foreach (KeyValuePair<string, Queue<object>> pair in _pool)
                {
                    Queue<object> queue = pair.Value;
                    while (queue.Count > 0)
                    {
                        object view = queue.Peek();
                        if (_time - _pooledAt[view] < PoolExpireSeconds)
                        {
                            break;
                        }

                        queue.Dequeue();
                        _pooledAt.Remove(view);
                        if (view is IUIPanelView panelView)
                        {
                            panelView.OnDestroyed();
                            _factory.DestroyView(panelView);
                        }
                    }
                }
            }

            RefreshCoverage();

            // OnTick 只驱动 Open 且未暂停的面板（快照遍历，允许回调内变更集合）
            PanelRecord[] snapshot = _all.ToArray();
            foreach (PanelRecord record in snapshot)
            {
                if (record.State == PanelState.Open && !record.Paused && record.View != null)
                {
                    record.View.OnTick(deltaTime);
                }
            }
        }

        // ================= 遮挡 / 暂停计算 =================

        /// <summary>
        /// 重新计算所有 Open 面板的遮挡 / 暂停状态，并在翻转时回调视图。
        /// 判定规则见类注释。快照遍历，允许回调内再次打开 / 关闭面板。
        /// </summary>
        private void RefreshCoverage()
        {
            PanelRecord[] snapshot = _all.ToArray();
            foreach (PanelRecord record in snapshot)
            {
                if (record.State != PanelState.Open || record.View == null)
                {
                    continue;
                }

                bool covered = false;
                bool hasPauseBelowAbove = false;
                foreach (PanelRecord other in snapshot)
                {
                    if (other == record || other.State != PanelState.Open)
                    {
                        continue;
                    }

                    if (!IsCovering(other, record))
                    {
                        continue;
                    }

                    covered = true;
                    if (other.EffectivePauseBelow)
                    {
                        hasPauseBelowAbove = true;
                    }
                }

                bool paused = covered && hasPauseBelowAbove;

                if (record.Covered != covered)
                {
                    record.Covered = covered;
                    if (covered)
                    {
                        record.View.OnCover();
                    }
                    else
                    {
                        record.View.OnReveal();
                    }
                }

                if (record.Paused != paused)
                {
                    record.Paused = paused;
                    if (paused)
                    {
                        record.View.OnPause();
                    }
                    else
                    {
                        record.View.OnResume();
                    }
                }
            }
        }

        /// <summary>
        /// other 是否遮挡 target。只有声明为覆盖型（Modal / PauseBelow）的面板
        /// 才会形成遮挡，避免 HUD 或并排窗口仅因层级更高就误暂停下层面板。
        /// </summary>
        private static bool IsCovering(PanelRecord other, PanelRecord target)
        {
            if (other.Layer == UILayer.Toast || target.Layer == UILayer.Toast)
            {
                return false;
            }

            if (!other.CoversBelow)
            {
                return false;
            }

            if (other.Layer == target.Layer)
            {
                return other.OpenOrder > target.OpenOrder;
            }

            return UILayerOrder.IsAbove(other.Layer, target.Layer);
        }

        // ================= 查询 =================

        /// <summary>取面板记录（多实例时返回最新打开的一个；无返回 null）。</summary>
        public PanelRecord GetRecord(string panelName)
        {
            if (_singles.TryGetValue(panelName, out PanelRecord single))
            {
                return single;
            }

            for (int i = _all.Count - 1; i >= 0; i--)
            {
                if (_all[i].PanelName == panelName)
                {
                    return _all[i];
                }
            }

            return null;
        }

        public PanelRecord GetRecord(int serialId)
        {
            return _bySerial.TryGetValue(serialId, out PanelRecord record) ? record : null;
        }

        public bool IsOpen(string panelName)
        {
            PanelRecord record = GetRecord(panelName);
            return record != null && record.IsOpen;
        }

        /// <summary>
        /// 返回（Back）：有历史时关闭当前顶层面板；没有历史时保持根页面不动。
        /// 关闭流程会按面板成功打开时登记的入口清理历史，避免手工关闭与 Back 重复消费记录。
        /// </summary>
        public void Back()
        {
            // 关闭动画期间忽略重复返回，避免在下层面板尚未露出时连续消费历史。
            if (_disposed || _closing.Count > 0)
            {
                return;
            }

            PanelRecord top = GetTopOpenRecord(null, stackableOnly: false);
            if (top == null)
            {
                return;
            }

            if (top.View is IUINavigationHandler navigationHandler
                && navigationHandler.HandleBack())
            {
                return;
            }

            if (top.NavigationEntrySerialId < 0 || Navigation.Count == 0)
            {
                return;
            }

            // 返回动作立即消费本面板的导航入口；关闭流程不再重复清理。
            if (top.NavigationEntrySerialId >= 0)
            {
                Navigation.Remove(top.NavigationEntrySerialId);
                top.NavigationEntrySerialId = -1;
            }

            ClosePanel(top.SerialId, immediate: false);
        }

        /// <summary>当前顶层面板的 serialId（最高层 + 层内最新）；无任何面板返回 -1（导航栈 Push 用）。</summary>
        public int GetTopOpenSerialId()
        {
            PanelRecord best = GetTopOpenRecord(null, stackableOnly: false);
            return best != null ? best.SerialId : -1;
        }

        /// <summary>
        /// 返回当前最高层的打开记录。stackableOnly=true 时只考虑参与返回导航的面板，
        /// 并排除正在完成打开的当前记录。
        /// </summary>
        private PanelRecord GetTopOpenRecord(PanelRecord excluded, bool stackableOnly)
        {
            PanelRecord best = null;
            foreach (PanelRecord record in _all)
            {
                if (record == excluded || record.State != PanelState.Open)
                {
                    continue;
                }

                if (stackableOnly && !record.Stackable)
                {
                    continue;
                }

                if (best == null
                    || UILayerOrder.IsAbove(record.Layer, best.Layer)
                    || (record.Layer == best.Layer && record.OpenOrder > best.OpenOrder))
                {
                    best = record;
                }
            }

            return best;
        }

        /// <summary>
        /// 面板成功打开后登记返回入口。登记发生在成功路径上，因此加载失败 / 取消不会污染导航栈。
        /// 每个新面板登记一个当前最高可返回父面板；并发打开同一父面板时允许重复入口。
        /// </summary>
        private void RegisterNavigation(PanelRecord record)
        {
            record.NavigationEntrySerialId = -1;
            if (!record.Stackable || record.EffectiveOpenMode != UIOpenMode.Push)
            {
                return;
            }

            PanelRecord parent = GetTopOpenRecord(record, stackableOnly: true);
            if (parent == null)
            {
                return;
            }

            Navigation.Push(parent.SerialId, allowDuplicate: true);
            record.NavigationEntrySerialId = parent.SerialId;
        }

        /// <summary>
        /// Replace：新页面成功打开后，关闭被替换的可返回页面，并继承它的返回入口。
        /// 不增加导航层级，因此 A -> Replace(B) 后 Back 仍然返回 A。
        /// </summary>
        private void ApplyReplace(PanelRecord record)
        {
            if (record.EffectiveOpenMode != UIOpenMode.Replace)
            {
                return;
            }

            PanelRecord replaced = GetTopOpenRecord(record, stackableOnly: true);
            if (replaced == null)
            {
                return;
            }

            record.NavigationEntrySerialId = replaced.NavigationEntrySerialId;
            replaced.NavigationEntrySerialId = -1;
            BeginClose(replaced, immediate: false);
        }

        /// <summary>Release all active and pooled views and clear runtime state.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            var released = new HashSet<object>();
            PanelRecord[] records = _all.ToArray();
            foreach (PanelRecord record in records)
            {
                if (record.View == null
                    && (record.State == PanelState.Requested || record.State == PanelState.Loading))
                {
                    _discardedLoads.Add(record.SerialId);
                }

                IUIPanelView view = record.View;
                if (view == null || !released.Add(view))
                {
                    continue;
                }

                try
                {
                    view.OnClose(false);
                }
                catch
                {
                    // Shutdown must finish even if user lifecycle code throws.
                }

                try
                {
                    view.OnDestroyed();
                    _factory.DestroyView(view);
                }
                catch
                {
                    // Continue releasing remaining views.
                }
            }

            foreach (Queue<object> queue in _pool.Values)
            {
                while (queue.Count > 0)
                {
                    object pooled = queue.Dequeue();
                    if (!released.Add(pooled) || !(pooled is IUIPanelView view))
                    {
                        continue;
                    }

                    try
                    {
                        view.OnDestroyed();
                        _factory.DestroyView(view);
                    }
                    catch
                    {
                        // Continue releasing remaining pooled views.
                    }
                }
            }

            _pool.Clear();
            _pooledAt.Clear();
            _closing.Clear();
            _singles.Clear();
            _bySerial.Clear();
            _all.Clear();
            Navigation.Clear();
            _disposed = true;
        }
    }
}
