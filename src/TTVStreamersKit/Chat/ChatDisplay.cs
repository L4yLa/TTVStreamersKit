// Copyright (c) 2020 brian91292, (c) 2026 L4yLa. MIT License.
// EnhancedStreamChat の ChatDisplay を元にしている。
using BeatSaberMarkupLanguage.FloatingScreen;
using BeatSaberMarkupLanguage.ViewControllers;
using CameraUtils.Core;
using HMUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TTVCore.Api.V1;
using TTVStreamersKit.Configuration;
using TTVStreamersKit.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zenject;
#if BS_1_40_8
using IPA.Utilities;
#endif

namespace TTVStreamersKit.Chat
{
    /// <summary>空間に浮かべるチャット表示。TTVCore の表示用の流れ（Timeline）を受け取って表示する。</summary>
    internal sealed partial class ChatDisplay : BSMLAutomaticViewController, IInitializable, IDisposable
    {
        public override string Content => Localization.Bsml(base.Content);

        private const string MenuScene = "MainMenu";
        private const string GameScene = "GameCore";
        // メニューの作り直しで表示を戻すために残す件数
        private const int HistoryLimit = 100;
        private const int DeletedLimit = 500;
        private const int ViewLimit = 200;

        // メニューの作り直し（設定の変更による再起動など）をまたいで残す
        private static readonly List<TimelineItem> s_history = new List<TimelineItem>();
        private static readonly HashSet<string> s_deleted = new HashSet<string>();
        private static readonly Queue<string> s_deletedOrder = new Queue<string>();

        private readonly List<ChatMessageView> _messages = new List<ChatMessageView>();
        private readonly Dictionary<ChatMessageView, float> _heights = new Dictionary<ChatMessageView, float>();

        private PluginConfig _config;
        private ChatFontManager _fontManager;
        private ChatMessageView.Pool _pool;

        private FloatingScreen _chatScreen;
        private GameObject _chatContainer;
        private GameObject _rootGameObject;
        private Material _chatMoverMaterial;
        private ImageView _bg;
        private bool _isInGame;
        private bool _inChatScene = true;
        private bool _updateMessagePositions;
        private bool _started;
        private bool _disposed;

        [Inject]
        public void Construct(PluginConfig config, ChatFontManager fontManager, ChatMessageView.Pool pool)
        {
            _config = config;
            _fontManager = fontManager;
            _pool = pool;
            // メニューの設定画面が BSML の解析より先に参照しても正しい値になるよう、構築時に設定値で初期化する
            _layer = config.Layer.ToString();
        }

        public void Initialize()
        {
            if (_fontManager.IsInitialized) {
                this.BeginDisplay();
            }
            else {
                _fontManager.Initialized += this.OnFontInitialized;
            }
        }

        private void OnFontInitialized()
        {
            _fontManager.Initialized -= this.OnFontInitialized;
            this.BeginDisplay();
        }

        /// <summary>フォントの準備ができてから画面を作り、受信を始める。</summary>
        private void BeginDisplay()
        {
            if (_started || _disposed) {
                return;
            }
            _started = true;
            this.SetupScreens();
            ((RectTransform)this.transform).pivot = new Vector2(0.5f, 0f);

            _config.OnConfigChanged += this.OnConfigChanged;
            SceneManager.activeSceneChanged += this.OnActiveSceneChanged;
            var chat = TTVCoreApi.Chat;
            chat.TimelineItemReceived += this.OnTimelineItem;
            chat.MessageDeleted += this.OnMessageDeleted;
            chat.UserMessagesCleared += this.OnUserMessagesCleared;
            chat.ChatCleared += this.OnChatCleared;

            this.ReplayHistory();
        }

        // ================= 受信 =================

        private void OnTimelineItem(TimelineItem item)
        {
            s_history.Add(item);
            if (s_history.Count > HistoryLimit) {
                s_history.RemoveAt(0);
            }
            if (_config.ChatEnabled) {
                this.Show(item);
            }
        }

        private void OnMessageDeleted(MessageDeletedEvent e)
        {
            if (string.IsNullOrEmpty(e?.MessageId)) {
                return;
            }
            MarkDeleted(e.MessageId);
            foreach (var view in _messages.Where(v => v.Line?.MessageId == e.MessageId).ToList()) {
                this.ShowDeleted(view);
            }
        }

        private void OnUserMessagesCleared(UserMessagesClearedEvent e)
        {
            var userId = e?.TargetUser?.Id;
            if (string.IsNullOrEmpty(userId)) {
                return;
            }
            foreach (var id in HistoryMessageIds(m => m.Sender?.Id == userId)) {
                MarkDeleted(id);
            }
            foreach (var view in _messages.Where(v => v.Line?.SenderId == userId).ToList()) {
                this.ShowDeleted(view);
            }
        }

        private void OnChatCleared(ChatClearedEvent e)
        {
            foreach (var id in HistoryMessageIds(m => true)) {
                MarkDeleted(id);
            }
            foreach (var view in _messages.Where(v => v.Line?.MessageId != null).ToList()) {
                this.ShowDeleted(view);
            }
        }

        private static IEnumerable<string> HistoryMessageIds(Func<ChatMessage, bool> predicate)
        {
            foreach (var item in s_history) {
                var m = item is TimelineMessage tm ? tm.Message : (item as TimelineNotification)?.Message;
                if (m != null && m.MessageId != null && predicate(m)) {
                    yield return m.MessageId;
                }
            }
        }

        private static void MarkDeleted(string messageId)
        {
            if (!s_deleted.Add(messageId)) {
                return;
            }
            s_deletedOrder.Enqueue(messageId);
            while (s_deletedOrder.Count > DeletedLimit) {
                s_deleted.Remove(s_deletedOrder.Dequeue());
            }
        }

        // ================= 表示 =================

        private void Show(TimelineItem item)
        {
            if (_chatContainer == null) {
                return;
            }
            ChatLine line;
            try {
                line = ChatLineBuilder.Build(item);
            }
            catch (Exception e) {
                Plugin.Log.Error($"Chat: failed to build a line: {e}");
                return;
            }
            if (line == null) {
                return;
            }
            if (line.MessageId != null && s_deleted.Contains(line.MessageId)) {
                line.MarkDeleted();
            }
            var view = _pool.Spawn();
            view.transform.SetParent(_chatContainer.transform, false);
            view.Timestamp = item.Timestamp;
            this.ApplyStyle(view, line);
            view.SetLine(line, this.TimestampPrefix(view.Timestamp));
            _messages.Add(view);
            while (_messages.Count > ViewLimit) {
                this.Despawn(_messages[0]);
            }
            _updateMessagePositions = true;
        }

        private void ShowDeleted(ChatMessageView view)
        {
            var line = view.Line;
            if (line == null || line.Deleted) {
                return;
            }
            line.MarkDeleted();
            this.ApplyStyle(view, line);
            view.SetLine(line, this.TimestampPrefix(view.Timestamp));
            _updateMessagePositions = true;
        }

        private string TimestampPrefix(DateTimeOffset timestamp) =>
            _config.ShowTimestamp ? timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + " " : null;

        private void ReplayHistory()
        {
            this.ClearAll();
            if (!_config.ChatEnabled) {
                return;
            }
            foreach (var item in s_history) {
                this.Show(item);
            }
        }

        private void ClearAll()
        {
            while (_messages.Count > 0) {
                this.Despawn(_messages[0]);
            }
        }

        private void Despawn(ChatMessageView view)
        {
            _messages.Remove(view);
            _heights.Remove(view);
            if (view != null) {
                _pool.Despawn(view);
            }
        }

        /// <summary>チャット表示を使うかを切り替える。</summary>
        public void SetChatEnabled(bool enabled)
        {
            if (_config.ChatEnabled == enabled) {
                return;
            }
            _config.ChatEnabled = enabled;
            this.UpdateRootActive();
            if (enabled) {
                this.ReplayHistory();
            }
            else {
                this.ClearAll();
            }
        }

        private void UpdateRootActive()
        {
            if (_rootGameObject != null) {
                _rootGameObject.SetActive(_config.ChatEnabled && _inChatScene);
            }
        }

        private void ApplyStyle(ChatMessageView view, ChatLine line)
        {
            view.RectTransform.sizeDelta = new Vector2(this.ChatWidth, view.RectTransform.sizeDelta.y);
            foreach (var text in new[] { view.Text, view.SubText }) {
                if (_fontManager.MainFont != null) {
                    text.font = _fontManager.MainFont;
                }
                text.color = this.TextColor;
                text.fontSize = this.FontSize;
            }
            if (line == null) {
                return;
            }
            if (line.Highlight) {
                view.HighlightColor = this.HighlightColor;
                view.HighlightEnabled = true;
            }
            else if (line.Ping) {
                view.HighlightColor = this.PingColor;
                view.HighlightEnabled = true;
            }
            else {
                view.HighlightEnabled = false;
            }
            view.AccentColor = this.AccentColor;
            view.AccentEnabled = line.Accent;
        }

        private void UpdateMessages()
        {
            foreach (var view in _messages) {
                this.ApplyStyle(view, view.Line);
            }
            _updateMessagePositions = true;
        }

        /// <summary>時刻の表示を切り替えたときなどに、すべての内容を作り直す。</summary>
        private void RebuildMessages()
        {
            foreach (var view in _messages) {
                if (view.Line != null) {
                    view.SetLine(view.Line, this.TimestampPrefix(view.Timestamp));
                }
            }
            _updateMessagePositions = true;
        }

        private void UpdateMessagePositions()
        {
            var height = this.ChatHeight;
            var pos = height / (this.ReverseChatOrder ? 2f : -2f);
            var remove = new List<ChatMessageView>();
            for (var i = _messages.Count - 1; i >= 0; i--) {
                var view = _messages[i];
                if (view == null) {
                    remove.Add(view);
                    continue;
                }
                var h = view.Height;
                if (this.ReverseChatOrder) {
                    pos -= h;
                }
                view.transform.localPosition = new Vector3(0, pos);
                // 表示範囲から完全に出たものは片付ける
                var outside = this.ReverseChatOrder ? pos + h < -height / 2f : pos >= height / 2f;
                if (outside) {
                    remove.Add(view);
                }
                if (!this.ReverseChatOrder) {
                    pos += h;
                }
            }
            foreach (var view in remove) {
                this.Despawn(view);
            }
        }

        private void LateUpdate()
        {
            foreach (var view in _messages) {
                if (view == null) {
                    continue;
                }
                view.FitAccent();
                var h = view.Height;
                if (!_heights.TryGetValue(view, out var old) || Math.Abs(old - h) > 0.01f) {
                    _heights[view] = h;
                    _updateMessagePositions = true;
                }
            }
            if (_updateMessagePositions) {
                _updateMessagePositions = false;
                this.UpdateMessagePositions();
            }
        }

        // ================= 画面 =================

        private void SetupScreens()
        {
            if (_chatScreen != null) {
                return;
            }
            var screenSize = new Vector2(_config.ChatWidth, _config.ChatHeight);
            _chatScreen = FloatingScreen.CreateFloatingScreen(screenSize, true, _config.Menu_ChatPosition, Quaternion.identity, 0f, true);
            _chatScreen.HandleReleased += this.OnHandleReleased;
            this.ApplyLayer();

            var rectMask2D = _chatScreen.GetComponent<RectMask2D>();
            if (rectMask2D) {
                Destroy(rectMask2D);
            }
            _chatContainer = new GameObject("chatContainer");
            _chatContainer.transform.SetParent(_chatScreen.transform, false);
            _chatContainer.AddComponent<RectMask2D>().rectTransform.sizeDelta = screenSize;

            var canvas = _chatScreen.GetComponent<Canvas>();
            canvas.worldCamera = Camera.main;
            canvas.sortingOrder = 3;

            _chatScreen.SetRootViewController(this, AnimationType.None);

            if (BeatSaberUtils.UINoGlowMaterial != null) {
                _chatMoverMaterial = Instantiate(BeatSaberUtils.UINoGlowMaterial);
                _chatMoverMaterial.color = Color.clear;
                var renderer = _chatScreen.Handle.gameObject.GetComponent<Renderer>();
                renderer.material = _chatMoverMaterial;
            }

            _rootGameObject = new GameObject("TTVStreamersKit Chat");
            _chatScreen.transform.SetParent(_rootGameObject.transform);
            DontDestroyOnLoad(_rootGameObject);

            _bg = _chatScreen.GetComponentsInChildren<ImageView>().FirstOrDefault(x => x.name == "Background");
            if (_bg != null) {
                _bg.raycastTarget = false;
                _bg.material = Instantiate(_bg.material);
#if BS_1_40_8
                _bg.SetField("_gradient", false);
#else
                _bg.gradient = false;
#endif
                _bg.material.color = Color.white;
                _bg.SetAllDirty();
            }
            this.UpdateChatUI();
            this.UpdateRootActive();
        }

        private void ApplyLayer()
        {
            this.SetCurrentLayer(_config.Layer == PluginConfig.LayerType.Manual ? _config.UILayer : (int)_config.Layer);
        }

        private void SetCurrentLayer(int layer)
        {
            VisibilityLayer visible;
            switch ((PluginConfig.LayerType)layer) {
                case PluginConfig.LayerType.UI:
                    visible = VisibilityLayer.UI;
                    break;
                case PluginConfig.LayerType.HMDOnly:
                    visible = VisibilityLayer.HmdOnlyAndReflected;
                    break;
                default:
                    visible = (VisibilityLayer)layer;
                    break;
            }
            if (_chatScreen) {
                _chatScreen.gameObject.SetLayer(visible);
            }
            if (_settingsModalGameObject) {
                _settingsModalGameObject.SetLayer(visible);
            }
            this.gameObject.SetLayer(visible);
        }

        /// <summary>BSIPA は値の変更ごとに呼ぶ。ファイルの変更では別のスレッドから届くため、メインスレッドで後から反映する。</summary>
        private void OnConfigChanged()
        {
            if (_mainContext == null || _configUpdatePosted) {
                return;
            }
            _configUpdatePosted = true;
            _mainContext.Post(_ => {
                _configUpdatePosted = false;
                if (!_disposed) {
                    this.UpdateChatUI();
                }
            }, null);
        }

        private System.Threading.SynchronizationContext _mainContext;
        private volatile bool _configUpdatePosted;

        private void Awake()
        {
            _mainContext = System.Threading.SynchronizationContext.Current;
            DontDestroyOnLoad(this.gameObject);
        }

        private void OnHandleReleased(object sender, FloatingScreenHandleEventArgs e)
        {
            if (this.SyncOrientation) {
                _config.Menu_ChatPosition = e.Position;
                _config.Menu_ChatRotation = e.Rotation.eulerAngles;
                _config.Song_ChatPosition = e.Position;
                _config.Song_ChatRotation = e.Rotation.eulerAngles;
            }
            else if (_isInGame) {
                _config.Song_ChatPosition = e.Position;
                _config.Song_ChatRotation = e.Rotation.eulerAngles;
            }
            else {
                _config.Menu_ChatPosition = e.Position;
                _config.Menu_ChatRotation = e.Rotation.eulerAngles;
            }
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (_chatScreen == null) {
                return;
            }
            _inChatScene = next.name == GameScene || next.name == MenuScene;
            if (!_inChatScene) {
                _isInGame = false;
                this.UpdateRootActive();
                return;
            }
            _isInGame = next.name == GameScene;
            foreach (var canvas in _chatScreen.GetComponentsInChildren<Canvas>(true)) {
                canvas.sortingOrder = _isInGame ? 0 : 3;
            }
            this.UpdateRootActive();
            this.UpdateChatUI();
        }

        private void UpdateChatUI()
        {
            if (_chatScreen == null) {
                return;
            }
            this.Load();
            _chatContainer.GetComponent<RectMask2D>().rectTransform.sizeDelta = new Vector2(this.ChatWidth, this.ChatHeight);
            _chatScreen.Handle.transform.localScale = new Vector3(this.ChatWidth, this.ChatHeight * 0.9f, 0.01f);
            _chatScreen.Handle.transform.localPosition = Vector3.zero;
            _chatScreen.Handle.transform.localRotation = Quaternion.identity;
            this.UpdateRootActive();
            this.UpdateMessages();
        }

        // ================= 破棄 =================

        public void Dispose()
        {
            if (_disposed) {
                return;
            }
            _disposed = true;
            try {
                _fontManager.Initialized -= this.OnFontInitialized;
                if (_started) {
                    _config.OnConfigChanged -= this.OnConfigChanged;
                    SceneManager.activeSceneChanged -= this.OnActiveSceneChanged;
                    var chat = TTVCoreApi.Chat;
                    chat.TimelineItemReceived -= this.OnTimelineItem;
                    chat.MessageDeleted -= this.OnMessageDeleted;
                    chat.UserMessagesCleared -= this.OnUserMessagesCleared;
                    chat.ChatCleared -= this.OnChatCleared;
                }
                this.ClearAll();
                if (_chatMoverMaterial != null) {
                    Destroy(_chatMoverMaterial);
                    _chatMoverMaterial = null;
                }
                if (_bg != null) {
                    Destroy(_bg.material);
                }
                if (_chatScreen != null) {
                    _chatScreen.HandleReleased -= this.OnHandleReleased;
                    Destroy(_chatScreen);
                    _chatScreen = null;
                }
                if (_rootGameObject != null) {
                    Destroy(_rootGameObject);
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"Chat: {e}");
            }
        }
    }
}
