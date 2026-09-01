using System.Runtime.CompilerServices;

// 允许 MyUI.Runtime（Unity 胶水程序集）访问 Core 的 internal 成员
// （如 PanelRecord 的 internal setter，供 Inspector 配置在 AttachView 时覆盖）。
[assembly: InternalsVisibleTo("MyUI.Runtime")]