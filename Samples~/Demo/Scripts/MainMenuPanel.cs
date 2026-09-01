using MyUI.Core;
using MyUI.Runtime;
using UnityEngine;

namespace MyUI.Examples
{
    /// <summary>
    /// 主菜单页（全屏）：标题 + 开始游戏 / 设置 / 退出三个按钮。
    /// 打开新页面只需 OpenPanel 一句（返回路径由框架自动记录，Back() 可回退）。
    /// </summary>
    [UIPanel(UILayer.Normal, FullScreen = true)]
    public sealed class MainMenuPanel : UIPanel
    {
        protected override void OnInit()
        {
            BindButton("Btn_Start", OnStartClicked);
            BindButton("Btn_Settings", OnSettingsClicked);
            BindButton("Btn_Quit", OnQuitClicked);
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