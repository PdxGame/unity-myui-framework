using System;
using MyUI.Core;
using NUnit.Framework;

namespace MyUI.Tests
{
    public class UIManagerCoreTests
    {
        private sealed class FakeView : IUIPanelView, IUINavigationHandler
        {
            public int OpenCount;
            public int DestroyedCount;
            public int ClosedCount;
            public int CoverCount;
            public int PauseCount;
            public int BackCount;
            public bool HandleBackResult;

            public void OnInit() { }
            public void OnOpen(object userData) { OpenCount++; }
            public void OnShow() { }
            public void OnCover() { CoverCount++; }
            public void OnReveal() { }
            public void OnPause() { PauseCount++; }
            public void OnResume() { }
            public void OnHide() { }
            public void OnClose(bool pooled) { ClosedCount++; }
            public void OnDestroyed() { DestroyedCount++; }
            public void OnTick(float deltaTime) { }
            public bool HandleBack()
            {
                BackCount++;
                return HandleBackResult;
            }
        }

        private sealed class FakeLoader : IAssetLoader
        {
            public Action<object, string> Pending;
            public object NextView;
            public string NextError;
            public bool ThrowOnLoad;
            public int LoadCalls;

            public void LoadViewAsync(string address, Action<object, string> onDone)
            {
                LoadCalls++;
                if (ThrowOnLoad)
                {
                    throw new InvalidOperationException("load exploded");
                }

                Pending = onDone;
                if (NextView != null || NextError != null)
                {
                    onDone?.Invoke(NextView, NextError);
                }
            }

            public void ReleaseView(object view) { }
            public string DefaultAddress(Type panelType) => panelType.Name;
        }

        private sealed class FakeFactory : IUIPanelFactory
        {
            public IUIPanelView NextView = new FakeView();
            public bool ThrowOnAttach;
            public int AttachCalls;
            public int RefreshCalls;
            public int DestroyCalls;
            public int RawReleaseCalls;

            public IUIPanelView AttachView(PanelRecord record, object viewInstance)
            {
                AttachCalls++;
                if (ThrowOnAttach)
                {
                    throw new InvalidOperationException("attach exploded");
                }

                return NextView;
            }

            public void DestroyView(IUIPanelView view) { DestroyCalls++; }
            public void ReleaseViewInstance(object viewInstance) { RawReleaseCalls++; }
            public void SetViewActive(IUIPanelView view, bool active) { }
            public void MoveToTop(IUIPanelView view) { }
            public void RefreshViewContext(IUIPanelView view, PanelRecord record) { RefreshCalls++; }
            public void OnViewReady(IUIPanelView view, PanelRecord record) { }
        }

        [Test]
        public void LoaderException_IsReportedOnce()
        {
            var loader = new FakeLoader { ThrowOnLoad = true };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);
            string callbackError = null;
            int failedEvents = 0;
            core.PanelLoadFailed += _ => failedEvents++;

            core.OpenPanel(typeof(FakeView), "Fake", "Fake", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, (_, error) => callbackError = error);

            Assert.That(callbackError, Does.Contain("load exploded"));
            Assert.That(failedEvents, Is.EqualTo(1));
        }

        [Test]
        public void AttachException_ReleasesRawView()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory { ThrowOnAttach = true };
            var core = new UIManagerCore(loader, factory);
            string callbackError = null;

            core.OpenPanel(typeof(FakeView), "Fake", "Fake", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, (_, error) => callbackError = error);

            Assert.That(callbackError, Does.Contain("attach exploded"));
            Assert.That(factory.RawReleaseCalls, Is.EqualTo(1));
            Assert.That(factory.DestroyCalls, Is.Zero);
        }

        [Test]
        public void PooledReopen_RefreshesContext()
        {
            var loader = new FakeLoader { NextView = new object() };
            var view = new FakeView();
            var factory = new FakeFactory { NextView = view };
            var core = new UIManagerCore(loader, factory);

            core.OpenPanel(typeof(FakeView), "Fake", "Fake", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);
            core.ClosePanelByName("Fake", immediate: true);
            core.OpenPanel(typeof(FakeView), "Fake", "Fake", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            Assert.That(loader.LoadCalls, Is.EqualTo(1));
            Assert.That(factory.RefreshCalls, Is.EqualTo(1));
            Assert.That(view.OpenCount, Is.EqualTo(2));
        }

        [Test]
        public void Dispose_ReleasesActiveView()
        {
            var loader = new FakeLoader { NextView = new object() };
            var view = new FakeView();
            var factory = new FakeFactory { NextView = view };
            var core = new UIManagerCore(loader, factory);

            core.OpenPanel(typeof(FakeView), "Fake", "Fake", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);
            core.Dispose();

            Assert.That(view.ClosedCount, Is.EqualTo(1));
            Assert.That(view.DestroyedCount, Is.EqualTo(1));
            Assert.That(factory.DestroyCalls, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_ReleasesPooledViewWithoutReclosingIt()
        {
            var loader = new FakeLoader { NextView = new object() };
            var view = new FakeView();
            var factory = new FakeFactory { NextView = view };
            var core = new UIManagerCore(loader, factory);

            core.OpenPanel(typeof(FakeView), "Fake", "Fake", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);
            core.ClosePanelByName("Fake", immediate: true);
            core.Dispose();

            Assert.That(view.ClosedCount, Is.EqualTo(1));
            Assert.That(view.DestroyedCount, Is.EqualTo(1));
            Assert.That(factory.DestroyCalls, Is.EqualTo(1));
        }

        [Test]
        public void HigherPopup_CoversLowerPageWithoutPausingIt()
        {
            var loader = new FakeLoader { NextView = new object() };
            var lowerView = new FakeView();
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            loader.NextView = new object();
            factory.NextView = lowerView;
            core.OpenPanel(typeof(FakeView), "Page", "Page", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            loader.NextView = new object();
            factory.NextView = new FakeView();
            core.OpenPanel(typeof(FakeView), "Popup", "Popup", UILayer.Popup,
                UIInputMode.Modal, UIPauseBelowMode.Never, UIOpenMode.Overlay,
                false, true, null, null);

            PanelRecord page = core.GetRecord("Page");
            Assert.That(page.Covered, Is.True);
            Assert.That(page.Paused, Is.False);
            Assert.That(lowerView.CoverCount, Is.EqualTo(1));
            Assert.That(lowerView.PauseCount, Is.Zero);
        }

        [Test]
        public void NonCoveringPopup_DoesNotMarkLowerPageCovered()
        {
            var loader = new FakeLoader { NextView = new object() };
            var lowerView = new FakeView();
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            factory.NextView = lowerView;
            core.OpenPanel(typeof(FakeView), "Page", "Page", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            factory.NextView = new FakeView();
            core.OpenPanel(typeof(FakeView), "Popup", "Popup", UILayer.Popup,
                UIInputMode.Self, UIPauseBelowMode.Never, UIOpenMode.Overlay,
                false, true, null, null);

            PanelRecord page = core.GetRecord("Page");
            Assert.That(page.Covered, Is.False);
            Assert.That(lowerView.CoverCount, Is.Zero);
        }

        [Test]
        public void ExplicitModalPopup_CoversLowerPageWithoutPausingIt()
        {
            var loader = new FakeLoader { NextView = new object() };
            var lowerView = new FakeView();
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            factory.NextView = lowerView;
            core.OpenPanel(typeof(FakeView), "Page", "Page", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            factory.NextView = new FakeView();
            core.OpenPanel(typeof(FakeView), "Popup", "Popup", UILayer.Popup,
                UIInputMode.Modal, UIPauseBelowMode.Never, UIOpenMode.Overlay,
                false, true, null, null);

            PanelRecord page = core.GetRecord("Page");
            Assert.That(page.Covered, Is.True);
            Assert.That(page.Paused, Is.False);
        }

        [Test]
        public void ExplicitPauseBelow_PausesCoveredPage()
        {
            var loader = new FakeLoader { NextView = new object() };
            var lowerView = new FakeView();
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            factory.NextView = lowerView;
            core.OpenPanel(typeof(FakeView), "Page", "Page", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            factory.NextView = new FakeView();
            core.OpenPanel(typeof(FakeView), "PauseLayer", "PauseLayer", UILayer.Popup,
                UIInputMode.None, UIPauseBelowMode.Always, UIOpenMode.Overlay,
                false, true, null, null);

            PanelRecord page = core.GetRecord("Page");
            Assert.That(page.Covered, Is.True);
            Assert.That(page.Paused, Is.True);
            Assert.That(lowerView.PauseCount, Is.EqualTo(1));
        }

        [Test]
        public void HudLayer_IsBelowPopupAndDoesNotCoverPage()
        {
            Assert.That(UILayerOrder.GetOrder(UILayer.HUD),
                Is.LessThan(UILayerOrder.GetOrder(UILayer.Popup)));

            var loader = new FakeLoader { NextView = new object() };
            var lowerView = new FakeView();
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            factory.NextView = lowerView;
            core.OpenPanel(typeof(FakeView), "Page", "Page", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            factory.NextView = new FakeView();
            core.OpenPanel(typeof(FakeView), "Hud", "Hud", UILayer.HUD,
                UIInputMode.Self, UIPauseBelowMode.Never, UIOpenMode.Overlay,
                false, true, null, null);

            PanelRecord page = core.GetRecord("Page");
            Assert.That(page.Covered, Is.False);
            Assert.That(page.Paused, Is.False);
        }

        [Test]
        public void InputAndPause_DefaultToSelfAndNever()
        {
            var record = new PanelRecord(1, typeof(FakeView), "Page", "Page", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null);

            Assert.That(record.EffectiveInputMode, Is.EqualTo(UIInputMode.Self));
            Assert.That(record.EffectivePauseBelow, Is.False);
            Assert.That(record.CoversBelow, Is.False);
        }

        [Test]
        public void DisposeWhileLoading_ReleasesLateView()
        {
            var loader = new FakeLoader();
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            core.OpenPanel(typeof(FakeView), "Pending", "Pending", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);
            Assert.That(loader.Pending, Is.Not.Null);

            core.Dispose();
            loader.Pending.Invoke(new object(), null);

            Assert.That(factory.RawReleaseCalls, Is.EqualTo(1));
        }

        [Test]
        public void Back_DelegatesToInternalNavigationHandler()
        {
            var loader = new FakeLoader { NextView = new object() };
            var view = new FakeView { HandleBackResult = true };
            var factory = new FakeFactory { NextView = view };
            var core = new UIManagerCore(loader, factory);

            core.OpenPanel(typeof(FakeView), "Equipment", "Equipment", UILayer.Popup,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);
            core.Back();

            Assert.That(view.BackCount, Is.EqualTo(1));
            Assert.That(core.IsOpen("Equipment"), Is.True);
        }

        [Test]
        public void FailedOpen_DoesNotPolluteNavigationHistory()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            core.OpenPanel(typeof(FakeView), "A", "A", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            loader.NextView = null;
            loader.NextError = "load failed";
            core.OpenPanel(typeof(FakeView), "B", "B", UILayer.Normal,
                UIInputMode.Inherit, UIPauseBelowMode.Inherit, UIOpenMode.Inherit,
                false, true, null, null);

            Assert.That(core.Navigation.Count, Is.Zero);
            Assert.That(core.IsOpen("A"), Is.True);
            Assert.That(core.IsOpen("B"), Is.False);

            core.Back();
            Assert.That(core.IsOpen("A"), Is.True, "失败打开不应让 Back 误关根页面");
        }

        [Test]
        public void SuccessfulOpen_RecordsBackNavigation()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            Open(core, "A");
            Open(core, "B");
            Open(core, "C");

            Assert.That(core.Navigation.Count, Is.EqualTo(2));
            core.Back();
            Assert.That(core.IsOpen("C"), Is.False);
            Assert.That(core.IsOpen("B"), Is.True);

            core.Back();
            Assert.That(core.IsOpen("B"), Is.False);
            Assert.That(core.IsOpen("A"), Is.True);

            core.Back();
            Assert.That(core.IsOpen("A"), Is.True, "根页面没有剩余历史时不应被关闭");
        }

        [Test]
        public void Replace_KeepsNavigationDepthAndClosesReplacedPanel()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            Open(core, "A");
            Open(core, "B");
            Open(core, "C", inputMode: UIInputMode.Inherit,
                pauseBelow: UIPauseBelowMode.Inherit, openMode: UIOpenMode.Replace);

            Assert.That(core.IsOpen("A"), Is.True);
            Assert.That(core.IsOpen("B"), Is.False);
            Assert.That(core.IsOpen("C"), Is.True);
            Assert.That(core.Navigation.Count, Is.EqualTo(1));

            core.Back();
            Assert.That(core.IsOpen("C"), Is.False);
            Assert.That(core.IsOpen("A"), Is.True);
        }

        [Test]
        public void Overlay_DoesNotConsumeUnderlyingBackHistory()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            Open(core, "A");
            Open(core, "B");
            Open(core, "Overlay", openMode: UIOpenMode.Overlay);

            core.Back();
            Assert.That(core.IsOpen("Overlay"), Is.True);
            Assert.That(core.IsOpen("B"), Is.True);

            core.ClosePanelByName("Overlay", immediate: true);
            core.Back();
            Assert.That(core.IsOpen("B"), Is.False);
            Assert.That(core.IsOpen("A"), Is.True);
        }

        [Test]
        public void ClosingPanel_RemovesItsOwnNavigationEntry()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            Open(core, "A");
            Open(core, "B");
            Open(core, "C");

            int cSerial = core.GetRecord("C").SerialId;
            core.ClosePanel(cSerial, immediate: true);

            Assert.That(core.Navigation.Count, Is.EqualTo(1));
            core.Back();
            Assert.That(core.IsOpen("B"), Is.False);
            Assert.That(core.IsOpen("A"), Is.True);

            core.Back();
            Assert.That(core.IsOpen("A"), Is.True);
        }

        [Test]
        public void NonStackablePanel_DoesNotCreateNavigationEntry()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory);

            Open(core, "A");
            Open(core, "Toast", stackable: false);
            Open(core, "B");

            Assert.That(core.Navigation.Count, Is.EqualTo(1));
            Assert.That(core.Navigation.Peek(), Is.EqualTo(core.GetRecord("A").SerialId));

            core.Back();
            Assert.That(core.IsOpen("B"), Is.False);
            Assert.That(core.IsOpen("A"), Is.True);
            Assert.That(core.IsOpen("Toast"), Is.True);
        }

        [Test]
        public void Back_ConsumesHistoryBeforeDelayedCloseFinishes()
        {
            var loader = new FakeLoader { NextView = new object() };
            var factory = new FakeFactory();
            var core = new UIManagerCore(loader, factory) { CloseDelaySeconds = 1f };

            Open(core, "A");
            Open(core, "B");
            Open(core, "C");

            core.Back();
            Assert.That(core.Navigation.Count, Is.EqualTo(1));
            Assert.That(core.GetRecord("C").State, Is.EqualTo(PanelState.Closing));

            core.Back();
            Assert.That(core.IsOpen("B"), Is.True, "关闭动画期间不应连续关闭下层页面");

            core.Tick(1f);
            core.Back();
            Assert.That(core.IsOpen("B"), Is.False);
            Assert.That(core.IsOpen("A"), Is.True);
        }

        private static void Open(UIManagerCore core, string name, bool stackable = true,
            UIInputMode inputMode = UIInputMode.Inherit,
            UIPauseBelowMode pauseBelow = UIPauseBelowMode.Inherit,
            UIOpenMode openMode = UIOpenMode.Inherit)
        {
            core.OpenPanel(typeof(FakeView), name, name, UILayer.Normal,
                inputMode, pauseBelow, openMode,
                false, true, null, null, stackable);
        }
    }
}
