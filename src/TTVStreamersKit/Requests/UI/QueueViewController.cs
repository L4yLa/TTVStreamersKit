using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using SongCore;
using System;
using System.Collections.Generic;
using System.Linq;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.Requests.UI
{
    /// <summary>キューの画面。キューと履歴を切り替えて表示し、選んだ曲を操作する。</summary>
    internal sealed class QueueViewController : BSMLResourceViewController
    {
        // 難易度の案内を同じ曲に続けて送らない間隔
        private static readonly TimeSpan PromptInterval = TimeSpan.FromSeconds(30);

        private readonly Dictionary<SongRequest, DateTime> _lastPrompt = new Dictionary<SongRequest, DateTime>();
        private RequestManager _manager;
        private SongPlayer _player;
        private List<SongRequest> _shown = new List<SongRequest>();
        private SongRequest _selected;
        private bool _showHistory;
        private Action _confirmAction;
        private string _confirmText = "";

        public override string ResourceName => "TTVStreamersKit.Requests.UI.QueueView.bsml";

        public override string Content => Localization.Bsml(base.Content);

        [UIComponent("list")]
        private CustomListTableData _list = null;

        [UIParams]
        private BSMLParserParams _parserParams = null;

        public void Setup(RequestManager manager, SongPlayer player)
        {
            _manager = manager;
            _player = player;
        }

        // ================= 表示 =================

        [UIValue("title-text")]
        private string TitleText => !string.IsNullOrEmpty(_player?.Status)
            ? $"<color=#ffd966>{_player.Status}</color>"
            : _showHistory
            ? Localization.T("History", "履歴")
            : Localization.T($"Queue ({this.QueuedCount})", $"キュー ({this.QueuedCount} 曲)");

        private int QueuedCount => _manager?.Queue.Count(r => r.Status == RequestStatus.Queued) ?? 0;

        [UIValue("history-button-text")]
        private string HistoryButtonText => _showHistory ? Localization.T("Queue", "キュー") : Localization.T("History", "履歴");

        [UIValue("open-button-text")]
        private string OpenButtonText => _manager != null && _manager.IsOpen
            ? Localization.T("Close requests", "リクエストを閉じる")
            : Localization.T("Open requests", "リクエストを開ける");

        /// <summary>選んだ曲がまだ手元にない（ダウンロードが要る）ときは「ダウンロード」にする。</summary>
        [UIValue("play-button-text")]
        private string PlayButtonText => _selected?.Map != null && Loader.GetLevelByHash(_selected.Map.Hash) == null
            ? Localization.T("Download", "ダウンロード")
            : Localization.T("Play", "プレイ");

        [UIValue("can-play")]
        private bool CanPlay => _selected != null && !SongDownloader.IsBusy
                                && (_showHistory || !Plugin.Config.Request.RequireDifficulty || _selected.HasDifficulty);

        [UIValue("can-skip")]
        private bool CanSkip => _selected != null && !_showHistory;

        [UIValue("can-block")]
        private bool CanBlock => _selected != null;

        [UIValue("can-move")]
        private bool CanMove => _selected != null && !_showHistory;

        [UIValue("confirm-text")]
        private string ConfirmText => _confirmText;

        private string _keyboardText = "";

        [UIValue("keyboard-text")]
        private string KeyboardText
        {
            get => _keyboardText;
            set => _keyboardText = value ?? "";
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            if (_manager != null) {
                _manager.QueueChanged += this.Refresh;
            }
            CoverCache.Loaded += this.OnCoverLoaded;
            if (_player != null) {
                _player.StatusChanged += this.OnPlayerStatusChanged;
            }
            this.Refresh();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            if (_manager != null) {
                _manager.QueueChanged -= this.Refresh;
            }
            CoverCache.Loaded -= this.OnCoverLoaded;
            if (_player != null) {
                _player.StatusChanged -= this.OnPlayerStatusChanged;
            }
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        private void OnPlayerStatusChanged()
        {
            this.NotifyPropertyChanged(nameof(this.TitleText));
            this.NotifyPropertyChanged(nameof(this.CanPlay));
            this.NotifyPropertyChanged(nameof(this.PlayButtonText));
        }

        private void OnCoverLoaded(string hash)
        {
            if (_shown.Any(r => string.Equals(r.Map?.Hash, hash, StringComparison.OrdinalIgnoreCase))) {
                this.Refresh();
            }
        }

        /// <summary>一覧を作り直す。選んでいた曲が残っていれば選び直す。</summary>
        public void Refresh()
        {
            if (_list == null || _manager == null) {
                return;
            }
            _shown = (_showHistory ? _manager.History : _manager.Queue).ToList();
            if (_selected != null && !_shown.Contains(_selected)) {
                _selected = null;
            }
            var requireDifficulty = Plugin.Config.Request.RequireDifficulty;
            _list.Data = _shown.Select(r => {
                var dim = !_showHistory && requireDifficulty && !r.HasDifficulty;
                var name = $"{r.Map.SongName} <size=80%>({r.Map.Key})</size>";
                // 下段は「譜面作者 長さ 難易度 リクエストした人」。難易度は指定があるときだけ
                var sub = $"{r.Map.LevelAuthorName}  {RequestManager.FormatDuration(r.Map.Duration)}";
                if (r.HasDifficulty) {
                    sub += $"  {r.DifficultyDisplayName}";
                }
                if (r.Status == RequestStatus.SearchResult) {
                    sub += $"  [{Localization.T("Search result", "検索の結果")}]";
                }
                else if (r.Requester != null) {
                    sub += $"  {r.Requester.Name}";
                }
                if (dim) {
                    name = $"<color=#808080>{name}</color>";
                    sub = $"<color=#808080>{sub}</color>";
                }
                return new CustomListTableData.CustomCellInfo(name, sub, CoverCache.Get(r.Map));
            }).ToList();
            _list.TableView.ReloadData();
            var index = _selected != null ? _shown.IndexOf(_selected) : -1;
            if (index >= 0) {
                _list.TableView.SelectCellWithIdx(index);
            }
            else {
                _list.TableView.ClearSelection();
            }
            this.NotifyAll();
        }

        private void NotifyAll()
        {
            this.NotifyPropertyChanged(nameof(this.TitleText));
            this.NotifyPropertyChanged(nameof(this.HistoryButtonText));
            this.NotifyPropertyChanged(nameof(this.OpenButtonText));
            this.NotifyPropertyChanged(nameof(this.PlayButtonText));
            this.NotifyPropertyChanged(nameof(this.CanPlay));
            this.NotifyPropertyChanged(nameof(this.CanSkip));
            this.NotifyPropertyChanged(nameof(this.CanBlock));
            this.NotifyPropertyChanged(nameof(this.CanMove));
        }

        // ================= 操作 =================

        [UIAction("select-cell")]
        private void SelectCell(TableView table, int index)
        {
            _selected = index >= 0 && index < _shown.Count ? _shown[index] : null;
            this.NotifyAll();
            // 難易度が未指定なら、リクエストした人に指定を求める（同じ曲には続けて送らない）
            var r = _selected;
            if (r != null && !_showHistory && Plugin.Config.Request.RequireDifficulty && !r.HasDifficulty && r.Requester != null) {
                if (!_lastPrompt.TryGetValue(r, out var last) || DateTime.UtcNow - last > PromptInterval) {
                    _lastPrompt[r] = DateTime.UtcNow;
                    _manager.ReplyDifficultyPrompt(r);
                }
            }
        }

        [UIAction("play")]
        private void Play()
        {
            var r = _selected;
            if (r == null || !this.CanPlay) {
                return;
            }
            _player?.Play(r, _showHistory);
            this.NotifyAll();
        }

        [UIAction("skip")]
        private void Skip()
        {
            var r = _selected;
            if (r == null) {
                return;
            }
            this.Confirm(Localization.T($"Skip {r.Map.SongName}?", $"{r.Map.SongName} をスキップしますか"), () => _manager.RemoveFromQueue(r, RequestStatus.Skipped));
        }

        [UIAction("block")]
        private void Block()
        {
            var r = _selected;
            if (r == null) {
                return;
            }
            this.Confirm(Localization.T($"Block {r.Map.SongName} ({r.Map.Key})?", $"{r.Map.SongName} ({r.Map.Key}) をブロックしますか"), () => _manager.Block(r));
        }

        [UIAction("move-up")]
        private void MoveUp() => this.MoveSelected(-1);

        [UIAction("move-down")]
        private void MoveDown() => this.MoveSelected(1);

        private void MoveSelected(int offset)
        {
            if (_selected != null && !_showHistory) {
                _manager.Move(_selected, offset);
            }
        }

        [UIAction("toggle-history")]
        private void ToggleHistory()
        {
            _showHistory = !_showHistory;
            _selected = null;
            this.Refresh();
        }

        [UIAction("toggle-open")]
        private void ToggleOpen()
        {
            _manager.SetQueueOpen(!_manager.IsOpen);
        }

        [UIAction("keyboard-enter")]
        private void KeyboardEnter(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) {
                return;
            }
            if (text.StartsWith("!", StringComparison.Ordinal) || text.StartsWith("！", StringComparison.Ordinal)) {
                _manager.ExecuteLocal(text);
            }
            else {
                _manager.SearchToQueue(text);
            }
        }

        private void Confirm(string text, Action action)
        {
            _confirmText = text;
            _confirmAction = action;
            this.NotifyPropertyChanged(nameof(this.ConfirmText));
            _parserParams?.EmitEvent("show-confirm");
        }

        [UIAction("confirm-yes")]
        private void ConfirmYes()
        {
            _parserParams?.EmitEvent("hide-confirm");
            var action = _confirmAction;
            _confirmAction = null;
            action?.Invoke();
        }
    }
}
