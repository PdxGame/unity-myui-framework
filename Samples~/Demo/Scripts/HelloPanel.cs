using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;

namespace MyUI.Examples
{
    /// <summary>
    /// 【教学示例】面板脚本 = 继承 UiPanel 的类。
    /// 流程：① 建这个类 → ② 搭同名预制体 → ③ 挂本脚本到预制体根 → ④ OpenPanel 打开。
    /// [UiPanel] 特性可省略（默认：Normal 层、非全屏、单实例、可入池）。
    /// </summary>
    [UiPanel(UiLayer.Popup)] // 放在弹窗层（可选）
    public sealed class HelloPanel : UiPanel
    {
        protected override void OnInit()
        {
            BindButton("Btn_Close", OnCloseClicked);
        }

        private void OnCloseClicked()
        {
            Close();
        }

        protected override void OnOpen(object userData)
        {
            Debug.Log("[HelloPanel] 打开，收到数据 = " + (userData == null ? "null" : userData.ToString()));
        }
    }
}