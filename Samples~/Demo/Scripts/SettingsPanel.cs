using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI.Examples
{
    /// <summary>
    /// 设置弹窗（Popup 非全屏）：关闭按钮。验证非全屏弹窗只遮挡不暂停下层。
    /// </summary>
    [UIPanel(UILayer.Popup, InputMode = UIInputMode.Modal, PauseBelow = UIPauseBelowMode.Never)]
    public class SettingsPanel : UIPanel
    {
        [SerializeField] private Button closeButton;

        protected override void OnOpen(object userData)
        {
            BindButtonListeners();
            Debug.Log("[SettingsPanel] Settings opened");
        }

        protected override void OnClose(bool pooled)
        {
            UnbindButtonListeners();
        }

        private void BindButtonListeners()
        {
            UnbindButtonListeners();
            closeButton ??= Find<Button>("Btn_Close");
            if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
        }

        private void UnbindButtonListeners()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
        }

        private void OnCloseClicked()
        {
            Close();
        }

        protected override void OnHide()
        {
            Debug.Log("[SettingsPanel] Closing");
        }
    }
}
