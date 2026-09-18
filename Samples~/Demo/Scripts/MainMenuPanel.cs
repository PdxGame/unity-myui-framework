using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI.Examples
{
    /// <summary>
    /// 主菜单页（全屏）：标题 + 开始游戏 / 设置 / 退出三个按钮。
    /// 打开新页面只需 OpenPanel 一句（返回路径由框架自动记录，Back() 可回退）。
    /// </summary>
    [UIPanel(UILayer.Normal, FullScreen = true)]
    public class MainMenuPanel : UIPanel
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

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

            startButton ??= Find<Button>("Btn_Start");
            settingsButton ??= Find<Button>("Btn_Settings");
            quitButton ??= Find<Button>("Btn_Quit");

            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettingsClicked);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);
        }

        private void UnbindButtonListeners()
        {
            if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OnSettingsClicked);
            if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitClicked);
        }

        private void OnStartClicked()
        {
            UIManager.OpenPanel<GamePlayPanel>();
        }

        private void OnSettingsClicked()
        {
            UIManager.OpenPanel<SettingsPanel>();
        }

        private void OnQuitClicked()
        {
            UIManager.CloseAll();
            Debug.Log("[MainMenu] Quit (demo only)");
        }
    }
}
