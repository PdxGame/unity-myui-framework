# GameFramework / UnityGameFramework UI 模块源码深挖报告

> 分析对象与版本考据（全部结论来自直接拉取的源码，非二手资料）：
> - **EllanJiang/GameFramework**（纯 C# 核心）与 **EllanJiang/UnityGameFramework**（Runtime 封装）的 `master` 分支（最后推送 2023-09-05）。经 Git blob SHA 比对，`v2021.05.31` tag 与 master 的 UI 模块**完全一致**（仅 3 处 `.ToString()` 格式化微调：`UIManager.cs`、`UIManager.UIGroup.cs`、`UIComponent.cs` 各 1 处），因此下文结论同时适用于 v2021.05.31 与 master。
> - **重要纠偏**：官方仓库里**不存在** `UGUIForm` / `UGUIFormHelper` / `UGUIGroupHelper` 这三个类。UnityGameFramework 仓库自 v2020.12.31 起就是 UPM 包布局（根目录 `Scripts/Runtime/` + `package.json`），UI 目录中只有与渲染层无关的 `DefaultUIFormHelper` / `DefaultUIGroupHelper`。uGUI 的具体落地在官方示例工程 **StarForce**（EllanJiang/StarForce）的 `Assets/GameMain/Scripts/UI/UGuiForm.cs`（注意拼写是 **UGui**）和 `UGuiGroupHelper.cs`。StarForce 也没有自己的 UIFormHelper，实例化/销毁直接用框架包的 `DefaultUIFormHelper`。所谓 "ColorTween" 在官方代码中不存在——显隐动画是 `CanvasGroup.FadeToAlpha` 协程扩展（见 §6.2）。
> - 文中 `文件:行号` 均指 master 版本。

---

## (a) 类关系图（文本）

```text
══════════════ 纯 C# 核心（GameFramework/UI，无 UnityEngine 依赖）══════════════

GameFrameworkEntry.GetModule<IUIManager>() ──► UIManager (internal sealed partial : GameFrameworkModule, IUIManager)
                                                 │
   注入（Unity 侧在组件 Start 里调用）            │
   ├── SetResourceManager(IResourceManager)      │── LoadAsset(assetName, priority, LoadAssetCallbacks, OpenUIFormInfo)
   ├── SetObjectPoolManager(IObjectPoolManager)  │── CreateSingleSpawnObjectPool<UIFormInstanceObject>("UI Instance Pool")
   └── SetUIFormHelper(IUIFormHelper)            │
                                                 │
   ├── Dictionary<string,UIGroup> m_UIGroups ──► UIGroup (private sealed partial : IUIGroup)
   │                                              ├── IUIGroupHelper m_UIGroupHelper ──► SetDepth(int depth)
   │                                              └── GameFrameworkLinkedList<UIFormInfo> m_UIFormInfos（First = 栈顶/最新）
   │                                                    └── UIFormInfo (private sealed : IReference)
   │                                                          ├── IUIForm UIForm
   │                                                          ├── bool Paused      ─┐ 两个状态位，
   │                                                          └── bool Covered     ─┘ 转移时触发 IUIForm 回调
   ├── IObjectPool<UIFormInstanceObject> m_InstancePool
   │        └── UIFormInstanceObject (private sealed : ObjectBase)
   │              ├── Name = 资源名；Target = 实例（GameObject）
   │              └── Release(isShutdown) ⇒ IUIFormHelper.ReleaseUIForm(m_UIFormAsset, Target)
   ├── IUIFormHelper m_UIFormHelper：InstantiateUIForm(asset) / CreateUIForm(instance, group, userData) / ReleaseUIForm(asset, instance)
   ├── Queue<IUIForm> m_RecycleQueue（关闭后下一帧统一回池）
   ├── Dictionary<int,string> m_UIFormsBeingLoaded（serialId→资源名，加载中）
   ├── HashSet<int> m_UIFormsToReleaseOnLoad（加载中即被取消的 serialId）
   └── 5 个 C# event：OpenUIFormSuccess / OpenUIFormFailure / OpenUIFormUpdate /
                      OpenUIFormDependencyAsset / CloseUIFormComplete

══════════════ Unity 侧（UnityGameFramework.Runtime，UPM 包）══════════════

UIComponent : GameFrameworkComponent（sealed partial）
   ├── 组合 IUIManager；Inspector 序列化：事件开关×5、池参数×4、m_InstanceRoot、
   │   helper 类型名/自定义 helper×2、UIGroup[]（Name+Depth）
   ├── Awake：订阅 IUIManager 的 C# 事件 ──► 转成 Unity 侧 EventArgs ──► EventComponent.Fire(this, e)
   └── Start：SetResourceManager / SetObjectPoolManager / 池参数 / 创建 UIFormHelperBase /
              m_InstanceRoot / 逐个 AddUIGroup(name, depth)

UIForm : MonoBehaviour, IUIForm（框架壳层，纯转发 + try/catch 隔离异常）
   └── 持有 UIFormLogic m_UIFormLogic（游戏侧继承的抽象基类，11 个虚方法）
UIFormHelperBase : MonoBehaviour, IUIFormHelper ◄── DefaultUIFormHelper（Instantiate / SetParent+GetOrAddComponent<UIForm> / UnloadAsset+Destroy）
UIGroupHelperBase : MonoBehaviour, IUIGroupHelper ◄── DefaultUIGroupHelper（SetDepth 为空实现！）
                                                  └── StarForce UGuiGroupHelper（Canvas.sortingOrder = 10000 × depth）

══════════════ 游戏侧（StarForce 示例，uGUI 落地）══════════════

UGuiForm : UIFormLogic（abstract）
   ├── OnInit：GetOrAddComponent<Canvas>(overrideSorting=true) + CanvasGroup + GraphicRaycaster，记录 OriginalDepth
   ├── OnOpen / OnResume：CanvasGroup.alpha 0→1（FadeToAlpha 协程，0.3s）
   ├── Close(ignoreFade)：立即 CloseUIForm，或 FadeToAlpha(0) 协程结束后再 CloseUIForm
   └── OnDepthChanged：sortingOrder = 10000×组深 + 100×组内深，并对所有子 Canvas 平移 delta
UIExtension：FadeToAlpha(CanvasGroup) 协程扩展、按 UIFormId+DataTable 的 OpenUIForm/HasUIForm/GetUIForm 扩展
```

---

## (c) 关键类速览（每类 2~4 行）

### 核心侧（GameFramework/UI/）

| 类 | 职责与关键成员 |
|---|---|
| `UIManager`（internal sealed partial，UIManager.cs:18） | UI 模块唯一状态持有者。字段：`m_UIGroups`、`m_InstancePool`、`m_RecycleQueue`、`m_UIFormsBeingLoaded`、`m_UIFormsToReleaseOnLoad`、`m_LoadAssetCallbacks`、`m_Serial`、`m_IsShutdown`、5 个事件委托。`Update()` 只做两件事：清回收队列 + 逐组 Update（:211-224）。`Shutdown()` 置 `m_IsShutdown=true` 后 `CloseAllLoadedUIForms()`（:229-237）。 |
| `UIManager.UIGroup`（private sealed partial : IUIGroup，UIManager.UIGroup.cs:17） | 组 = 名称 + 深度 + 组级 `Pause` + 一个 `LinkedList<UIFormInfo>`（First 为栈顶）+ `IUIGroupHelper`。核心方法 `Refresh()`（:393-477）是全部遮挡/暂停/深度逻辑的唯一入口；`Update()`（:144-159）只对**未暂停**的表单（从栈顶到第一个 Paused 节点为止）调 `OnUpdate`。 |
| `UIManager.UIGroup.UIFormInfo`（IReference，UIManager.UIGroup.UIFormInfo.cs:17） | 每个表单在组内的记录：`UIForm` + `Paused` + `Covered` 两个 bool。`Create()` 故意把两个标志初始化为 **true/true**（:71-72），迫使下一次 `Refresh()` 把新栈顶"转出"遮挡态 → 立刻收到一次 `OnReveal`。用完 `ReferencePool.Release` 回收。 |
| `UIManager.UIFormInstanceObject`（: ObjectBase，UIManager.UIFormInstanceObject.cs:17） | 池对象：`Name`=资源名，`Target`=实例化出来的 GameObject。`Release(isShutdown)` ⇒ `m_UIFormHelper.ReleaseUIForm(m_UIFormAsset, Target)`（:56），即"真正销毁 UI 实例"的唯一路径。 |
| `UIManager.OpenUIFormInfo`（IReference，UIManager.OpenUIFormInfo.cs:12） | 一次打开请求的上下文（serialId、UIGroup、pauseCoveredUIForm、userData），作为 userData 传进资源加载回调，加载完成后释放。 |
| `IUIManager`（IUIManager.cs） | 完整公开 API：8 个 `OpenUIForm` 重载、4 个 `CloseUIForm` 重载、`CloseAllLoadedUIForms`、`CloseAllLoadingUIForms`、`RefocusUIForm`、组管理（`AddUIGroup`×2/`HasUIGroup`/`GetUIGroup`）、查询（`HasUIForm`/`GetUIForm(s)`/`GetAllLoadedUIForms`/`IsLoadingUIForm`×2/`GetAllLoadingUIFormSerialIds`）、池参数 4 属性、`SetUIFormInstanceLocked/Priority`、5 个事件、3 个 Set 注入方法。 |
| `IUIForm`（IUIForm.cs:13） | 表单抽象：`SerialId`、`UIFormAssetName`、`Handle`（实例 object）、`UIGroup`、`DepthInUIGroup`、`PauseCoveredUIForm` + 11 个生命周期方法（OnInit/OnRecycle/OnOpen/OnClose/OnPause/OnResume/OnCover/OnReveal/OnRefocus/OnUpdate/OnDepthChanged）。 |
| `IUIGroup`（IUIGroup.cs） | `Name`、`Depth`（**可写**，setter 触发 helper.SetDepth+Refresh）、`Pause`（可写）、`UIFormCount`、`CurrentUIForm`、`Helper` + 查询方法。 |
| `IUIFormHelper`（IUIFormHelper.cs:13） | 3 个方法把"实例化/包装/销毁"全部交给实现方：`object InstantiateUIForm(object uiFormAsset)`、`IUIForm CreateUIForm(object uiFormInstance, IUIGroup uiGroup, object userData)`、`void ReleaseUIForm(object uiFormAsset, object uiFormInstance)`。 |
| `IUIGroupHelper`（IUIGroupHelper.cs:13） | 仅 `void SetDepth(int depth)`——组深度到渲染层的唯一出口。 |
| 5 个 EventArgs | 均继承 `GameFrameworkEventArgs`，`static Create(...)` 从 `ReferencePool.Acquire` 取实例，`Clear()` 清字段。核心字段：Success{UIForm, Duration, UserData}；Failure{SerialId, UIFormAssetName, UIGroupName, PauseCoveredUIForm, ErrorMessage, UserData}；Update{…, Progress}；DependencyAsset{…, DependencyAssetName, LoadedCount, TotalCount}；CloseComplete{SerialId, UIFormAssetName, UIGroup, UserData}。 |

### Unity 侧（UnityGameFramework Runtime/Scripts/Runtime/UI/）

| 类 | 职责与关键成员 |
|---|---|
| `UIComponent`（: GameFrameworkComponent，UIComponent.cs:22） | Unity 门面。序列化字段：5 个事件开关（默认 Success/Failure/CloseComplete 开，Update/DependencyAsset 关）、池参数 `m_InstanceAutoReleaseInterval=60f / m_InstanceCapacity=16 / m_InstanceExpireTime=60f / m_InstancePriority=0`、`m_InstanceRoot`、helper 类型名与自定义 helper（UIForm/UIGroup 各一对）、`m_UIGroups[]`。Awake 订阅 core 事件并桥接；Start 组装一切；业务 API 全部一行转发给 `m_UIManager`。 |
| `UIComponent.UIGroup`（UIComponent.UIGroup.cs:16） | 纯 Inspector 数据类：`string m_Name` + `int m_Depth`，供面板上预配置界面组。 |
| `UIForm`（MonoBehaviour, IUIForm，UIForm.cs:17） | 壳层：保存 `SerialId/UIFormAssetName/UIGroup/DepthInUIGroup/PauseCoveredUIForm`，把 11 个生命周期调用 try/catch 后转发给 `m_UIFormLogic`（异常只 Log.Error 不上抛）。`OnInit` **仅 isNewInstance 时** `GetComponent<UIFormLogic>()` 并调 `Logic.OnInit(userData)`（:120-130）；`OnRecycle` 清 `m_SerialId=0, m_DepthInUIGroup=0, m_PauseCoveredUIForm=true`（:156-158）。`Handle` 即 `gameObject`（:55）。 |
| `UIFormLogic`（abstract MonoBehaviour，UIFormLogic.cs:15） | 游戏逻辑基类。属性 `Available`、`Visible`（setter 经 `InternalSetVisible`，默认 `gameObject.SetActive(visible)`，:202-205）、`CachedTransform`。11 个 `protected internal virtual` 生命周期方法 + `InternalSetVisible`。基类默认行为：OnOpen 置 Available+Visible=true；OnClose 恢复原 Layer+Visible=false+Available=false；OnPause→Visible=false；OnResume→Visible=true；OnCover/OnReveal/OnRefocus/OnUpdate/OnDepthChanged 为空。 |
| `UIFormHelperBase` / `UIGroupHelperBase` | 两个 MonoBehaviour 抽象基类，分别实现 `IUIFormHelper` / `IUIGroupHelper`。 |
| `DefaultUIFormHelper`（DefaultUIFormHelper.cs:16） | `InstantiateUIForm` = `Instantiate((Object)uiFormAsset)`（无父节点，:27）；`CreateUIForm` = `SetParent(((MonoBehaviour)uiGroup.Helper).transform)` + `localScale=one` + `gameObject.GetOrAddComponent<UIForm>()`（:50）；`ReleaseUIForm` = `m_ResourceComponent.UnloadAsset(uiFormAsset); Destroy((Object)uiFormInstance)`（:60-61）。 |
| `DefaultUIGroupHelper`（DefaultUIGroupHelper.cs:19-21） | `SetDepth(int depth)` 为**空实现**——官方包开箱不带 UGUI 层级管理，必须自己实现（StarForce 给了范本）。 |
| `UIIntKey` / `UIStringKey` | 两个只含 `Key` 字段的 MonoBehaviour。UI 模块内（UIManager/UIForm/UIComponent）**无任何引用**，属于预留/未完成的按 Key 检索机制，可忽略。 |
| Unity 侧 EventArgs×4 | 与 core 同名类，字段把 `IUIForm` 换成具体 `UIForm`，并新增 `public static readonly int EventId = typeof(T).GetHashCode();` 与 `override int Id`（OpenUIFormSuccessEventArgs.cs:21,36-42）；`Create(GameFramework.UI.XxxEventArgs e)` 负责类型转换。 |

### 游戏侧（StarForce）

| 类 | 职责与关键成员 |
|---|---|
| `UGuiForm`（abstract : UIFormLogic，UGuiForm.cs:16） | uGUI 落地范本：`DepthFactor=100`、`FadeTime=0.3f`；OnInit 装 Canvas(overrideSorting)+CanvasGroup+GraphicRaycaster、RectTransform 满屏、Text 换字体+本地化；OnOpen/OnResume 播 alpha 淡入；`Close(ignoreFade)` 两段式关闭；OnDepthChanged 全子 Canvas 平移（详见 §5、§6.2）。 |
| `UGuiGroupHelper`（: UIGroupHelperBase，UGuiGroupHelper.cs:17） | 每组一个 GameObject（挂 Canvas+GraphicRaycaster）；`SetDepth` = `overrideSorting=true; m_CachedCanvas.sortingOrder = DepthFactor(10000) * depth`（:28-33）。 |
| `UIExtension`（UIExtension.cs） | `FadeToAlpha(this CanvasGroup, float, float)` 协程插值（:19-31）；`OpenUIForm(this UIComponent, int uiFormId, object userData)`：查 DRUIForm 数据表 → 拼资源名 → 非多例时先查 `IsLoadingUIForm/HasUIForm` 防重 → `OpenUIForm(assetName, drUIForm.UIGroupName, Constant.AssetPriority.UIFormAsset, drUIForm.PauseCoveredUIForm, userData)`（:128-153）。 |
| `UIFormId` | `enum UIFormId : byte`（DialogForm=1, MenuForm=100…）+ 数据表 `DRUIForm`（资源名/组名/优先级/PauseCoveredUIForm/AllowMultiInstance），把"打开哪个界面"配置化。 |

---

## (b) OpenUIForm 完整时序（编号步骤）

入口：`UIComponent.OpenUIForm(string uiFormAssetName, string uiGroupName, int priority, bool pauseCoveredUIForm, object userData)`（8 个重载，缺省 `DefaultPriority=0`，UIComponent.cs:582-585）→ 一行转发 core。

`UIManager.OpenUIForm`（UIManager.cs:723-764）：

1. **前置校验**：`m_ResourceManager == null` / `m_UIFormHelper == null` 抛异常（"You must set ... first"）；资源名/组名非空校验；`(UIGroup)GetUIGroup(uiGroupName)` 取组，**组不存在直接抛异常，不自动创建**（:745-749）。
2. **生成序列号**：`int serialId = ++m_Serial;`（int，从 1 起单调递增，永不回收；UIForm.OnRecycle 时壳层清零）。
3. **查实例池**：`UIFormInstanceObject uiFormInstanceObject = m_InstancePool.Spawn(uiFormAssetName);` —— 池按**资源名**查找空闲实例（AllowMultiSpawn=false，同名实例同时只服务一个界面）。
4. **A. 池未命中（异步路径）**：
   - `m_UIFormsBeingLoaded.Add(serialId, uiFormAssetName);`
   - `m_ResourceManager.LoadAsset(uiFormAssetName, priority, m_LoadAssetCallbacks, OpenUIFormInfo.Create(serialId, uiGroup, pauseCoveredUIForm, userData));`
   - `LoadAssetCallbacks` 四个回调在 UIManager 内部组装（:46）：Success / Failure / Update / DependencyAsset。
   - `return serialId;`（此时界面还没影）。
5. **A-成功回调 `LoadAssetSuccessCallback`**（:976-998）：
   - 若 `m_UIFormsToReleaseOnLoad.Contains(serialId)`（加载期间被 Close/CloseAllLoadingUIForms 取消）：摘标记 → `ReferencePool.Release(openUIFormInfo)` → `m_UIFormHelper.ReleaseUIForm(uiFormAsset, null)`（**只卸资源，没有实例可销毁**）→ return。
   - 否则：`m_UIFormsBeingLoaded.Remove(serialId);` → **实例化交给 helper**：`UIFormInstanceObject.Create(assetName, asset, m_UIFormHelper.InstantiateUIForm(asset), m_UIFormHelper)` → `m_InstancePool.Register(uiFormInstanceObject, true)`（spawned=true 占用中；Register 内部若 `Count > Capacity` 立即触发一次 `Release()`，ObjectPoolManager.ObjectPool.cs:202-205）→ `InternalOpenUIForm(..., isNewInstance:true, duration, userData)` → `ReferencePool.Release(openUIFormInfo)`。
6. **B. 池命中（同步路径）**：直接 `InternalOpenUIForm(serialId, assetName, group, uiFormInstanceObject.Target, pauseCoveredUIForm, /*isNewInstance*/false, 0f, userData)`（:760）。
7. **`InternalOpenUIForm`**（:940-974），整体 try/catch：
   1. `IUIForm uiForm = m_UIFormHelper.CreateUIForm(uiFormInstance, uiGroup, userData);` —— DefaultUIFormHelper：挂到组 helper 节点下 + `GetOrAddComponent<UIForm>()`。
   2. `uiForm.OnInit(serialId, uiFormAssetName, uiGroup, pauseCoveredUIForm, isNewInstance, userData);` —— UIForm 记录字段；**仅 isNewInstance=true 时**才 `GetComponent<UIFormLogic>()` + `Logic.OnInit(userData)`（复用实例跳过逻辑层初始化，UIForm.cs:120-123）。
   3. `uiGroup.AddUIForm(uiForm);` —— `m_UIFormInfos.AddFirst(UIFormInfo.Create(uiForm))`：新表单进栈顶，UIFormInfo 初始 Paused=Covered=true。
   4. `uiForm.OnOpen(userData);` —— UIFormLogic.OnOpen：`m_Available=true; Visible=true;`（`InternalSetVisible` ⇒ `SetActive(true)`）。
   5. `uiGroup.Refresh();` —— 全组重算深度/遮挡/暂停（见 §5）：新栈顶此刻收到 `OnReveal`。
   6. 若订阅了 `OpenUIFormSuccess`：`OpenUIFormSuccessEventArgs.Create(uiForm, duration, userData)` → `handler(this, e)` → `ReferencePool.Release(e)`。事件是**同步**触发的。
   7. catch：fire `OpenUIFormFailureEventArgs`（ErrorMessage=`exception.ToString()`）；**若无人订阅该事件则 rethrow**（:964-973）。
8. **失败回调 `LoadAssetFailureCallback`**（:1000-1025）：同样处理"加载中取消"；否则 fire `OpenUIFormFailureEventArgs`（含 LoadResourceStatus），无人订阅时抛 `GameFrameworkException`。
9. 进度/依赖回调（:1027-1057）分别转发 `OpenUIFormUpdateEventArgs{Progress}` / `OpenUIFormDependencyAssetEventArgs{DependencyAssetName, LoadedCount, TotalCount}`（默认关闭，见 §8）。

> 资源加载抽象：core 只依赖 `IResourceManager.LoadAsset` 的 8 个重载（IResourceManager.cs:521-582，均为 `void LoadAsset(string assetName, ...)`，异步回调式）。UnityGameFramework 的 `ResourceComponent`（打包模式）或 `BaseComponent.EditorResourceHelper`（编辑器模式）都实现该接口，因此换 Resources/Addressables/YooAsset 只需再写一个 `IResourceManager`。**core 不出现任何 UnityEngine/Resources/AssetBundle 字样。**

---

## 3. ObjectPool<UIFormInstanceObject>：实例复用细节

- **池的创建**：`SetObjectPoolManager()` 里 `m_ObjectPoolManager.CreateSingleSpawnObjectPool<UIFormInstanceObject>("UI Instance Pool")`（UIManager.cs:251）——单获取池（SingleSpawn：同名对象同时只能 spawn 给一个使用者），池内数据结构是 `MultiDictionary<string /*资源名*/, Object<UIFormInstanceObject>>` + `Dictionary<object /*Target*/, Object<T>>`（ObjectPoolManager.ObjectPool.cs:21-22）。
- **参数与默认值**（UIComponent Inspector → Start 注入，UIComponent.cs:210-213）：`AutoReleaseInterval=60s`、`Capacity=16`、`ExpireTime=60s`、`Priority=0`。core 侧同名四属性透传（UIManager.cs:74-129）。
- **何时"回池"（复用开始可用）**：**不在 CloseUIForm 里**。Close 只把 IUIForm 塞进 `m_RecycleQueue`；下一次 `UIManager.Update` 开头统一处理：
  ```csharp
  while (m_RecycleQueue.Count > 0)
  {
      IUIForm uiForm = m_RecycleQueue.Dequeue();
      uiForm.OnRecycle();                    // → UIFormLogic.OnRecycle()，并重置壳层字段
      m_InstancePool.Unspawn(uiForm.Handle); // Handle = gameObject
  }
  ```
  （UIManager.cs:213-218）`Unspawn(target)` 按 m_ObjectMap 找到包装对象，`SpawnCount--`、`LastUseTime=UtcNow`、`OnUnspawn()`；若此时 `Count > Capacity` 且该对象空闲则立刻尝试 Release（ObjectPool.cs:298-318）。
- **何时"真正销毁"**：对象被池 Release 时（唯一路径）：`ObjectBase.Release(isShutdown)` → `UIFormInstanceObject.Release` → `m_UIFormHelper.ReleaseUIForm(m_UIFormAsset, Target)` → `m_ResourceComponent.UnloadAsset(asset); Destroy(gameObject)`。触发 Release 的四种途径：
  1. **定时**：`ObjectPool.Update` 累加 `realElapseSeconds`，达到 `AutoReleaseInterval(60s)` 执行一次 `Release()`（ObjectPool.cs:536-545）。
  2. **容量**：`Register`/`Unspawn` 后 `Count > Capacity(16)` → `Release()`。
  3. **显式**：`IObjectPool.ReleaseObject(obj/target)`（UIComponent 未封装此 API，需拿池对象操作）。
  4. **Shutdown**：`ObjectPool.Shutdown()` 对全部对象 `Release(true)`（ObjectPool.cs:547-559）。
- **Release 的挑选算法**（`DefaultReleaseObjectFilterCallback`，ObjectPool.cs:597-634）：候选 = 未使用 && 未加锁 && `CustomCanReleaseFlag`；**先释放 `LastUseTime ≤ now − ExpireTime(60s)` 的过期对象**（无论容量）；剩余按 `Priority 升序、同优先级 LastUseTime 新→旧` 排序（先交出低优先级、更久未用的），取 `Count − Capacity` 个。
- **保活手段**：`UIComponent.SetUIFormInstanceLocked(uiForm.gameObject, true)`（常驻 UI 防释放）、`SetUIFormInstancePriority`（调释放顺序）——底层就是池的 `SetLocked/SetPriority`（UIManager.cs:915-938）。

---

## 4. UIFormInfo 状态机

**没有显式状态机**：不存在 `WillOpen/Open/WillClose/Closed` 之类的枚举（全仓库可证实）。状态就是 `UIFormInfo` 上的两个 bool，**状态转移与通知调用被合并在一起**，全部由两处代码驱动：

| 转移 | 触发方法 | 伴随回调 |
|---|---|---|
| `Covered: true→false` | `UIGroup.Refresh()` | `IUIForm.OnReveal()` |
| `Covered: false→true` | `UIGroup.Refresh()` 或 `RemoveUIForm()` | `IUIForm.OnCover()` |
| `Paused: true→false` | `UIGroup.Refresh()` | `IUIForm.OnResume()` |
| `Paused: false→true` | `UIGroup.Refresh()` 或 `RemoveUIForm()` | `IUIForm.OnPause()` |

- `Create()` 初始 `Paused=true, Covered=true`（UIFormInfo.cs:71-72）→ 新表单必然先被 `Refresh()` 转出，栈顶必然收到一次 `OnReveal`（配合第 7 步的 `OnOpen`，形成 OnInit→OnOpen→OnReveal 的固定首帧序列）。
- `Refresh()` 里 **`OnDepthChanged(Depth, depth--)` 对每个表单无条件调用**（不看状态是否变化），其余 4 个回调只在标志翻转时触发（幂等）。
- **Update 循环对它做什么**：`UIManager.Update` → 每组 `UIGroup.Update`：从 `First` 起逐个 `current.Value.UIForm.OnUpdate(elapseSeconds, realElapseSeconds)`，**遇到第一个 `Paused` 节点就 `break`**（UIGroup.cs:144-159）——即：被暂停的表单收不到 OnUpdate；"仅被遮挡但未暂停"的表单**仍然**每帧收到 OnUpdate。迭代中用 `m_CachedNode` 缓存 next，`RemoveUIForm` 检测并修正该缓存（UIGroup.cs:360-363），保证回调里关 UI 不会打断迭代。

---

## 5. UIGroup：深度换算与遮挡/暂停判定（(d) 原文级转述）

### 5.1 组内栈与刷新入口
`AddUIForm` 永远 `AddFirst`（新表单=栈顶）；`CurrentUIForm` = `First.Value.UIForm`。任何增删/激活/深度/组暂停变化后都调用 `UIGroup.Refresh()` 重算全组。

### 5.2 Refresh() 精确转述（UIGroup.cs:393-477，摘录主干）

```csharp
LinkedListNode<UIFormInfo> current = m_UIFormInfos.First;
bool pause = m_Pause;        // 组级暂停（IUIGroup.Pause）作为初值
bool cover = false;          // "暴露区"还没确定
int  depth = UIFormCount;
while (current != null && current.Value != null)
{
    current.Value.UIForm.OnDepthChanged(Depth, depth--);   // ① 组内深度：栈顶最大，向下递减
    if (pause)
    {
        // 暂停区：全部 进入遮挡 + 进入暂停
        if (!current.Value.Covered) { current.Value.Covered = true;  current.Value.UIForm.OnCover(); }
        if (!current.Value.Paused)  { current.Value.Paused  = true;  current.Value.UIForm.OnPause(); }
    }
    else
    {
        if (current.Value.Paused) { current.Value.Paused = false; current.Value.UIForm.OnResume(); }
        if (current.Value.UIForm.PauseCoveredUIForm) { pause = true; }   // ★ 见下
        if (cover) { if (!current.Value.Covered) { current.Value.Covered = true;  current.Value.UIForm.OnCover(); } }
        else       { if (current.Value.Covered)  { current.Value.Covered = false; current.Value.UIForm.OnReveal(); }
                     cover = true; }
    }
    current = next;
}
```

**判定规则总结：**
1. 每组**恰好一个"暴露"表单**：栈顶第一个（`cover` 只在第一次进入 else 分支后置 true）。它从 Covered=true（UIFormInfo 初值或之前被盖）翻转为 false → `OnReveal()`。
2. 它下面的所有表单：`Covered=true → OnCover()`——**保持可见**（基类 OnCover 不改 Visible）、仍收 OnUpdate，但不再算"当前露出"。
3. **暂停由"更上层"的表单决定，与"全屏"无关**：`PauseCoveredUIForm` 是打开界面时的入参（存在 `IUIForm.PauseCoveredUIForm` 上，UIForm.cs:118）。Refresh 自栈顶向下扫描，扫到某个表单的该标志为 true 时置 `pause=true`，**它以下的所有表单**（包括本来只 OnCover 的）额外 `OnPause()`；基类 `OnPause` = `Visible=false` ⇒ `SetActive(false)`。换句话说：弹一个 pauseCoveredUIForm=true 的界面 = "我之下全部暂停并隐藏"；false = "我之下只标记遮挡，照常运行可见"。是否全屏是资源/业务的事，框架只认这个 bool。
4. 组级 `UIGroup.Pause = true`（`IUIGroup.Pause`）时，全组所有表单 OnCover+OnPause。
5. 每个表单的组内深度：栈顶 = `UIFormCount`（最大），向下逐个减 1；`OnDepthChanged(uiGroupDepth=组Depth, depthInUIGroup)`。
6. 回调里关 UI 是安全的：每次触发后都检查 `current.Value == null`（UIFormInfo 被 RemoveUIForm → ReferencePool.Release → Clear 后 UIForm 为 null）立即退出（:414-417 等多处）。

### 5.3 Depth → Canvas.sortingOrder（StarForce 实现）

- **组级**：`UGuiGroupHelper`（UGuiGroupHelper.cs:19-33）——`DepthFactor = 10000`；Awake `GetOrAddComponent<Canvas>() + GraphicRaycaster`；`SetDepth(depth)` ⇒ `m_CachedCanvas.overrideSorting = true; m_CachedCanvas.sortingOrder = DepthFactor * depth;`。UIComponent.AddUIGroup 为每组创建独立 GameObject（"UI Group - {组名}"，挂 `m_InstanceRoot` 下）⇒ **一组一个 Canvas**。
- **表单级**：`UGuiForm.OnDepthChanged`（UGuiForm.cs:197-213）：
  ```csharp
  int deltaDepth = UGuiGroupHelper.DepthFactor * uiGroupDepth + DepthFactor * depthInUIGroup - oldDepth + OriginalDepth;
  GetComponentsInChildren(true, m_CachedCanvasContainer);
  for (...) m_CachedCanvasContainer[i].sortingOrder += deltaDepth;
  ```
  即自身 Canvas 目标值 = `10000×组深 + 100×组内深`；用增量 delta 对**所有子 Canvas**（含 prefab 里美术自建的嵌套 Canvas，其相对偏移被保留）整体平移；`OriginalDepth` 是 OnInit 时记录的 prefab 初始 sortingOrder。`UGuiForm.DepthFactor = 100` 限制组内最多 100 层不串号。
- master 的 `DefaultUIGroupHelper.SetDepth` 是空方法——官方把层管理完全留给游戏侧。

---

## 6. UIFormLogic：虚方法全表与调用时机

### 6.1 11 个虚方法（UIFormLogic.cs，全部 `protected internal virtual`）

| 方法签名 | 首次/触发时机 | 基类默认行为 |
|---|---|---|
| `OnInit(object userData)` | 每个新实例第一次 InternalOpenUIForm；**池复用不再调用** | 缓存 transform、`GetComponent<UIForm>()`、记录 `m_OriginalLayer` |
| `OnRecycle()` | 关闭后的**下一帧** UIManager.Update 处理 RecycleQueue 时 | 空 |
| `OnOpen(object userData)` | OnInit + AddUIForm 之后（每帧打开都会调） | `m_Available=true; Visible=true;` |
| `OnClose(bool isShutdown, object userData)` | CloseUIForm 内**同步立即**；isShutdown=关整个 UI 管理器 | `gameObject.SetLayerRecursively(m_OriginalLayer); Visible=false; m_Available=false;` |
| `OnPause()` | Refresh 判定进入暂停区 / RemoveUIForm 补发 | `Visible=false` |
| `OnResume()` | Refresh 判定脱离暂停区 | `Visible=true` |
| `OnCover()` | Refresh 判定被遮挡 / RemoveUIForm 补发 | 空（不隐藏！） |
| `OnReveal()` | Refresh 判定从遮挡恢复（含新表单首帧） | 空 |
| `OnRefocus(object userData)` | RefocusUIForm：移到组顶 + Refresh 之后 | 空 |
| `OnUpdate(float elapseSeconds, float realElapseSeconds)` | 每帧 UIGroup.Update（栈顶→第一个 Paused 之前的区间） | 空 |
| `OnDepthChanged(int uiGroupDepth, int depthInUIGroup)` | 每次 Refresh 对每个表单无条件调用 | 空 |

`Visible` 属性（:63-85）：get = `m_Available && m_Visible`；set 在不可用时告警并**忽略**；仅变化时调 `InternalSetVisible`（默认 `SetActive`）。所有调用经壳层 `UIForm`（MonoBehaviour）try/catch 转发，逻辑层异常只 `Log.Error` 不打断框架。

### 6.2 UGuiForm 的显隐动画（"ColorTween"的真相）

- 淡入：`OnOpen` / `OnResume` 里 `m_CanvasGroup.alpha = 0f; StopAllCoroutines(); StartCoroutine(m_CanvasGroup.FadeToAlpha(1f, FadeTime));`（UGuiForm.cs:125-128, 156-158）。
- `FadeToAlpha` 是 `UIExtension` 里的协程扩展：每帧 `Mathf.Lerp` 后 `WaitForEndOfFrame`（UIExtension.cs:19-31）。**没有任何 Tween 库，也没有 ColorTween**。
- 淡出关闭：`public void Close(bool ignoreFade)`（UGuiForm.cs:45-57）——`StopAllCoroutines()` 后要么立即 `GameEntry.UI.CloseUIForm(this)`，要么 `StartCoroutine(CloseCo(FadeTime))`：先 `FadeToAlpha(0f, 0.3f)`，协程结束才 `CloseUIForm`。**框架本身不知道关闭动画的存在**——淡出是游戏侧延迟调用框架的瞬时 Close。注意淡出期间表单仍在组内，可能再次收到 OnPause/OnCover 等回调。

---

## 7. CloseUIForm / CloseAll / Refocus / 组深度调节调用链

- **按 serialId**：`UIManager.CloseUIForm(int serialId, object userData)`（:780-796）：
  1. 若 `IsLoadingUIForm(serialId)`：`m_UIFormsToReleaseOnLoad.Add(serialId); m_UIFormsBeingLoaded.Remove(serialId); return;` —— 作废进行中的打开（资源加载完成后在 Success 回调里被丢弃）。
  2. `GetUIForm(serialId)`（跨组线性查找，找不到抛异常）→ 走下一条。
- **按实例** `CloseUIForm(IUIForm, userData)`（:812-837）：
  1. `uiGroup.RemoveUIForm(uiForm)`（UIGroup.cs:340-371）：找不到 UIFormInfo 抛异常；若 `!Covered` → `Covered=true; OnCover();`；若 `!Paused` → `Paused=true; OnPause();`（强制收尾两个状态）；修正 `m_CachedNode`；从链表移除；`ReferencePool.Release(uiFormInfo)`。
  2. `uiForm.OnClose(m_IsShutdown, userData)` —— 同步，Visible=false。
  3. `uiGroup.Refresh()` —— 新栈顶 OnReveal/OnResume。
  4. fire `CloseUIFormCompleteEventArgs`（**同步**；仅有人订阅时才创建）。
  5. `m_RecycleQueue.Enqueue(uiForm)` —— 回池延到下一帧。
- **CloseAllLoadedUIForms(userData)**（:851-863）：先 `GetAllLoadedUIForms()` 取快照，逐个检查 `HasUIForm(uiForm.SerialId)`（前一个的关闭回调可能已把后面的关掉）再 Close。`Shutdown()` 也走它（此时 `m_IsShutdown=true` 会传入 OnClose）。
- **CloseAllLoadingUIForms()**（:868-876）：把所有加载中 serialId 移入 `m_UIFormsToReleaseOnLoad` 并清空 BeingLoaded。
- **RefocusUIForm(uiForm, userData)**（:892-908）：`uiGroup.RefocusUIForm`（链表内 Remove+AddFirst 置顶，UIGroup.cs:378-388）→ `uiGroup.Refresh()` → `uiForm.OnRefocus(userData)`。
- **组深度调节**：core `UIGroup.Depth` setter（UIGroup.cs:66-83）→ `m_UIGroupHelper.SetDepth(m_Depth); Refresh();`。UIComponent 没有单独封装改深度的方法，但 `IUIGroup.Depth` 可写：`(GameEntry.UI.GetUIGroup("Dialog") as IUIGroup).Depth = 5;` 即生效（换算 sortingOrder + 全组 OnDepthChanged）。

---

## 8. 事件机制（完整链路）

```
core UIManager（C# event，同步直调）
  m_OpenUIFormSuccessEventHandler(this, OpenUIFormSuccessEventArgs.Create(uiForm, duration, userData));
  ReferencePool.Release(e);                       // core 参数用后即还池
        │  (UIComponent.Awake 里 += 订阅；OpenUIFormFailure 恒订阅用于 Log.Warning，其余 4 个按 Inspector 开关)
        ▼
UIComponent.OnOpenUIFormSuccess(object sender, GameFramework.UI.OpenUIFormSuccessEventArgs e)
  m_EventComponent.Fire(this, UnityGameFramework.Runtime.OpenUIFormSuccessEventArgs.Create(e));
        │  (类型转换：IUIForm → UIForm；Unity 侧 args 自带 EventId = typeof(T).GetHashCode()，override int Id)
        ▼
EventComponent.Fire → IEventManager（EventPool<GameEventArgs>，mode = AllowNoHandler | AllowMultiHandler）
  Fire: ReferencePool 取 Event 节点入队（lock，线程安全，下一帧分发）
  EventManager.Update → EventPool.Update → HandleEvent(sender, e)：
      按 e.Id 在 MultiDictionary<int, EventHandler<GameEventArgs>> 找订阅者依次调用（迭代中可安全 Unsubscribe）
      最后 ReferencePool.Release(e)                // ★ 分发完参数强制回池：handler 内不得跨帧持有引用
      无订阅者 → 默认 handler（BaseComponent.SetDefaultHandler，通常打日志）
```

- **谁监听**：框架内只有 UIComponent 自己（做桥接）；游戏侧统一 `GameEntry.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenUIFormSuccess)`（StarForce 的 Procedure 组件即此用法）。UIFormLogic 内更常用的是 `userData` 直传 + 委托回调（如 `DialogParams.OnClickConfirm`），绕开事件系统。
- 事件名与载荷：Success{UIForm, Duration, UserData}（打开完成，同帧同步）；Failure{SerialId, UIGroupName, ErrorMessage, …}；Update{Progress}（默认关）；DependencyAsset{DependencyAssetName, LoadedCount, TotalCount}（默认关）；CloseComplete{SerialId, UIFormAssetName, UIGroup, UserData}（关闭同步完成时）。开关字段：`m_EnableOpenUIFormSuccessEvent=true / Failure=true / Update=false / DependencyAsset=false / CloseComplete=true`（UIComponent.cs:32-44）。
- 注意 core 侧失败事件的特殊语义：**无人订阅 OpenUIFormFailure 时异常原样 rethrow**（UIManager.cs:964-973, 1016-1024），有人订阅则吞掉——接 EventComponent 后恒有人订阅，所以游戏侧总能靠事件兜底。

---

## 9. Unity 侧封装补充（UIComponent / DefaultUIFormHelper）

- **Start 组装顺序**（UIComponent.cs:184-246）：取 BaseComponent/EventComponent → 编辑器资源模式注入 `baseComponent.EditorResourceHelper`（实现 IResourceManager），否则注入 `GameFrameworkEntry.GetModule<IResourceManager>()` → 注入 IObjectPoolManager → 应用 4 个池参数 → `Helper.CreateHelper(m_UIFormHelperTypeName, m_CustomUIFormHelper)`（按类型名反射创建或克隆自定义 helper 的 GameObject，命名为 "UI Form Helper" 挂在 UIComponent 节点下）→ 注入 IUIFormHelper → `m_InstanceRoot` 缺省创建 "UI Form Instances"（UI 层）→ 遍历 Inspector `m_UIGroups[]` 调 `AddUIGroup(name, depth)`。
- **AddUIGroup(name, depth)**（:302-323）：每组 `Helper.CreateHelper(m_UIGroupHelperTypeName, m_CustomUIGroupHelper, UIGroupCount)` 造一个组节点（"UI Group - {name}"，UI 层，挂在 m_InstanceRoot 下）→ `m_UIManager.AddUIGroup(name, depth, helper)`。
- **"是否繁忙/正在加载"**：`IsLoadingUIForm(serialId | assetName)`（后者实现是 `m_UIFormsBeingLoaded.ContainsValue(...)`，O(n)）、`GetAllLoadingUIFormSerialIds()`；没有"是否正在关闭"的概念。
- **实例化与销毁**（DefaultUIFormHelper）：`InstantiateUIForm` 纯 `Instantiate`（此时无父节点、未激活无所谓）；`CreateUIForm` 挂到组节点 + `GetOrAddComponent<UIForm>()`；`ReleaseUIForm` = `ResourceComponent.UnloadAsset(资源) + Destroy(实例)`——**资源卸载与 GameObject 销毁都由 helper 一手包办**，core 只负责时机。
- **约束**：界面 prefab 根节点必须挂一个 `UIFormLogic` 派生组件（复用时沿用首次 `GetComponent<UIFormLogic>` 的结果，找不到只 Log.Error，之后所有调用静默失败）。

---

## 10. 局限与常见吐槽（逐条给出源码依据）

1. **打开异步但返回值只能当句柄**：`OpenUIForm` 返回 serialId，界面本身要等 Success 事件；没有"打开完成回调"直通，必须走事件或轮询 `IsLoadingUIForm`。
2. **关闭瞬时、CloseUIFormComplete 名不副实**：OnClose 后同帧同步触发"Complete"事件；真正回池在下一帧。没有框架级关闭过渡，淡出要像 StarForce 那样游戏侧协程延迟调 Close（期间表单仍在组内，可能再收 OnPause/OnCover）。
3. **无预加载 API**：没有 PreloadUIForm。想预热只能自行组合 `IResourceManager.LoadAsset + helper.InstantiateUIForm + pool.Register`，或"打开后立刻关"。
4. **无导航栈**：只有组内"栈顶=当前"，没有跨页面返回栈/历史；`RefocusUIForm` 只是重新置顶。
5. **四套回调风格并存**：core C# event / Unity 侧 int-Id 事件 / userData 直传 / StarForce 的 DialogParams 委托，心智负担大；且事件参数被 ReferencePool 复用，**handler 里把 args 存起来跨帧用必踩脏数据**（EventPool.HandleEvent 分发完即 `ReferencePool.Release(e)`）。
6. **Refresh 全量刷**：每次打开/关闭/Refocus/改组深度都会对全组表单**无条件**调 `OnDepthChanged`（即使组内深度没变），组大时有浪费。
7. **查找线性**：`GetUIForm(serialId)` 跨组逐组逐表单找；`IsLoadingUIForm(assetName)` 用 `ContainsValue`。规模小无所谓，量大要自建索引。
8. **暂停语义固定**："上层 pauseCoveredUIForm=true ⇒ 下层全部 OnPause+隐藏"。想实现"下层可见但暂停"（半透明弹窗+模糊背景）必须覆写 `OnPause`/`InternalSetVisible` 与基类默认行为对抗。
9. **关闭进行中的打开只能作废**：连点打开+关闭，资源照样加载完成才在 Success 回调里被丢弃（好在 `ReleaseUIForm(asset, null)` 只卸资源不实例化，浪费可控）。
10. **UIIntKey/UIStringKey 是半成品**：UI 模块核心代码零引用，纯摆设。
11. **DefaultUIGroupHelper.SetDepth 空实现**：官方包开箱不支持 UGUI 层级；层级方案（每组 Canvas + DepthFactor 10000/100）整体下放到 StarForce，教程与仓库不一致的混乱源头。
12. **吞异常策略**：壳层 UIForm 对 11 个回调 try/catch + Log.Error，逻辑层异常不会传播（好处是框架稳，坏处是错误易被淹没）。
13. **serialId 为 int 自增不复用**：理论溢出风险极低，但 UIForm.OnRecycle 清零 serialId 后再拿旧引用查询会失效——跨帧持有 IUIForm 引用时要用 `IsValidUIForm` 校验。
14. **`foreach Dictionary` 遍历组**（UIManager.Update）：组间 Update 顺序不确定（无功能影响，但别依赖顺序）。

---

## (e) 设计启示（仿写建议）

### 值得照抄的决策
1. **三依赖接口注入**：`SetResourceManager / SetObjectPoolManager / SetUIFormHelper` 让核心 UI 模块对"资源从哪来、实例怎么造"零感知。仿写时保持这条铁律，换 Addressables/YooAsset 不动 UI 代码。
2. **serialId + 池按资源名复用实例**（`Spawn(assetName)` / `Unspawn(Handle)`）：高频界面零实例化；`UIFormInstanceObject{Name=资源名, Target=GameObject, Release=helper销毁}` 的三元组把"复用"与"销毁"职责分干净。
3. **RecycleQueue 延迟一帧回池**：Close 的调用栈（OnClose、事件、业务回调）里不会出现"对象刚被回池又被拿走"的诡异状态。
4. **两 bool + 单点 Refresh 的状态推导**：`Paused/Covered` 存在 UIFormInfo，全部显隐/暂停由 `UIGroup.Refresh()` 一次遍历推导、只在翻转时回调——比到处手写 Show/Hide 可维护一个数量级。`Create` 初值 true/true 逼出首帧 OnReveal 的小技巧值得保留（但要写进文档）。
5. **pauseCoveredUIForm 打开参数**：用"打开动作"声明遮挡语义，简单、可配置（StarForce 放数据表 DRUIForm）。
6. **加载中取消的竞态处理**：`m_UIFormsBeingLoaded` + `m_UIFormsToReleaseOnLoad` 两集合把"请求已发、结果未到、用户反悔"处理得很干净。
7. **壳层与逻辑分离 + 异常隔离**：`UIForm`（壳，IUIForm 实现）转发并 try/catch，`UIFormLogic`（游戏继承）保持纯净；事件参数 ReferencePool 化实现零 GC 派发。

### 可以简化/改进的
1. `Refresh` 改为**只在深度值变化时**发 `OnDepthChanged`；组内深度可改为栈顶=1 向下递增/递减均可，关键是稳定。
2. 事件体系统一：要么全 C# event（core 已是），要么直接一条类型化事件总线；参数不必池化（现代项目直接 new 或用 record struct + 回调）。
3. serialId → 直接返回 `UIForm` 引用 + 内部句柄，砍掉线性查找；或补 `Dictionary<int, IUIForm>` 索引。
4. 补齐官方三个缺口：**预加载**（PreloadUIForm(assetName)）、**异步关闭**（CloseUIForm(animated) + 完成回调/事件）、**导航栈**（组内栈可保留，另建跨页面历史）。
5. 若不打算每组一个 Canvas，可以把 `IUIGroupHelper`/深度换算整体删掉（master 的空实现说明作者也承认它属于渲染层）；要做就照抄 StarForce 的 `10000×组深 + 100×组内深 + 子 Canvas 增量平移` 方案。
6. `IsLoadingUIForm(assetName)` 加一个名字索引；`UIIntKey/UIStringKey` 直接删掉。
7. 淡入淡出等"打开/关闭过渡"值得框架化（提供 OpenUIFormAsync + 过渡完成事件），否则每个项目都要复制 StarForce 的 CloseCo 模式并自己处理过渡期状态回调整口。

---

### 附：本次分析读取的源码文件清单
- core：`GameFramework/UI/`（UIManager.cs + 4 个 partial、IUIManager/IUIForm/IUIGroup/IUIFormHelper/IUIGroupHelper、5 个 EventArgs）
- 关联：`GameFramework/ObjectPool/`（IObjectPool、ObjectPoolManager.ObjectPool.cs、ObjectPoolManager.Object.cs、ObjectBase 等）、`GameFramework/Base/EventPool/`（EventPool.cs、BaseEventArgs.cs）、`GameFramework/Event/`（EventManager、GameEventArgs）、`GameFramework/Resource/IResourceManager.cs`、`Resource/Constant.cs`
- Unity 包：`Scripts/Runtime/UI/`（UIComponent.cs + UIComponent.UIGroup.cs、UIForm.cs、UIFormLogic.cs、UIFormHelperBase、DefaultUIFormHelper、UIGroupHelperBase、DefaultUIGroupHelper、UIIntKey、UIStringKey、4 个 EventArgs）、`Scripts/Runtime/Event/EventComponent.cs`
- StarForce：`Assets/GameMain/Scripts/UI/UGuiForm.cs`、`UGuiGroupHelper.cs`、`UIExtension.cs`、`UIFormId.cs`
