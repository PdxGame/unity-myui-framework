# MyUI 使用手册与实现原理

MyUI 是面向 Unity 2022.3+ 的轻量 UI 管理框架，基于 UGUI 与 TextMeshPro。框架负责顶层面板的加载、生命周期、层级、输入阻断、逻辑暂停、实例池与返回导航。

核心状态机为纯 C#，不依赖 UnityEngine。Unity 胶水层、Addressables 加载器和编辑器工具均通过独立程序集接入。

---

## 1. 安装

### 1.1 Git URL 安装

Unity 中打开：

```text
Window -> Package Manager -> + -> Add package from git URL...
```

输入：

```text
https://github.com/PdxGame/unity-myui-framework.git
```

### 1.2 本地安装

将以下内容复制到项目的 `Packages` 或 `Assets` 目录：

```text
package.json
Runtime/
Editor/
Tests/
```

如果放入 `Assets`，应避免与项目已有程序集名称或命名空间冲突。

## 2. 包结构

```text
Runtime/
  MyUI.Core/                    纯 C# 状态机
  MyUI.Runtime/                 Unity 门面、UIPanel、UIRoot、加载器接口
  MyUI.Loaders.Addressables/    Addressables 加载器
Editor/
  MyUI.Editor/                  Inspector 扩展与注册窗口
Samples~/Demo/                  Demo 面板与预制体
Tests/                          EditMode 测试
```

核心职责：

| 模块 | 职责 |
|---|---|
| `MyUI.Core` | 面板记录、状态迁移、遮挡与暂停计算、导航栈、实例池决策 |
| `MyUI.Runtime` | `UIManager`、`UIPanel`、`UIRoot`、运行时生命周期与 Unity 对象管理 |
| `MyUI.Loaders.Addressables` | 从 Addressables 异步加载面板 |
| `MyUI.Editor` | 加载模式配置、资源注册、Inspector 调试入口 |

## 3. 启动框架

在项目入口调用一次：

```csharp
using MyUI.Runtime;

public class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        UIManager.Init();
    }
}
```

`Init()` 会：

- 创建常驻 `UIRoot`
- 创建每个层级的 Canvas
- 确保场景存在 `EventSystem`
- 根据配置选择默认资源加载器

`Init()` 是幂等操作。重复调用会返回现有管理器。

## 4. 创建面板

### 4.1 面板脚本

```csharp
using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;
using UnityEngine.UI;

[UIPanel(
    UILayer.Normal,
    FullScreen = true,
    OpenMode = UIOpenMode.Push)]
public class EquipmentPanel : UIPanel
{
    [SerializeField] private Button closeButton;

    protected override void OnInit()
    {
        closeButton.onClick.AddListener(OnCloseButton);
    }

    private void OnCloseButton()
    {
        Close();
    }

    protected override void OnClose(bool pooled)
    {
        closeButton.onClick.RemoveListener(OnCloseButton);
    }

    protected override void OnOpen(object userData)
    {
        if (userData is EquipmentOpenData data)
        {
            Refresh(data);
        }
    }

    private void Refresh(EquipmentOpenData data)
    {
        Debug.Log("Open source: " + data.Source);
    }
}
```

按钮监听使用 Unity 原生 `Button.onClick.AddListener`。页面池化复用时，动态监听在 `OnOpen` 中添加，并在 `OnClose` 中移除。

### 4.2 预制体

面板预制体根节点必须挂载对应 `UIPanel` 子类。

推荐保持以下命名一致：

```text
类型名 == Prefab 文件名 == Addressables 地址

EquipmentPanel.cs
EquipmentPanel.prefab
Address = EquipmentPanel
```

### 4.3 打开面板

```csharp
UIManager.OpenPanel<EquipmentPanel>();

UIManager.OpenPanel<ItemDetailPanel>(new ItemDetailOpenData
{
    ItemInstanceId = itemInstanceId,
    Source = ItemSource.Equipment,
});
```

## 5. 面板配置

配置可以写在 `[UIPanel]` 特性中，也可以在预制体 Inspector 上配置。

### 5.1 配置项

| 配置 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `Layer` | `UILayer` | `Normal` | 面板所属层级 |
| `FullScreen` | `bool` | `false` | 是否自动拉伸根节点到所属层 |
| `InputMode` | `UIInputMode` | `Inherit` | 输入阻断方式 |
| `PauseBelow` | `UIPauseBelowMode` | `Inherit` | 是否暂停被覆盖的下层 |
| `OpenMode` | `UIOpenMode` | `Inherit` | Overlay、Push 或 Replace |
| `Poolable` | `bool` | `true` | 关闭后是否进入实例池 |
| `Stackable` | `bool` | `true` | 兼容返回开关，主要配合 Push |
| `AllowMulti` | `bool` | `false` | 是否允许同类型多实例 |
| `Address` | `string` | `null` | 覆盖默认资源地址 |

`AllowMulti` 与 `Address` 需要在打开前读取，因此只支持特性配置。

### 5.2 FullScreen

`FullScreen = true`：

- 面板打开时自动设置为全屏锚点
- `anchorMin = (0, 0)`
- `anchorMax = (1, 1)`
- `anchoredPosition = (0, 0)`
- `sizeDelta = (0, 0)`

`FullScreen = false`：

- 保留 Prefab 中的尺寸、锚点与布局
- 适合窗口、弹窗、HUD、Toast

`FullScreen` 只控制布局，不直接决定输入阻断或暂停逻辑。

### 5.3 InputMode

| 值 | 行为 |
|---|---|
| `Inherit` | `FullScreen=true` 时为 `Modal`，否则为 `Self` |
| `None` | 不创建框架输入阻断器 |
| `Self` | 依赖 Prefab 自身的 Graphic Raycast 设置 |
| `Modal` | 自动创建全屏透明射线阻断器 |

模态阻断器在运行时创建为面板子节点：

```text
__MyUI_ModalBlocker
```

它的作用是阻止点击穿透到低层 Canvas。业务代码不需要主动管理该节点。

### 5.4 PauseBelow

| 值 | 行为 |
|---|---|
| `Inherit` | `FullScreen=true` 时暂停下层，否则不暂停 |
| `Never` | 永远不因本面板暂停下层 |
| `Always` | 本面板形成覆盖时暂停下层 |

`InputMode` 与 `PauseBelow` 相互独立：

- 模态但不停逻辑：`InputMode=Modal`、`PauseBelow=Never`
- 非模态但暂停逻辑：`InputMode=None`、`PauseBelow=Always`
- 全屏模态并暂停：`FullScreen=true`，其余使用默认继承

### 5.5 OpenMode

| 值 | 行为 |
|---|---|
| `Inherit` | `Stackable=true` 时为 `Push`，否则为 `Overlay` |
| `Overlay` | 覆盖当前页面，不增加返回层级 |
| `Push` | 保留当前页面并登记返回路径 |
| `Replace` | 新页面成功后关闭当前可返回页面，不增加返回层级 |

### 5.6 单次打开覆盖

```csharp
UIManager.OpenPanel<ItemDetailPanel>(
    data: openData,
    inputMode: UIInputMode.Modal,
    pauseBelow: UIPauseBelowMode.Never,
    openMode: UIOpenMode.Overlay);
```

单次覆盖不会修改面板资源中的默认配置。

## 6. 层级

层级渲染与遮挡顺序：

```text
Background
Normal
HUD
Popup
Guide
System
Toast
```

推荐用途：

| 层级 | 用途 |
|---|---|
| `Background` | 全屏背景、场景底图 |
| `Normal` | 主菜单、装备页、背包页、游戏主界面 |
| `HUD` | 金币、血量、任务追踪、常驻操作区 |
| `Popup` | 设置、确认框、商店窗口 |
| `Guide` | 新手引导、教学遮罩 |
| `System` | 加载遮罩、断线提示、强制更新 |
| `Toast` | 飘字、轻提示、短消息 |

每层使用独立 Canvas。Panels 的渲染顺序由 Canvas Sorting Order 控制，同层面板由打开顺序控制。

## 7. 遮挡与暂停

面板被判定为覆盖型需要满足至少一项：

- `FullScreen = true`
- `InputMode = Modal`
- `PauseBelow = Always`

遮挡规则：

1. 同层中，后打开的覆盖型面板遮挡先打开的页面。
2. 更高层的覆盖型面板遮挡低层页面。
3. 非模态窗口、普通 HUD 不形成跨层遮挡。
4. 被覆盖且遮挡源声明 `PauseBelow` 时触发 `OnPause`。
5. `Toast` 不参与遮挡和暂停。

### 推荐组合

```csharp
// 全屏页面：全屏、模态、暂停下层
[UIPanel(UILayer.Normal, FullScreen = true)]

// 设置窗口：阻断输入，不暂停下层
[UIPanel(
    UILayer.Popup,
    InputMode = UIInputMode.Modal,
    PauseBelow = UIPauseBelowMode.Never)]

// 确认框：阻断输入并暂停下层
[UIPanel(
    UILayer.Popup,
    InputMode = UIInputMode.Modal,
    PauseBelow = UIPauseBelowMode.Always)]

// HUD：不阻断、不暂停、不进入返回层级
[UIPanel(
    UILayer.HUD,
    InputMode = UIInputMode.None,
    PauseBelow = UIPauseBelowMode.Never,
    OpenMode = UIOpenMode.Overlay)]

// Toast：多实例、无遮挡、无返回
[UIPanel(
    UILayer.Toast,
    InputMode = UIInputMode.None,
    PauseBelow = UIPauseBelowMode.Never,
    OpenMode = UIOpenMode.Overlay,
    AllowMulti = true,
    Stackable = false)]
```

## 8. 生命周期

```text
OnInit
OnOpen(object userData)
OnShow
OnCover
OnReveal
OnPause
OnResume
OnTick(float deltaTime)
OnHide
OnClose(bool pooled)
OnDestroyed
```

| 回调 | 调用时机 | 建议用途 |
|---|---|---|
| `OnInit` | 实例创建后仅一次 | 获取控件、绑定按钮、初始化静态结构 |
| `OnOpen` | 每次打开 | 接收数据、刷新页面状态 |
| `OnShow` | 打开流程完成 | 播放进入动画、启动页面行为 |
| `OnCover` | 被覆盖型面板遮挡 | 处理可见性相关状态 |
| `OnReveal` | 遮挡解除 | 恢复显示相关状态 |
| `OnPause` | 被声明 `PauseBelow` 的遮挡源覆盖 | 停止计时器、动画或逻辑更新 |
| `OnResume` | 暂停解除 | 恢复页面逻辑 |
| `OnTick` | 每帧，仅打开且未暂停 | 页面持续更新 |
| `OnHide` | 关闭流程开始 | 播放关闭动画、隐藏页面 |
| `OnClose` | 进入实例池或销毁前 | 释放订阅、协程、事件和临时资源 |
| `OnDestroyed` | 实例真正销毁 | 释放不可复用资源 |

## 9. 数据传递

推荐传入业务 ID 与上下文，而不是让 UI 持有运行时数据源：

```csharp
public class ItemDetailOpenData
{
    public long ItemInstanceId;
    public ItemSource Source;
}
```

打开：

```csharp
UIManager.OpenPanel<ItemDetailPanel>(new ItemDetailOpenData
{
    ItemInstanceId = itemInstanceId,
    Source = ItemSource.Equipment,
});
```

接收：

```csharp
protected override void OnOpen(object userData)
{
    if (userData is not ItemDetailOpenData data)
    {
        return;
    }

    var detail = inventoryService.GetDetailViewData(data.ItemInstanceId);
    Refresh(detail);
}
```

运行时数据应由业务 Model、Service 或 Repository 持有。面板关闭后，业务数据仍应继续存在。

## 10. 导航

### 10.1 Push

```csharp
UIManager.OpenPanel<EquipmentPanel>(
    openMode: UIOpenMode.Push);
```

返回时：

```csharp
UIManager.Back();
```

### 10.2 Overlay

```csharp
UIManager.OpenPanel<SettingsPanel>(
    openMode: UIOpenMode.Overlay);
```

Overlay 不增加返回层级。需要关闭时：

```csharp
UIManager.ClosePanel<SettingsPanel>();
```

页面存在内部返回步骤时，实现 `IUINavigationHandler`：

```csharp
public class EquipmentPanel : UIPanel, IUINavigationHandler
{
    public bool HandleBack()
    {
        if (detailVisible)
        {
            ShowList();
            return true;
        }

        return false;
    }
}
```

`HandleBack()` 返回 `true` 表示页面已消费返回事件。

### 10.3 Replace

```csharp
UIManager.OpenPanel<GamePlayPanel>(
    openMode: UIOpenMode.Replace);
```

适用场景：

- 主菜单进入游戏
- 登录页进入大厅
- 启动页进入主界面

Replace 不会增加返回层级。被替换页面的返回入口会转移给新页面。

## 11. 页面内多级内容

MyUI 管理的是顶层面板。页面内部的列表、页签、详情区、侧栏等内容建议保留在同一个面板组件树中，通过普通 View、Controller 或自定义子组件管理。

推荐划分：

```text
EquipmentPanel
  Header
  CategoryView
  ItemListView
  DetailView
```

以下情况应拆成独立 `UIPanel`：

- 全屏页面
- 模态弹窗
- 可被多个页面复用的详情页
- 需要独立返回路径的页面
- 需要独立资源加载与层级控制的页面

以下情况不建议拆成全局 Panel：

- 同一个页面内的 Tab
- 列表与详情联动
- 并排的商店和物品栏
- 仅用于布局分组的区域

## 12. 实例池

关闭面板时，`Poolable = true` 的实例会进入池。

默认参数：

| 参数 | 默认值 |
|---|---|
| 每地址容量 | 3 |
| 闲置淘汰时间 | 60 秒 |

池化面板注意事项：

- `OnInit` 不会在复用后再次调用
- 每次打开都会调用 `OnOpen`
- 关闭时必须清理事件订阅、协程、定时器和异步请求
- 打开时应重置列表、滚动位置、选中状态和动画状态

## 13. 资源加载

### 13.1 Addressables

默认加载模式。

通过：

```text
MyUI -> Settings & Registration
```

选择加载模式和面板资源。

推荐命名：

```text
类型名 == Prefab 文件名 == Addressables 地址
```

### 13.2 Resources

在设置中选择 Resources，或将预制体放入：

```text
Resources/UIPanel/
```

默认地址：

```text
UIPanel/{类型名}
```

### 13.3 自定义加载器

实现 `IAssetLoader`：

```csharp
public interface IAssetLoader
{
    void LoadViewAsync(string address, Action<object, string> onDone);
    void ReleaseView(object view);
    string DefaultAddress(Type panelType);
}
```

启动时传入：

```csharp
UIManager.Init(new MyAssetLoader());
```

## 14. 编辑器工具

### Settings & Registration

```text
MyUI -> Settings & Registration
```

包含：

- 加载模式切换
- Addressables 面板注册
- 自动创建 `UIPanels` 组
- 自动设置地址

### UIPanelTester

挂到场景任意 GameObject：

1. 填写面板类型全名
2. 进入 Play 模式
3. 点击 Open

### Inspector 调试

选中面板预制体后，可以在 Inspector 中直接执行打开或关闭操作。

## 15. 测试

打开：

```text
Window -> General -> Test Runner -> EditMode
```

运行 `MyUI.Tests.EditMode`。

测试覆盖：

- 加载失败与异常路径
- 视图挂接与释放
- 池复用上下文刷新
- Dispose 清理
- Push、Overlay、Replace
- 同层与跨层遮挡
- `PauseBelow` 暂停规则

## 16. 性能建议

- 层与层之间使用独立 Canvas，跨层刷新不会影响其他层。
- 同层面板可共享 Canvas 合批。
- 高频变化区域建议独立成子 Canvas 或拆分到 HUD 层。
- 页面根节点避免无意义的额外 Canvas。
- 列表项使用对象池或虚拟列表。
- 不在每帧查询整个面板树。
- 关闭页面时取消事件订阅与异步任务。

## 17. 常见问题

### 打开后点击穿透

检查：

- `InputMode` 是否为 `Modal`
- 面板根节点是否覆盖目标区域
- Prefab 内部是否错误关闭了关键 Graphic 的 `raycastTarget`

### 页面被上层遮挡但没有暂停

`PauseBelow` 控制逻辑暂停，`InputMode` 只控制输入阻断。需要暂停时设置：

```csharp
PauseBelow = UIPauseBelowMode.Always
```

### Back 没有关闭当前页面

可能原因：

- 当前页面是 Overlay，没有返回入口
- 页面不是 Push 打开
- 页面实现了 `IUINavigationHandler` 并消费了返回事件
- 当前是根页面，没有可返回历史

### Addressables 找不到面板

检查：

- 类型名、Prefab 名、地址是否一致
- 是否已加入 `UIPanels` 组
- 是否通过设置窗口完成注册
- 是否使用了错误的显式地址

### 面板复用时状态残留

在 `OnOpen` 中重置页面状态，在 `OnClose` 中释放事件、协程和临时对象。

## 18. 实现原理

### 18.1 分层

```text
MyUI.Core
  <- IAssetLoader
  <- IUIPanelFactory
  <- IUIPanelView

MyUI.Runtime
  -> UIManager
  -> UIPanel
  -> UIRoot
```

Core 不直接引用 Unity 对象，因此可以在 EditMode 中使用假加载器与假视图执行测试。

### 18.2 面板记录

每个打开请求都有独立的 `PanelRecord`：

- `SerialId`
- `PanelType`
- `PanelName`
- `Address`
- `Layer`
- `FullScreen`
- `InputMode`
- `PauseBelow`
- `OpenMode`
- `State`
- `Covered`
- `Paused`

### 18.3 打开流程

```text
查重
-> 创建 PanelRecord
-> 复用池实例或异步加载
-> AttachView
-> 应用层级与行为配置
-> 拉伸全屏节点
-> 创建模态阻断器
-> RegisterNavigation
-> OnInit / OnOpen / OnShow
-> Replace 收尾
-> RefreshCoverage
-> 派发事件
```

### 18.4 关闭流程

```text
OnHide
-> OnClose(pooled)
-> 入池或 OnDestroyed
-> 移除记录
-> 清理导航入口
-> RefreshCoverage
-> 派发关闭事件
```

### 18.5 遮挡计算

每次面板状态变化后统一刷新：

- 同层按打开顺序判断覆盖
- 跨层按 `UILayerOrder` 判断覆盖
- 只有覆盖型面板形成遮挡
- `PauseBelow` 决定是否触发 `OnPause`

### 18.6 导航记账

Push 模式在面板成功打开后登记父页面：

```text
当前页 -> 新页面
```

返回时关闭当前页面并恢复父页面。

Replace 模式直接把被替换页面的父入口转移给新页面。

Overlay 不登记导航入口。

## 19. 扩展点

### 自定义加载器

实现 `IAssetLoader` 后通过 `Init(loader)` 注入。

### 自定义面板工厂

实现 `IUIPanelFactory` 可接管：

- 视图挂接
- 激活状态
- 层级置顶
- 运行时上下文刷新
- 销毁与释放

### 自定义层级

修改 `UILayer` 并同步更新 `UILayerOrder`。

## 20. 边界

- MyUI 负责顶层面板管理，不强制页面内部必须使用统一导航栈。
- 页面内部的多级内容推荐由页面自身组织。
- 需要独立返回路径、独立资源或模态行为的内容应拆成独立 `UIPanel`。
- 框架不内置业务数据 Model、Inventory、Equipment 或服务器同步逻辑。
- 业务数据应由项目自己的 Model、Service、Repository 或 Architecture 层维护。
