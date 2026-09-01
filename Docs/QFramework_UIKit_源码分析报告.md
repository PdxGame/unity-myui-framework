# QFramework UIKit UI 管理源码深挖报告

> 分析对象：https://github.com/liangxiegame/QFramework （仅 master 一个分支）
> 分析方式：直接拉取 raw.githubusercontent.com 原始源码逐文件精读（非推断），并拉取 v0.3.0 / v0.10.49 / v0.14.47 三个历史 tag 做版本对照。
> 素材版本：master（commit 4a25d817，2025）、v0.3.0（2018）、v0.10.49（2020）、v0.14.47（2021）。
> 少量 GameFramework 对比项基于 GameFramework 公开源码常识，文中已标注。

---

## 0. 路径考古（版本差异先行说明）

| 版本 | UIKit 运行时代码路径 | 形态 |
|---|---|---|
| v0.3.0 (2018) | `Assets/QFramework/Framework/3.UIKit/1.UI/Script/System/` | `UIManager` 兼任 UIRoot，11 层枚举，消息驱动架构 |
| v0.10.49 (2020) | `Framework/3.UIKit/Runtime/`（ToolKits 子仓布局） | `UIKit` 静态门面成型，UIRoot 独立为 4 层 |
| v0.14.47 (2021) | `QFramework.ToolKits/Assets/QFramework/Toolkits/UIKit/Scripts/` | 加载器对象池化，UILevel 精简 |
| master (2025) | `QFramework.Unity2018+/Assets/QFramework/Toolkits/UIKit/Scripts/` | Resources 默认加载（与 ResKit 解耦），异步 API 齐全 |

**关键纠偏**：QFramework 所有已发布版本的层级实现都是"根 Canvas 下挂固定 RectTransform 分层节点 + SetParent"，**从未使用"每层启动一个 Canvas、sortingOrder = 层索引 × N"的方案**。用户问题 4 中预设的 sortingOrder 计算规则不存在于 QFramework 任何版本代码中（那是部分其他框架或博客教程变体的做法）。详见 §5。

---

## 1. 类关系图（文本形式，master 版）

```
                          ┌────────────────────────────┐
                          │ UIKit (static facade)      │
                          │  Config : UIKitConfig      │
                          │  Table  : UIPanelTable     │──┐
                          │  Stack  : UIPanelStack     │  │
                          │  Root   => Config.Root     │  │
                          └─────┬──────────────────────┘  │
                                │ OpenPanel<T>/ClosePanel<T>/Show/Hide/Get/Back
                                ▼                         │
                          ┌────────────────────────────┐ │
                          │ UIManager : MonoBehaviour   │ │
                          │             , ISingleton    │ │  索引查询
                          │  OpenUI / OpenUIAsync      │ │
                          │  CreateUI / CreateUIAsync  │ │
                          │  CloseUI / CloseAllUI      │ │
                          │  ShowUI / HideUI / GetUI   │ │
                          │  RemoveUI                  │ │
                          └──┬────────┬────────────┬───┘ │
              加载+实例化     │        │登记/检索    │    │
                    ┌────────▼──┐  ┌──▼──────────┐ │    │
                    │UIKitConfig│  │UIPanelTable │◄├────┘
                    │ LoadPanel │  │ (UIKitTable)│ │
                    └────┬──────┘  │  ├ GameObjectNameIndex (name→IPanel)
                         │         │  └ TypeIndex          (Type→IPanel)
        PanelLoaderPool  │         └───────────────────────
        ┌────────────────▼─────────────────┐
        │ IPanelLoaderPool / Abstract...   │  ← UIKit.Config.PanelLoaderPool（可整池替换）
        │   └ DefaultPanelLoaderPool       │     master 默认：Resources.Load(GameObjName)
        │       └ DefaultPanelLoader       │     ResKit 版：ResKitPanelLoaderPool(SupportOldQF)
        └──────────────────────────────────┘

  ┌───────────────────────────────────────────────────────────┐
  │ IPanel (partial interface)                                │
  │  Transform / Loader / Info / State                        │
  │  Init(IUIData) Open(IUIData) Show() Hide() Close(bool)    │
  └──────────────┬────────────────────────────────────────────┘
                 ▲ 实现
  ┌──────────────┴──────────────────┐   持有    ┌──────────────────────┐
  │ UIPanel : MonoBehaviour, IPanel │──────────▶│ PanelInfo (池化)      │
  │  OnInit/OnOpen/OnShow/OnHide    │           │  UIData/Level/       │
  │  /OnClose(abstract)/OnBefore-   │           │  AssetBundleName/    │
  │  Destroy/ClearUIComponents      │           │  GameObjName/PanelType│
  │  mUIData / Info / State         │           └──────────────────────┘
  └─────────────────────────────────┘
                 ▲ 数据传入
  ┌──────────────┴──────────┐
  │ IUIData (marker 空接口)  │◀── UIPanelData（空基类，CodeGen 生成 XXXPanelData）
  └─────────────────────────┘

  UIRoot : MonoBehaviour, ISingleton   ← Resources.Load("UIRoot") 自动建
    UICamera / Canvas / CanvasScaler / GraphicRaycaster
    Bg / Common / PopUI / CanvasPanel  (4 个 RectTransform 分层)

  辅助：PanelSearchKeys(池化查询参数)、PanelOpenType{Single,Multiple}、
        PanelState{Opening,Hide,Closed}、SafeObjectPool、ListPool
```

---

## 2. UIKit.OpenPanel<T> 完整时序（master，同步版）

门面签名（`Toolkits/UIKit/Scripts/UIKit.cs`）：

```csharp
public static T OpenPanel<T>(PanelOpenType panelOpenType, UILevel canvasLevel = UILevel.Common,
    IUIData uiData = null, string assetBundleName = null, string prefabName = null) where T : UIPanel

public static T OpenPanel<T>(UILevel canvasLevel = UILevel.Common, IUIData uiData = null,
    string assetBundleName = null, string prefabName = null) where T : UIPanel          // OpenType=Single

public static T OpenPanel<T>(IUIData uiData, PanelOpenType panelOpenType = PanelOpenType.Single,
    string assetBundleName = null, string prefabName = null) where T : UIPanel          // Level 固定 Common

public static UIPanel OpenPanel(string panelName, UILevel level = UILevel.Common, string assetBundleName = null)

public static IEnumerator OpenPanelAsync<T>(UILevel canvasLevel, IUIData uiData,
    string assetBundleName, string prefabName)   // 协程，OpenType 强制 Single
```

编号步骤（以 `OpenPanel<UIHomePanel>(UILevel.Common, data)` 为例）：

1. **组装查询键**：`PanelSearchKeys.Allocate()`（SafeObjectPool 取池化对象），填入 `OpenType=Single、Level、PanelType=typeof(T)、AssetBundleName、GameObjName=prefabName、UIData`。
2. **进入单例调度器**：`UIManager.Instance.OpenUI(keys)`。首次访问触发 `UIRoot.Instance`（`FindObjectOfType<UIRoot>()` 失败则 `Resources.Load("UIRoot")` 实例化内置 prefab 并 `DontDestroyOnLoad`），随后 `MonoSingletonProperty<UIManager>.Instance` 把 UIManager 挂到 `UIRoot/Manager` 节点（`[MonoSingletonPath("UIRoot/Manager")]`）。
3. **查重**（Single 模式）：`UIKit.Table.GetPanelsByPanelSearchKeys(keys).FirstOrDefault()`。UIPanelTable 有两个索引：`TypeIndex`（key=面板 GetType()）和 `GameObjectNameIndex`（key=Transform.name）。组合规则：有 PanelType 且有名字/Panel 引用 → TypeIndex 再 Where 过滤；只有 PanelType → TypeIndex；只有 Panel 引用 → 名字索引再过滤；只有名字 → 名字索引。
4. **命中已有面板**：若 `retPanel.Info != null && retPanel.Info.Level != keys.Level` → `UIKit.Root.SetLevelOfPanel(keys.Level, retPanel)` 迁移层级；然后跳到第 8 步。**不会重新实例化。**
5. **未命中 → 创建**：`CreateUI(keys)`：
   1. `UIKit.Config.LoadPanel(keys)`：`PanelLoaderPool.AllocateLoader()`（Stack 池）→ `loader.LoadPanelPrefab(keys)`。**master 默认 DefaultPanelLoader 只用 `Resources.Load<GameObject>(keys.GameObjName)`，AssetBundleName 被忽略**；若装了 ResKit（`UIKitWithResKitInit` 的 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 替换 PanelLoaderPool），则按三段优先级：`PanelType.Name`（类名即 prefab 名）→ `(AssetBundleName, GameObjName)` → `GameObjName`，经 `ResLoader.LoadSync<GameObject>()`（内部 `ResSearchKeys.Allocate(assetName, ownerBundle, GameObjectType)` → `LoadAssetSync`）加载。→ `Object.Instantiate(prefab)` → `GetComponent<UIPanel>()` → `(panel as IPanel).Loader = loader`（把加载器绑定到实例，供关闭时卸载）。
   2. `UIKit.Root.SetLevelOfPanel(keys.Level, panel)`：面板 Transform 上**自带 Canvas 组件** → `SetParent(UIRoot.CanvasPanel)`；否则 switch：`Bg→SetParent(Bg)`、`Common→SetParent(Common)`、`PopUI→SetParent(PopUI)`（其他值无分支、不动）。随后同步 `panel.Info.Level`。
   3. `UIKit.Config.SetDefaultSizeOfPanel(panel)`：RectTransform 拉伸铺满：`anchorMin=(0,0)、anchorMax=(1,1)、offsetMin/Max=0、anchoredPosition3D=0、localScale=1`。
   4. 命名：`gameObject.name = keys.GameObjName ?? keys.PanelType.Name`。
   5. `panel.Info = PanelInfo.Allocate(gameObjName, level, uiData, panelType, assetBundleName)`（SafeObjectPool 分配）。
   6. `UIKit.Table.Add(panel)`（双索引登记）。
   7. `panel.Init(keys.UIData)`：`mUIData = uiData; OnInit(uiData)`。
6. **打开**：`retPanel.Open(keys.UIData)`：`State = PanelState.Opening; OnOpen(uiData)`。
7. **显示**：`retPanel.Show()`：`gameObject.SetActive(true); OnShow()`。
8. **回收查询键**：`keys.Recycle2Cache()`（OnRecycled 清空所有字段），返回 `T`。

异步版差异：`OpenPanelAsync<T>` 调 `UIManager.OpenUIAsync`，加载走 `loader.LoadPanelPrefabAsync`（master 默认实现是 `Resources.LoadAsync`，completed 回调里继续 CreateUIAsync 的同构步骤；ResKit 版是 `Add2Load<T> + LoadAsync`）。门面用 `while (!loaded) yield return mWaitForEndOfFrame;` 轮询闭包标志位，可 `yield return` 也可 `.ToAction().Start(this)`（ActionKit）。

**重复 Open 行为结论**：Single 面板二次 Open = 查表命中 → （必要时迁层）→ `Open(新 uiData)` + `Show()`，**实例复用**；`Multiple` 面板每次 Open 都 `CreateUI` 新实例，同一类型可同时存在多份。

---

## 3. UIPanel 生命周期全集与调用顺序

```csharp
public abstract class UIPanel : MonoBehaviour, IPanel
{
    public void Init(IUIData uiData = null);        // mUIData = uiData; OnInit(uiData)
    public void Open(IUIData uiData = null);        // State = Opening; OnOpen(uiData)
    public virtual void Show();                     // SetActive(true); OnShow()
    public virtual void Hide();                     // State = Hide; OnHide(); SetActive(false)
    void IPanel.Close(bool destroyed = true);       // 显式接口实现，子类不可直接调
    protected virtual void OnInit(IUIData uiData);  // 子类重写
    protected virtual void OnOpen(IUIData uiData);
    protected virtual void OnShow();
    protected virtual void OnHide();
    protected abstract void OnClose();              // 必须实现（编译期强制）
    protected virtual void OnDestroy();             // Application.isPlaying 时转调 OnBeforeDestroy()
    protected virtual void OnBeforeDestroy();       // → ClearUIComponents()
    protected virtual void ClearUIComponents();     // 生成代码中 mData = null
    protected void CloseSelf();                     // → UIKit.ClosePanel(this)
    protected void Back();                          // → UIKit.Back(name)
    public void OnClosed(Action onPanelClosed);     // 注册一次性关闭回调
    public PanelInfo Info { get; set; }
    public PanelState State { get; set; }
    protected IUIData mUIData;
    IPanelLoader IPanel.Loader { get; set; }        // 显式接口，子类不可见
}
```

`IPanel.Close(bool destroyed)` 内部顺序（源码逐行）：
1. `Info.UIData = mUIData`（把运行期数据回写进 PanelInfo，供栈恢复）；
2. `mOnClosed?.Invoke(); mOnClosed = null;`（一次性回调）；
3. `Hide()`（→ OnHide → SetActive(false)）；
4. `State = PanelState.Closed`；
5. `OnClose()`；
6. `if (destroyed) Destroy(gameObject)`；
7. `Loader.Unload(); PanelLoaderPool.RecycleLoader(Loader); Loader = null`（归还加载器）；
8. `mUIData = null`（**UI 数据销毁约定：关闭即置空**）。

**真实调用顺序**（源码级 + 官方测试 `UIKitV0_10_XTests.cs` 用计数 Data 验证）：

| 场景 | 顺序 |
|---|---|
| 首次 Open（Single） | `OnInit → OnOpen → OnShow`（State=Opening） |
| 重复 Open（已打开） | 仅 `OnOpen → OnShow`（不重新 Init） |
| `ShowPanel`（已隐藏） | 仅 `OnShow` |
| `HidePanel` | `OnHide`（State=Hide） |
| `ClosePanel` | `OnClosed回调 → OnHide → OnClose → OnDestroy → OnBeforeDestroy → ClearUIComponents`（State=Closed，对象 Destroy，Loader 卸载，mUIData=null） |
| Push（入栈） | 同 Close（destroyed=true → 对象销毁） |
| Pop（出栈） | **重新实例化** → `OnInit(旧UIData) → OnOpen → OnShow` |

**数据传入约定**：`IUIData` 是空 marker 接口；`UIPanelData` 是空基类。CodeGen（UIKitTestPanel.Designer.cs 为实证）为每个面板生成 partial：
```csharp
public UIKitTestPanelData Data { get { return mData; } }
UIKitTestPanelData mData {
    get { return mPrivateData ?? (mPrivateData = new UIKitTestPanelData()); }
    set { mUIData = value; mPrivateData = value; } }
protected override void ClearUIComponents() { mData = null; }
```
用户 partial 里在 `OnInit` 中 `mData = uiData as XXXPanelData ?? new XXXPanelData()`。因此：**PanelData 的生命周期 = 面板实例生命周期**；Close 时 `mUIData=null + ClearUIComponents` 双重销毁。数据跨关闭"存活"的唯一通道是 `PanelInfo.UIData`（见 §6 栈）。

---

## 4. 层级系统精确转述

**UILevel 枚举（master 现状）**：
```csharp
public enum UILevel {
    [Obsolete] AlwayBottom = -3,  Bg = -2,  [Obsolete] AnimationUnderPage = -1,
    Common = 0,  [Obsolete] AnimationOnPage = 1,  PopUI = 2,
    [Obsolete] Guide = 3, [Obsolete] Const = 4, [Obsolete] Toast = 5,
    [Obsolete] Forward = 6, [Obsolete] AlwayTop = 7,
    CanvasPanel = 100,  // "一个 Panel 就是一个 Canvas 的 Panel"
}
```
历史上是 11 层全量枚举（v0.3.0：AlwayBottom/Bg/AnimationUnderPage/Common/AnimationOnPage/PopUI/Guide/Const/Toast/Forward/AlwayTop），后逐步标 `[Obsolete]`，实际生效只剩 **Bg(-2) / Common(0) / PopUI(2) / CanvasPanel(100)**。没有 UnderHud/Guide/System/Popup 这些层名（Guide/Const/Toast 曾存在但已废弃）。

**UIRoot 结构（`[MonoSingletonPath("UIRoot")]`）**：
```csharp
public Camera UICamera;  public Canvas Canvas;  public CanvasScaler CanvasScaler;
public GraphicRaycaster GraphicRaycaster;
public RectTransform Bg;  public RectTransform Common;
public RectTransform PopUI;  public RectTransform CanvasPanel;
```
- 自动创建：`Resources.Load<GameObject>("UIRoot")` 实例化内置 prefab，`DontDestroyOnLoad`。
- **分层方式 = 4 个 RectTransform 子节点**（对应用户 prefab 上的引用），不是每层一个 Canvas。根 Canvas 上只有**一个** GraphicRaycaster。
- `SetLevelOfPanel(UILevel level, IPanel panel)` 全部逻辑：`panel.Transform.GetComponent<Canvas>()` 有 Canvas → `SetParent(CanvasPanel)`（忽略 level）；否则 switch(Bg/Common/PopUI) → `SetParent(对应节点)`；**switch 无 default，其余枚举值面板原地不动**。结尾若 `panel.Info != null && panel.Info.Level != level` 则更新 `Info.Level`。
- **排序规则**：层间顺序 = 分层节点在 Hierarchy 中的固定兄弟顺序（Bg < Common < PopUI < CanvasPanel）；**同层内顺序 = UGUI sibling order**——`Transform.SetParent(parent)` 默认把新节点追加到子列表末尾，因此**后打开的面板显示在先打开的面板之上**；层级迁移（重复 Open 换 level）会改变兄弟位置。**没有任何 sortingOrder 乘法/加法计算**；`CanvasPanel=100` 只是个哨兵枚举值，不参与运算。
- **CanvasPanel 的用途**：面板 prefab 自带 Canvas 组件时（"一个 Panel 就是一个 Canvas"），因 UGUI 嵌套 Canvas 的排序局限，被集中挂到 CanvasPanel 层，由用户自己控制每个面板 Canvas 的 `overrideSorting/sortingOrder`。这是框架给"特殊面板"（全屏转场、可穿透遮挡管理）留的逃生门。
- **Raycast 处理**：分层节点无独立 Raycaster；`Empty4Raycast : MaskableGraphic`（`OnPopulateMesh` 里 `toFill.Clear()`）是框架提供的零面数透明图形，替代"空 Image 扩大点击区"，降低 DrawCall 且不产生顶点。
- 渲染模式：`ScreenSpaceOverlayRenderMode()`（关 UICamera）与 `ScreenSpaceCameraRenderMode()`（`Canvas.worldCamera = UICamera`）两个开关；`SetResolution(w,h,match)` 转发 CanvasScaler。

---

## 5. 导航栈（UIKit.Stack）

```csharp
public class UIPanelStack
{
    private Stack<PanelInfo> mUIStack;
    public void Push<T>() where T : UIPanel;   // Push(UIKit.GetPanel<T>())
    public void Push(IPanel view);             // mUIStack.Push(view.Info); view.Close(); UIManager.RemoveUI(名字)
    public void Pop();                         // 弹出 PanelInfo → OpenUI(重建)
}
```
- **Push 语义 = "关闭并记住"**：把该面板的 `PanelInfo`（含 UIData、Level、AssetBundleName、PanelType、GameObjName）压栈，然后 `view.Close()`（接口默认参 `destroy=true` → **对象销毁**），再 `RemoveUI` 把表项移除。注意 `Close()` 内 `Info.UIData = mUIData`，运行期数据被捕获进 PanelInfo——这就是数据能跨关闭存活的机制。
- **Pop 语义 = "重新打开"**：弹出 PanelInfo → 组 PanelSearchKeys → `UIManager.OpenUI(keys)`。此时表中已无该面板 → **走完整 CreateUI：重新加载 prefab、重新 Instantiate、重新 Init(旧 UIData)**。二次打开是全新实例，不是恢复旧实例。
- `UIKit.Back(...)` 三重载（string / UIPanel / T）：先 `CloseUI(当前面板)`（查 `LastOrDefault`），再 `Stack.Pop()` 恢复上一个。官方测试 `UIKit_PushBackTest` 验证：Push 面板 A → State=Closed；Open 面板 B；Back(B) → A 重新 Opening。
- **坑**：`Pop()` 对空栈无保护（直接 `Stack.Pop()` 抛异常）；被 Pop 出的 PanelInfo 不会 `Recycle2Cache`；Push 时若 `view.Info` 已被回收（二次 Push 同一面板）会把 null 压栈。用途定位：轻量的"设置页 → 子页 → 返回"式流，不是全场景导航器。

---

## 6. 关闭 / 销毁与资源释放调用链

```
UIKit.ClosePanel<T>() / ClosePanel(name) / ClosePanel(panel) / panel.CloseSelf()
  → PanelSearchKeys.Allocate()（T→PanelType / name→GameObjName / panel→Panel）
  → UIManager.CloseUI(keys)
      → Table.GetPanelsByPanelSearchKeys(keys).LastOrDefault()   ← 注意是 Last
      → panel.Close()   // IPanel.Close(destroy=true)
          回调 mOnClosed → Hide → State=Closed → OnClose → Destroy(gameObject)
          → Loader.Unload() → PanelLoaderPool.RecycleLoader → mUIData=null
      → Table.Remove(panel)
      → panel.Info.Recycle2Cache()   // PanelInfo.OnRecycled: UIData/AssetBundleName/GameObjName/PanelType 全置 null
      → panel.Info = null
UIKit.CloseAllPanel() → UIManager.CloseAllUI()：遍历 Table 逐个 Close + 回收 Info → Table.Clear()
UIKit.HideAllPanel() → 全部 Hide()（State=Hide，不销毁）
UIManager.OnApplicationQuit → UIKit.Table.Clear()
```
资源释放责任分层：**prefab 资源**由 IPanelLoader 负责（master Resources 版 Unload 只置空引用——Resources 资源本就不卸载；ResKit 版 `mResLoader.Recycle2Cache()` 走引用计数卸载 AB）；**GameObject** 由 `Destroy(destroyed=true)` 负责；**PanelInfo/PanelSearchKeys** 是 SafeObjectPool 对象池回收；**PanelData** 由 ClearUIComponents + mUIData=null 销毁。PanelLoaderPool 池化了 loader 实例本身（`Stack<IPanelLoader>`，初始容量 16），避免 ResKit 版反复 Allocate ResLoader。

---

## 7. 关键类速览（每类 2-4 行）

- **UIKit**（静态门面）：全部 API 都是"组 PanelSearchKeys → 委托 UIManager → 回收 keys"的三段式；持有 Config/Table/Stack 三个静态状态点；Back 封装"关当前+Pop"。无任何 Unity 生命周期。
- **UIManager**（MonoBehaviour 单例，挂在 UIRoot/Manager）：真正的调度器；Open/Close/Show/Hide/Get/Create(Async)/Remove；Single 查表复用、Multiple 每次新建；OnApplicationQuit 清表。
- **UIPanel**（抽象 MonoBehaviour）：生命周期模板方法容器；Close 用显式接口实现防止子类随意关闭、OnClose 设为 abstract 强制子类收尾；数据经 IUIData 注入，关闭即销毁。
- **UIPanelInfo/PanelInfo**（池化 POJO）：面板元数据快照（名字/层级/UIData/类型/AB 名），是 PanelTable 登记项、Stack 的栈元素、PanelSearchKeys 之外的唯一持久数据载体；OnRecycled 全清。
- **PanelSearchKeys**（池化查询参数）：一次调用的参数包（OpenType/Level/PanelType/ABName/GameObjName/UIData/Panel），用完即还池；避免 OpenPanel 重载爆炸。
- **UIPanelTable**（UIKitTable<IPanel>）：双索引（Type、GameObjectName）的多重映射，索引 List 用 ListPool；GetPanelsByPanelSearchKeys 提供组合查询；GetEnumerator 遍历名字索引。
- **UIRoot**（MonoBehaviour 单例）：UI 总根（Canvas/Scaler/Raycaster/UICamera + 4 分层节点）+ 分辨率/渲染模式 API + SetLevelOfPanel 挂载逻辑；Resources 自动创建。
- **UILevel**：历史 11 层枚举精简为 Bg/Common/PopUI/CanvasPanel（其余 Obsolete）。
- **UIKitConfig**（可继承覆写的配置点）：Root 解析 + LoadPanel/LoadPanelAsync（加载-实例化-绑 Loader）+ PanelLoaderPool（默认 DefaultPanelLoaderPool，Resources 实现）+ SetDefaultSizeOfPanel（锚点铺满）。
- **IPanelLoader / IPanelLoaderPool / AbstractPanelLoaderPool / DefaultPanelLoaderPool**：加载策略插件点；master 默认 Resources.Load(GameObjName)，`UIKitWithResKitInit` 在 BeforeSceneLoad 把整池替换为 ResKitPanelLoaderPool（按 类名 → (AB,名) → 名 优先级经 ResLoader 引用计数加载）。
- **UIPanelStack**：PanelInfo 栈，Push=关+记、Pop=重开；轻量导航。
- **UIPanelTester**：调试挂件——Inspector 填 PanelName/Level/otherPanels 列表，Start 延迟 0.2s 逐个 `UIKit.OpenPanel(名字, 层级)`，用于场景里一键拉起被测面板（编辑期手测 UI 布局）。
- **UIElement/UIComponent**（CodeGen 配套）：`GetBindType()`（Element/Component）、`ComponentName` 抽象属性，配合 CodeGenKit 的 UIMark/Bind 生成 Designer partial（控件绑定 + Data 属性）。
- **Empty4Raycast**：零顶点 MaskableGraphic，空 RectTransform 的 raycast 载体。
- **架构集成（QFramework.cs 单文件，968 行）**：`Architecture<T>` 内嵌 `IOCContainer`（`Dictionary<Type,object>` 的 Register/Get）与 `TypeEventSystem` 实例；`InitArchitecture()` 先 Init 全部 IModel 再 ISystem；事件 API `SendEvent/RegisterEvent/UnRegisterEvent` 全部转发给内部 TypeEventSystem（另有静态 `TypeEventSystem.Global`）。

---

## 8. 与 QFramework 架构（Architecture/IOC/TypeEventSystem）的集成点

**核心事实：UIKit 与 Architecture 零耦合。** UIPanel 基类不实现 IController，UIManager 不引用 IArchitecture。集成完全靠**用户在自己面板类上追加接口声明 + 一个方法**：

```csharp
public partial class UIGamePanel : UIPanel, IController
{
    public IArchitecture GetArchitecture() => GameApp.Interface;  // IBelongToArchitecture 的实现
}
```

之后 `IController` 的全部能力经"能力接口 + 扩展方法"注入（QFramework.cs 根单文件 L226-229、L364-462）：

```csharp
public interface IController : IBelongToArchitecture, ICanSendCommand, ICanGetSystem,
    ICanGetModel, ICanRegisterEvent, ICanSendQuery, ICanGetUtility { }

// 能力全由扩展方法落地，例如：
public static T GetModel<T>(this ICanGetModel self) where T : class, IModel
    => self.GetArchitecture().GetModel<T>();
public static IUnRegister RegisterEvent<T>(this ICanRegisterEvent self, Action<T> onEvent)
    => self.GetArchitecture().RegisterEvent<T>(onEvent);
```

- 每个能力接口都是空标记（`ICanSendCommand/ICanGetSystem/ICanGetModel/ICanGetUtility/ICanRegisterEvent/ICanSendEvent/ICanSendQuery`），配一个 `CanXxxExtension` 静态类——**权限由接口组合表达**（IController 有发送命令权，ISystem 无；IModel 不能 GetSystem 等）。
- 事件注销与 UI 生命周期绑定：`this.RegisterEvent<T>(OnXxx).UnRegisterWhenGameObjectDestroyed(gameObject)`（`UnRegisterExtension` + `UnRegisterOnDestroyTrigger` MonoBehaviour），弥补 TypeEventSystem 回调泄漏。
- `TypeEventSystem` 本体（L624-641）：`Dictionary<Type, EasyEvent>` + `Global` 静态单例；`Send<T>(e)`/`Register<T>(Action<T>)→IUnRegister`/`UnRegister<T>`。
- `Architecture<T>`（L70-206）：`InitArchitecture()` 幂等单例化 → `Init()` → `OnRegisterPatch` → 逐个 Init 未初始化的 IModel、ISystem；`RegisterSystem/RegisterModel` 时 `SetArchitecture(this)` 并注入容器；`SendCommand/ExecuteCommand`、`SendQuery/DoQuery` 走 AbstractCommand/AbstractQuery 的 SetArchitecture。
- 另：ResKit 自带一个 `internal Architecture : Architecture<Architecture>`（`Toolkits/ResKit/Scripts/Architecture/Architecture.cs`），`RuntimeInitializeOnLoadMethod` 自动 Init，注册 ConsoleModuleSystem/ZipFileHelper/BinarySerializer——各 Kit 用 Architecture 自治，UI 层可选接入。
- 历史版（v0.3.0）UIPanel 基类是 `QMonoBehaviour`（ManagerOfManagers 消息架构，`Manager => UIManager.Instance`，QMsgCenter 派发），v0.11+ 重构后彻底剥离。

---

## 9. 局限与常见吐槽（全部源码可证）

1. **异步竞态未处理**：`OpenPanelAsync` 加载完成前 Table 无登记，并发打开同一 Single 面板会双双 `CreateUIAsync` → 双实例、双 Type 索引记录；`CloseUI` 用 `LastOrDefault` 只关掉一个。门面用 `WaitForEndOfFrame` 忙轮询闭包标志，也不是干净的任务模型。
2. **Single/Multiple 的 Close 语义不对称**：Open 用 `FirstOrDefault`（Get/Show/Hide 同），Close 用 `LastOrDefault`——Multiple 多实例下 GetPanel 拿到第一个、ClosePanel 关掉最后一个，无法按 serialId 精确寻址。
3. **重复 Open 的数据陷阱**：Single 面板二次 Open 时 `Open(newData)` 只把 newData 传给 OnOpen 参数，`mUIData`/`Data` 属性仍是 Init 时的旧对象（Init 不再调用）；且 keys 立刻回收，想更新数据得自己 GetPanel 后改。
4. **层级表达力弱**：只有 3 个实际分层 + sibling order；层内精细排序（拖拽置顶、层级插队）无 API；`CanvasPanel=100` 的语义靠注释脑补；同层多面板弹出时"谁在上"只由打开顺序决定。
5. **遮罩/遮挡零支持**：无 OnCovered/OnReveal（GameFramework 有 pauseCovered）、无模态遮罩管理、无焦点（Refocus）概念——Hide 只是 SetActive(false)，被弹窗覆盖的面板照常收 Input（除非用户手动 Hide）。
6. **加载端历史包袱**：master 默认 DefaultPanelLoader **忽略 AssetBundleName**（只用 Resources）；ResKit 接入靠 `UIKitWithResKitInit`（放在 SupportOldQF 目录、类名带 WithInit）这种运行时静态替换，隐式全局副作用，装饰痕迹重。
7. **PanelSearchKeys/PanelInfo 双池化对象引用交错**：Push 出栈的 PanelInfo 不回收；Push 已 Close 的面板会把 null 压栈；Info 在 CloseUI 后被回收置空，外部若仍持有 panel.Info 就是悬空引用。
8. **错误处理薄弱**：prefab 加载失败不判空（`Object.Instantiate(null)` 抛 NRE）；`Resources.Load` 找不到 prefab 时静默 null 组件导致后续 `GetComponent<UIPanel>()` 返回 null；Pop 空栈抛异常。
9. **强依赖单例与 Resources 约定**：UIRoot 必须能从 `Resources/UIRoot` 加载；面板 prefab 名必须与类名一致（ResKit 模式）或显式传名；[MonoSingletonPath] 依赖字符串节点路径。
10. **`CanvasPanel` 面板关闭后 Loader 一样走 Resources/ResKit 通道**，但自带 Canvas 的面板全屏转场等高级需求（遮罩渲染顺序、相机栈）框架不管。

---

## 10. 设计启示：UIKit vs GameFramework 的 UI 哲学

（GameFramework 侧基于其公开源码常识：UIComponent → UIManager → UIGroup/Depth → UIForm(UIFormLogic) + UIFormInstanceInfo 对象池，生命周期 OnInit/OnOpen/OnClose/OnPause/OnReveal/OnRefocus/OnUpdate/OnDepthChanged；此段为对比背景，非本次拉取内容。）

| 维度 | QFramework UIKit | GameFramework UI |
|---|---|---|
| 核心抽象 | 静态门面 + Mono 单例调度器 | Component 门面（GameEntry.UI）+ 纯 C# 模块 + 逻辑/实例分离（UIFormLogic ↔ UIForm） |
| 分层 | 3 层 RectTransform + sibling order | UIGroup（任意命名多组）+ 组内 int Depth + OnDepthChanged |
| 实例复用 | 无（Close 即 Destroy） | IObjectPool 复用 UIForm 实例（SetInstance 可自定义"实例化"） |
| 遮挡 | 无 | pauseCovered → OnPause/OnReveal/OnRefocus |
| 生命周期 | Init/Open/Show/Hide/Close（+Close 强制 abstract） | 9 个钩子（含 OnUpdate、OnDepthChanged） |
| 数据 | IUIData marker + CodeGen 生成的 Data 属性 + 池化 PanelInfo 快照 | UIFormData 任意 object |
| 资源 | IPanelLoader 插件（默认 Resources） | ResourceManager + LoadAssetCallbacks + UIFormHelper |
| 可测试性 | 依赖 Mono/场景单例 | 纯 C# 模块可脱离 Unity 测 |

**值得模仿的（QFramework 的优点）**：
1. **门面三段式 + 参数对象池**：`UIKit.OpenPanel<T>` 这种"组装 keys → 调度 → 归还 keys"让 API 面极小、重载可控，PanelSearchKeys/PanelInfo 池化消 GC——自研框架值得保留这个形状。
2. **生命周期上"必须收尾"的编译期约束**：`OnClose()` 设为 abstract（而非 virtual 空实现），子类不写收尾逻辑直接编译失败；`IPanel.Close` 显式接口实现防止业务代码绕过管理器直接关 UI。两条都是低成本高收益的防呆设计。
3. **加载器插件点 + 整池替换**：`IPanelLoader/IPanelLoaderPool` 抽象让 Resources/ResKit/Addressables 一行替换（`UIKit.Config.PanelLoaderPool = ...`），Close 时 `Loader.Unload()` 把资源释放责任精确绑定到面板实例——比全局资源管理器干净。
4. **索引表替代字典**：UIPanelTable 的 Type/Name 双索引 + 组合查询，天然支持 Multiple 多实例与泛型/字符串双寻址，代价极小。
5. **状态与数据随栈迁移**：PanelInfo 持有 UIData，Push 关闭时 `Info.UIData = mUIData`，Pop 重建时原样回注——"轻量导航 + 数据保留"用一个 POJO 栈就解决了，不必引入路由系统。
6. **代码生成绑定 Data**：Designer partial 提供 `Data` 属性 + `ClearUIComponents` 清理钩子，把"面板数据生命周期 = 面板生命周期"固化成模板。
7. **官方行为测试**：用计数 PanelData 断言生命周期调用次数（OnInitCalledCount==1 等），自研框架照抄这个测试法。

**建议简化的 / 不照抄的**：
1. **sortingOrder 方案自己补上**：不要学它的"3 层 RectTransform + 打开顺序"。更稳的折中是 GameFramework 的 UIGroup+Depth，或"每层一个常驻 Canvas、sortingOrder = 层基值（如层索引×100），层内再 sibling order"——注意 UGUI 嵌套 Canvas 排序陷阱（子 Canvas 必须 overrideSorting 才参与全局序），这是 QFramework 用 CanvasPanel 层回避的问题。
2. **Close 用显式接口 + abstract OnClose 可以保留，但补状态机校验**：QFramework 的 PanelState 只是记录，不拦截非法调用（Close 已 Hide 的面板、Open 已 Closed 的面板都能跑出怪序列）；自研时在 Open/Close 前做状态断言。
3. **Multiple 多实例建议直接上 serialId 寻址**（GameFramework 做法），别用 First/Last 这种脆弱的 FirstOrDefault 约定。
4. **异步打开返回句柄而非协程轮询**：把 OpenPanelAsync 改成返回 `IProgress/UniTask<UIPanel>` 或带序列号的请求对象，加载期间同面板去重。
5. **数据快照的回收要闭环**：Push/Pop 的 PanelInfo 出栈即回收；跨场景的持久数据别塞 UIData。
6. **IOC/事件不必绑进 UI 基类**：QFramework 用"面板类自己声明 IController + GetArchitecture()"保持 UIKit 独立，这比把 Architecture 引用塞进 UIPanel 基类好；自研时可保留"UI 层不知道架构层"的方向，但把 `GetArchitecture()` 也换成注入。
7. **ResKit 式静态 RuntimeInitializeOnLoadMethod 替换全局配置**要谨慎：隐式全局副作用对多人协作不友好，显式初始化入口更好。

**一句话总结**：QFramework UIKit 是"极简主义"的 UI 管理——静态门面 + 索引表 + 池化参数/快照 + 插件化加载器，代价是层级表达、遮挡回调、多实例寻址和异步竞态的缺失；它最适合作为自研框架的"骨架参考"（API 形状、防呆技巧、对象池纪律），而 GameFramework 更适合作为"运行时完备性"的参考（组/深度、遮挡暂停、实例池、serialId 寻址）。

---

## 附：源码文件清单（本次分析读取）

master：`Toolkits/UIKit/Scripts/{UIKit,UIManager,UIPanel,UIRoot,UILevel,UIKitConfig,IPanel,PanelInfo,PanelOpenType,PanelSearchKeys,UIPanelStack,UIPanelTable,UIDefaultPanel,DontDestroyOnLoad,Hide,Empty4Raycast}.cs`、`Scripts/Script/UIPanelTester.cs`、`Scripts/Component/{UIComponent,UIElement,UIElementList}.cs`、`Toolkits/SupportOldQF/Scripts/UIKitWithResKitInit.cs`、根 `QFramework.cs`（968 行架构单文件）、`Toolkits/ResKit/Scripts/{ResKit,Framework/ResLoader/{IResLoader,IResLoaderExtensions,ResLoader},Architecture/Architecture}.cs`、`_CoreKit/IOCKit/IOCKit.cs`、`QFrameworkTests/3.UIKitTests/{UIKitV0_10_XTests,Scripts/UI/UIKitTestPanel*,Scripts/UI/UIKitTestPanel2}.cs`、`_CoreKit/CodeGenKit/Scripts/Components/ViewController.cs`。
历史 tag：v0.3.0 `Framework/3.UIKit/1.UI/Script/System/{UIManager,UIPanel,UIStack,IPanel}.cs` + `Res/{IPanelLoader,DefaultPanelLoader}.cs`；v0.10.49 `Framework/3.UIKit/Runtime/{UIKit,Script/System/{UIRoot,UILevel,UIManager,UIPanel},Script/Config/UIKitConfig}.cs`；v0.14.47 `Toolkits/UIKit/Scripts/{UIRoot,UILevel,UIPanel,UIKitConfig,IPanel,PanelInfo}.cs`。
