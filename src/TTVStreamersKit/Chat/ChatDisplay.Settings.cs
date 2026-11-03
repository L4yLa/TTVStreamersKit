// Copyright (c) 2020 brian91292, (c) 2026 L4yLa. MIT License.
// EnhancedStreamChat の ChatDisplay.Settings を元にしている。
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using TTVStreamersKit.Configuration;
using TTVStreamersKit.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace TTVStreamersKit.Chat
{
    /// <summary>チャット表示の右下の歯車から開く設定。</summary>
    internal sealed partial class ChatDisplay
    {
        [UIParams]
        private BSMLParserParams _parserParams = null;

        [UIObject("settings-icon")]
        private GameObject _settingsIconGameObject = null;

        [UIObject("settings-modal")]
        private GameObject _settingsModalGameObject = null;

        [UIComponent("background-color-setting")]
        private ColorSetting _backgroundColorSetting = null;

        [UIComponent("accent-color-setting")]
        private ColorSetting _accentColorSetting = null;

        [UIComponent("highlight-color-setting")]
        private ColorSetting _highlightColorSetting = null;

        [UIComponent("ping-color-setting")]
        private ColorSetting _pingColorSetting = null;

        [UIComponent("text-color-setting")]
        private ColorSetting _textColorSetting = null;

        [UIAction("#post-parse")]
        private void PostParse()
        {
            this.Load();
            foreach (var setting in new[] { _backgroundColorSetting, _accentColorSetting, _highlightColorSetting, _pingColorSetting, _textColorSetting }) {
                if (setting == null) {
                    continue;
                }
                // 色の選択は別のモーダルで開くため、設定のモーダルを閉じ、取り消したら戻す
                setting.EditButton.onClick.AddListener(this.HideSettings);
                setting.ModalColorPicker.CancelEvent += this.ShowSettings;
            }
            if (_backgroundColorSetting != null) {
                _backgroundColorSetting.CurrentColor = _config.BackgroundColor;
            }
            if (_accentColorSetting != null) {
                _accentColorSetting.CurrentColor = _config.AccentColor;
            }
            if (_highlightColorSetting != null) {
                _highlightColorSetting.CurrentColor = _config.HighlightColor;
            }
            if (_pingColorSetting != null) {
                _pingColorSetting.CurrentColor = _config.PingColor;
            }
            if (_textColorSetting != null) {
                _textColorSetting.CurrentColor = _config.TextColor;
            }

            // 操作できる部品を画面の手前に出す
            MoveToFront(_settingsModalGameObject);
            MoveToFront(_settingsIconGameObject);
            this.ApplyLayer();
        }

        private static void MoveToFront(GameObject go)
        {
            if (go != null) {
                var p = go.transform.localPosition;
                go.transform.localPosition = new Vector3(p.x, p.y, -2f);
            }
        }

        private void HideSettings() => _parserParams?.EmitEvent("hide-settings");

        private void ShowSettings() => _parserParams?.EmitEvent("show-settings");

        // 設定から読み込んでいる間は、設定に書き戻さない
        private bool _loading;

        /// <summary>設定の値を画面の値に読み込む。</summary>
        private void Load()
        {
            _loading = true;
            try {
                this.LoadValues();
            }
            finally {
                _loading = false;
            }
        }

        private void LoadValues()
        {
            this.AccentColor = _config.AccentColor;
            this.TextColor = _config.TextColor;
            this.BackgroundColor = _config.BackgroundColor;
            this.AllowMovement = _config.AllowMovement;
            this.ChatHeight = _config.ChatHeight;
            this.ChatWidth = _config.ChatWidth;
            // 設定画面の範囲（2～6）に収める。範囲外の値は設定ファイルには書き戻さない
            this.FontSize = Mathf.Clamp(_config.FontSize, MinFontSize, MaxFontSize);
            this.HighlightColor = _config.HighlightColor;
            this.PingColor = _config.PingColor;
            this.ReverseChatOrder = _config.ReverseChatOrder;
            this.ShowTimestamp = _config.ShowTimestamp;
            this.ShowBadges = _config.ShowBadges;
            this.SyncOrientation = _config.SyncOrientation;
            this.ChatPosition = _isInGame ? _config.Song_ChatPosition : _config.Menu_ChatPosition;
            this.ChatRotation = _isInGame ? _config.Song_ChatRotation : _config.Menu_ChatRotation;
            this.Layer = _config.Layer.ToString();
        }

        private bool Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) {
                return false;
            }
            field = value;
            this.NotifyPropertyChanged(name);
            return true;
        }

        private Color _accentColor;
        [UIValue("accent-color")]
        public Color AccentColor
        {
            get => _accentColor;
            set {
                if (this.Set(ref _accentColor, value, nameof(this.AccentColor))) {
                    if (!_loading) {
                        _config.AccentColor = value;
                    }
                    this.UpdateMessages();
                }
            }
        }

        private Color _highlightColor;
        [UIValue("highlight-color")]
        public Color HighlightColor
        {
            get => _highlightColor;
            set {
                if (this.Set(ref _highlightColor, value, nameof(this.HighlightColor))) {
                    if (!_loading) {
                        _config.HighlightColor = value;
                    }
                    this.UpdateMessages();
                }
            }
        }

        private Color _pingColor;
        [UIValue("ping-color")]
        public Color PingColor
        {
            get => _pingColor;
            set {
                if (this.Set(ref _pingColor, value, nameof(this.PingColor))) {
                    if (!_loading) {
                        _config.PingColor = value;
                    }
                    this.UpdateMessages();
                }
            }
        }

        private Color _backgroundColor;
        [UIValue("background-color")]
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set {
                if (this.Set(ref _backgroundColor, value, nameof(this.BackgroundColor))) {
                    if (!_loading) {
                        _config.BackgroundColor = value;
                    }
                }
                if (_bg != null) {
                    _bg.color = value;
                }
            }
        }

        private Color _textColor;
        [UIValue("text-color")]
        public Color TextColor
        {
            get => _textColor;
            set {
                if (this.Set(ref _textColor, value, nameof(this.TextColor))) {
                    if (!_loading) {
                        _config.TextColor = value;
                    }
                    this.UpdateMessages();
                }
            }
        }

        // 設定画面の文字の大きさの範囲（ChatDisplay.bsml の slider-setting と合わせる）
        private const float MinFontSize = 2f;
        private const float MaxFontSize = 6f;

        private float _fontSize;
        [UIValue("font-size")]
        public float FontSize
        {
            get => _fontSize;
            set {
                if (this.Set(ref _fontSize, value, nameof(this.FontSize))) {
                    if (!_loading) {
                        _config.FontSize = value;
                    }
                    this.UpdateMessages();
                }
            }
        }

        // Manual（設定ファイルで UILayer を指定する）は選択肢に出さない。Manual のときは先頭の選択肢が表示される
        [UIValue("layer-types")]
        private readonly List<object> _layerTypes = new List<object> {
            PluginConfig.LayerType.UI.ToString(),
            PluginConfig.LayerType.HMDOnly.ToString(),
        };

        [UIAction("layer-formatter")]
        private string LayerFormatter(object value) => FormatLayer(value);

        /// <summary>表示対象の選択肢の表記。</summary>
        internal static string FormatLayer(object value) =>
            value as string == PluginConfig.LayerType.HMDOnly.ToString()
                ? Localization.T("Hide on desktop", "デスクトップ非表示")
                : Localization.T("All", "全て");

        private string _layer = PluginConfig.LayerType.UI.ToString();
        /// <summary>表示するレイヤー。メニューの設定画面とも共有する。</summary>
        [UIValue("layer-type")]
        public string Layer
        {
            get => _layer;
            set {
                if (!this.Set(ref _layer, value, nameof(this.Layer))) {
                    return;
                }
                var layer = Enum.GetValues(typeof(PluginConfig.LayerType)).OfType<PluginConfig.LayerType>()
                    .FirstOrDefault(x => x.ToString() == value);
                if (!_loading) {
                    _config.Layer = layer;
                    if (layer == PluginConfig.LayerType.UI || layer == PluginConfig.LayerType.HMDOnly) {
                        _config.UILayer = (int)layer;
                    }
                }
                this.ApplyLayer();
            }
        }

        [UIValue("settings-width")]
        public int SettingsWidth => 110;

        private int _chatWidth;
        [UIValue("chat-width")]
        public int ChatWidth
        {
            get => _chatWidth;
            set {
                if (this.Set(ref _chatWidth, value, nameof(this.ChatWidth))) {
                    if (!_loading) {
                        _config.ChatWidth = value;
                    }
                    this.ResizeScreen();
                }
            }
        }

        private int _chatHeight;
        [UIValue("chat-height")]
        public int ChatHeight
        {
            get => _chatHeight;
            set {
                if (this.Set(ref _chatHeight, value, nameof(this.ChatHeight))) {
                    if (!_loading) {
                        _config.ChatHeight = value;
                    }
                    this.ResizeScreen();
                }
            }
        }

        private void ResizeScreen()
        {
            if (_chatScreen == null || _chatWidth <= 0 || _chatHeight <= 0) {
                return;
            }
            _chatScreen.ScreenSize = new Vector2(_chatWidth, _chatHeight);
            _chatContainer.GetComponent<RectMask2D>().rectTransform.sizeDelta = new Vector2(_chatWidth, _chatHeight);
            this.UpdateMessages();
        }

        private Vector3 _chatPosition;
        public Vector3 ChatPosition
        {
            get => _chatPosition;
            set {
                _chatPosition = value;
                if (_chatScreen != null) {
                    _chatScreen.ScreenPosition = value;
                }
            }
        }

        private Vector3 _chatRotation;
        public Vector3 ChatRotation
        {
            get => _chatRotation;
            set {
                _chatRotation = value;
                if (_chatScreen != null) {
                    _chatScreen.ScreenRotation = Quaternion.Euler(value);
                }
            }
        }

        private bool _allowMovement;
        [UIValue("allow-movement")]
        public bool AllowMovement
        {
            get => _allowMovement;
            set {
                if (this.Set(ref _allowMovement, value, nameof(this.AllowMovement))) {
                    if (!_loading) {
                        _config.AllowMovement = value;
                    }
                }
                if (_chatScreen != null) {
                    _chatScreen.ShowHandle = value;
                }
            }
        }

        private bool _syncOrientation;
        [UIValue("sync-orientation")]
        public bool SyncOrientation
        {
            get => _syncOrientation;
            set {
                if (!this.Set(ref _syncOrientation, value, nameof(this.SyncOrientation))) {
                    return;
                }
                if (!_loading) {
                    _config.SyncOrientation = value;
                }
                if (value && !_loading) {
                    // 今いる場面の位置に揃える
                    if (_isInGame) {
                        _config.Menu_ChatPosition = _config.Song_ChatPosition;
                        _config.Menu_ChatRotation = _config.Song_ChatRotation;
                    }
                    else {
                        _config.Song_ChatPosition = _config.Menu_ChatPosition;
                        _config.Song_ChatRotation = _config.Menu_ChatRotation;
                    }
                }
            }
        }

        private bool _reverseChatOrder;
        [UIValue("reverse-chat-order")]
        public bool ReverseChatOrder
        {
            get => _reverseChatOrder;
            set {
                if (this.Set(ref _reverseChatOrder, value, nameof(this.ReverseChatOrder))) {
                    if (!_loading) {
                        _config.ReverseChatOrder = value;
                    }
                    _updateMessagePositions = true;
                }
            }
        }

        private bool _showTimestamp;
        [UIValue("show-timestamp")]
        public bool ShowTimestamp
        {
            get => _showTimestamp;
            set {
                if (this.Set(ref _showTimestamp, value, nameof(this.ShowTimestamp))) {
                    if (!_loading) {
                        _config.ShowTimestamp = value;
                    }
                    this.RebuildMessages();
                }
            }
        }

        private bool _showBadges;
        [UIValue("show-badges")]
        public bool ShowBadges
        {
            get => _showBadges;
            set {
                if (this.Set(ref _showBadges, value, nameof(this.ShowBadges))) {
                    if (!_loading) {
                        _config.ShowBadges = value;
                    }
                    this.RebuildMessages();
                }
            }
        }
    }
}
