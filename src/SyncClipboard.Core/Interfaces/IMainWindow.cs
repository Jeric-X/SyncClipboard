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
        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? para);
        public void NavigateToLastLevel();
        public void NavigateToNextLevel(PageDefinition page, object? para);
        public void SetFont(string font);
        public void ExitApp();
        public void Init(bool hide);
        public void ChangeTheme(string theme);
    }
}
