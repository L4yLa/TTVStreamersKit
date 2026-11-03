using System;
using TTVCore.Api.V1;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.Chat
{
    /// <summary>チャット表示の1件の内容。1行目（Main）と、必要なら2行目（Sub）からなる。</summary>
    internal sealed class ChatLine
    {
        public Action<TmpContentBuilder> Main;
        /// <summary>2行目。1行だけなら null。</summary>
        public Action<TmpContentBuilder> Sub;

        /// <summary>送信者の名前とバッジ。削除の表示に使う。</summary>
        public Action<TmpContentBuilder> Header;
        /// <summary>チャットのメッセージを含む場合、その ID。</summary>
        public string MessageId;
        public string SenderId;
        /// <summary>メッセージが2行目にあるか。</summary>
        public bool MessageInSub;
        public bool Deleted;

        public bool Highlight;
        public bool Accent;
        /// <summary>配信者へのメンション。</summary>
        public bool Ping;
        /// <summary>時刻を付けるか（システムの行には付けない）。</summary>
        public bool ShowTimestamp = true;

        public TmpContent BuildMain(string prefix)
        {
            var b = TTVCoreApi.Images.CreateTmpContent();
            if (!string.IsNullOrEmpty(prefix)) {
                b.AppendRichText(prefix);
            }
            this.Main?.Invoke(b);
            return b.Build();
        }

        public TmpContent BuildSub()
        {
            if (this.Sub == null) {
                return null;
            }
            var b = TTVCoreApi.Images.CreateTmpContent();
            this.Sub(b);
            return b.Build();
        }

        /// <summary>メッセージの部分を「削除されました」に置き換える。</summary>
        public void MarkDeleted()
        {
            if (this.Deleted || this.MessageId == null) {
                return;
            }
            this.Deleted = true;
            var header = this.Header;
            Action<TmpContentBuilder> deleted = b => {
                header?.Invoke(b);
                b.AppendRichText(header != null ? ": <color=#bbbbbbff>" : "<color=#bbbbbbff>")
                 .AppendText(Localization.T("<message deleted>", "<メッセージは削除されました>"))
                 .AppendRichText("</color>");
            };
            if (this.MessageInSub) {
                this.Sub = deleted;
            }
            else {
                this.Main = deleted;
            }
            this.Highlight = this.Highlight && this.MessageInSub;
            this.Ping = false;
        }
    }
}
