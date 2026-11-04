using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryTagChoice(string name, bool? initialValue) : ObservableObject
{
    public string Name { get; } = name;
    public bool? InitialValue { get; } = initialValue;

    [ObservableProperty]
    private bool? isChecked = initialValue;
}

public partial class HistoryTagEditorViewModel : ObservableObject
{
    public ObservableCollection<HistoryTagChoice> Choices { get; } = [];
    public string Title { get; }

    public HistoryTagEditorViewModel(IEnumerable<string> knownTags, IReadOnlyList<string[]> assignments)
    {
        Title = assignments.Count == 1 ? Strings.HistoryTagsEdit : string.Format(Strings.HistoryTagsSelected, assignments.Count);
        foreach (var name in knownTags.Concat(assignments.SelectMany(t => t)).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            var count = assignments.Count(t => t.Contains(name, StringComparer.Ordinal));
            Choices.Add(new(name, count == 0 ? false : count == assignments.Count ? true : null));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredChoices))]
    private string search = string.Empty;

    [ObservableProperty]
    private string error = string.Empty;

    public IEnumerable<HistoryTagChoice> FilteredChoices => Choices
        .Where(t => t.Name.Contains(Search, StringComparison.OrdinalIgnoreCase));

    public string[] Added => Choices.Where(t => t.IsChecked == true && t.InitialValue != true).Select(t => t.Name).ToArray();
    public string[] Removed => Choices.Where(t => t.IsChecked == false && t.InitialValue != false).Select(t => t.Name).ToArray();

    [RelayCommand]
    private void Add()
    {
        try
        {
            var name = HistoryTags.NormalizeName(Search);
            var existing = Choices.FirstOrDefault(t => t.Name == name);
            if (existing is not null)
                existing.IsChecked = true;
            else
                Choices.Add(new(name, false) { IsChecked = true });
            Search = string.Empty;
            Error = string.Empty;
            OnPropertyChanged(nameof(FilteredChoices));
        }
        catch (ArgumentException)
        {
            Error = Strings.HistoryTagsInvalid;
        }
    }
}
