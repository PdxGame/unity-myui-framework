using MyUI.Core;
using MyUI.Runtime;

namespace MyUI.Examples
{
    /// <summary>
    /// 游戏页（全屏）：从主菜单「开始游戏」进入；点「返回主菜单」关闭自己露出主菜单。
    /// </summary>
    [UIPanel(UILayer.Normal, FullScreen = true)]
    public sealed class GamePlayPanel : UIPanel
    {
        protected override void OnInit()
        {
            BindButton("Btn_Back", OnBackClicked);
        }

        private void OnBackClicked()
        {
            Close();
        }
    }
}