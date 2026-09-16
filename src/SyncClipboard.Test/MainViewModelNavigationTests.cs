using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class MainViewModelNavigationTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void BreadcrumbClick_PreservesIndexAndRemovesOnlyLaterPages(int index)
    {
        var window = new RecordingWindow();
        using var services = new ServiceCollection().AddSingleton<IMainWindow>(window).BuildServiceProvider();
        var viewModel = new MainViewModel(services, new ConfigBase());
        PageDefinition[] pages = [PageDefinition.SyncSetting, PageDefinition.ServerConfig, PageDefinition.About];
        foreach (var page in pages)
            viewModel.BreadcrumbList.Add(page);

        viewModel.BreadcrumbBarClicked(index);

        Assert.AreSequenceEqual(pages.Take(index + 1).ToArray(), viewModel.BreadcrumbList.ToArray());
        Assert.HasCount(index < pages.Length - 1 ? 1 : 0, window.Calls);
        if (window.Calls.Count > 0)
            Assert.AreEqual(new NavigationCall(pages[index], NavigationTransitionEffect.FromLeft, null), window.Calls[0]);
    }

    [TestMethod]
    public void ForwardAndBackNavigation_PreservesPageParameterAndRoot()
    {
        var window = new RecordingWindow();
        using var services = new ServiceCollection().AddSingleton<IMainWindow>(window).BuildServiceProvider();
        var viewModel = new MainViewModel(services, new ConfigBase());
        viewModel.BreadcrumbList.Add(PageDefinition.About);
        var parameter = new object();

        viewModel.NavigateToNextLevel(PageDefinition.License, parameter);
        Assert.AreSequenceEqual([PageDefinition.About, PageDefinition.License], viewModel.BreadcrumbList.ToArray());
        viewModel.NavigateToLastLevel();
        viewModel.NavigateToLastLevel();

        Assert.AreSequenceEqual([PageDefinition.About], viewModel.BreadcrumbList.ToArray());
        Assert.AreSequenceEqual(
        [
            new NavigationCall(PageDefinition.License, NavigationTransitionEffect.FromRight, parameter),
            new NavigationCall(PageDefinition.About, NavigationTransitionEffect.FromLeft, null)
        ], window.Calls.ToArray());
    }

    private sealed record NavigationCall(PageDefinition Page, NavigationTransitionEffect Effect, object? Parameter);

    private sealed class RecordingWindow : IMainWindow
    {
        public List<NavigationCall> Calls { get; } = [];

        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? para)
            => Calls.Add(new NavigationCall(page, effect, para));

        public void Show() => throw new NotSupportedException();
        public void OpenPage(PageDefinition page, object? para = null) => throw new NotSupportedException();
        public void NavigateToLastLevel() => throw new NotSupportedException();
        public void NavigateToNextLevel(PageDefinition page, object? para) => throw new NotSupportedException();
        public void SetFont(string font) => throw new NotSupportedException();
        public void ExitApp() => throw new NotSupportedException();
    }
}
