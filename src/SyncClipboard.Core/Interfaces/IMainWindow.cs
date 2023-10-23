using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core.Interfaces
{
    public enum NavigationTransitionEffect
    {
        FromBottom = 0,
        FromLeft,
        FromRight,
        FromTop
    }

    public interface IMainWindow
    {
        public void Show();
        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect);
    }
}
