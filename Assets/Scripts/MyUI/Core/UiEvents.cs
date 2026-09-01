using System;

namespace MyUI.Core
{
    /// <summary>面板打开完成事件参数（GameFramework OpenUIFormSuccessEventArgs 思路）。</summary>
    public sealed class PanelOpenedEventArgs : EventArgs
    {
        public PanelOpenedEventArgs(PanelRecord record)
        {
            Record = record;
        }

        public PanelRecord Record { get; }
    }

    /// <summary>面板关闭完成事件参数（GameFramework CloseUIFormCompleteEventArgs 思路）。</summary>
    public sealed class PanelClosedEventArgs : EventArgs
    {
        public PanelClosedEventArgs(PanelRecord record, bool pooled)
        {
            Record = record;
            Pooled = pooled;
        }

        public PanelRecord Record { get; }

        /// <summary>true = 已入池待复用；false = 已销毁。</summary>
        public bool Pooled { get; }
    }

    /// <summary>面板加载失败事件参数（GameFramework OpenUIFormFailureEventArgs 思路）。</summary>
    public sealed class PanelLoadFailedEventArgs : EventArgs
    {
        public PanelLoadFailedEventArgs(string panelName, string error)
        {
            PanelName = panelName;
            Error = error;
        }

        public string PanelName { get; }

        public string Error { get; }
    }
}