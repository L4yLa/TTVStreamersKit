using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using System.Collections.Generic;
using System.ComponentModel;
using TTVStreamersKit.Chat;
using TTVStreamersKit.Configuration;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.UI
{
    /// <summary>言語、機能ごとのオン・オフ、チャット表示の表示レイヤー。</summary>
    internal sealed class SettingsViewController : BSMLResourceViewController
    {
        private ChatDisplay _chatDisplay;

        // 今の言語が読めなくても選べるよう、選択肢は言語によらず同じ表記にする
        private const string LanguageSystem = "System / システム";
        private const string LanguageEnglish = "English";
        private const string LanguageJapanese = "日本語";

        public override string ResourceName => "TTVStreamersKit.UI.SettingsView.bsml";

        public override string Content => Localization.Bsml(base.Content);

        [UIValue("languages")]
        private readonly List<object> _languages = new List<object> { LanguageSystem, LanguageEnglish, LanguageJapanese };

        [UIValue("language")]
        private string Language
        {
            get {
                switch (Plugin.Config.Language) {
                    case UiLanguage.English: return LanguageEnglish;
                    case UiLanguage.Japanese: return LanguageJapanese;
                    default: return LanguageSystem;
                }
            }
            set {
                var language = value == LanguageEnglish ? UiLanguage.English
                             : value == LanguageJapanese ? UiLanguage.Japanese
                             : UiLanguage.System;
                Plugin.Config.Language = language;
                Localization.Apply(language);
            }
        }

        public ChatDisplay ChatDisplay
        {
            get => _chatDisplay;
            set {
                if (_chatDisplay == value) {
                    return;
                }
                if (_chatDisplay != null) {
                    _chatDisplay.PropertyChanged -= this.OnChatDisplayPropertyChanged;
                }
                _chatDisplay = value;
                if (_chatDisplay != null) {
                    _chatDisplay.PropertyChanged += this.OnChatDisplayPropertyChanged;
                }
            }
        }

        [UIValue("chat-enabled")]
        private bool ChatEnabled
        {
            get => Plugin.Config.ChatEnabled;
            set => _chatDisplay?.SetChatEnabled(value);
        }

        [UIValue("request-enabled")]
        private bool RequestEnabled
        {
            get => Plugin.Config.RequestEnabled;
            set => Plugin.Config.RequestEnabled = value;
        }

        [UIValue("emotes-enabled")]
        private bool EmotesEnabled
        {
            get => Plugin.Config.EmotesEnabled;
            set => Plugin.Config.EmotesEnabled = value;
        }

        // Manual（設定ファイルで UILayer を指定する）は選択肢に出さない
        [UIValue("layer-types")]
        private readonly List<object> _layerTypes = new List<object> {
            PluginConfig.LayerType.UI.ToString(),
            PluginConfig.LayerType.HMDOnly.ToString(),
        };

        [UIAction("layer-formatter")]
        private string LayerFormatter(object value) => ChatDisplay.FormatLayer(value);

        /// <summary>チャットの設定と同じ値を読み書きする（HMDOnly にすると、デスクトップの画面からチャットの歯車を操作できなくなるため）。</summary>
        [UIValue("layer-type")]
        private string Layer
        {
            get => _chatDisplay != null ? _chatDisplay.Layer : Plugin.Config.Layer.ToString();
            set {
                if (_chatDisplay != null) {
                    _chatDisplay.Layer = value;
                }
            }
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            this.NotifyPropertyChanged(nameof(this.ChatEnabled));
            this.NotifyPropertyChanged(nameof(this.Layer));
        }

        private void OnChatDisplayPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Chat.ChatDisplay.Layer)) {
                this.NotifyPropertyChanged(nameof(this.Layer));
            }
        }

        protected override void OnDestroy()
        {
            this.ChatDisplay = null;
            base.OnDestroy();
        }
    }
}
