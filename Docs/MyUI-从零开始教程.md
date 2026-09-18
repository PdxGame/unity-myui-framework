# MyUI 从零开始教程

这份教程假设你只熟悉 Unity、MonoBehaviour 和 UGUI，不需要提前了解 MyUI 的内部实现。

教程会按照实际开发顺序讲解：

1. 先做一个最简单的页面
2. 再学习打开、关闭和数据传递
3. 然后学习层级、输入阻断、暂停和导航
4. 最后再介绍共享业务数据、实例池和资源加载

教程中的示例不使用 `sealed`。`sealed` 是 C# 的可选关键字，用于禁止类被继承，不是 MyUI 的要求。

---

## 1. MyUI 是什么

MyUI 负责管理 Unity UI 页面。

你可以把它理解成：

```text
UIPanel       一个页面脚本
UIManager     全局打开、关闭、返回入口
Prefab        页面的视觉结构
UIRoot        框架运行时创建的 UI 根节点
Addressables  面板资源的加载来源
```

一个页面通常由两部分组成：

```text
ShopPanel.cs        页面逻辑
ShopPanel.prefab    页面视觉与控件
```

`ShopPanel.cs` 挂在 `ShopPanel.prefab` 的根节点上。

## 2. 安装

### 2.1 Git URL

Unity 中打开：

```text
Window -> Package Manager -> + -> Add package from git URL...
```

输入：

```text
https://github.com/PdxGame/unity-myui-framework.git
```

### 2.2 本地包

也可以把包复制到项目的 `Packages` 目录，或把 `Runtime`、`Editor`、`Tests` 和 `package.json` 放入 `Assets` 下的独立目录。

## 3. 初始化

在项目启动入口调用一次：

```csharp
using MyUI.Runtime;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        UIManager.Init();
    }
}
```

`UIManager.Init()` 会：

- 创建 `UIRoot`
- 创建各层 Canvas
- 确保场景里有 `EventSystem`
- 创建默认资源加载器

重复调用 `Init()` 不会重复创建。

## 4. 创建第一个页面

### 4.1 页面脚本

先写一个最简单的页面：

```csharp
using MyUI.Runtime;
using UnityEngine;
using UnityEngine.UI;

public class ShopPanel : UIPanel
{
    [SerializeField] private Button closeButton;

    protected override void OnInit()
    {
        closeButton.onClick.AddListener(OnCloseClicked);
    }

    private void OnCloseClicked()
    {
        Close();
    }

    protected override void OnClose(bool pooled)
    {
        closeButton.onClick.RemoveListener(OnCloseClicked);
    }
}
```

逐行说明：

```csharp
public class ShopPanel : UIPanel
```

表示 `ShopPanel` 是一个页面类，继承 MyUI 的 `UIPanel`。

```csharp
[SerializeField] private Button closeButton;
```

这是 Unity 原生写法。把预制体里的关闭按钮拖到这个字段上。

```csharp
protected override void OnInit()
```

`OnInit` 只在页面实例第一次创建时调用一次。适合绑定按钮、查找控件和初始化固定结构。

```csharp
closeButton.onClick.AddListener(OnCloseClicked);
```

这是 Unity UGUI 原生按钮绑定。

```csharp
Close();
```

`Close()` 不是 Unity 方法，也不是 C# 内置方法，而是 `UIPanel` 提供的关闭方法。它会通过 `UIManager` 关闭当前页面实例。

```csharp
protected override void OnClose(bool pooled)
```

页面关闭或进入实例池时调用。这里移除按钮监听，避免重复订阅。

### 4.2 创建预制体

在 Unity 中：

1. 创建 UI 根节点。
2. 在根节点挂上 `ShopPanel`。
3. 添加标题、关闭按钮等 UGUI 控件。
4. 把关闭按钮拖到 `ShopPanel` 的 `closeButton` 字段。
5. 保存为 `ShopPanel.prefab`。

推荐预制体名和类名一致：

```text
ShopPanel.cs
ShopPanel.prefab
```

### 4.3 注册资源

使用 Addressables 时：

```text
MyUI -> Settings & Registration
```

选择面板目录或拖入预制体，然后注册。地址会自动使用类型名：

```text
ShopPanel
```

### 4.4 打开页面

```csharp
UIManager.OpenPanel<ShopPanel>();
```

如果使用 Resources：

```text
Resources/UIPanel/ShopPanel.prefab
```

资源路径为：

```text
UIPanel/ShopPanel
```

## 5. 关闭页面

页面内部关闭自己：

```csharp
Close();
```

页面外部关闭指定类型：

```csharp
UIManager.ClosePanel<ShopPanel>();
```

关闭全部页面：

```csharp
UIManager.CloseAll();
```

返回上一页：

```csharp
UIManager.Back();
```

`Back()` 只有在当前页面有返回历史时才会关闭页面。根页面不会被误关。

## 6. 生命周期

`UIPanel` 的生命周期方法都可以按需覆写：

```csharp
protected override void OnInit()
{
}

protected override void OnOpen(object userData)
{
}

protected override void OnShow()
{
}

protected override void OnCover()
{
}

protected override void OnReveal()
{
}

protected override void OnPause()
{
}

protected override void OnResume()
{
}

protected override void OnTick(float deltaTime)
{
}

protected override void OnHide()
{
}

protected override void OnClose(bool pooled)
{
}

protected override void OnDestroyed()
{
}
```

最常用的是四个：

| 方法 | 用途 |
|---|---|
| `OnInit` | 获取控件、绑定按钮，只调用一次 |
| `OnOpen` | 接收打开参数，每次打开都会调用 |
| `OnClose` | 清理事件、协程和临时状态 |
| `OnTick` | 页面每帧更新 |

## 7. 打开参数

`OpenPanel<T>()` 可以不传参数：

```csharp
UIManager.OpenPanel<ShopPanel>();
```

也可以传一个对象：

```csharp
public class ItemDetailOpenData
{
    public long ItemInstanceId;
    public string Source;
}

UIManager.OpenPanel<ItemDetailPanel>(new ItemDetailOpenData
{
    ItemInstanceId = 10021,
    Source = "Equipment",
});
```

页面在 `OnOpen` 中接收：

```csharp
protected override void OnOpen(object userData)
{
    if (userData is ItemDetailOpenData data)
    {
        mItemInstanceId = data.ItemInstanceId;
        RefreshDetail();
    }
}
```

### 7.1 两种调用的区别

```csharp
UIManager.OpenPanel<ShopPanel>();
```

表示：

- 不传数据
- 使用面板默认配置
- `OnOpen` 收到的 `userData` 是 `null`

```csharp
UIManager.OpenPanel<ItemDetailPanel>(new ItemDetailOpenData
{
    ItemInstanceId = itemId,
});
```

表示：

- 把 `ItemDetailOpenData` 传给 `OnOpen`
- 页面根据传入数据刷新
- 其余行为仍使用面板默认配置

### 7.2 成功与失败回调

如果调用方需要知道打开结果：

```csharp
UIManager.OpenPanel<ShopPanel>(
    data: null,
    onOpened: panel => Debug.Log("打开成功"),
    onFailed: error => Debug.LogError(error));
```

`onOpened` 在页面打开成功后调用。

`onFailed` 在资源加载或初始化失败时调用。

普通页面不需要传这两个回调。

## 8. 单次覆盖输入、暂停和导航

```csharp
UIManager.OpenPanel<ItemDetailPanel>(
    data: detailData,
    blockInput: true,
    pauseBelow: UIPauseBelowMode.Never,
    openMode: UIOpenMode.Overlay);
```

这三个参数只覆盖当前这一次打开：

```text
blockInput    输入阻断
pauseBelow   是否暂停下层
openMode     导航方式
```

不传时使用面板自身配置。

推荐做法：

- 页面固定行为写在 `[UIPanel]`
- 只有当前场景需要特殊行为时才在 `OpenPanel` 中覆盖

## 9. 面板配置

```csharp
using MyUI.Core;
using MyUI.Runtime;

[UIPanel(
    UILayer.Normal,
    OpenMode = UIOpenMode.Push)]
public class EquipmentPanel : UIPanel
{
}
```

### 9.1 BlockInput

| 值 | 说明 |
|---|---|
| `false` | 不创建输入阻断器，保留 Prefab 自身 Raycast 行为 |
| `true` | 创建全屏透明输入阻断器 |

### 9.2 PauseBelow

| 值 | 说明 |
|---|---|
| `Inherit` | 不在 Prefab 覆盖，使用 `[UIPanel]` 的值；代码也未设置时不暂停 |
| `Never` | 不暂停下层 |
| `Always` | 覆盖下层时暂停下层 |

### 9.3 OpenMode

| 值 | 说明 |
|---|---|
| `Overlay` | 覆盖当前页面，不增加返回层级；`Back()` 不会自动关闭 |
| `Push` | 保留当前页面并增加返回层级，默认值 |
| `Replace` | 关闭当前最高的可导航旧页面，不增加返回层级，并跳过 `Overlay` |

常用组合：

```csharp
// 全屏页面
[UIPanel(
    UILayer.Normal,
    BlockInput = true,
    PauseBelow = UIPauseBelowMode.Always)]

// 设置窗口
[UIPanel(
    UILayer.Popup,
    BlockInput = true,
    PauseBelow = UIPauseBelowMode.Never)]

// HUD
[UIPanel(
    UILayer.HUD,
    BlockInput = false,
    PauseBelow = UIPauseBelowMode.Never,
    OpenMode = UIOpenMode.Overlay)]

// Toast
[UIPanel(
    UILayer.Toast,
    BlockInput = false,
    PauseBelow = UIPauseBelowMode.Never,
    OpenMode = UIOpenMode.Overlay,
    AllowMulti = true)]
```

## 10. 层级

渲染顺序：

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
| `Background` | 背景图 |
| `Normal` | 主页面、背包、装备、关卡选择 |
| `HUD` | 血量、金币、任务追踪 |
| `Popup` | 设置、确认框、商店窗口 |
| `Guide` | 新手引导 |
| `System` | 加载遮罩、断线提示 |
| `Toast` | 飘字和短消息 |

## 11. 按钮绑定

MyUI 支持两种绑定方式。正常业务优先使用第一种。

### 11.1 Unity 原生方式（推荐）

推荐正常业务优先使用：

```csharp
[SerializeField] private Button startButton;

protected override void OnInit()
{
    startButton.onClick.AddListener(OnStartClicked);
}

private void OnStartClicked()
{
    StartGame();
}

protected override void OnClose(bool pooled)
{
    startButton.onClick.RemoveListener(OnStartClicked);
}
```

优点：

- Unity 原生写法
- 不需要记住节点名字
- 拖引用时能直接检查

### 11.2 监听器清理

按钮监听应该在页面打开时添加，在页面关闭时移除：

```csharp
protected override void OnOpen(object userData)
{
    startButton.onClick.AddListener(OnStartClicked);
}

protected override void OnClose(bool pooled)
{
    startButton.onClick.RemoveListener(OnStartClicked);
}
```

在页面池化复用时，`OnInit` 不会再次执行，因此动态监听必须按需在 `OnOpen` 中重新添加。

## 12. 共享业务数据

简单页面可以直接在 `OnOpen` 中处理数据。

当多个页面需要共享数据时，建议增加业务 Service：

下面的 `GameServices`、`ItemViewData`、`ItemDetailViewData` 都是项目自己的业务类型示例，不是 MyUI API。

```csharp
public class InventoryService
{
    public event System.Action Changed;

    public IReadOnlyList<ItemViewData> GetEquipments()
    {
        // 返回装备列表
    }

    public ItemDetailViewData GetDetail(long instanceId)
    {
        // 返回道具详情
    }

    public void UseItem(long instanceId)
    {
        // 修改背包数据
        Changed?.Invoke();
    }
}
```

列表页：

```csharp
public class EquipmentPanel : UIPanel
{
    private InventoryService mInventory;

    protected override void OnInit()
    {
        mInventory = GameServices.Inventory;
        mInventory.Changed += RefreshList;
    }

    protected override void OnOpen(object userData)
    {
        RefreshList();
    }

    protected override void OnClose(bool pooled)
    {
        mInventory.Changed -= RefreshList;
    }

    private void RefreshList()
    {
        var items = mInventory.GetEquipments();
        // 刷新列表
    }

    private void OpenDetail(long instanceId)
    {
        UIManager.OpenPanel<ItemDetailPanel>(new ItemDetailOpenData
        {
            ItemInstanceId = instanceId,
            Source = "Equipment",
        });
    }
}
```

详情页：

```csharp
public class ItemDetailPanel : UIPanel
{
    private InventoryService mInventory;
    private long mInstanceId;

    protected override void OnInit()
    {
        mInventory = GameServices.Inventory;
    }

    protected override void OnOpen(object userData)
    {
        if (userData is not ItemDetailOpenData data)
        {
            return;
        }

        mInstanceId = data.ItemInstanceId;
        RefreshDetail(mInventory.GetDetail(mInstanceId));
    }

    private void RefreshDetail(ItemDetailViewData detail)
    {
        // 刷新详情 UI
    }
}
```

业务层只负责数据：

```text
InventoryService
  -> 保存背包数据
  -> 提供查询
  -> 处理装备、使用、出售

EquipmentPanel
  -> 显示装备列表
  -> 发起打开详情请求

ItemDetailPanel
  -> 根据 ItemInstanceId 查询详情
```

UI 不保存真实背包数据，关闭页面后数据仍然存在。

## 13. 实例池

默认情况下，关闭后的页面会进入实例池。

下次打开时：

- 不重新加载资源
- 不重新创建页面实例
- 不再次调用 `OnInit`
- 会再次调用 `OnOpen`

因此：

- 在 `OnInit` 中绑定固定事件
- 在 `OnOpen` 中刷新当前数据
- 在 `OnClose` 中移除临时订阅、协程和定时器

需要每次重新创建页面时：

```csharp
[UIPanel(Poolable = false)]
```

## 14. 页面内多级内容

一个页面内部的 Tab、列表和详情区，建议继续放在同一个 Prefab 和同一个 `UIPanel` 中。

例如装备页：

```text
EquipmentPanel
  CategoryView
  ItemListView
  DetailView
```

这些区域通常不需要各自动态加载，也不需要分别进入全局返回栈。

如果详情需要独立占用整屏、独立模态或从多个页面打开，再拆成独立页面：

```csharp
UIManager.OpenPanel<ItemDetailPanel>(detailData);
```

选择原则：

| 内容 | 建议 |
|---|---|
| Tab、列表、侧栏 | 留在当前页面内部 |
| 同一页面内的详情面板 | 留在当前页面内部 |
| 全屏详情页 | 独立 `UIPanel` |
| 模态确认框 | 独立 `UIPanel` |
| HUD | 独立 `UIPanel` |
| Toast | 独立 `UIPanel` |

## 15. 资源加载

### Addressables

默认模式。

```text
MyUI -> Settings & Registration
```

选择面板目录或预制体，注册后地址等于类型名。

### Resources

预制体放入：

```text
Resources/UIPanel/
```

打开地址：

```text
UIPanel/ShopPanel
```

### 自定义加载器

实现 `IAssetLoader` 后：

```csharp
UIManager.Init(new MyAssetLoader());
```

## 16. 常见问题

### 页面打开后点击穿透

检查 `BlockInput`：

```csharp
[UIPanel(
    UILayer.Popup,
    BlockInput = true)]
```

### 页面遮挡了下层，但下层没有暂停

检查 `PauseBelow`：

```csharp
PauseBelow = UIPauseBelowMode.Always
```

### 页面被关闭后再次打开，数据没刷新

把刷新逻辑放在 `OnOpen`，不要只放在 `OnInit`。

### `Back()` 没有关闭页面

可能原因：

- 页面是 `Overlay`
- 页面不是通过 `Push` 打开
- 页面是根页面
- 页面自己实现了 `IUINavigationHandler`

### 资源加载失败

检查：

- 类名
- 预制体名
- Addressables 地址
- 是否已经注册

推荐保持一致：

```text
ShopPanel.cs
ShopPanel.prefab
Address = ShopPanel
```

## 17. API 速查

```csharp
// 初始化
UIManager.Init();

// 打开
UIManager.OpenPanel<ShopPanel>();
UIManager.OpenPanel<ItemDetailPanel>(detailData);

// 打开并覆盖本次行为
UIManager.OpenPanel<ItemDetailPanel>(
    data: detailData,
    blockInput: true,
    pauseBelow: UIPauseBelowMode.Never,
    openMode: UIOpenMode.Overlay);

// 关闭
Close();
UIManager.ClosePanel<ShopPanel>();
UIManager.ClosePanel(panel);
UIManager.CloseAll();

// 返回
UIManager.Back();

// 查询
UIManager.GetPanel<ShopPanel>();
UIManager.IsOpen<ShopPanel>();
```

## 18. 推荐学习顺序

```text
1. 初始化 UIManager
2. 做一个只有打开和关闭的页面
3. 学会 OnInit / OnOpen / OnClose
4. 学会传数据
5. 学会 BlockInput、PauseBelow、OpenMode
6. 学会层级
7. 学会 Push / Overlay / Replace
8. 学会 Addressables 注册
9. 学会实例池复用注意事项
10. 再引入 Service 处理共享业务数据
```

按这个顺序使用，最小的 MyUI 页面只需要：

```text
一个 UIPanel 子类
一个 Prefab
一次 OpenPanel
```
