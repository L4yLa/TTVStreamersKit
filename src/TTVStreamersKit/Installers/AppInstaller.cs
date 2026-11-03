using HMUI;
using TTVStreamersKit.Chat;
using TTVStreamersKit.Emotes;
using TTVStreamersKit.Requests;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace TTVStreamersKit.Installers
{
    internal sealed class AppInstaller : Installer
    {
        public override void InstallBindings()
        {
            this.Container.BindInterfacesAndSelfTo<ChatFontManager>().AsSingle().NonLazy();
            this.Container.BindInterfacesAndSelfTo<RequestManager>().AsSingle().NonLazy();
            this.Container.Bind<SongPlayer>().AsSingle();
            this.Container.BindInterfacesAndSelfTo<EmoteEmitterManager>().AsSingle().NonLazy();
            this.Container.BindMemoryPool<ChatMessageView, ChatMessageView.Pool>()
                .WithInitialSize(32)
                .FromComponentInNewPrefab(CreateMessagePrefab());
        }

        private static GameObject CreateMessagePrefab()
        {
            var go = new GameObject(nameof(ChatMessageView), typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter), typeof(ImageView), typeof(ChatMessageView));
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            return go;
        }
    }
}
