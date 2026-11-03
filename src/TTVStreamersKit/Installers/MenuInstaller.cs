using TTVStreamersKit.Chat;
using TTVStreamersKit.Requests.UI;
using TTVStreamersKit.UI;
using Zenject;

namespace TTVStreamersKit.Installers
{
    internal sealed class MenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            this.Container.BindInterfacesAndSelfTo<ChatDisplay>().FromNewComponentAsViewController().AsSingle().NonLazy();
            this.Container.BindInterfacesAndSelfTo<SettingsMenu>().AsSingle().NonLazy();
            this.Container.BindInterfacesAndSelfTo<RequestButton>().AsSingle().NonLazy();
            this.Container.BindInterfacesAndSelfTo<DifficultyHighlighter>().AsSingle().NonLazy();
        }
    }
}
