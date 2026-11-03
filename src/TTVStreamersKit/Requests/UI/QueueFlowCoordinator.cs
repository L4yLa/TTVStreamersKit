using BeatSaberMarkupLanguage;
using HMUI;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.Requests.UI
{
    /// <summary>キューの画面を表示する FlowCoordinator。</summary>
    internal sealed class QueueFlowCoordinator : FlowCoordinator
    {
        private QueueViewController _view;
        private RequestManager _manager;
        private SongPlayer _player;

        /// <summary>この画面を開いた FlowCoordinator。</summary>
        public FlowCoordinator Parent { get; private set; }

        public void Setup(RequestManager manager, SongPlayer player)
        {
            _manager = manager;
            _player = player;
        }

        /// <summary>今いちばん手前の画面から開く。</summary>
        public void Open()
        {
            if (this.isActivated) {
                return;
            }
            this.Parent = BeatSaberUI.MainFlowCoordinator.YoungestChildFlowCoordinatorOrSelf();
            this.Parent.PresentFlowCoordinator(this);
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation) {
                this.SetTitle(Localization.T("Song requests", "リクエスト"));
                this.showBackButton = true;
                _view = BeatSaberUI.CreateViewController<QueueViewController>();
                _view.Setup(_manager, _player);
            }
            if (addedToHierarchy) {
                this.ProvideInitialViewControllers(_view);
            }
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            (this.Parent ?? BeatSaberUI.MainFlowCoordinator).DismissFlowCoordinator(this);
        }
    }
}
