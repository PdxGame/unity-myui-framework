using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;

namespace MyUI.Examples
{
    /// <summary>
    /// 设置弹窗（Popup 非全屏）：关闭按钮。验证非全屏弹窗只遮挡不暂停下层。
    /// </summary>
    [UiPanel(UiLayer.Popup)]
    public sealed class SettingsPanel : UiPanel
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
            Debug.Log("[SettingsPanel] Settings opened");
        }

        protected override void OnHide()
        {
            Debug.Log("[SettingsPanel] Closing");
        }
    }
}