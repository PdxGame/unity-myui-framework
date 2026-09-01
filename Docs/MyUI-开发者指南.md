# MyUI 开发者指南（dev 分支）

> 本分支服务于**框架开发者**：包含单元测试、源码分析报告与本指南。
> 使用者请切到 `main` 分支（稳定的使用版本，不含开发材料）。
> 分支约定：**main = 使用稳定版**（README + 使用手册 + 代码）；**dev = 开发者版**（main 全部 + Tests/ + 源码分析报告 + 本指南）。

## 1. 单元测试

- 运行：`Window → General → Test Runner → EditMode → Run All`（20 条）；
- 覆盖：打开/关闭序列、单实例聚焦、双开合并、加载中取消、延迟关闭、遮挡/暂停翻转、池复用（SerialId 刷新）、池容量/过期、导航栈、CloseAll 逆序、失败路径、多实例；
- 依赖：`com.unity.test-framework`（仅本分支需要，main 分支的 package.json 不包含）；
- 约定：**任何核心（Core）改动必须保证全绿**再提交；新增行为先补用例。

## 2. 代码结构导览

```
Runtime/MyUI.Core/               纯 C# 状态机（零 UnityEngine）
  UIManagerCore.cs               打开/关闭/合并/取消/遮挡/暂停/池/导航
  PanelRecord.cs / PanelState.cs 面板记录与状态
  UILayer.cs                     层级枚举（扩展 = 加成员，见使用手册 §6）
  IUIPanelFactory / IUIPanelView / IAssetLoader  三个抽象接口
Runtime/MyUI.Runtime/            Unity 胶水
  UIManager.cs                   门面：Init / OpenPanel / Close / Back / 事件
  UIPanel.cs                     面板基类：生命周期虚方法 + BindButton/Find + Inspector 配置
  UIRoot.cs                      层级 Canvas 创建（每层一个）
  ResourcesPanelLoader.cs        兼容加载器（地址 UIPanel/{类型名}）
Runtime/MyUI.Loaders.Addressables/  Addressables 加载器（默认加载方式）
Editor/MyUI.Editor/              UIManager 管理窗口（模式切换/面板注册）+ Inspector 调试
Samples~/Demo/                   使用示例（main/dev 两分支均含）
```

## 3. 扩展点

| 需求 | 做法 |
|---|---|
| 新增层级 | `UILayer` 枚举加成员（自动建 Canvas / 接入遮挡链，见使用手册 §6） |
| 自定义加载器 | 实现 `IAssetLoader`，`UIManager.Init(myLoader)` 传入 |
| 新面板 | 类继承 `UIPanel` → 同名预制体 → 命名三统一（类型名=预制体名=地址）→ 窗口②注册 |
| 编辑器功能 | 在 `Editor/MyUI.Editor/` 扩展（勿写入 Runtime） |

## 4. 命名与规范

- 统一 `UI` 全大写前缀：`UIManager / UIPanel / UILayer / UIRoot / UIAssetMode / IUIPanelFactory…`；
- 地址三统一：类型名 == 预制体文件名 == Addressables 地址；
- 加载失败必须输出 Console 红色错误（使用者可感知），不得静默。

## 5. 发布流程

1. 在 dev 开发并跑全绿单测；
2. 合并到 main 前核对：main 不应包含 `Tests/`、分析报告与本指南；`package.json` 不依赖 test-framework；
3. `git merge dev --no-ff` 到 main 时**选择性同步**（保留 main 干净结构：若改的是使用者可见内容（代码/手册/README），同步过去；纯开发者内容留在 dev）；
4. 推送两个分支。

## 6. 源码分析报告（对照依据）

- `Docs/GameFramework-UI-模块源码分析报告.md`
- `Docs/QFramework_UIKit_源码分析报告.md`