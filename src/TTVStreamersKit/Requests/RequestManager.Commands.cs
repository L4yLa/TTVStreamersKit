using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace TTVStreamersKit.Requests
{
    internal sealed partial class RequestManager
    {
        /// <summary>コマンド 18 種。</summary>
        private void RegisterCommands()
        {
            const CommandPermission everyone = CommandPermission.Everyone;
            const CommandPermission mod = CommandPermission.Moderator;
            this.AddCommand("bsr", new[] { "request", "add", "srm" }, everyone, ArgumentKind.Required, "!bsr <key | words>",
                c => this.BeginRequest(c, c.Argument, AddMode.Normal));
            this.AddCommand("bsrd", new[] { "bsrdiff" }, everyone, ArgumentKind.Required, "!bsrd <key> <difficulty>", this.HandleBsrd);
            this.AddCommand("difficulty", new[] { "diff", "d", "難易度" }, everyone, ArgumentKind.Required, "!difficulty <difficulty>", this.HandleDifficulty);
            this.AddCommand("wrongsong", new[] { "wrong", "oops" }, everyone, ArgumentKind.None, "!wrongsong", this.HandleWrongSong);
            this.AddCommand("queue", Array.Empty<string>(), everyone, ArgumentKind.None, "!queue", this.HandleQueue);
            this.AddCommand("link", Array.Empty<string>(), everyone, ArgumentKind.None, "!link", this.HandleLink);
            this.AddCommand("help", Array.Empty<string>(), everyone, ArgumentKind.Optional, "!help [command]", this.HandleHelp);
            this.AddCommand("lookup", new[] { "find" }, CommandPermission.Subscriber | CommandPermission.Vip | mod, ArgumentKind.Required, "!lookup <key | words>",
                c => this.EnqueueWork(() => this.HandleLookup(c)));
            this.AddCommand("open", Array.Empty<string>(), mod, ArgumentKind.None, "!open", c => this.SetQueueOpen(true));
            this.AddCommand("close", Array.Empty<string>(), mod, ArgumentKind.None, "!close", c => this.SetQueueOpen(false));
            this.AddCommand("queuestatus", Array.Empty<string>(), mod, ArgumentKind.None, "!queuestatus", this.HandleQueueStatus);
            this.AddCommand("modadd", Array.Empty<string>(), mod, ArgumentKind.Required, "!modadd <key | words>",
                c => this.BeginRequest(c, c.Argument, AddMode.ModAdd));
            this.AddCommand("att", Array.Empty<string>(), mod, ArgumentKind.Required, "!att <key | words>",
                c => this.BeginRequest(c, c.Argument, AddMode.AddToTop));
            this.AddCommand("remove", Array.Empty<string>(), mod, ArgumentKind.Required, "!remove <key | words>", this.HandleRemove);
            this.AddCommand("clearqueue", Array.Empty<string>(), mod, ArgumentKind.None, "!clearqueue", this.HandleClearQueue);
            this.AddCommand("block", Array.Empty<string>(), mod, ArgumentKind.Alphanumeric, "!block <key>",
                c => this.EnqueueWork(() => this.HandleBlock(c)));
            this.AddCommand("unblock", Array.Empty<string>(), mod, ArgumentKind.Alphanumeric, "!unblock <key>", this.HandleUnblock);
            this.AddCommand("about", Array.Empty<string>(), CommandPermission.Broadcaster, ArgumentKind.None, "!about", this.HandleAbout);
        }

        private void AddCommand(string name, string[] aliases, CommandPermission permission, ArgumentKind arg, string usage, Action<CommandContext> handler)
        {
            _commands.Add(new CommandDefinition {
                Name = name, Aliases = aliases, Permission = permission, Argument = arg, Usage = usage, Handler = handler,
            });
        }

        private void HandleBsrd(CommandContext ctx)
        {
            var parts = ctx.Argument.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = _commands.Find("bsrd");
            if (parts.Length < 2) {
                this.Reply("Usage", new Dictionary<string, string> { ["usage"] = cmd?.Usage ?? "" });
                return;
            }
            this.SetPendingDifficulty(ctx.User.Id, parts[1].Trim());
            this.BeginRequest(ctx, parts[0], AddMode.KeyOnly);
        }

        private void HandleWrongSong(CommandContext ctx)
        {
            var request = this.LatestRequestOf(ctx.User.Id);
            if (request == null) {
                this.Reply("NoRequestsInQueue", new Dictionary<string, string> { ["user"] = ctx.User.Name });
                return;
            }
            // 取り消した曲は再びリクエストできる。履歴には残さない
            _store.RemoveSessionRequest(request.Map.Key);
            this.RemoveFromQueue(request, RequestStatus.WrongSong);
            this.Reply("WrongSongDone", this.SongVars(request.Map, ctx.User, request));
        }

        private void HandleQueue(CommandContext ctx)
        {
            var queued = _store.Queue.Where(r => r.Status == RequestStatus.Queued).ToList();
            if (queued.Count == 0) {
                this.Reply("QueueEmpty");
                return;
            }
            var items = queued.Select(r => _messages.Format("QueueItem", this.SongVars(r.Map, null, r))).ToList();
            var prefix = _messages.Format("QueueList", new Dictionary<string, string> { ["list"] = "" });
            var reserve = prefix.Length + _messages.Format("QueueMore", new Dictionary<string, string> { ["count"] = "9999" }).Length + 4;
            var maxMessages = Math.Max(1, _config.MaximumQueueMessages);
            var messages = ChatReply.Pack(items, ", ", ChatReply.MaxLength - reserve, maxMessages, out var omitted);
            for (var i = 0; i < messages.Count; i++) {
                var text = i == 0 ? _messages.Format("QueueList", new Dictionary<string, string> { ["list"] = messages[i] }) : messages[i];
                if (i == messages.Count - 1 && omitted > 0) {
                    text += _messages.Format("QueueMore", new Dictionary<string, string> { ["count"] = omitted.ToString(CultureInfo.InvariantCulture) });
                }
                this.ReplyText(text);
            }
        }

        private void HandleLink(CommandContext ctx)
        {
            var song = _config.LinkType == LinkType.All && this.SelectedMap != null
                ? new SongRequest { Map = this.SelectedMap }
                : this.CurrentSong;
            if (song?.Map == null) {
                this.Reply("LinkNone");
                return;
            }
            this.Reply("Link", this.SongVars(song.Map, null, song));
        }

        /// <summary>選曲画面で選んでいる曲（!link の All で使う）。BeatSaver の曲でなければ null。</summary>
        public MapInfo SelectedMap { get; set; }

        private void HandleHelp(CommandContext ctx)
        {
            if (!string.IsNullOrWhiteSpace(ctx.Argument)) {
                var cmd = _commands.Find(ctx.Argument.Trim());
                if (cmd != null && cmd.Enabled && cmd.IsAllowed(ctx, _config.ModFullRights)) {
                    this.ReplyHelp(cmd);
                }
                return;
            }
            var names = _commands.All.Where(c => c.Enabled && c.IsAllowed(ctx, _config.ModFullRights)).Select(c => "!" + c.Name);
            this.Reply("HelpList", new Dictionary<string, string> { ["list"] = string.Join(" ", names) });
        }

        private void ReplyHelp(CommandDefinition cmd)
        {
            var key = "Help." + cmd.Name;
            this.Reply("HelpCommand", new Dictionary<string, string> {
                ["command"] = "!" + cmd.Name,
                ["description"] = _messages.Has(key) ? _messages.Get(key) : "",
                ["usage"] = cmd.Usage,
            });
        }

        private async Task HandleLookup(CommandContext ctx)
        {
            List<MapInfo> maps;
            string key;
            if (TryParseKey(ctx.Argument, out key)) {
                var (status, map) = await BeatSaverClient.GetByKeyAsync(key);
                if (status == BeatSaverStatus.Unavailable) {
                    this.Reply("BeatSaverUnavailable");
                    return;
                }
                maps = map != null ? new List<MapInfo> { map } : new List<MapInfo>();
                if (maps.Count == 0) {
                    var (s2, found) = await BeatSaverClient.SearchAsync(ctx.Argument, "Relevance");
                    if (s2 != BeatSaverStatus.Ok) {
                        this.Reply("BeatSaverUnavailable");
                        return;
                    }
                    maps = found;
                }
            }
            else {
                if (ctx.Argument.Trim().Length < 3) {
                    this.Reply("TooShort");
                    return;
                }
                var (status, found) = await BeatSaverClient.SearchAsync(ctx.Argument, "Relevance");
                if (status != BeatSaverStatus.Ok) {
                    this.Reply("BeatSaverUnavailable");
                    return;
                }
                maps = found;
            }
            if (maps.Count == 0) {
                this.Reply("NotFound", new Dictionary<string, string> { ["query"] = ctx.Argument });
                return;
            }
            var items = SortOrder.Sort(maps, _config.LookupSortOrder).Select(m => _messages.Format("LookupItem", this.SongVars(m))).ToList();
            var head = _messages.Format("LookupResult", new Dictionary<string, string> { ["count"] = maps.Count.ToString(CultureInfo.InvariantCulture), ["list"] = "" });
            var packed = ChatReply.Pack(items, ", ", ChatReply.MaxLength - head.Length - 4, 1, out _);
            this.Reply("LookupResult", new Dictionary<string, string> {
                ["count"] = maps.Count.ToString(CultureInfo.InvariantCulture),
                ["list"] = packed.FirstOrDefault() ?? "",
            });
        }

        private void HandleQueueStatus(CommandContext ctx)
        {
            var queued = _store.Queue.Where(r => r.Status == RequestStatus.Queued).ToList();
            this.Reply("QueueStatus", new Dictionary<string, string> {
                ["state"] = _messages.Get(_config.RequestQueueOpen ? "StateOpen" : "StateClosed"),
                ["count"] = queued.Count.ToString(CultureInfo.InvariantCulture),
                ["length"] = FormatDuration(queued.Sum(r => r.Map.Duration)),
            });
        }

        private void HandleRemove(CommandContext ctx)
        {
            var request = this.FindInQueue(ctx.Argument);
            if (request == null) {
                this.Reply("RemoveNotFound", new Dictionary<string, string> { ["query"] = ctx.Argument });
                return;
            }
            this.RemoveFromQueue(request, RequestStatus.Skipped);
            this.Reply("RemoveDone", this.SongVars(request.Map, null, request));
        }

        private void HandleClearQueue(CommandContext ctx)
        {
            var count = this.ClearQueue();
            this.Reply("ClearQueueDone", new Dictionary<string, string> { ["count"] = count.ToString(CultureInfo.InvariantCulture) });
        }

        private async Task HandleBlock(CommandContext ctx)
        {
            var key = ctx.Argument.Trim().ToLowerInvariant();
            if (_lists.SongBlockList.Contains(key)) {
                this.Reply("BlockExists", new Dictionary<string, string> { ["key"] = key });
                return;
            }
            _lists.SongBlockList.Add(key);
            var (status, map) = await BeatSaverClient.GetByKeyAsync(key);
            if (status == BeatSaverStatus.Ok && map != null) {
                this.Reply("BlockAdded", this.SongVars(map));
            }
            else {
                this.Reply("BlockAddedKeyOnly", new Dictionary<string, string> { ["key"] = key });
            }
        }

        private void HandleUnblock(CommandContext ctx)
        {
            var key = ctx.Argument.Trim().ToLowerInvariant();
            this.Reply(_lists.SongBlockList.Remove(key) ? "UnblockDone" : "UnblockNotFound", new Dictionary<string, string> { ["key"] = key });
        }

        private void HandleAbout(CommandContext ctx)
        {
            this.Reply("About", new Dictionary<string, string> { ["version"] = Plugin.Version });
        }
    }
}
