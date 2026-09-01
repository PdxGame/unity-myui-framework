# MyUI Framework (UPM 包)

轻量 Unity UI 管理框架（Unity 2022.3+，UGUI + TextMeshPro）。

- 核心 **纯 C#**（零 UnityEngine 依赖，20 条 EditMode 单测）；生命周期 / 固定层级 / 遮挡回调（OnCover/OnPause）/ 实例池 / 自动返回导航；
- 固定层级结构，每层独立 Canvas：跨层面板互不干扰，同层内可合批；
- **默认 Addressables 加载**（可选程序集，缺失自动回退 Resources）。

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

> 启动 = 入口处一行 `UIManager.Init()`（唯一启动方式，幂等；流程：创建 UI 根、自动创建 EventSystem、绑定加载器）。
> Sample 附带的 Demo 启动器（`RuntimeInitializeOnLoadMethod`）只是**示例入口**（负责演示打开主菜单），正式项目在自己入口调用 Init() 即可。
> Demo 走 Resources 兼容模式（导入的 Sample 位于 `Resources/UIPanel/` 子目录，地址 = `UIPanel/{类型名}`），克隆即跑零配置。
> Demo 文案为英文，使用 TMP 默认字体（LiberationSans SDF）——若编辑器提示导入 TMP Essentials，执行 `Window → TextMeshPro → Import TMP Essential Resources`（TMP 标准流程一次即可）。

## 编辑器菜单（MyUI）

菜单 **`MyUI → Settings & Registration`** 打开管理窗口：

- **① 加载模式**：勾选 Addressables（默认）/ Resources → 「保存并应用模式」即生效（自动重编译，秒级），无需改任何源码；
- **② Addressables 面板注册**：选一个目录（或直接拖入预制体，可一次拖多个）→ 组名可填（默认 `UIPanels`，不存在自动创建）→ 自动入组、**地址自动 = 类型名**、重复自动跳过。

## 做一个新页面（5 步）

1. 写一个类继承 `UIPanel`（配置在 `[UIPanel(...)]` 特性或预制体 Inspector 的 UIPanel 组件上选：层级/全屏/池化/入栈）；
2. Unity 里搭同名预制体，根节点挂上这个类；
3. 放进你自己定的目录；
4. 生产（Addressables）：菜单 `MyUI → Settings & Registration` → ②区选该目录（或拖入预制体）→「注册」；地址自动=类型名；
5. 代码里：

```csharp
using MyUI.Runtime;

UIManager.Init();                       // 启动一次（入口处；幂等）
UIManager.OpenPanel<MyPagePanel>(数据);  // 打开（返回路径自动记录）
panel.Close();                           // 关闭
UIManager.Back();                        // 返回（有历史时才关当前页）
UIManager.CloseAll();                    // 清场
```

加载模式由窗口①配置（`MyUI → Settings & Registration`，缺配置=Addressables）；`UIManager.AssetMode` 可代码覆盖；`Init(loader)` 可传入自定义加载器。

## 目录结构

```
├─ package.json            包描述（UPM 元数据 + 依赖 + Sample 清单）
├─ Runtime/
│   ├─ MyUI.Core/          纯 C# 状态机：打开/关闭/合并/取消/遮挡/暂停/池/导航
│   ├─ MyUI.Runtime/       UIPanel 基类、UIManager 门面、UIRoot 分层、加载器、Tester
│   └─ MyUI.Loaders.Addressables/  Addressables 加载器（可选程序集，默认加载方式）
├─ Editor/MyUI.Editor/     Inspector 调试按钮、面板注册菜单
├─ Samples~/Demo/          Demo：面板脚本（Scripts/）+ 预制体（Resources/UIPanel/，TMP 默认字体）
└─ Tests/                   EditMode 单测（20 条）
Docs/                       使用手册与实现原理 + 两家框架源码分析报告
```

## 单测

Test Runner → EditMode → Run All（20 条：打开/关闭序列、聚焦、双开合并、加载中取消、延迟关闭、遮挡/暂停翻转、池复用/容量/过期、导航栈、CloseAll 逆序、失败路径、多实例）。

## 文档与许可

- `Docs/MyUI-使用手册与实现原理.md`：完整手册（API/生命周期/配置/性能）与实现原理；
- `Docs/GameFramework-UI-模块源码分析报告.md`、`Docs/QFramework_UIKit_源码分析报告.md`：设计对照的源码级考据；
- 框架代码 **MIT**；Demo 使用 TMP 内置默认字体（无第三方字体资产，包体积保持纯脚本级）。