# unity-myui-framework

自研 Unity UI 管理框架（含一个零配置的最小 Demo）。

- **核心（状态机）纯 C#**，20 条 EditMode 单测，不依赖 Play 与 Unity 资源；
- 生命周期 / 层级 / 遮挡回调（OnCover/OnPause）/ 实例池 / 自动返回导航；
- 分层 Canvas（每层独立 Canvas，符合官方"动静分离"最佳实践）；
- 默认 **Addressables** 加载（可选程序集，缺失自动回退 Resources）；本仓库 Demo 走 Resources 兼容模式，**克隆即跑，零配置**。

## 克隆后运行 Demo（三步）

1. `git clone` 后，用 Unity Hub（2022.3+）**Add project from disk** 打开；
2. 首次导入完成后，**新建一个空场景**（File → New Scene → Basic，Ctrl+S 保存）；
3. 点 **Play**：自动弹出「我的菜单」→ 开始游戏 → 返回 → 设置 → 关闭 → 退出。

> 原理：`Assets/Scripts/MyUI/Examples/DemoBoot.cs` 是静态自举启动器（`RuntimeInitializeOnLoadMethod`），不需要场景对象，也不需要相机。

## 做一个新页面（5 步）

1. 写一个类继承 `UiPanel`（配置可在 `[UiPanel(...)]` 特性或预制体 Inspector 的 UiPanel 组件上选：层级/全屏/池化/入栈）；
2. Unity 里搭同名预制体，根节点挂上这个类；
3. 放进 `Assets/Resources/`（Demo 模式，地址=类型名）；生产用 Addressables 时放任意目录并在 Groups 窗口标注入组（地址=类型名）；
4. （Addressables 模式）菜单 `MyUI → Panels → Add to Addressables` 注册，幂等；
5. 代码里 `UiManager.OpenPanel<MyPagePanel>(数据);` 打开。

## 目录

```
Assets/
├─ Scenes/                  （Demo 不需要场景文件）
├─ Resources/               Demo 面板预制体（地址=类型名）
├─ Scripts/MyUI/
│   ├─ Core/                纯 C#：状态机/遮挡/池/导航（零 Unity 依赖）
│   ├─ Runtime/             UiPanel 基类、UiManager 门面、UiRoot 分层、加载器、Tester
│   ├─ Loaders/Addressables/ Addressables 加载器（可选程序集，默认加载方式）
│   ├─ Editor/              Inspector 调试按钮、手动注册菜单
│   ├─ Examples/            示例面板与 DemoBoot
│   └─ Tests/               EditMode 单测（20 条）
├─ Art/Font/SmileySans/     Smiley Sans 开源字体（OFL-1.1，Demo 中文显示用）
└─ TextMesh Pro/Resources/  TMP 标准资产
Docs/                       使用手册与实现原理 + 两家框架源码分析报告
```

## 单测

Unity Test Runner → EditMode → Run All（20 条：打开/关闭序列、聚焦、合并、取消、延迟关闭、遮挡/暂停翻转、池复用、导航、失败路径、多实例）。

## 许可

框架代码 MIT（见 LICENSE）。字体 Smiley Sans 为 [OFL-1.1](https://github.com/SmileySansOfficial/SmileySans) 开源字体（许可见字体资产内嵌信息）。