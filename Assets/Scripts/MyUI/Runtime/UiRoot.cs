using System;
using System.Collections.Generic;
using MyUI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI.Runtime
{
    /// <summary>
    /// UI 总根：常驻（DontDestroyOnLoad）；启动时为每个 UiLayer 创建一个 Canvas 子节点。
    /// 每层 sortingOrder = 层索引 x LayerOrderStep（100），层内面板按打开顺序用 SiblingIndex 排序。
    /// （QFramework UIRoot 固定层级思路 + GameFramework 每容器独立 Canvas 排序思路的结合。）
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiRoot : MonoBehaviour
    {
        /// <summary>相邻层 Canvas 的 sortingOrder 步长。</summary>
        public const int LayerOrderStep = 100;

        private static UiRoot _instance;

        public static UiRoot Instance => _instance;

        private readonly Dictionary<UiLayer, RectTransform> _layers = new Dictionary<UiLayer, RectTransform>();

        /// <summary>确保根节点存在（没有则自动创建并常驻）。</summary>
        public static UiRoot EnsureCreated()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("MyUI Root");
            _instance = go.AddComponent<UiRoot>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            foreach (UiLayer layer in Enum.GetValues(typeof(UiLayer)))
            {
                var layerGo = new GameObject("Layer_" + layer);
                layerGo.transform.SetParent(transform, false);
                var canvas = layerGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = (int)layer * LayerOrderStep;
                layerGo.AddComponent<GraphicRaycaster>();
                _layers[layer] = (RectTransform)layerGo.transform;
            }
        }

        /// <summary>取某层根节点（用于挂接面板）。</summary>
        public RectTransform GetLayerRoot(UiLayer layer)
        {
            return _layers.TryGetValue(layer, out RectTransform root) ? root : null;
        }
    }
}