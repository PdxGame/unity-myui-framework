using System;
using System.Collections.Generic;
using MyUI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MyUI.Runtime
{
    /// <summary>
    /// UI 总根：常驻（DontDestroyOnLoad）；启动时为每个 UILayer 创建一个 Canvas 子节点。
    /// 每层 sortingOrder = UILayerOrder.GetOrder(layer) x LayerOrderStep（100），
    /// 层内面板按打开顺序用 SiblingIndex 排序。
    /// （QFramework UIRoot 固定层级思路 + GameFramework 每容器独立 Canvas 排序思路的结合。）
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        /// <summary>相邻层 Canvas 的 sortingOrder 步长。</summary>
        public const int LayerOrderStep = 100;

        [Header("Canvas Scaling")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float matchWidthOrHeight = 0.5f;
        [SerializeField] private bool pixelPerfect = true;

        private static UIRoot _instance;

        public static UIRoot Instance => _instance;

        private readonly Dictionary<UILayer, RectTransform> _layers = new Dictionary<UILayer, RectTransform>();

        /// <summary>确保根节点存在（没有则自动创建并常驻）。</summary>
        public static UIRoot EnsureCreated()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("MyUI Root");
            _instance = go.AddComponent<UIRoot>();
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

            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                var layerGo = new GameObject("Layer_" + layer);
                layerGo.transform.SetParent(transform, false);
                var canvas = layerGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = UILayerOrder.GetOrder(layer) * LayerOrderStep;
                canvas.pixelPerfect = pixelPerfect;

                var scaler = layerGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = referenceResolution;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = matchWidthOrHeight;
                scaler.referencePixelsPerUnit = 100f;

                layerGo.AddComponent<GraphicRaycaster>();
                _layers[layer] = (RectTransform)layerGo.transform;
            }
        }

        private void OnValidate()
        {
            if (referenceResolution.x <= 0f || referenceResolution.y <= 0f)
            {
                referenceResolution = new Vector2(1920f, 1080f);
            }
        }

        /// <summary>Destroy the auto-created UI root and clear its static instance.</summary>
        public static void DestroyInstance()
        {
            if (_instance == null)
            {
                return;
            }

            GameObject root = _instance.gameObject;
            _instance = null;
            if (Application.isPlaying)
            {
                Destroy(root);
            }
            else
            {
                DestroyImmediate(root);
            }
        }

        /// <summary>取某层根节点（用于挂接面板）。</summary>
        public RectTransform GetLayerRoot(UILayer layer)
        {
            return _layers.TryGetValue(layer, out RectTransform root) ? root : null;
        }
    }
}
