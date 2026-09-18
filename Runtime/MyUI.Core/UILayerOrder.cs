namespace MyUI.Core
{
    /// <summary>
    /// 层级渲染与遮挡顺序。枚举值保留序列化兼容，显示顺序在此集中定义。
    /// </summary>
    public static class UILayerOrder
    {
        public static int GetOrder(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.Background:
                    return 0;
                case UILayer.Normal:
                    return 1;
                case UILayer.HUD:
                    return 2;
                case UILayer.Popup:
                    return 3;
                case UILayer.Guide:
                    return 4;
                case UILayer.System:
                    return 5;
                case UILayer.Toast:
                    return 6;
                default:
                    return (int)layer;
            }
        }

        public static bool IsAbove(UILayer left, UILayer right)
        {
            return GetOrder(left) > GetOrder(right);
        }
    }
}
