# MyUI —— 自研 Unity UI 管理框架

一个从零实现的 UI 管理框架：**打开/关闭/层级/遮挡回调/实例池/返回导航**交给框架，页面逻辑写在自己继承 `UiPanel` 的类里。

- 核心（状态机）是**纯 C#**，20 条 EditMode 单测直接跑（不依赖 Play 与 Unity 资源）；
- 框架本体零第三方依赖；默认用 **Addressables** 加载（可选程序集，缺失自动回退 Resources）；
- 完整生命周期与遮挡语义；分层 Canvas 符合官方"动静分离"最佳实践。

## 极简用法

```csharp
// 启动：全自动（游戏运行时框架自动初始化，无需写任何代码）
// 打开页面（默认地址 = 类型名，不用写地址）
UiManager.OpenPanel<MainMenuPanel>();
// 关闭
panel.Close();            // 面板自关
UiManager.CloseAll();     // 清场（切场景）
// 返回（自动记录返回路径）
UiManager.Back();
```

## 做一个新页面（5 步）

1. 写一个类继承 `UiPanel`（可选 `[UiPanel(...)]` 特性，或直接在预制体 Inspector 上配置层级/全屏/池化/入栈）；
2. Unity 里搭同名预制体，根节点挂上这个类；
3. 放进 `Assets/MyUI/Panels/`；
4. 菜单 `MyUI → Panels → Add to Addressables` 注册（地址=类型名，幂等）；
5. 代码里 `UiManager.OpenPanel<MyPagePanel>(数据);`。

## 构成

```
Assets/Scripts/MyUI/
├─ Core/        纯 C#：状态机/遮挡/池/导航（零 Unity 依赖）
├─ Runtime/     UiPanel 基类、UiManager 门面、UiRoot 分层、加载器、Tester
├─ Loaders/     Addressables 加载器（可选程序集）
├─ Editor/      Inspector 调试按钮、手动注册菜单
├─ Examples/    示例面板（主菜单/游戏页/设置弹窗）与 DemoBoot
└─ Tests/       EditMode 单测（20 条）
```

## 文档

- `Docs/MyUI-使用手册与实现原理.md`：完整使用手册（API/生命周期/配置/性能）+ 实现原理；
- `Docs/GameFramework-UI-模块源码分析报告.md` / `Docs/QFramework_UIKit_源码分析报告.md`：两家框架源码级考据，本框架的设计对照依据。

## 测试

- **单测**：Test Runner → EditMode → Run All（20 条）；
- **Play 演示**：打开 `Scenes/SampleScene` → Play：主菜单（我的菜单）→ 开始游戏 → 返回 → 设置 → 关闭 → 退出。