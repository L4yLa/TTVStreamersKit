using IPA.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVStreamersKit.Configuration;
using TTVStreamersKit.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// リクエスト機能の中心。チャットのコマンドを受け、BeatSaver で曲を探し、キューを管理する。
    /// 状態はメインスレッドからだけ触る（TTVCore のイベントはメインスレッドで届き、await もメインスレッドに戻る）。
    /// </summary>
    internal sealed partial class RequestManager : IInitializable, IDisposable
    {
        private const string GameScene = "GameCore";
        private static readonly TimeSpan PendingDifficultyLifetime = TimeSpan.FromSeconds(30);

        private readonly PluginConfig _pluginConfig;
        private readonly RequestConfig _config;
        private readonly string _dir = Path.Combine(UnityGame.UserDataPath, "TTVStreamersKit");
        private readonly CommandRegistry _commands = new CommandRegistry();
        private readonly Queue<Func<Task>> _work = new Queue<Func<Task>>();
        private readonly Dictionary<string, (string Input, DateTime Expires)> _pendingDifficulty = new Dictionary<string, (string, DateTime)>();

        private RequestStore _store;
        private RequestLists _lists;
        private Messages _messages;
        private ChatReply _reply;
        private bool _processing;
        private bool _inSong;
        private bool _initialized;

        public RequestManager(PluginConfig config)
        {
            _pluginConfig = config;
            _config = config.Request;
        }

        public IReadOnlyList<SongRequest> Queue => _store?.Queue ?? (IReadOnlyList<SongRequest>)Array.Empty<SongRequest>();
        public IReadOnlyList<SongRequest> History => _store?.History ?? (IReadOnlyList<SongRequest>)Array.Empty<SongRequest>();
        public bool IsOpen => _config.RequestQueueOpen;

        /// <summary>今の曲（キューの画面から再生した曲）。!link に使う。</summary>
        public SongRequest CurrentSong { get; set; }

        /// <summary>キューか履歴か受付状態が変わった。</summary>
        public event Action QueueChanged;

        /// <summary>曲がキューに追加された。</summary>
        public event Action<SongRequest> RequestAdded;

        public void Initialize()
        {
            Directory.CreateDirectory(_dir);
            _store = new RequestStore(_dir);
            _store.Load(_config.SessionResetAfterXHours);
            _lists = new RequestLists(_dir);
            _lists.LoadAll();
            _messages = Messages.Load(_dir, Localization.IsJapanese);
            _reply = new ChatReply(_config);
            // 起動時に受付を開始する（チャットには知らせない）
            if (_config.AutoOpenRequestQueue && !_config.RequestQueueOpen) {
                _config.RequestQueueOpen = true;
            }
            this.RegisterCommands();
            _commands.ApplyOverrides(_dir);

            TTVCoreApi.Chat.MessageReceived += this.OnChatMessage;
            SceneManager.activeSceneChanged += this.OnActiveSceneChanged;
            Application.quitting += this.OnQuitting;
            Localization.Changed += this.ReloadMessages;
            _initialized = true;
            this.RaiseQueueChanged();
        }

        /// <summary>言語を変えたときに、その言語の文言を読み直す。</summary>
        private void ReloadMessages()
        {
            _messages = Messages.Load(_dir, Localization.IsJapanese);
            this.RaiseQueueChanged();
        }

        public void Dispose()
        {
            if (!_initialized) {
                return;
            }
            TTVCoreApi.Chat.MessageReceived -= this.OnChatMessage;
            SceneManager.activeSceneChanged -= this.OnActiveSceneChanged;
            Application.quitting -= this.OnQuitting;
            Localization.Changed -= this.ReloadMessages;
            _store.SaveAll();
        }

        private void OnQuitting()
        {
            // 履歴のリクエストした人の情報は、ゲームの終了時に消す
            _store?.ForgetHistoryRequesters();
            _store?.SaveAll();
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            _inSong = next.name == GameScene;
            if (!_inSong) {
                this.PumpWork();
            }
        }

        // ================= コマンドの受け付け =================

        private void OnChatMessage(ChatMessage message)
        {
            if (!_pluginConfig.RequestEnabled || message?.Sender == null || string.IsNullOrEmpty(message.Text)) {
                return;
            }
            if (message.Source != null && !_config.AcceptSharedChatCommands) {
                return;
            }
            var text = message.Text.Trim();
            if (!text.StartsWith("!", StringComparison.Ordinal) && !text.StartsWith("！", StringComparison.Ordinal)) {
                return;
            }
            if (_lists.BlockedUsers.Contains(message.Sender.Login)) {
                return;
            }
            var ctx = new CommandContext {
                User = new Requester { Id = message.Sender.Id, Login = message.Sender.Login, DisplayName = message.Sender.DisplayName },
                Roles = message.Sender.Roles,
            };
            this.Execute(text, ctx);
        }

        /// <summary>キューの画面のキーボードからの操作。</summary>
        public void ExecuteLocal(string text)
        {
            var broadcaster = TTVCoreApi.Channel.Broadcaster;
            var ctx = new CommandContext {
                User = new Requester { Id = broadcaster?.Id ?? "", Login = broadcaster?.Login ?? "", DisplayName = broadcaster?.DisplayName ?? "" },
                Roles = ChatRoles.Broadcaster,
                IsLocal = true,
            };
            this.Execute(text, ctx);
        }

        private void Execute(string text, CommandContext ctx)
        {
            text = "!" + text.Substring(1);
            var space = IndexOfWhitespace(text);
            var name = space < 0 ? text.Substring(1) : text.Substring(1, space - 1);
            var arg = space < 0 ? "" : text.Substring(space).Trim();
            var cmd = _commands.Find(name);
            if (cmd == null || !cmd.Enabled) {
                return;
            }
            if (!cmd.IsAllowed(ctx, _config.ModFullRights)) {
                this.Reply("PermissionDenied", new Dictionary<string, string> { ["command"] = cmd.Name, ["roles"] = this.DescribeRoles(cmd.Permission) });
                return;
            }
            if (arg == "?") {
                this.ReplyHelp(cmd);
                return;
            }
            if ((cmd.Argument == ArgumentKind.Required && arg.Length == 0)
                || (cmd.Argument == ArgumentKind.Alphanumeric && !Regex.IsMatch(arg, "^[0-9A-Za-z]+$"))) {
                this.Reply("Usage", UsageVars(cmd));
                return;
            }
            ctx.Argument = arg;
            try {
                cmd.Handler(ctx);
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: !{cmd.Name} failed: {e}");
            }
        }

        private static int IndexOfWhitespace(string s)
        {
            for (var i = 0; i < s.Length; i++) {
                if (char.IsWhiteSpace(s[i])) {
                    return i;
                }
            }
            return -1;
        }

        // ================= 処理の順番 =================

        /// <summary>リクエストは受付待ちの列に積み、1件ずつ処理する。PerformanceMode では曲の最中は処理しない。</summary>
        private void EnqueueWork(Func<Task> work)
        {
            _work.Enqueue(work);
            this.PumpWork();
        }

        private async void PumpWork()
        {
            if (_processing) {
                return;
            }
            _processing = true;
            try {
                while (_work.Count > 0) {
                    if (_inSong && _config.PerformanceMode) {
                        return;
                    }
                    var work = _work.Dequeue();
                    try {
                        await work();
                    }
                    catch (Exception e) {
                        Plugin.Log.Error($"Requests: {e}");
                    }
                }
            }
            finally {
                _processing = false;
            }
        }

        // ================= 返信 =================

        private void Reply(string key, IDictionary<string, string> vars = null)
        {
            _reply.Send(_messages.Format(key, vars ?? new Dictionary<string, string>()));
        }

        private void ReplyText(string text) => _reply.Send(text);

        private string DescribeRoles(CommandPermission p)
        {
            var names = new List<string>();
            if ((p & CommandPermission.Everyone) != 0) {
                names.Add(_messages.Get("RoleEveryone"));
            }
            if ((p & CommandPermission.Subscriber) != 0) {
                names.Add(_messages.Get("RoleSubscriber"));
            }
            if ((p & CommandPermission.Vip) != 0) {
                names.Add(_messages.Get("RoleVip"));
            }
            if ((p & CommandPermission.Moderator) != 0) {
                names.Add(_messages.Get("RoleModerator"));
            }
            names.Add(_messages.Get("RoleBroadcaster"));
            return string.Join(" / ", names.Distinct());
        }

        /// <summary>曲と利用者の変数。</summary>
        internal Dictionary<string, string> SongVars(MapInfo map, Requester user = null, SongRequest request = null)
        {
            var now = DateTime.Now;
            var vars = new Dictionary<string, string> {
                ["user"] = user?.Name ?? request?.Requester?.Name ?? "",
                ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["time"] = now.ToString("HH:mm", CultureInfo.InvariantCulture),
                ["nl"] = "\n",
            };
            if (map != null) {
                vars["songName"] = map.SongName;
                vars["songSubName"] = map.SongSubName;
                vars["songAuthorName"] = map.SongAuthorName;
                vars["levelAuthorName"] = map.LevelAuthorName;
                vars["key"] = map.Key;
                vars["length"] = FormatDuration(map.Duration);
                vars["rating"] = map.Rating > 0f ? (map.Rating * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "";
                vars["stars"] = map.MaxStars.HasValue ? map.MaxStars.Value.ToString("0.00", CultureInfo.InvariantCulture) + "★" : "";
                vars["beatsaverLink"] = map.BeatSaverUrl;
                vars["njs"] = map.MaxNjs.ToString("0.#", CultureInfo.InvariantCulture);
            }
            if (request != null) {
                vars["difficulty"] = request.DifficultyDisplayName;
            }
            return vars;
        }

        internal static string FormatDuration(int seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";
        }

        private void RaiseQueueChanged()
        {
            this.WriteObsFiles();
            try {
                this.QueueChanged?.Invoke();
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: {e}");
            }
        }
    }
}
