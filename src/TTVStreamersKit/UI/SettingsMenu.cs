using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using HMUI;
using System;
using TTVStreamersKit.Chat;
using TTVStreamersKit.Requests;
using TTVStreamersKit.Utilities;
using UnityEngine;
using Zenject;

namespace TTVStreamersKit.UI
{
    /// <summary>メインメニューの左側の MOD ボタンから開く設定画面。</summary>
    internal sealed class SettingsMenu : IInitializable, ITickable, IDisposable
    {
        private const float RetryInterval = 0.5f;

        private readonly ChatDisplay _chatDisplay;
        private MenuButton _button;
        private bool _pending = true;
        private float _nextTry;
        private SettingsFlowCoordinator _flow;
        private bool _rebuild;

        public SettingsMenu(ChatDisplay chatDisplay)
        {
            _chatDisplay = chatDisplay;
        }

        private static string HoverHint => Localization.T("Chat display and song request settings", "チャット表示とリクエストの設定");

        public void Initialize()
        {
            Localization.Changed += this.OnLanguageChanged;
        }

        /// <summary>メニューに入った直後は BSML の準備ができていないため、登録できるまで間隔をあけて試す。</summary>
        public void Tick()
        {
            if (_rebuild) {
                _rebuild = false;
                this.Rebuild();
            }
            if (!_pending || Time.realtimeSinceStartup < _nextTry) {
                return;
            }
            try {
                _button = new MenuButton("TTVStreamersKit", HoverHint, this.Show);
                MenuButtons.Instance.RegisterButton(_button);
                _pending = false;
            }
            catch (InvalidOperationException) {
                _button = null;
                _nextTry = Time.realtimeSinceStartup + RetryInterval;
            }
            catch (Exception e) {
                _button = null;
                _pending = false;
                Plugin.Log.Error($"UI: failed to add the menu button: {e}");
            }
        }

        private void OnLanguageChanged()
        {
            if (_button != null) {
                _button.HoverHint = HoverHint;
            }
            // 設定の値を変えている最中に画面を作り直さないよう、次のフレームで行う
            _rebuild = true;
        }

        /// <summary>言語を変えたら画面を作り直す。開いていれば、作り直した画面を開き直す。</summary>
        private void Rebuild()
        {
            if (_flow == null) {
                return;
            }
            var wasOpen = _flow.isActivated;
            try {
                if (wasOpen) {
                    BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(_flow, immediately: true);
                }
                UnityEngine.Object.Destroy(_flow.gameObject);
                _flow = null;
                if (wasOpen) {
                    this.Show(true);
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"UI: failed to rebuild the settings screen: {e}");
            }
        }

        private void Show() => this.Show(false);

        private void Show(bool immediately)
        {
            if (_flow == null) {
                _flow = BeatSaberUI.CreateFlowCoordinator<SettingsFlowCoordinator>();
                _flow.ChatDisplay = _chatDisplay;
            }
            BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(_flow, immediately: immediately);
        }

        public void Dispose()
        {
            Localization.Changed -= this.OnLanguageChanged;
            if (_button != null) {
                try {
                    MenuButtons.Instance.UnregisterButton(_button);
                }
                catch (Exception e) {
                    Plugin.Log.Warn($"UI: failed to remove the menu button: {e.Message}");
                }
                _button = null;
            }
            if (_flow != null) {
                UnityEngine.Object.Destroy(_flow.gameObject);
                _flow = null;
            }
        }
    }

    internal sealed class SettingsFlowCoordinator : FlowCoordinator
    {
        private SettingsViewController _view;
        private RequestSettingsViewController _requestView;
        private EmoteSettingsViewController _emoteView;

        public ChatDisplay ChatDisplay { get; set; }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation) {
                this.SetTitle("TTVStreamersKit");
                this.showBackButton = true;
                _view = BeatSaberUI.CreateViewController<SettingsViewController>();
                _requestView = BeatSaberUI.CreateViewController<RequestSettingsViewController>();
                _emoteView = BeatSaberUI.CreateViewController<EmoteSettingsViewController>();
            }
            _view.ChatDisplay = this.ChatDisplay;
            if (addedToHierarchy) {
                // 項目の多い設定は左右の画面に置く
                this.ProvideInitialViewControllers(_view, _emoteView, _requestView);
            }
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }

        private void OnDestroy()
        {
            // 作り直すときに、古い画面を残さない
            if (_view != null) {
                Destroy(_view.gameObject);
            }
            if (_requestView != null) {
                Destroy(_requestView.gameObject);
            }
            if (_emoteView != null) {
                Destroy(_emoteView.gameObject);
            }
        }
    }
}
