# MyUI 框架：使用手册与实现原理

> 自研 Unity UI 管理框架（Unity 2022.3+，UGUI + TextMeshPro）。
> 设计参照 GameFramework（状态机 / serialId / 遮挡生命周期 / 实例池）与 QFramework（固定层级 / 门面 API / 导航）。
> 核心为纯 C# 状态机，20 条 EditMode 单元测试；框架本体零第三方依赖，Addressables 为默认加载方式（可选程序集，缺失自动回退 Resources）。

---

## 第一部分：使用手册

### 1. 安装与包结构

**方式 A（推荐）**：`Window → Package Manager → ＋ → Add package from git URL`：

```
https://github.com/PdxGame/unity-myui-framework.git
```

依赖（ugui / textmeshpro / addressables / test-framework）自动安装。

**方式 B**：将 `Runtime/`、`Editor/`、`Tests/` 与 `package.json` 拷贝进项目任意目录。

包结构：

```
├─ package.json              UPM 元数据（依赖 / Sample 清单）
├─ Runtime/
│   ├─ MyUI.Core/            纯 C# 状态机：打开/关闭/合并/取消/遮挡/暂停/池/导航
│   ├─ MyUI.Runtime/         UIPanel 基类、UIManager 门面、UIRoot 分层、加载器、Tester
│   └─ MyUI.Loaders.Addressables/  Addressables 加载器（可选程序集）
├─ Editor/MyUI.Editor/       Inspector 调试按钮、面板注册菜单
├─ Samples~/Demo/            示例：3 个面板（脚本 + 预制体）
└─ Tests/                    EditMode 单测（20 条）
```

### 2. 快速开始

入口处调用一次 `UIManager.Init()`（唯一启动方式，幂等），随后全局可用（Init 会自动创建 UI 根与 EventSystem、按窗口①配置绑定加载器）：

```csharp
using MyUI.Runtime;

UIManager.Init();                           // 启动框架（入口一次）
UIManager.OpenPanel<MainMenuPanel>();       // 打开面板（地址默认 = 类型名）

// 关闭（面板内部）
Close();
// 关闭（框架外）
UIManager.ClosePanel(panel);
UIManager.ClosePanel("MainMenuPanel");
UIManager.ClosePanel<MainMenuPanel>();
UIManager.CloseAll();         // 清场（切场景时）
```

打开新页面时框架自动记录返回路径，`Back()` 在有历史时回退：

```csharp
UIManager.Back();
```

> 说明：`Init()` 放在你自己的启动脚本（任意 MonoBehaviour 的 Start/Awake）即可，**不需要任何 `RuntimeInitializeOnLoadMethod` 之类标签**（Sample 的 Demo 用它只是"空场景也能演示"的示例技巧）。

### 3. 开发一个新页面（5 步）

**第 1 步：写脚本**（一个类 = 一个页面）：

```csharp
using MyUI.Runtime;

public sealed class ShopPanel : UIPanel
{
    protected override void OnInit()
    {
        BindButton("Btn_Close", OnCloseClicked);   // 一行绑定按钮
    }

    private void OnCloseClicked()
    {
        Close();
    }
}
```

**第 2 步：搭预制体**：Unity 里正常 UGUI 搭建，节点名与脚本里 `BindButton` 的参数一致。

**第 3 步：根节点挂脚本**：把 `ShopPanel` 组件挂到预制体根节点。

**第 4 步（Addressables 模式）**：打开菜单 **`MyUI → Settings & Registration`** → ②区「选择目录…」选中面板所在目录（或直接拖入预制体单片）→ 组名可填（默认 `UIPanels`，不存在自动创建）→ 点注册：自动入组、**地址自动 = 类型名**、重复自动跳过。
（Resources 兼容模式：预制体放入任意 `Resources/` 目录即可，推荐 `Resources/UIPanel/` 子目录，地址自动 = `UIPanel/{类型名}`，无需注册。）

**第 5 步：打开**：

```csharp
UIManager.OpenPanel<ShopPanel>(shopData);   // 可选传数据，OnOpen 里接收
```

### 4. 生命周期

生命周期由框架按固定次序驱动，面板类可选择性覆写（基类全部为空实现，不写即不生效）。

```
打开：OnInit（仅新实例）→ OnOpen(data)（每次打开）→ OnShow
遮挡：OnCover →（遮挡链含全屏面板时）OnPause
露出：OnReveal → OnResume
关闭：OnHide → OnClose(pooled) → 入池或 OnDestroyed + 销毁
驱动：OnTick(dt) 每帧，仅处于打开且未暂停状态的面板收到
```

| 回调 | 时机 | 典型用途 |
|---|---|---|
| `OnInit()` | 实例创建后调用一次；池复用不调用 | 绑定按钮（`BindButton`）、查找控件（`Find<T>`） |
| `OnOpen(object userData)` | 每次打开（含池复用） | 按传入数据刷新界面 |
| `OnShow()` | 完全显示（打开动画后） | 与显隐相关的逻辑 |
| `OnHide()` | 关闭流程开始 | 关闭动画等 |
| `OnCover()` | 被其它面板遮挡 | 遮挡事实处理 |
| `OnReveal()` | 重新露出 | 恢复显示相关 |
| `OnPause()` | 被全屏面板遮挡（逻辑暂停） | 停计时器、动画 |
| `OnResume()` | 暂停解除 | 恢复逻辑 |
| `OnClose(bool pooled)` | 面板即将入池或销毁；`pooled=true` 表示入池复用 | 清理临时资源（订阅、协程） |
| `OnDestroyed()` | 真正销毁 | 最终清理 |
| `OnTick(float dt)` | 每帧；仅打开且未暂停 | 持续逻辑 |

**数据传递示例**：

```csharp
// 打开时传入
UIManager.OpenPanel<ResultPanel>(score);

// 面板内接收
protected override void OnOpen(object userData)
{
    if (userData == null)
    {
        return;
    }

    int score = (int)userData;
    ScoreText.text = score.ToString();
}
```

### 5. 配置项（特性与 Inspector，两者等价，Inspector 优先）

| 配置 | `[UIPanel]` 特性 | 预制体 Inspector（UIPanel 组件） | 说明 |
|---|---|---|---|
| 层级 | `[UIPanel(UILayer.Popup)]` | 层级下拉 | 所在层（见 §6） |
| 全屏 | `FullScreen = true` | 全屏勾选 | 打开时其下层面板收到 `OnPause` |
| 池化 | `Poolable = false` | 池化勾选 | 关闭时是否入池复用 |
| 返回导航 | `Stackable = false` | 参与返回导航勾选 | 飘字/过场 UI 设为 false |
| 多实例 | `AllowMulti = true` | （无） | Toast 类；需打开前判定，仅特性 |
| 地址 | `Address = "..."` | （无） | 覆盖默认类型名地址 |

优先级：**Inspector > 特性 > 默认值**。两种方式可混用；`AllowMulti` 与 `Address` 因需在打开前判定，仅代码特性可用。

### 6. 层级（Layer）

默认 6 层（渲染顺序由低到高、遮挡链按此顺序）：

| 层 | 典型用途 |
|---|---|
| `Background` | 背景层（全屏背景、场景底图） |
| `Normal` | 主界面（主菜单、游戏主界面） |
| `Popup` | 弹窗（设置、确认框） |
| `Guide` | 引导层 |
| `System` | 常驻系统 UI（HUD、血量、金币） |
| `Toast` | 飘字（伤害飘字、提示） |

每层一个独立 Canvas（sortingOrder = 层值 × 100）。

#### 扩展层级（修改枚举，共 3 行以内）

想增加一层，只需在 `UILayer` 枚举中追加成员（框架代码位置：包内 `Runtime/MyUI.Core/UILayer.cs`）：

```csharp
public enum UILayer
{
    Background,
    Normal,
    Popup,
    Guide,
    System,
    Toast,
    Trade,          // ← 新增层（示例：交易所）
}
```

保存编译后自动生效：

- `UIRoot` 启动时按枚举自动为 `Trade` 创建独立 Canvas（排序 = 6 × 100 = 600，位于 Toast 之上）；
- 遮挡链自动衔接：`Trade` 层的非全屏面板不暂停下层、全屏面板遮挡至其下所有层；
- 代码中即可引用：

```csharp
[UIPanel(UILayer.Trade)]
public sealed class TradePanel : UIPanel { }
```

补充说明：

- **加在末尾最安全**（后续层排序不变）；插在中间同样正确（相对顺序由枚举声明顺序决定）；
- 每个新层会创建一张 Canvas；不使用则不要挂载面板（空层无渲染成本）；
- 删除/停用层：不引用它即可（面板不挂入则零开销）；不建议删除枚举定义（历史引用会失效）。

### 7. 遮挡与暂停

规则：

1. 同层：更晚打开的面板遮挡更早者；
2. 跨层：仅更高层 **全屏（FullScreen）** 面板构成遮挡源；
3. 暂停：被遮挡 且 遮挡源存在全屏 → 收到 `OnPause`（含 `OnCover`）；
4. 恢复：`OnReveal → OnResume`。

经验：主界面/游戏主界面标 `FullScreen = true`；弹窗（Popup、非全屏）打开时下层继续运行（如音乐）。

### 8. 实例池

- **机制**：关闭的面板（`Poolable = true` 且未超容量）不销毁，藏入池；下次打开直接复用（不重新加载、不重建实例，`OnInit` 不再调用）；
- **容量**：每个面板类型保留最多 3 个闲置实例，超出销毁；闲置超过 60 秒自动销毁；
- **关闭池**：Inspector 取消「池化」或特性 `Poolable = false`（一次性引导页等）；
- **验证**：池化面板关闭再打开，Console 不再出现第二次 `OnInit`。

### 9. 导航（返回）

- 打开新页面时自动把当前顶层记录入返回栈（无需手动 `Push`）；
- `Stackable = false` 的面板（飘字/过场）不记录；
- `Back()`：有返回历史时关闭当前顶层；无历史（第一页）时安全无操作。

### 10. API 参考

```csharp
// ---- 启动（唯一入口：Init，幂等）----
UIManager.Init();                                   // 启动框架（入口一次；按窗口①配置绑定加载器）
UIManager.Init(IAssetLoader loader);                // 自定义加载器启动
UIManager.AssetMode                                 // Addressables（默认）/ Resources（窗口①或代码均可）

// ---- 打开 ----
UIManager.OpenPanel<T>(object data = null,
    Action<UIPanel> onOpened = null, Action<string> onFailed = null);
UIManager.OpenPanel<T>(string address, object data = null, ...);   // 显式地址
UIManager.OpenPanelAsync<T>(object data = null);                   // Task 版

// ---- 关闭 ----
panel.Close(bool immediate = false);
UIManager.ClosePanel(UIPanel panel, bool immediate = false);
UIManager.ClosePanel(string panelName, bool immediate = false);
UIManager.ClosePanel<T>(bool immediate = false);
UIManager.CloseAll(bool immediate = false);   // immediate=true 跳过关闭延迟

// ---- 查询 ----
UIManager.GetPanel<T>();      // 单实例语义，未打开返回 null
UIManager.IsOpen<T>();

// ---- 导航 ----
UIManager.Back();             // 有历史才关当前页

// ---- 事件 ----
UIManager.Instance.PanelOpened     += r => { };             // (PanelRecord)
UIManager.Instance.PanelClosed     += (r, pooled) => { };   // (PanelRecord, bool)
UIManager.Instance.PanelLoadFailed += (name, error) => { };
```

### 11. 资源加载

- **加载模式**：菜单 `MyUI → Settings & Registration` ①区勾选 Addressables（默认）/ Resources → 保存即应用（自动重编译，秒级）；也可代码指定 `UIManager.AssetMode`；
- **默认 Addressables**：`Init()` 自动选用 `AddressablesPanelLoader`（程序集缺失时回退 Resources 并打警告）；
- **回退**：`ResourcesPanelLoader`（地址 = 类型名 + `UIPanel/` 前缀，预制体放任意 `Resources/` 目录）；
- **自定义**：实现 `IAssetLoader`（`LoadViewAsync` / `ReleaseView` / `DefaultAddress`），`Init(myLoader)` 传入；
- **面板注册**：菜单 `MyUI → Settings & Registration` ②区：选目录（或单片拖入）→ 自动建组（组名可填，默认 `UIPanels`）→ 自动入组、地址自动=类型名、重复跳过（见下文规范）。

#### Addressables 组与命名规范

**组（Group）**

- 面板统一放入名为 **`UIPanels`** 的专用组（注册菜单默认写入该组；手工建组时取同名）；
- 不要将面板放入 `Built In Data`（内建数据组，用于场景列表等内建资源）与 `Default Local Group`（默认空组）之外的无关分组，避免打包/检索混乱；
- 同一组内可共享打包设置（如同时打 AssetBundle 或不打）。

**地址（Address）**

- **地址 = 面板类型名**（如 `ShopPanel`）。框架默认地址约定（`DefaultAddress`）即类型名；
- 地址与类名不一致会导致加载失败：Console 报 `InvalidKeyException: No Location found for Key=...`；
- 例外：确需自定义地址时，用 `[UIPanel(Address = "...")]`（该面板固定用）或 `OpenPanel<T>("地址")`（仅本次调用）。

**命名三统一**（强烈建议）

```
类型名 == 预制体文件名 == Addressables 地址
示例：ShopPanel.cs / ShopPanel.prefab / 地址 ShopPanel
```

三统一时 `OpenPanel<T>()` 无需任何额外参数、零配置；破坏任一环都会造成加载失败或额外传址。

**标签**（可选）

组内可为面板添加标签（如 `ui`、`panel`）用于批量构建设置，非必需。

#### Resources 模式（不使用 Addressables）

不引入 Addressables 时，框架以 Resources 兼容模式运行，使用成本与约定目录方案一致：

- **触发方式**（任一即可）：
  1. 窗口勾选：`MyUI → Settings & Registration` ①区选「Resources」→ 保存并应用（推荐）；
  2. 项目不安装 Addressables 包 —— `Init()` 自动回退并打印一次警告；
  3. 已安装但用代码指定：`UIManager.AssetMode = UIAssetMode.Resources;`（Init 前设置）；
  4. `UIManager.Init(new ResourcesPanelLoader());`（显式传入）。
- **资源放置**：预制体放入任意名为 `Resources` 的目录（如 `Assets/Resources/`）；约定子目录 `UIPanel/` 与其对齐（地址 `UIPanel/{类型名}`）；
- **寻址**：`OpenPanel<T>()` 通过 `Resources.Load("UIPanel/{类型名}")` 同步加载（无需组、无需注册、无需配置资产）；
- **注意事项**：Resources 目录内容会全部打入安装包（无法按需/分包/远程），同步加载适合原型与小项目；示例 Demo 即此模式。

两种模式可共存：面板地址约定相同（类型名），切换加载器后资源位置对应调整即可。

### 12. 调试工具

- **UIPanelTester**：场景中任意 GameObject 挂该组件，填面板类型全名（如 `MyUI.Examples.MainMenuPanel`），Play 中一键打开；
- **Inspector 调试按钮**：选中面板预制体，Inspector 顶部有打开/关闭按钮（编辑模式可预览生命周期流程）。

### 13. 单元测试

`Window → General → Test Runner → EditMode → Run All`：20 条用例覆盖打开/关闭序列、单实例聚焦、双开合并、加载中取消、延迟关闭、遮挡/暂停翻转、池复用（含 SerialId 刷新）、池容量/过期、导航栈、CloseAll 逆序、失败路径、多实例。

### 14. 性能考虑

- 每层独立 Canvas：跨层的面板互不影响彼此的渲染重建；同层面板仍可合批；
- 高频刷新区域（如常驻 HUD）建议放置于 System 层，或局部包一层子 Canvas（勾 Override Sorting），避免频繁更新拖累整层渲染；
- 常规面板根保持 Panel（如无特殊需求，避免为面板额外挂 Canvas 拆散合批）。

### 15. 常见问题

1. **缺字/文字警告**：烘焙字体字符集不含「」等字符时 TMP 会警告。方案：文案避开未收录字符，或在烘焙字符集文件中追加。
2. **文字挡住点击**：TMP 的 `raycastTarget` 记得关闭（`Bg` 等全屏色块同理）。
3. **面板根布局**：根节点使用 RectTransform（框架会自动修复普通 Transform，但排版基准以根为准）。
4. **TMP Essentials 提示**：新项目若提示导入 TMP 基础资源，执行 `Window → TextMeshPro → Import TMP Essential Resources`。
5. **包更新**：git 安装方式下，Package Manager 中该包显示为远程源，重新拉取/切换版本在 Package Manager 中进行；开发调试可改用 embedded（拷贝包目录至项目 `Packages/` 下）。
6. **`InvalidKeyException: No Location found for Key=...`**：Addressables 找不到该地址——检查面板的地址是否等于类型名（三统一，见 §11），是否已入组（`UIPanels`），以及是否忘了执行注册。

---

## 第二部分：实现原理

### 1. 分层动机

Core（纯 C#）→ Runtime（Unity 胶水）→ Loaders（可选）：Core 只通过 `IAssetLoader` / `IUIPanelFactory` / `IUIPanelView` 与世界交互，可单测、可换、零 Unity 依赖。

### 2. 核心数据结构（UIManagerCore）

`_all`（激活记录）、`_singles`（单实例索引）、`_bySerial`（serialId 索引）、`_pool`（地址→空闲实例）、`_closing`（延迟关闭队列）、三把尺子（`_nextSerialId` / `_openOrder` / `_time`）。

### 3. 打开流程

查重（Open→聚焦 / 加载中→合并 / Closing→取消关闭重开）→ 建记录 → 池优先复用（`RefreshViewContext` 刷新 SerialId）→ 加载 → `AttachView`（应用 Inspector 配置：Layer/FullScreen/Poolable 覆盖）→ `OnInit/OnOpen/OnShow` → 完成（遮挡刷新 + 事件 + 回调）。

### 4. 关闭流程

Loading→取消（记录保留至资源释放）；Open→OnHide→（延迟销毁可选）→OnClose(pooled)→入池或销毁→导航清理→遮挡刷新→事件。

### 5. 遮挡 / 暂停

单点 `RefreshCoverage` 全量重算，仅在状态翻转时回调（同层按 OpenOrder；跨层仅 FullScreen 构成遮挡源）。

### 6. 池

入池（容量 3/地址）、复用（同一视图实例，SerialId 经 `RefreshViewContext` 刷新）、过期淘汰（60s）。

### 7. 导航栈

打开自动 Push（`Stackable=false` 除外；Inspector 取消勾选经 `OnViewReady` 撤销）；Back = 有历史才关当前顶层；关闭自动清理栈记录。

### 8. 已知边界

关闭动画（延迟销毁窗口由面板自播）；未提供预加载 API；面板间通信走直接调用与框架事件；池参数、延迟销毁与过期时间为 Core 默认值（可经门面扩展为配置）。