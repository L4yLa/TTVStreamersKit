// Copyright (c) 2020 brian91292, (c) 2026 L4yLa. MIT License.
// EnhancedStreamChat の EnhancedTextMeshProUGUIWithBackground を元にしている。
using HMUI;
using System;
using TMPro;
using TTVCore.Api.V1;
using TTVStreamersKit.Utilities;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace TTVStreamersKit.Chat
{
    /// <summary>チャットの1件（1行、または2行のまとまり）。背景のハイライトと左端のアクセントを持つ。</summary>
    internal sealed class ChatMessageView : MonoBehaviour
    {
        private ImageView _highlight;
        private ImageView _accent;
        private VerticalLayoutGroup _layout;
        private TmpBinding _mainBinding;
        private TmpBinding _subBinding;

        public TextMeshProUGUI Text { get; private set; }
        public TextMeshProUGUI SubText { get; private set; }

        /// <summary>表示している内容。削除の表示に使う。</summary>
        public ChatLine Line { get; private set; }

        public DateTimeOffset Timestamp { get; set; }

        public RectTransform RectTransform => (RectTransform)this.transform;

        public float Height => this.RectTransform.sizeDelta.y;

        public Color HighlightColor
        {
            get => _highlight.color;
            set => _highlight.color = value;
        }

        public Color AccentColor
        {
            get => _accent.color;
            set => _accent.color = value;
        }

        public bool HighlightEnabled
        {
            get => _highlight.enabled;
            set {
                _highlight.enabled = value;
                _layout.padding = value ? new RectOffset(5, 5, 2, 2) : new RectOffset(5, 5, 1, 1);
            }
        }

        public bool AccentEnabled
        {
            get => _accent.enabled;
            set => _accent.enabled = value;
        }

        public bool SubTextEnabled
        {
            get => this.SubText.enabled;
            private set {
                this.SubText.enabled = value;
                this.SubText.gameObject.SetActive(value);
            }
        }

        /// <summary>内容を表示する。画像の読み込みが終わると、TTVCore がテキストを作り直す。</summary>
        public void SetLine(ChatLine line, string timestampPrefix)
        {
            this.ReleaseBindings();
            this.Line = line;
            _mainBinding = TTVCoreApi.Images.Bind(this.Text, line.BuildMain(line.ShowTimestamp ? timestampPrefix : null));
            var sub = line.BuildSub();
            if (sub != null) {
                this.SubTextEnabled = true;
                _subBinding = TTVCoreApi.Images.Bind(this.SubText, sub);
            }
            else {
                this.SubTextEnabled = false;
                this.SubText.text = "";
            }
        }

        /// <summary>アクセントの高さをこの件の高さに合わせる。</summary>
        public void FitAccent()
        {
            var rt = (RectTransform)_accent.transform;
            var h = this.Height;
            if (Math.Abs(rt.sizeDelta.y - h) > 0.01f) {
                rt.sizeDelta = new Vector2(1, h);
            }
        }

        private void ReleaseBindings()
        {
            _mainBinding?.Dispose();
            _mainBinding = null;
            _subBinding?.Dispose();
            _subBinding = null;
        }

        private void OnDestroy()
        {
            this.ReleaseBindings();
        }

        internal sealed class Pool : MonoMemoryPool<ChatMessageView>
        {
            protected override void OnCreated(ChatMessageView item)
            {
                base.OnCreated(item);
                item._highlight = item.GetComponent<ImageView>();
                item._highlight.raycastTarget = false;

                item._layout = item.GetComponent<VerticalLayoutGroup>();
                item._layout.childAlignment = TextAnchor.MiddleLeft;
                item._layout.spacing = 1;
                item._layout.childControlWidth = true;
                item._layout.childControlHeight = true;
                item._layout.childForceExpandWidth = true;
                item._layout.childForceExpandHeight = false;

                var fitter = item.GetComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                item.Text = CreateText("Text", item.transform);
                item.SubText = CreateText("SubText", item.transform);

                item._accent = new GameObject("Accent", typeof(RectTransform)).AddComponent<ImageView>();
                item._accent.raycastTarget = false;
                item._accent.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                var accentRect = (RectTransform)item._accent.transform;
                accentRect.SetParent(item.transform, false);
                accentRect.anchorMin = new Vector2(0, 0.5f);
                accentRect.anchorMax = new Vector2(0, 0.5f);
                accentRect.pivot = new Vector2(0, 0.5f);
                accentRect.sizeDelta = new Vector2(1, 10);

                item.RectTransform.pivot = new Vector2(0.5f, 0f);
                item.SubTextEnabled = false;
                item.HighlightEnabled = false;
                item.AccentEnabled = false;
            }

            protected override void OnSpawned(ChatMessageView item)
            {
                base.OnSpawned(item);
                var noGlow = BeatSaberUtils.UINoGlowMaterial;
                if (noGlow != null) {
                    item._highlight.material = noGlow;
                    item._accent.material = noGlow;
                }
            }

            protected override void OnDespawned(ChatMessageView item)
            {
                if (item == null) {
                    return;
                }
                item.ReleaseBindings();
                item.Line = null;
                item.HighlightEnabled = false;
                item.AccentEnabled = false;
                item.SubTextEnabled = false;
                item.Text.text = "";
                item.SubText.text = "";
                base.OnDespawned(item);
            }

            private static TextMeshProUGUI CreateText(string name, Transform parent)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var text = go.AddComponent<TextMeshProUGUI>();
                text.raycastTarget = false;
                text.richText = true;
#if BS_1_44_1
                text.textWrappingMode = TextWrappingModes.Normal;
#else
                text.enableWordWrapping = true;
#endif
                text.overflowMode = TextOverflowModes.Overflow;
                text.alignment = TextAlignmentOptions.BottomLeft;
                text.lineSpacing = 1.5f;
                return text;
            }
        }
    }
}
