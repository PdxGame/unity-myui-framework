# MyUI Framework

MyUI 是一个面向 Unity 2022.3+ 的轻量 UI 管理框架，基于 UGUI 与 TextMeshPro，负责顶层面板的加载、生命周期、层级、输入阻断、逻辑暂停、实例池和返回导航。

框架核心为纯 C# 状态机，不依赖 UnityEngine；Unity Runtime、Addressables 加载器与编辑器工具分别通过独立程序集接入。

## 功能概览

- 固定层级与独立 Canvas：`Background / Normal / HUD / Popup / Guide / System / Toast`
- 面板生命周期：初始化、打开、显示、遮挡、暂停、恢复、关闭、销毁与实例池复用
- 输入阻断：`BlockInput` 控制是否创建全屏射线阻断器
- 逻辑暂停：`PauseBelow` 独立控制是否暂停下层面板
- 导航策略：`Overlay / Push / Replace`
- 数据传递：打开面板时通过 `userData` 传入，面板在 `OnOpen` 中接收
- 资源加载：默认 Addressables，可切换 Resources 或自定义 `IAssetLoader`
- 回归测试：核心状态机提供 EditMode 测试

## 安装

### Unity Package Manager

在 Unity 中打开：

`Window → Package Manager → + → Add package from git URL...`

输入：

```text
https://github.com/PdxGame/unity-myui-framework.git
```

### 本地拷贝

也可以将以下内容复制到项目的 `Packages` 或 `Assets` 目录：

```text
package.json
Runtime/
Editor/
Tests/
```

复制到 `Assets` 时应删除 `Samples~` 之外的包管理文件，并确保目录名不影响项目自身的程序集结构。

## 快速开始

如果你第一次接触 MyUI，建议先阅读：

[从零开始教程](Docs/MyUI-从零开始教程.md)

### 1. 初始化

在项目启动入口调用一次 `UIManager.Init()`：

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

`Init()` 会创建常驻 UI 根、EventSystem 和默认加载器。重复调用是幂等的。

### 2. 创建面板

面板类继承 `UIPanel`，通过 `[UIPanel]` 声明层级与行为：

```csharp
using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;
using UnityEngine.UI;

[UIPanel(
    UILayer.Normal,
    OpenMode = UIOpenMode.Push)]
public class EquipmentPanel : UIPanel
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

预制体根节点挂载该脚本，预制体名称与地址保持和类型名一致：

```text
EquipmentPanel.cs
EquipmentPanel.prefab
Addressables Address = EquipmentPanel
```

### 3. 打开与关闭

```csharp
UIManager.OpenPanel<EquipmentPanel>();
UIManager.OpenPanel<ItemDetailPanel>(new ItemDetailOpenData
{
    ItemInstanceId = itemId,
    Source = ItemSource.Equipment,
});

UIManager.Back();
UIManager.ClosePanel<EquipmentPanel>();
UIManager.CloseAll();
```

面板内部直接调用 `Close()` 即可关闭当前实例。

## 面板配置

`[UIPanel]` 与预制体 Inspector 均可配置面板。特性提供默认值，Inspector 可覆盖或收紧配置。

| 配置 | 取值 | 说明 |
|---|---|---|
| `Layer` | `UILayer` | 面板所在层级 |
| `BlockInput` | `true / false` | `true` 自动创建全屏透明阻断器；`false` 依赖 Prefab 自身射线设置 |
| `PauseBelow` | `Inherit / Never / Always` | `Inherit` 使用代码配置，两边都未配置时不暂停；`Never / Always` 强制执行 |
| `OpenMode` | `Overlay / Push / Replace` | 打开策略与返回层级规则，默认 `Push` |
| `Poolable` | `true / false` | 关闭后是否进入实例池 |
| `AllowMulti` | `true / false` | 是否允许同类型多实例，仅特性配置 |
| `Address` | `string` | 覆盖默认资源地址，仅特性配置 |

### 默认值

未显式配置时：

```text
PauseBelow  Inherit（代码也未配置）-> Never
OpenMode    Push
```

页面尺寸与锚点完全由 Prefab 决定。需要阻断输入时设置 `BlockInput = true`，需要暂停下层时设置 `PauseBelow = Always`。

### 常用组合

```csharp
// 全屏页面：布局由 Prefab 控制，输入、暂停显式配置
[UIPanel(
    UILayer.Normal,
    BlockInput = true,
    PauseBelow = UIPauseBelowMode.Always)]

// 窗口弹窗：按 Prefab 尺寸显示，阻断输入但不暂停下层
[UIPanel(
    UILayer.Popup,
    BlockInput = true,
    PauseBelow = UIPauseBelowMode.Never)]

// 常驻 HUD：不阻断、不暂停、不进入返回历史
[UIPanel(
    UILayer.HUD,
    BlockInput = false,
    PauseBelow = UIPauseBelowMode.Never,
    OpenMode = UIOpenMode.Overlay)]

// Toast：不阻断、不暂停、no navigation
[UIPanel(
    UILayer.Toast,
    BlockInput = false,
    PauseBelow = UIPauseBelowMode.Never,
    OpenMode = UIOpenMode.Overlay,
    AllowMulti = true)]
```

### 单次打开覆盖

需要临时改变行为时，可在打开时传入覆盖参数：

```csharp
UIManager.OpenPanel<ItemDetailPanel>(
    data: openData,
    blockInput: true,
    pauseBelow: UIPauseBelowMode.Never,
    openMode: UIOpenMode.Overlay);
```

## 层级

渲染顺序由 `UILayerOrder` 定义：

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
| `Background` | 场景底图、背景 |
| `Normal` | 主菜单、装备页、背包页等页面 |
| `HUD` | 金币、血量、任务追踪等常驻信息 |
| `Popup` | 设置、确认框、商店窗口 |
| `Guide` | 新手引导 |
| `System` | 加载遮罩、断线提示、强制更新 |
| `Toast` | 飘字与轻提示 |

每层拥有独立 Canvas。跨层渲染顺序由 Canvas Sorting Order 控制，同层面板按打开顺序排列。

## 生命周期

```text
OnInit
  -> OnOpen(userData)
  -> OnShow
  -> OnCover / OnReveal
  -> OnPause / OnResume
  -> OnTick
  -> OnHide
  -> OnClose(pooled)
  -> OnDestroyed
```

关键规则：

- `OnInit` 只在新实例创建后调用一次，池复用不会重复调用。
- `OnOpen` 每次打开都会调用，适合接收 `userData` 和刷新界面。
- `OnCover` 表示面板被其他覆盖型面板遮挡。
- `OnPause` 表示遮挡源声明了 `PauseBelow`，适合停止计时器、动画和逻辑更新。
- `OnTick` 只会在面板处于打开且未暂停状态时调用。
- `OnClose(pooled)` 中清理订阅、协程、事件和临时资源。

## 导航

### Push

保留当前页面，并登记一条返回路径：

```csharp
UIManager.OpenPanel<EquipmentPanel>(
    openMode: UIOpenMode.Push);
```

### Overlay

覆盖在当前页面之上，不增加返回层级：

```csharp
UIManager.OpenPanel<SettingsPanel>(
    openMode: UIOpenMode.Overlay);
```

Overlay 不会因为调用 `Back()` 而自动关闭；需要在面板内部关闭，或实现 `IUINavigationHandler` 处理返回。Overlay 也不会被 `Replace` 当作替换目标。

### Replace

新面板打开成功后替换当前最高的可导航页面，不增加返回层级：

```csharp
UIManager.OpenPanel<GamePlayPanel>(
    openMode: UIOpenMode.Replace);
```

替换会跨层选择目标，但会跳过 `Overlay` 页面。适合主菜单进入游戏、登录页进入大厅等页面替换场景。

## 实例池

- `Poolable = true` 的面板关闭后进入实例池。
- 下次打开优先复用实例，不重复加载资源，也不会再次调用 `OnInit`。
- 默认每个地址最多缓存 3 个闲置实例。
- 默认闲置超过 60 秒后销毁。
- 临时面板、一次性引导或含大量临时状态的页面可设置 `Poolable = false`。

## 资源加载

### Addressables

默认使用 Addressables。打开 `MyUI → Settings & Registration`：

1. 加载模式选择 `Addressables`。
2. 选择面板目录或拖入预制体。
3. 注册后，地址自动使用面板类型名。

建议保持一致：

```text
类型名 == Prefab 文件名 == Addressables 地址
```

### Resources

在窗口中选择 `Resources`，或将预制体放入 `Resources/UIPanel/`。

默认地址规则：

```text
UIPanel/{类型名}
```

### 自定义加载器

实现 `IAssetLoader` 后传入初始化入口：

```csharp
UIManager.Init(new MyCustomAssetLoader());
```

## 示例与测试

### Demo

通过 Package Manager 导入 `MyUI Demo` 示例，打开任意空场景后运行。示例包含主菜单、游戏页面和设置弹窗。

### EditMode 测试

打开：

`Window → General → Test Runner → EditMode`

运行 `MyUI.Tests.EditMode`。当前测试覆盖：

- 加载异常与失败回调
- 视图挂接异常与原始实例释放
- 实例池复用上下文刷新
- Dispose 统一释放
- Push、Overlay、Replace 导航
- 同层与跨层遮挡、暂停规则

## 文档

- [从零开始教程](Docs/MyUI-从零开始教程.md)
- [使用手册与实现原理](Docs/MyUI-使用手册与实现原理.md)
- [UI 行为配置](#面板配置)
- [导航策略](#导航)
- [资源加载](#资源加载)

## License

MIT
