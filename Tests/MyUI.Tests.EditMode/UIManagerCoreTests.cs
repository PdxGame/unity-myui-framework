using System;
using System.Collections.Generic;
using MyUI.Core;
using NUnit.Framework;

namespace MyUI.Tests
{
    /// <summary>
    /// UIManagerCore 状态机单元测试（EditMode）。
    /// 全部通过假加载器 / 假工厂 / 假视图驱动，不依赖 Play 模式与 Unity 资源；
    /// 覆盖：打开 / 关闭顺序、单实例聚焦、双开合并、加载中取消、遮挡 / 暂停规则、
    /// 导航栈、池复用 / 容量 / 过期淘汰、延迟销毁、加载失败、多实例。
    /// </summary>
    public class UIManagerCoreTests
    {
        /// <summary>占位面板类型（仅作为 Type 引用，加载走假加载器）。</summary>
        private sealed class TestPanel { }

        private sealed class FakeView : IUIPanelView
        {
            public readonly string Name;
            public readonly List<string> Log = new List<string>();
            public int SerialId;

            public FakeView(string name, int serialId)
            {
                Name = name;
                SerialId = serialId;
            }

            public void OnInit() => Log.Add("init");
            public void OnOpen(object userData) => Log.Add("open:" + (userData ?? "null"));
            public void OnShow() => Log.Add("show");
            public void OnCover() => Log.Add("cover");
            public void OnReveal() => Log.Add("reveal");
            public void OnPause() => Log.Add("pause");
            public void OnResume() => Log.Add("resume");
            public void OnHide() => Log.Add("hide");
            public void OnClose(bool pooled) => Log.Add("close:" + pooled);
            public void OnDestroyed() => Log.Add("destroyed");
            public void OnTick(float deltaTime) => Log.Add("tick");
        }

        private sealed class FakeFactory : IUIPanelFactory
        {
            public readonly List<string> Log = new List<string>();

            public IUIPanelView AttachView(PanelRecord record, object viewInstance)
            {
                Log.Add("attach:" + record.PanelName + ":" + record.SerialId);
                var view = new FakeView(record.PanelName, record.SerialId);
                RefreshViewContext(view, record);
                return view;
            }

            public void DestroyView(IUIPanelView view) => Log.Add("destroy:" + ((FakeView)view).Name);
            public void SetViewActive(IUIPanelView view, bool active) => Log.Add("active:" + ((FakeView)view).Name + ":" + active);
            public void MoveToTop(IUIPanelView view) => Log.Add("top:" + ((FakeView)view).Name);

            public void RefreshViewContext(IUIPanelView view, PanelRecord record)
            {
                ((FakeView)view).SerialId = record.SerialId;
            }

            public void OnViewReady(IUIPanelView view, PanelRecord record)
            {
                Log.Add("ready:" + ((FakeView)view).Name);
            }
        }

        private sealed class FakeLoader : IAssetLoader
        {
            public int LoadCount;
            public bool HoldResults; // true → 结果挂起，Flush() 统一交付（模拟异步）
            public string FailWith;  // 设置后下一次加载失败，只生效一次
            public readonly List<object> Released = new List<object>();
            private readonly List<Action> _pending = new List<Action>();

            public void LoadViewAsync(string address, Action<object, string> onDone)
            {
                LoadCount++;
                if (FailWith != null)
                {
                    string error = FailWith;
                    FailWith = null;
                    onDone(null, error);
                    return;
                }

                object view = new object();
                if (HoldResults)
                {
                    _pending.Add(() => onDone(view, null));
                }
                else
                {
                    onDone(view, null);
                }
            }

            /// <summary>交付所有挂起的加载结果。</summary>
            public void Flush()
            {
                Action[] batch = _pending.ToArray();
                _pending.Clear();
                foreach (Action callback in batch)
                {
                    callback();
                }
            }

            public void ReleaseView(object view) => Released.Add(view);

            public string DefaultAddress(Type panelType) => "UIPanel/" + panelType.Name;
        }

        // ---------- 工具 ----------

        private static UIManagerCore NewCore(out FakeLoader loader, out FakeFactory factory)
        {
            loader = new FakeLoader();
            factory = new FakeFactory();
            return new UIManagerCore(loader, factory);
        }

        private static void Open(UIManagerCore core, string name, UILayer layer,
            bool fullScreen = false, bool allowMulti = false, bool poolable = true,
            object userData = null, Action<object, string> onDone = null)
        {
            core.OpenPanel(typeof(TestPanel), name, null, layer, fullScreen, allowMulti, poolable, userData, onDone);
        }

        // ---------- 打开 / 关闭流程 ----------

        [Test]
        public void UILayer_Order_IsStable()
        {
            Assert.Less((int)UILayer.Background, (int)UILayer.Normal);
            Assert.Less((int)UILayer.Normal, (int)UILayer.Popup);
            Assert.Less((int)UILayer.Popup, (int)UILayer.Guide);
            Assert.Less((int)UILayer.Guide, (int)UILayer.System);
            Assert.Less((int)UILayer.System, (int)UILayer.Toast);
        }

        [Test]
        public void Open_CallsLifecycleInOrder_AndFiresEvent()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            bool opened = false;
            core.PanelOpened += _ => opened = true;
            object cbView = null;
            string cbError = "unset";

            Open(core, "Main", UILayer.Normal, onDone: (v, e) => { cbView = v; cbError = e; });

            FakeView view = (FakeView)(core.GetRecord("Main").View);
            Assert.AreEqual(new[] { "init", "open:null", "show" }, view.Log.ToArray());
            Assert.AreEqual(1, loader.LoadCount);
            Assert.IsTrue(opened);
            Assert.IsNotNull(cbView);
            Assert.IsNull(cbError);
            Assert.IsTrue(core.IsOpen("Main"));
        }

        [Test]
        public void Open_SingleInstance_Refocuses_WithoutReload()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "Main", UILayer.Normal);
            FakeView view = (FakeView)(core.GetRecord("Main").View);
            view.Log.Clear();
            factory.Log.Clear();
            int openedEvents = 0;
            core.PanelOpened += _ => openedEvents++;

            object doneView = null;
            Open(core, "Main", UILayer.Normal, userData: "x", onDone: (v, e) => doneView = v);

            Assert.AreEqual(1, loader.LoadCount, "聚焦不应重新加载");
            Assert.AreEqual(new[] { "open:x", "show" }, view.Log.ToArray(), "聚焦 = 重新 OnOpen/OnShow，不重复 OnInit");
            Assert.AreEqual(0, openedEvents, "聚焦不触发 PanelOpened");
            Assert.AreSame(view, doneView);
            Assert.IsTrue(factory.Log.Contains("top:Main"), "聚焦应置顶");
        }

        [Test]
        public void Open_WhileLoading_MergesIntoSingleLoad()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            loader.HoldResults = true;
            object v1 = null, v2 = null;
            Open(core, "Main", UILayer.Normal, onDone: (v, e) => v1 = v);
            Open(core, "Main", UILayer.Normal, userData: "y", onDone: (v, e) => v2 = v);

            Assert.AreEqual(1, loader.LoadCount, "加载中合并，只加载一次");
            loader.Flush();

            FakeView view = (FakeView)(core.GetRecord("Main").View);
            Assert.AreEqual(new[] { "init", "open:y", "show" }, view.Log.ToArray(), "OnInit 只一次，userData 以最后一次为准");
            Assert.IsNotNull(v1);
            Assert.AreSame(view, v1);
            Assert.AreSame(view, v2);
        }

        [Test]
        public void Close_WhileLoading_CancelsWithCallback_AndReleasesInstance()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            loader.HoldResults = true;
            string cbError = "unset";
            Open(core, "Main", UILayer.Normal, onDone: (v, e) => cbError = e);

            core.ClosePanel(core.GetRecord("Main").SerialId, immediate: true);

            Assert.AreEqual("cancelled", cbError, "被取消的打开请求应收到 (null, cancelled)");
            Assert.IsFalse(core.IsOpen("Main"));
            // 取消后记录保留（等加载回调到达以释放实例），状态为 Closed
            Assert.AreEqual(PanelState.Closed, core.GetRecord("Main").State);

            loader.Flush(); // 加载结果到达：实例被挂接后立即释放，记录移除
            Assert.IsTrue(factory.Log.Contains("attach:Main:1"));
            Assert.IsTrue(factory.Log.Contains("destroy:Main"), "取消的加载结果应释放");
            Assert.IsNull(core.GetRecord("Main"), "释放完成后记录应移出索引");
        }

        [Test]
        public void Close_Immediate_CallsHideCloseDestroyed_InOrder()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "Main", UILayer.Normal, poolable: false);
            FakeView view = (FakeView)(core.GetRecord("Main").View);
            view.Log.Clear();

            bool closed = false;
            bool pooledFlag = true;
            core.PanelClosed += args => { closed = true; pooledFlag = args.Pooled; };
            core.ClosePanel(core.GetRecord("Main").SerialId, immediate: true);

            Assert.AreEqual(new[] { "hide", "close:False", "destroyed" }, view.Log.ToArray());
            Assert.IsTrue(closed);
            Assert.IsFalse(pooledFlag);
            Assert.IsFalse(core.IsOpen("Main"));
            Assert.IsNull(core.GetRecord("Main"));
            Assert.IsTrue(factory.Log.Contains("destroy:Main"));
        }

        [Test]
        public void Close_Delayed_FinishesAfterCloseDelay()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            core.CloseDelaySeconds = 0.5f;
            Open(core, "Main", UILayer.Normal, poolable: false);
            PanelRecord record = core.GetRecord("Main");
            FakeView view = (FakeView)record.View;
            view.Log.Clear();

            core.ClosePanel(record.SerialId, immediate: false);

            Assert.AreEqual(new[] { "hide" }, view.Log.ToArray(), "延迟关闭只先 OnHide");
            Assert.AreEqual(PanelState.Closing, record.State);

            core.Tick(0.3f);
            Assert.AreEqual(PanelState.Closing, record.State, "未到点不应销毁");

            core.Tick(0.3f);
            Assert.AreEqual(PanelState.Closed, record.State);
            CollectionAssert.AreEqual(new[] { "hide", "close:False", "destroyed" }, view.Log);
        }

        // ---------- 池 ----------

        [Test]
        public void Pool_ReusesInstance_WithoutReload()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "Main", UILayer.Normal, poolable: true);
            FakeView view = (FakeView)(core.GetRecord("Main").View);
            core.ClosePanel(core.GetRecord("Main").SerialId, immediate: true);
            view.Log.Clear();

            Assert.IsTrue(factory.Log.Contains("active:Main:False"), "入池应 SetActive(false)");

            object doneView = null;
            Open(core, "Main", UILayer.Normal, userData: "reused", onDone: (v, e) => doneView = v);

            Assert.AreEqual(1, loader.LoadCount, "池复用不应再加载");
            Assert.AreSame(view, doneView);
            Assert.AreEqual(new[] { "open:reused", "show" }, view.Log.ToArray(), "复用不重复 OnInit");
            Assert.IsTrue(factory.Log.Contains("active:Main:True"));
            Assert.IsTrue(factory.Log.Contains("top:Main"));
            Assert.IsTrue(core.IsOpen("Main"));
            Assert.AreEqual(core.GetRecord("Main").SerialId, view.SerialId,
                "池复用必须刷新实例的 SerialId（否则实例自带 Close() 定向关闭会失效）");
        }

        [Test]
        public void Pool_CapacityOverflow_DestroysExcessInstance()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            core.PoolCapacityPerAddress = 1;
            Open(core, "T", UILayer.Toast, allowMulti: true, poolable: true);
            int serialA = core.GetRecord("T").SerialId;
            Open(core, "T", UILayer.Toast, allowMulti: true, poolable: true);
            int serialB = core.GetRecord("T").SerialId;

            var pooledFlags = new List<bool>();
            core.PanelClosed += args => pooledFlags.Add(args.Pooled);

            core.ClosePanel(serialA, immediate: true);
            core.ClosePanel(serialB, immediate: true);

            Assert.AreEqual(new[] { true, false }, pooledFlags.ToArray(), "第一个入池，第二个超容量销毁");
            Assert.IsTrue(factory.Log.Contains("active:T:False"), "入池实例应被禁用");
        }

        [Test]
        public void Pool_Expiry_EvictsOnTick()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            core.PoolExpireSeconds = 5f;
            Open(core, "Main", UILayer.Normal, poolable: true);
            FakeView view = (FakeView)(core.GetRecord("Main").View);
            core.ClosePanel(core.GetRecord("Main").SerialId, immediate: true);
            view.Log.Clear();
            factory.Log.Clear();

            core.Tick(5.1f);

            Assert.IsTrue(view.Log.Contains("destroyed"), "池过期应回调 OnDestroyed");
            Assert.IsTrue(factory.Log.Contains("destroy:Main"), "池过期应销毁实例");
        }

        // ---------- 遮挡 / 暂停 ----------

        [Test]
        public void Cover_SameLayer_ByLaterOpen_ThenReveal()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "A", UILayer.Normal);
            FakeView viewA = (FakeView)(core.GetRecord("A").View);
            viewA.Log.Clear();

            Open(core, "B", UILayer.Normal);

            Assert.IsTrue(core.GetRecord("A").Covered);
            CollectionAssert.AreEqual(new[] { "cover" }, viewA.Log);

            core.ClosePanel(core.GetRecord("B").SerialId, immediate: true);
            Assert.IsFalse(core.GetRecord("A").Covered);
            CollectionAssert.AreEqual(new[] { "cover", "reveal" }, viewA.Log);
        }

        [Test]
        public void Cover_CrossLayer_RequiresFullScreen()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "A", UILayer.Normal, fullScreen: true);
            FakeView viewA = (FakeView)(core.GetRecord("A").View);
            viewA.Log.Clear();

            // 非全屏弹窗：只在高一层但不构成遮挡源
            Open(core, "B", UILayer.Popup, fullScreen: false);
            Assert.IsFalse(core.GetRecord("A").Covered, "非全屏高层面板不应遮挡下层");
            Assert.IsTrue(viewA.Log.Count == 0);

            // 全屏系统层：遮挡 + 暂停
            Open(core, "C", UILayer.System, fullScreen: true);
            Assert.IsTrue(core.GetRecord("A").Covered);
            Assert.IsTrue(core.GetRecord("A").Paused);
            CollectionAssert.AreEqual(new[] { "cover", "pause" }, viewA.Log);

            // 关闭全屏：先恢复再露出
            core.ClosePanel(core.GetRecord("C").SerialId, immediate: true);
            Assert.IsFalse(core.GetRecord("A").Paused);
            Assert.IsFalse(core.GetRecord("A").Covered);
            CollectionAssert.AreEqual(new[] { "cover", "pause", "reveal", "resume" }, viewA.Log);
        }

        [Test]
        public void ToastLayer_NonFullScreen_DoesNotCoverAnything()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "A", UILayer.Normal, fullScreen: true);
            FakeView viewA = (FakeView)(core.GetRecord("A").View);
            viewA.Log.Clear();

            Open(core, "Toast", UILayer.Toast, allowMulti: true, fullScreen: false);

            Assert.IsFalse(core.GetRecord("A").Covered, "飘字 Toast 不应遮挡主界面逻辑");
            Assert.IsTrue(viewA.Log.Count == 0);
        }

        // ---------- 导航栈 ----------

        [Test]
        public void Navigation_Top_And_Back_ClosesTopPanel()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "A", UILayer.Normal);
            Open(core, "B", UILayer.Popup);

            Assert.AreEqual(core.GetRecord("B").SerialId, core.GetTopOpenSerialId(), "顶层应为最后打开的 B");

            core.Navigation.Push(core.GetTopOpenSerialId());
            int top = core.Navigation.Pop();
            core.ClosePanel(top, immediate: true);

            Assert.IsFalse(core.IsOpen("B"));
            Assert.IsTrue(core.IsOpen("A"));
        }

        [Test]
        public void Navigation_Remove_OnClose_ClearsStack()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "A", UILayer.Normal);
            Open(core, "B", UILayer.Popup);
            int serialA = core.GetRecord("A").SerialId;
            int serialB = core.GetRecord("B").SerialId;
            core.Navigation.Push(core.GetTopOpenSerialId());

            core.ClosePanel(serialB, immediate: true);

            Assert.AreEqual(0, core.Navigation.Count, "面板关闭应清理导航栈");
            Assert.AreEqual(serialA, core.GetTopOpenSerialId(), "B 关闭后顶层应为 A");
        }

        // ---------- 批量与竞态 ----------

        [Test]
        public void CloseAll_ClosesAllInReverseOrder()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            var closedSerials = new List<int>();
            core.PanelClosed += args => closedSerials.Add(args.Record.SerialId);
            Open(core, "A", UILayer.Normal);
            int serialA = core.GetRecord("A").SerialId;
            Open(core, "B", UILayer.Normal);
            int serialB = core.GetRecord("B").SerialId;
            Open(core, "C", UILayer.Normal);
            int serialC = core.GetRecord("C").SerialId;

            core.CloseAll(immediate: true);

            Assert.AreEqual(new[] { serialC, serialB, serialA }, closedSerials.ToArray(), "逆打开序关闭");
            Assert.IsFalse(core.IsOpen("A") || core.IsOpen("B") || core.IsOpen("C"));
        }

        [Test]
        public void Reopen_WhileClosing_CancelsDeferredClose()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            core.CloseDelaySeconds = 0.5f;
            Open(core, "Main", UILayer.Normal, poolable: false);
            FakeView view = (FakeView)(core.GetRecord("Main").View);
            view.Log.Clear();

            core.ClosePanel(core.GetRecord("Main").SerialId, immediate: false);
            Open(core, "Main", UILayer.Normal, userData: "again");

            Assert.AreEqual(PanelState.Open, core.GetRecord("Main").State, "关闭中重开应取消关闭");
            CollectionAssert.AreEqual(new[] { "hide", "open:again", "show" }, view.Log);
            Assert.IsTrue(factory.Log.Contains("active:Main:True"), "重开应恢复激活");

            core.Tick(1f);
            Assert.IsFalse(view.Log.Contains("destroyed"), "重开后不应再销毁");
        }

        [Test]
        public void Tick_Only_Drives_ActiveNonPausedPanels()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "A", UILayer.Normal);
            FakeView viewA = (FakeView)(core.GetRecord("A").View);
            Open(core, "B", UILayer.Normal, fullScreen: true);
            FakeView viewB = (FakeView)(core.GetRecord("B").View);
            viewA.Log.Clear();
            viewB.Log.Clear();

            core.Tick(0.1f);
            Assert.AreEqual(0, viewA.Log.Count, "被全屏遮挡暂停的面板不应收到 OnTick");
            Assert.AreEqual(new[] { "tick" }, viewB.Log.ToArray());

            core.ClosePanel(core.GetRecord("B").SerialId, immediate: true);
            viewA.Log.Clear();
            core.Tick(0.1f);
            Assert.AreEqual(new[] { "tick" }, viewA.Log.ToArray(), "恢复后应重新收到 OnTick");
        }

        [Test]
        public void LoadFailure_NotifiesCallbackAndEvent()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            loader.FailWith = "boom";
            bool failed = false;
            core.PanelLoadFailed += _ => failed = true;
            string cbError = "unset";
            Open(core, "Main", UILayer.Normal, onDone: (v, e) => cbError = e);

            Assert.AreEqual("boom", cbError);
            Assert.IsTrue(failed);
            Assert.IsFalse(core.IsOpen("Main"));
            Assert.IsNull(core.GetRecord("Main"));
            Assert.AreEqual(0, factory.Log.Count, "失败路径不应挂接视图");
        }

        [Test]
        public void AllowMulti_OpensSecondInstance()
        {
            UIManagerCore core = NewCore(out FakeLoader loader, out FakeFactory factory);
            Open(core, "Toast", UILayer.Toast, allowMulti: true);
            int serialA = core.GetRecord("Toast").SerialId;
            Open(core, "Toast", UILayer.Toast, allowMulti: true);
            int serialB = core.GetRecord("Toast").SerialId;

            Assert.AreNotEqual(serialA, serialB);
            Assert.AreEqual(2, loader.LoadCount);
            Assert.IsTrue(core.IsOpen("Toast"));
            Assert.AreNotSame(core.GetRecord(serialA).View, core.GetRecord(serialB).View);
        }
    }
}