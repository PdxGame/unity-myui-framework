using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI.Examples
{
    /// <summary>
    /// 游戏页（全屏）：从主菜单「开始游戏」进入；点「返回主菜单」关闭自己露出主菜单。
    /// </summary>
    [UIPanel(UILayer.Normal, FullScreen = true)]
    public class GamePlayPanel : UIPanel
    {
        [SerializeField] private Button backButton;

        protected override void OnOpen(object userData)
        {
            BindButtonListeners();
        }

        protected override void OnClose(bool pooled)
        {
            UnbindButtonListeners();
        }

        private void BindButtonListeners()
        {
            UnbindButtonListeners();
            backButton ??= Find<Button>("Btn_Back");
            if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
        }

        private void UnbindButtonListeners()
        {
            if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
        }

        private void OnBackClicked()
        {
            Close();
        }
    }
}
