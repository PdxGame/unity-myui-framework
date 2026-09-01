# MyUI Framework (UPM 包)

轻量 Unity UI 管理框架（Unity 2022.3+，UGUI + TextMeshPro）。

- **核心纯 C#**（零 UnityEngine 依赖，20 条 EditMode 单测）；生命周期 / 固定层级（每层独立 Canvas）/ 遮挡回调（OnCover/OnPause）/ 实例池 / 自动返回导航；
- **默认 Addressables 加载**（可选程序集，缺失自动回退 Resources）；
- 分层 Canvas 符合官方「动静分离」最佳实践。

## 安装

**方式 A：UPM（推荐）** —— 你的项目 `Window → Package Manager → ＋ → Add package from git URL…`：

```
https://github.com/PdxGame/unity-myui-framework.git
```

依赖（ugui / textmeshpro / addressables / test-framework）会自动安装。

**方式 B：拷贝** —— 把 `Runtime/`、`Editor/`、`Tests/` 与 `package.json` 拷进你项目任意目录（或 `Assets/` 下），去除 `Samples~`。

## 快速体验 Demo（1 分钟）

1. 安装包后，Package Manager 里找到 **MyUI Framework** → **Samples → MyUI Demo → Import**；
2. 导入完成后 **新建一个空场景**（File → New Scene → Basic，保存）；
3. 点 **Play**：自动弹出主菜单 → 开始游戏 → 返回 → 设置 → 关闭 → 退出。

> Demo 启动器是静态自举的（`RuntimeInitializeOnLoadMethod`）：不需要场景对象、不需要相机。
> Demo 走 Resources 兼容模式（导入的 Sample 位于 `Resources/` 子目录，地址=类型名），克隆即跑零配置。

## 做一个新页面（5 步）

1. 写一个类继承 `UiPanel`（配置在 `[UiPanel(...)]` 特性或预制体 Inspector 的 UiPanel 组件上选：层级/全屏/池化/入栈）；
2. Unity 里搭同名预制体，根节点挂上这个类；
3. 放进你自己定的目录；
4. 生产（Addressables）：在 Groups 窗口标注入组，**地址=类型名**（或 `MyUI → Panels → Add to Addressables` 自动注册，幂等）；
5. 代码里：

```csharp
using MyUI.Runtime;

UiManager.OpenPanel<MyPagePanel>(数据);  // 打开（返回路径自动记录）
panel.Close();                           // 关闭
UiManager.Back();                        // 返回（有历史时才关当前页）
UiManager.CloseAll();                    // 清场
```

框架启动全自动（`AutoBoot`，可关）。

## 目录结构

```
├─ package.json            包描述（UPM 元数据 + 依赖 + Sample 清单）
├─ Runtime/
│   ├─ MyUI.Core/          纯 C# 状态机：打开/关闭/合并/取消/遮挡/暂停/池/导航
│   ├─ MyUI.Runtime/       UiPanel 基类、UiManager 门面、UiRoot 分层、加载器、Tester
│   └─ MyUI.Loaders.Addressables/  Addressables 加载器（可选程序集，默认加载方式）
├─ Editor/MyUI.Editor/     Inspector 调试按钮、面板注册菜单
├─ Samples~/Demo/          Demo：面板脚本（Scripts/）+ 预制体（Resources/）+ 字体（Fonts/）
└─ Tests/                   EditMode 单测（20 条）
Docs/                       使用手册与实现原理 + 两家框架源码分析报告
```

## 单测

Test Runner → EditMode → Run All（20 条：打开/关闭序列、聚焦、双开合并、加载中取消、延迟关闭、遮挡/暂停翻转、池复用/容量/过期、导航栈、CloseAll 逆序、失败路径、多实例）。

## 文档与许可

- `Docs/MyUI-使用手册与实现原理.md`：完整手册（API/生命周期/配置/性能）与实现原理；
- `Docs/GameFramework-UI-模块源码分析报告.md`、`Docs/QFramework_UIKit_源码分析报告.md`：设计对照的源码级考据；
- 框架代码 **MIT**；Demo 内含 Smiley Sans 开源字体（OFL-1.1）。