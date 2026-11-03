using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TTVCore.Api.V1;

namespace TTVStreamersKit.Requests
{
    /// <summary>コマンドを使える立場。どれか1つを満たせば使える。</summary>
    [Flags]
    public enum CommandPermission
    {
        None = 0,
        Everyone = 1,
        Subscriber = 2,
        Vip = 4,
        /// <summary>モデレーターと配信者。</summary>
        Moderator = 8,
        Broadcaster = 16,
    }

    public enum ArgumentKind
    {
        /// <summary>引数を取らない（付いていても無視する）。</summary>
        None,
        /// <summary>1文字以上が必要。</summary>
        Required,
        /// <summary>英数字のみ。</summary>
        Alphanumeric,
        /// <summary>あってもなくてもよい。</summary>
        Optional,
    }

    /// <summary>コマンドを実行した人とその場所。</summary>
    internal sealed class CommandContext
    {
        public Requester User;
        public ChatRoles Roles;
        /// <summary>キューの画面のキーボードからの操作。すべてのコマンドを使え、受付の停止中でもリクエストできる。</summary>
        public bool IsLocal;
        public string Argument = "";

        public bool IsBroadcaster => this.IsLocal || (this.Roles & ChatRoles.Broadcaster) != 0;
        public bool IsModerator => this.IsBroadcaster || (this.Roles & ChatRoles.Moderator) != 0;
        public bool IsSubscriber => (this.Roles & (ChatRoles.Subscriber | ChatRoles.Founder)) != 0;
        public bool IsVip => (this.Roles & ChatRoles.Vip) != 0;
    }

    internal sealed class CommandDefinition
    {
        public string Name;
        public string[] Aliases = Array.Empty<string>();
        public CommandPermission Permission;
        public ArgumentKind Argument;
        public string Usage;
        public bool Enabled = true;
        public Action<CommandContext> Handler;

        public bool IsAllowed(CommandContext ctx, bool modFullRights)
        {
            if (ctx.IsLocal || ctx.IsBroadcaster) {
                return true;
            }
            if (modFullRights && ctx.IsModerator) {
                return true;
            }
            var p = this.Permission;
            return (p & CommandPermission.Everyone) != 0
                   || ((p & CommandPermission.Subscriber) != 0 && ctx.IsSubscriber)
                   || ((p & CommandPermission.Vip) != 0 && ctx.IsVip)
                   || ((p & CommandPermission.Moderator) != 0 && ctx.IsModerator);
        }
    }

    /// <summary>コマンドの一覧。有効・無効、権限、別名は commands.json で変えられる。</summary>
    internal sealed class CommandRegistry
    {
        private sealed class Override
        {
            [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
            [JsonProperty("aliases")] public List<string> Aliases { get; set; } = new List<string>();
            [JsonProperty("permissions", ItemConverterType = typeof(StringEnumConverter))]
            public List<CommandPermission> Permissions { get; set; } = new List<CommandPermission>();
        }

        private readonly List<CommandDefinition> _commands = new List<CommandDefinition>();
        private readonly Dictionary<string, CommandDefinition> _byName = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<CommandDefinition> All => _commands;

        public void Add(CommandDefinition def) => _commands.Add(def);

        /// <summary>commands.json を読み、なければ既定の内容で作る。</summary>
        public void ApplyOverrides(string dir)
        {
            var path = Path.Combine(dir, "commands.json");
            Dictionary<string, Override> overrides = null;
            try {
                if (File.Exists(path)) {
                    overrides = JsonConvert.DeserializeObject<Dictionary<string, Override>>(File.ReadAllText(path, Encoding.UTF8));
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to read {path}: {e.Message}");
            }
            overrides = overrides != null
                ? new Dictionary<string, Override>(overrides, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Override>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in _commands) {
                if (overrides.TryGetValue(c.Name, out var o) && o != null) {
                    c.Enabled = o.Enabled;
                    if (o.Aliases != null) {
                        c.Aliases = o.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim().TrimStart('!')).ToArray();
                    }
                    if (o.Permissions != null && o.Permissions.Count > 0) {
                        c.Permission = o.Permissions.Aggregate(CommandPermission.None, (a, b) => a | b);
                    }
                }
                else {
                    overrides[c.Name] = new Override {
                        Enabled = c.Enabled,
                        Aliases = c.Aliases.ToList(),
                        Permissions = Enum.GetValues(typeof(CommandPermission)).Cast<CommandPermission>()
                            .Where(p => p != CommandPermission.None && (c.Permission & p) != 0).ToList(),
                    };
                }
            }
            try {
                Directory.CreateDirectory(dir);
                var ordered = _commands.ToDictionary(c => c.Name, c => overrides[c.Name]);
                File.WriteAllText(path, JsonConvert.SerializeObject(ordered, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to write {path}: {e.Message}");
            }

            _byName.Clear();
            foreach (var c in _commands) {
                _byName[c.Name] = c;
            }
            foreach (var c in _commands) {
                foreach (var a in c.Aliases) {
                    if (!_byName.ContainsKey(a)) {
                        _byName[a] = c;
                    }
                }
            }
        }

        public CommandDefinition Find(string name) =>
            name != null && _byName.TryGetValue(name.TrimStart('!'), out var c) ? c : null;
    }
}
