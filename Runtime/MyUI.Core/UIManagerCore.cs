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
    ///   遮挡：OnCover →（若遮挡链含全屏面板）OnPause
    ///   露出：OnReveal → OnResume
    ///   关闭：OnHide →（延迟销毁可选）→ OnClose(pooled) → 入池或 OnDestroyed + 销毁
    ///
    /// 遮挡 / 暂停判定规则：
    ///   遮挡（Covered）：同层内存在更晚打开的面板，或更高层存在 FullScreen 面板；
    ///   暂停（Paused）：被遮挡 且 遮挡源中存在 FullScreen 面板。
    ///   （即：非全屏弹窗只遮挡不暂停；飘字类 Toast 面板不参与遮挡；跨层遮挡必须全屏。）
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

        private int _nextSerialId = 1;
        private int _openOrder;
        private float _time;

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
            bool fullScreen, bool allowMulti, bool poolable, object userData,
            Action<object, string> onDone)
        {
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
                fullScreen, allowMulti, poolable, userData);
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
                    _factory.OnViewReady(view, record); // 视图就绪（池复用路径：含入栈撤销检查）
                    view.OnOpen(record.UserData);
                    view.OnShow();
                    CompleteOpen(record);
                    return;
                }
            }

            _loader.LoadViewAsync(record.Address, (viewInstance, error) =>
                OnViewLoaded(record.SerialId, viewInstance, error));
        }

        /// <summary>加载完成（同步 / 异步加载器都会走到这里）。</summary>
        private void OnViewLoaded(int serialId, object viewInstance, string error)
        {
            if (!_bySerial.TryGetValue(serialId, out PanelRecord record))
            {
                return; // 理论上不可达（仅已取消且载荷已释放时）
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

            IUIPanelView view = _factory.AttachView(record, viewInstance);
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
            _factory.OnViewReady(view, record); // 视图就绪（新建路径：含入栈撤销检查）
            view.OnInit();
            view.OnOpen(record.UserData);
            view.OnShow();
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

            Navigation.Remove(record.SerialId);
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
                bool hasFullScreenAbove = false;
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
                    if (other.FullScreen)
                    {
                        hasFullScreenAbove = true;
                    }
                }

                bool paused = covered && hasFullScreenAbove;

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
        /// other 是否遮挡 target：
        ///   同层：opened 更晚者遮挡更早者（无条件）；
        ///   跨层：仅当 other 在更高层且 FullScreen 时才构成遮挡源（层间渲染顺序靠 Canvas，
        ///   逻辑遮挡由全屏标记驱动 —— 飘字 Toast 不会误遮挡下层）。
        /// </summary>
        private static bool IsCovering(PanelRecord other, PanelRecord target)
        {
            if (other.Layer == target.Layer)
            {
                return other.OpenOrder > target.OpenOrder;
            }

            return other.Layer > target.Layer && other.FullScreen;
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

        /// <summary>当前顶层面板的 serialId（最高层 + 层内最新）；无任何面板返回 -1（导航栈 Push 用）。</summary>
        public int GetTopOpenSerialId()
        {
            PanelRecord best = null;
            foreach (PanelRecord record in _all)
            {
                if (record.State != PanelState.Open)
                {
                    continue;
                }

                if (best == null
                    || record.Layer > best.Layer
                    || (record.Layer == best.Layer && record.OpenOrder > best.OpenOrder))
                {
                    best = record;
                }
            }

            return best != null ? best.SerialId : -1;
        }
    }
}