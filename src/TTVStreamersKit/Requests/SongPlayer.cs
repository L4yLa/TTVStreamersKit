using SongCore;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// キューの曲を再生する流れ。再生済みにしてキューから外し、手元になければダウンロードして
    /// 曲の一覧を読み直し、選曲画面でその曲を選び、チャットに次の曲を知らせる。
    /// </summary>
    internal sealed class SongPlayer
    {
        private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(60);

        private readonly RequestManager _manager;
        private bool _busy;

        public SongPlayer(RequestManager manager)
        {
            _manager = manager;
        }

        public bool IsBusy => _busy || SongDownloader.IsBusy;

        /// <summary>再生を始めた曲。難易度の自動選択に使う。</summary>
        public event Action<SongRequest> PlayStarted;

        /// <summary>今の処理の状態（ダウンロード中など）。何もしていなければ null。</summary>
        public string Status { get; private set; }

        /// <summary><see cref="Status"/> が変わった。メインスレッドで発生する。</summary>
        public event Action StatusChanged;

        private void SetStatus(string status)
        {
            if (this.Status == status) {
                return;
            }
            this.Status = status;
            try {
                this.StatusChanged?.Invoke();
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: {e}");
            }
        }

        /// <summary>
        /// <paramref name="fromHistory"/> なら履歴から選んだ曲で、キューは触らない。
        /// キューの曲は、選曲画面で選べると分かってから再生済みにする。ダウンロードや読み込みに失敗したときはキューに残し、
        /// 失敗したことを <see cref="Status"/> に出したままにする（次に再生するまで）。
        /// </summary>
        public async void Play(SongRequest request, bool fromHistory)
        {
            if (_busy || request?.Map == null) {
                return;
            }
            _busy = true;
            string failure = null;
            this.SetStatus(null);
            try {
                var map = request.Map;
                var level = Loader.GetLevelByHash(map.Hash);
                Plugin.Log.Info($"Requests: play {map.Key} (hash {map.Hash}, {(level != null ? "installed" : "not installed")}).");
                if (level == null) {
                    // 同じ曲がすでにフォルダーにあれば（SongCore が読み込めていなくても）ダウンロードしない
                    var folder = await Task.Run(() => LocalSongs.FindFolder(map));
                    if (folder != null) {
                        Plugin.Log.Info($"Requests: {map.Key} is already in {folder}. It is not downloaded again.");
                    }
                    else {
                        this.SetStatus(Localization.T("Downloading", "ダウンロード中"));
                        folder = await SongDownloader.DownloadAsync(map, p =>
                            this.SetStatus(Localization.T($"Downloading {p * 100f:0}%", $"ダウンロード中 {p * 100f:0}%")));
                        if (folder == null) {
                            failure = Localization.T("Download failed", "ダウンロードに失敗しました");
                            _manager.ReplyDownloadFailed(request);
                            return;
                        }
                        Plugin.Log.Info($"Requests: downloaded {map.Key} to {folder}.");
                    }
                    this.SetStatus(Localization.T("Loading songs", "曲の一覧を読み込み中"));
                    await RefreshSongsAsync();
                    level = Loader.GetLevelByHash(map.Hash);
                    if (level == null) {
                        Plugin.Log.Warn($"Requests: SongCore could not load {map.Key} ({folder}). See the SongCore errors above. The request stays in the queue.");
                        failure = Localization.T("The song could not be loaded", "曲を読み込めませんでした");
                        return;
                    }
                }

                if (fromHistory) {
                    _manager.CurrentSong = request;
                }
                else {
                    _manager.MarkPlayed(request);
                }
                try {
                    this.PlayStarted?.Invoke(request);
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Requests: {e}");
                }
                Plugin.Log.Info($"Requests: selecting {map.Key} ({level.levelID}) in the song list.");
                LevelNavigator.Show(level, map.IsWip);
                _manager.AnnounceNextSong(request);
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to play {request.Map.Key}: {e}");
                failure = Localization.T("Could not play the song", "曲を再生できませんでした");
            }
            finally {
                _busy = false;
                this.SetStatus(failure);
            }
        }

        /// <summary>SongCore に曲の一覧を読み直させ、終わるまで待つ。</summary>
        private static async Task RefreshSongsAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Action<Loader, ConcurrentDictionary<string, BeatmapLevel>> handler = (loader, levels) => tcs.TrySetResult(true);
            Loader.SongsLoadedEvent += handler;
            try {
                if (!Loader.AreSongsLoading) {
                    Loader.Instance.RefreshSongs(false);
                }
                await Task.WhenAny(tcs.Task, Task.Delay(RefreshTimeout));
            }
            finally {
                Loader.SongsLoadedEvent -= handler;
            }
        }
    }
}
