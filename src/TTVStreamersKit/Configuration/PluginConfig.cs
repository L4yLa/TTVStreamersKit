using IPA.Config.Stores;
using IPA.Config.Stores.Attributes;
using IPA.Config.Stores.Converters;
using TTVStreamersKit.Emotes;
using TTVStreamersKit.Requests;
using TTVStreamersKit.Utilities;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]
namespace TTVStreamersKit.Configuration
{
    /// <summary>TTVStreamersKit の設定（UserData\TTVStreamersKit.json）。チャット表示の項目は EnhancedStreamChat の設定を引き継ぐ。</summary>
    public class PluginConfig
    {
        public enum LayerType
        {
            Manual,
            UI = 5,
            HMDOnly = 6,
        }

        /// <summary>UI とチャットへの返信の言語。</summary>
        [UseConverter(typeof(EnumConverter<UiLanguage>))]
        public virtual UiLanguage Language { get; set; } = UiLanguage.System;

        /// <summary>チャット表示を使うか。</summary>
        public virtual bool ChatEnabled { get; set; } = true;

        /// <summary>リクエスト機能を使うか。</summary>
        public virtual bool RequestEnabled { get; set; } = true;

        /// <summary>リクエスト機能の設定。</summary>
        [NonNullable]
        public virtual RequestConfig Request { get; set; } = new RequestConfig();

        /// <summary>エモートの演出（ChatEmoteParticles の後継）を使うか。</summary>
        public virtual bool EmotesEnabled { get; set; } = true;

        /// <summary>エモートの演出の設定。</summary>
        [NonNullable]
        public virtual EmoteConfig Emotes { get; set; } = new EmoteConfig();

        [UseConverter(typeof(ColorConverterWithAlpha))]
        public virtual Color BackgroundColor { get; set; } = new Color(0.3f, 0.3f, 0.3f, 1f);
        [UseConverter(typeof(ColorConverterWithAlpha))]
        public virtual Color TextColor { get; set; } = Color.white;
        [UseConverter(typeof(ColorConverterWithAlpha))]
        public virtual Color AccentColor { get; set; } = new Color(0.57f, 0.28f, 1f, 1f);
        [UseConverter(typeof(ColorConverterWithAlpha))]
        public virtual Color HighlightColor { get; set; } = new Color(0.57f, 0.28f, 1f, 0.06f);
        [UseConverter(typeof(ColorConverterWithAlpha))]
        public virtual Color PingColor { get; set; } = new Color(1f, 0f, 0f, 0.13f);
        public virtual int ChatWidth { get; set; } = 120;
        public virtual int ChatHeight { get; set; } = 140;
        public virtual float FontSize { get; set; } = 3.4f;
        public virtual bool AllowMovement { get; set; } = false;
        public virtual bool SyncOrientation { get; set; } = false;
        public virtual bool ReverseChatOrder { get; set; } = false;
        public virtual bool ShowTimestamp { get; set; } = false;
        // 名前の前のバッジ（モデレーター・サブスクライバーなど）を表示するか
        public virtual bool ShowBadges { get; set; } = true;
        [UseConverter(typeof(Vector3Converter))]
        public virtual Vector3 Menu_ChatPosition { get; set; } = new Vector3(0, 3.75f, 2.5f);
        [UseConverter(typeof(Vector3Converter))]
        public virtual Vector3 Menu_ChatRotation { get; set; } = new Vector3(325, 0, 0);
        [UseConverter(typeof(Vector3Converter))]
        public virtual Vector3 Song_ChatPosition { get; set; } = new Vector3(0, 3.75f, 2.5f);
        [UseConverter(typeof(Vector3Converter))]
        public virtual Vector3 Song_ChatRotation { get; set; } = new Vector3(325, 0, 0);
        [UseConverter(typeof(EnumConverter<LayerType>))]
        public virtual LayerType Layer { get; set; } = LayerType.UI;
        public virtual int UILayer { get; set; } = 5;

        /// <summary>値が変わった（ファイルの変更を含む）。</summary>
        public event Action OnConfigChanged;

        /// <summary>BSIPA が値の変更やファイルの変更で呼ぶ。</summary>
        public virtual void Changed()
        {
            this.OnConfigChanged?.Invoke();
        }
    }
}
