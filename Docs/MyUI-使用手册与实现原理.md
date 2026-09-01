# MyUI 框架：使用手册与实现原理

> 自研 Unity UI 管理框架。核心思路对标 GameFramework（状态机 / serialId / 遮挡生命周期 / 实例池）+ QFramework（固定层级 / 门面 API / 导航）。
> 适用：Unity 2022.3+，UGUI + TextMeshPro。框架本体零第三方依赖（Addressables 为可选扩展，且为默认加载方式）。

---

## 第一部分：使用手册

### 1. 目录结构

```
Assets/Scripts/MyUI/
├─ Core/                        纯 C# 程序集（不引用 UnityEngine，可单元测试）
│   ├─ UiLayer.cs               层级枚举：Background/Normal/Popup/Guide/System/Toast
│   ├─ PanelState.cs            面板状态机枚举
│   ├─ PanelRecord.cs           面板实例运行时记录（serialId/状态/遮挡标记/配置）
│   ├─ UiManagerCore.cs         核心状态机：打开/关闭/合并/取消/遮挡/暂停/池/导航
│   ├─ IAssetLoader.cs          资源加载抽象
│   ├─ IUiPanelFactory.cs       视图工厂抽象（挂接/就绪/激活/上下文刷新）
│   ├─ IUiPanelView.cs          面板生命周期接口
│   ├─ NavigationStack.cs       导航栈
│   ├─ UiEvents.cs              事件参数（Opened/Closed/Failed）
│   └─ AssemblyInfo.cs          InternalsVisibleTo（胶水层访问内部成员）
├─ Runtime/                     Unity 胶水程序集（引用 Core）
│   ├─ UiPanel.cs               用户面板基类：生命周期虚方法 + BindButton/Find 便捷 + Inspector 配置
│   ├─ UiPanelAttribute.cs      [UiPanel(...)] 特性（层级/全屏/多例/池化/地址/入栈）
│   ├─ UiManager.cs             门面：启动/打开/关闭/查询/导航/事件
│   ├─ UiRoot.cs                常驻根：每层一个独立 Canvas（sortingOrder = 层索引×100）
│   ├─ UiAssetMode.cs           加载方式枚举（Resources / Addressables）
│   ├─ ResourcesPanelLoader.cs  兼容回退加载器（未装 Addressables 程序集时使用）
│   └─ UiPanelTester.cs         场景调试工具（填类型名一键打开面板）
├─ Loaders/Addressables/        AddressablesPanelLoader（可选程序集，默认加载方式）
├─ Editor/                      UiPanelInspector（调试按钮）/ PanelAutoRegistrar（手动注册菜单）
├─ Examples/                    示例：MainMenuPanel / GamePlayPanel / SettingsPanel / HelloPanel / DemoBoot
└─ Tests/                       EditMode 单元测试（20 条）
```

### 2. 快速开始（零配置）

```csharp
// 1) 启动：全自动（游戏运行时框架自动初始化，无需任何代码）
//    想手动控制时：UiManager.AutoBoot = false; 再自己调 UiManager.Bootstrap();

// 2) 打开面板（默认地址 = 类型名，与 Addressables 条目一致，不用写地址）
UiManager.OpenPanel<MainMenuPanel>();

// 3) 订阅框架事件（可选，观察用）
UiManager ui = UiManager.Instance;
ui.PanelOpened     += r => { };              // 面板打开完成
ui.PanelClosed     += (r, pooled) => { };    // 面板关闭（pooled=入池复用）
ui.PanelLoadFailed += (name, error) => { };  // 资源加载失败
```

> 必备件：面板脚本（继承 UiPanel）放进 `Assets/MyUI/Panels/`，用菜单 **MyUI → Panels → Add to Addressables** 注册（地址=类型名），即可被 `OpenPanel<T>()` 打开。

### 3. 定义面板

**脚本**（示例：主菜单）：

```csharp
using MyUI.Runtime;

// [UiPanel] 特性可省略；配置也可在预制体 Inspector 的 UiPanel 组件上直接选（优先于特性）
[UiPanel(UiLayer.Normal, FullScreen = true)]
public sealed class MainMenuPanel : UiPanel
{
    protected override void OnInit()          // 创建后一次：绑按钮
    {
        BindButton("Btn_Start", OnStartClicked);
    }

    protected override void OnOpen(object userData)   // 每次打开（含池复用）：按数据刷新
    {
        // userData 是 OpenPanel<T>(data) 传入的数据；没传就是 null
    }

    private void OnStartClicked()
    {
        UiManager.OpenPanel<GamePlayPanel>();
    }
}
```

**预制体**：Unity 里正常 UGUI 搭，根节点挂脚本，放进 `Assets/MyUI/Panels/`，菜单注册。

**配置项（两种方式，Inspector 优先）**：

| 配置 | [UiPanel] 特性 | Inspector（UiPanel 组件） | 说明 |
|---|---|---|---|
| 层级 | `UiLayer.Popup` | `Inspector Layer` | 页面所在层（渲染/遮挡依据） |
| 全屏 | `FullScreen = true` | `Inspector FullScreen` | 打开时下层收到 OnPause |
| 池化 | `Poolable = false` | `Inspector Poolable` | 关闭入池复用（默认勾选） |
| 返回导航 | `Stackable = false` | `Inspector Stackable` | 飘字/过场 UI 取消勾选/标注 false，不入栈 |
| 多实例 | `AllowMulti = true` | （无） | Toast 类，需在打开前判定，仅特性 |
| 地址 | `Address = "..."` | （无） | 覆盖默认类型名地址 |

### 4. 生命周期契约（框架保证的顺序）

```
打开：OnInit（仅新实例一次）→ OnOpen(userData) → OnShow
遮挡：OnCover →（遮挡链含全屏面板时）OnPause
露出：OnReveal → OnResume
关闭：OnHide → OnClose(pooled) → 入池 或 OnDestroyed + 销毁
驱动：OnTick(deltaTime) 每帧，仅 Open 且未暂停的面板收到
```

| 回调 | 时机 | 典型用途 |
|---|---|---|
| `OnInit` | 新实例创建后一次（池复用不调用） | 绑按钮（用 BindButton） |
| `OnOpen(data)` | 每次打开（含池复用） | 按 data 刷新界面 |
| `OnShow` / `OnHide` | 完全可见 / 关闭流程开始 | 显隐、播放动画 |
| `OnCover` / `OnReveal` | 被遮挡 / 重新露出 | 遮挡事实 |
| `OnPause` / `OnResume` | 被全屏遮挡 / 解除 | 停/启计时器、动画 |
| `OnClose(pooled)` | 即将入池或销毁 | 清理临时资源 |
| `OnDestroyed` | 真正销毁 | 最终清理 |
| `OnTick(dt)` | 每帧（活跃未暂停） | 持续逻辑 |

### 5. API 参考

```csharp
// ---- 启动 ----
UiManager.AutoBoot                       // 默认 true：自动启动
UiManager.Bootstrap(IAssetLoader loader = null)   // 手动启动；loader 覆盖默认
UiManager.AssetMode                      // Addressables（默认）/ Resources

// ---- 打开（返回路径自动记录，无需 Push）----
UiManager.OpenPanel<T>(object data = null, Action<UiPanel> onOpened = null, Action<string> onFailed = null);
UiManager.OpenPanel<T>(string address, object data = null, ...);   // 显式地址版
await UiManager.OpenPanelAsync<T>(object data = null);             // Task 版

// ---- 关闭 ----
panel.Close(bool immediate = false);
UiManager.ClosePanel(UiPanel panel, bool immediate = false);
UiManager.ClosePanel(string panelName, bool immediate = false);
UiManager.ClosePanel<T>(bool immediate = false);                   // 泛型版
UiManager.CloseAll(bool immediate = false);                        // 清场（切场景用）

// ---- 查询 ----
UiManager.GetPanel<T>();      UiManager.IsOpen<T>();

// ---- 导航 ----
// Push 自动：打开新页面时自动记录返回路径（Stackable=false 的面板除外）
UiManager.Back();   // 有返回历史时关闭当前顶层并回退；无历史（第一页）时安全无操作

// ---- 事件 ----
UiManager.Instance.PanelOpened / PanelClosed / PanelLoadFailed
```

### 6. 遮挡 / 暂停判定规则

1. 同层：更晚打开的面板无条件遮挡更早者；
2. 跨层：仅更高层面板 `FullScreen=true` 构成遮挡源（飘字不误遮）；
3. 暂停：被遮挡 且 遮挡源存在 `FullScreen` → `OnPause`；
4. 回调顺序：被遮挡 `OnCover → OnPause`；恢复 `OnReveal → OnResume`。

### 7. 加载器

- **默认 Addressables**：`Bootstrap()` 自动创建 `AddressablesPanelLoader`（可选程序集缺失时自动回退 Resources 并警告）；
- **兼容**：`ResourcesPanelLoader`（未装 Addressables 或用 `AssetMode = UiAssetMode.Resources` 时）；
- **自定义**：实现 `IAssetLoader`（LoadViewAsync / ReleaseView / DefaultAddress），`Bootstrap(loader)` 传入；
- **面板注册**：菜单 `MyUI → Panels → Add to Addressables` 把 `Assets/MyUI/Panels` 下带 UiPanel 的面板加进 `UIPanels` 组（地址=类型名，幂等）；组与打包内容的管理始终在你手上。

### 8. 单元测试

`Window → General → Test Runner → EditMode → Run All`：20 条用例覆盖打开/关闭序列、单实例聚焦、双开合并、加载中取消、延迟关闭、遮挡/暂停翻转、Toast 不遮挡、池复用（含 SerialId 刷新）、池容量/过期、导航栈、CloseAll 逆序、失败路径、多实例。

### 9. 常见坑

1. **中文缺字**：面板文案避免「」『』等直角引号（除非烘焙字符集已包含），缺字会警告；如确有需要，用 `Tools > TMP Font Toolkit` 烘焙时在字符集文件追加对应字符。
2. **文本/背景挡住点击**：TMP/Image 的 `raycastTarget=true` 会截走点击——文本一律设 false（背景 Bg 也是）。
3. **面板根必须是 RectTransform**（框架会自动替换，但排版基准以根为准）。
4. **字体材质断链**：烘焙后出现"文字不渲染 + Inspector 报错"时，用更新后的 TMPFontToolkit（已修复引用写回）重新烘焙。

### 10. 性能考虑

- 每层独立 Canvas：跨层的面板互不影响彼此的渲染重建；同层面板仍可合批；
- 高频刷新区域（如常驻 HUD）建议放置于 System 层，或局部包一层子 Canvas（勾 Override Sorting），避免频繁更新拖累整层渲染；
- 常规面板根保持 Panel（如无特殊需求，避免为面板额外挂 Canvas 拆散合批）。

---

## 第二部分：实现原理

### 1. 分层动机

Core（纯 C#）→ Runtime（Unity 胶水）→ Loaders（可选）。Core 只通过 IAssetLoader / IUiPanelFactory / IUiPanelView 与世界交互：可测、可换、零 Unity 依赖。

### 2. 核心数据结构（UiManagerCore）

`_all`（激活记录）、`_singles`（单实例索引）、`_bySerial`（serialId 索引）、`_pool`（address→空闲实例）、`_closing`（延迟关闭队列）、`_nextSerialId/_openOrder/_time`（三把尺子）。

### 3. 打开流程

查重（Open→聚焦 / 加载中→合并 / Closing→取消关闭重开）→ 建记录 → 池优先复用（RefreshViewContext 刷 SerialId；OnViewReady 就在此处与新建路径都触发）→ 加载 → AttachView（应用 Inspector 配置，覆盖 Layer/FullScreen/Poolable）→ OnInit/OnOpen/OnShow → 完成（遮挡刷新 + 事件 + 回调）。

### 4. 关闭流程

Loading→取消（记录保留至资源释放）；Open→OnHide→（延迟销毁可选）→OnClose(pooled)→入池或销毁→导航清理→遮挡刷新→事件。

### 5. 遮挡 / 暂停

单点 RefreshCoverage 全量重算，只在翻转时回调（IsCovering：同层 OpenOrder / 跨层 FullScreen）。

### 6. 池

入池（容量 3/地址）、复用、过期淘汰（60s）；池实例复用同一视图，SerialId 靠 RefreshViewContext 刷新。

### 7. 导航栈

打开自动 Push（Stackable=false 除外，Inspector 取消勾选经 OnViewReady 撤销）；Back = 有历史才关当前顶层；关闭自动清理栈记录。

### 8. 与两家框架对照

| 维度 | GameFramework | QFramework UIKit | MyUI |
|---|---|---|---|
| 核心可测性 | DLL 纯 C# | 强耦合 | 纯 C# Core + 20 单测 |
| 层组织 | 动态 UIGroup | 固定层节点 | 固定层、每层独立 Canvas |
| 遮挡回调 | OnCover/OnPause | 无 | 有 |
| 实例标识 | serialId | 无 | serialId + 字典 |
| 复用 | ObjectPool | 无 | 池 + SerialId 刷新 |
| 双开/取消 | 标志集合 | 有竞态 | 合并+取消（单测） |
| 导航 | 无 | Push/Back | 自动 Push + 安全 Back |

### 9. 已知边界

关闭动画（CloseDelaySeconds 延迟销毁窗口由面板自播放）；预加载 API 未提供；面板间通信走直接调用/框架事件；池参数与延迟销毁为 Core 默认值（可扩展门面 API）。