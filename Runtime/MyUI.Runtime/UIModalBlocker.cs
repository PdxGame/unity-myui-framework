using UnityEngine;
using UnityEngine.UI;

namespace MyUI.Runtime
{
    /// <summary>
    /// 运行时模态射线阻断器。只在模态面板下创建一个透明全屏 Image，
    /// 不修改业务节点结构，也不会影响视觉。
    /// </summary>
    internal static class UIModalBlocker
    {
        private const string BlockerName = "__MyUI_ModalBlocker";

        public static void Apply(RectTransform panelRoot, bool active)
        {
            if (panelRoot == null)
            {
                return;
            }

            Transform child = panelRoot.Find(BlockerName);
            if (!active)
            {
                if (child != null)
                {
                    child.gameObject.SetActive(false);
                }

                return;
            }

            RectTransform blocker;
            if (child == null)
            {
                var blockerGo = new GameObject(BlockerName, typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(LayoutElement), typeof(Image));
                blocker = (RectTransform)blockerGo.transform;
                blocker.SetParent(panelRoot, false);
            }
            else
            {
                blocker = child as RectTransform;
                if (blocker == null)
                {
                    Object.Destroy(child.gameObject);
                    var blockerGo = new GameObject(BlockerName, typeof(RectTransform), typeof(CanvasRenderer),
                        typeof(LayoutElement), typeof(Image));
                    blocker = (RectTransform)blockerGo.transform;
                    blocker.SetParent(panelRoot, false);
                }
            }

            blocker.anchorMin = Vector2.zero;
            blocker.anchorMax = Vector2.one;
            blocker.pivot = new Vector2(0.5f, 0.5f);
            blocker.anchoredPosition = Vector2.zero;
            blocker.sizeDelta = Vector2.zero;
            blocker.SetAsFirstSibling();
            blocker.gameObject.SetActive(true);

            var layoutElement = blocker.GetComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var image = blocker.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
        }
    }
}
