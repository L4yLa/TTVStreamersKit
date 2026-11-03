using System;
using System.Collections.Generic;
using System.Linq;
using TTVCore.Api.V1;

namespace TTVStreamersKit.Requests
{
    /// <summary>チャットへの返信。TTVCore の送信の窓口を使う。</summary>
    internal sealed class ChatReply
    {
        // Twitch の1通の上限
        public const int MaxLength = 500;

        private readonly RequestConfig _config;
        private ChatSender _sender;

        public ChatReply(RequestConfig config)
        {
            _config = config;
        }

        /// <summary>送る。%nl% などで改行を含む場合は行ごとに送る。</summary>
        public void Send(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) {
                return;
            }
            foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)) {
                var message = _config.BotPrefix ? "! " + line : line;
                if (message.Length > MaxLength) {
                    message = message.Substring(0, MaxLength);
                }
                this.SendOne(message);
            }
        }

        private async void SendOne(string message)
        {
            try {
                if (_sender == null) {
                    _sender = TTVCoreApi.Chat.CreateSender("TTVStreamersKit Requests");
                }
                var result = await _sender.SendAsync(message);
                if (result.Status != SendStatus.Sent) {
                    Plugin.Log.Warn($"Requests: reply was not sent ({result.Status}{(result.DropReasonMessage != null ? ": " + result.DropReasonMessage : "")})");
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to send a reply: {e.Message}");
            }
        }

        /// <summary>項目を区切って並べ、<paramref name="maxMessages"/> 通に収まらない分を数で返す。</summary>
        public static List<string> Pack(IList<string> items, string separator, int maxLength, int maxMessages, out int omitted)
        {
            var messages = new List<string>();
            var current = "";
            var used = 0;
            foreach (var item in items) {
                var candidate = current.Length == 0 ? item : current + separator + item;
                if (candidate.Length <= maxLength) {
                    current = candidate;
                    used++;
                    continue;
                }
                if (messages.Count + 1 >= maxMessages) {
                    break;
                }
                messages.Add(current);
                current = item;
                used++;
            }
            if (current.Length > 0) {
                messages.Add(current);
            }
            omitted = items.Count - used;
            return messages;
        }
    }
}
