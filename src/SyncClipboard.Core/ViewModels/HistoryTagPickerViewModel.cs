using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.ViewModels;

public sealed partial class HistoryTagSuggestion(string name, bool isAssigned) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool isAssigned = isAssigned;
}

public sealed partial class HistoryTagPickerViewModel : ObservableObject
{
    private readonly List<string> names;
    private readonly int pinnedCount;
    private readonly HashSet<string> assigned;
    private readonly Dictionary<string, HistoryTagSuggestion> suggestionsByName;
    private readonly Func<string, bool, Task> saveTag;
    private string? inputName;

    public HistoryTagPickerViewModel(IEnumerable<string> knownNames, IEnumerable<string> assignedNames,
        Func<string, bool, Task> saveTag, IEnumerable<string>? pinnedNames = null)
    {
        assigned = assignedNames.ToHashSet(StringComparer.Ordinal);
        var pinned = (pinnedNames ?? []).Distinct(StringComparer.Ordinal).ToArray();
        pinnedCount = pinned.Length;
        names = HistoryTagOrdering.Order(knownNames.Concat(assigned), pinned).ToList();
        suggestionsByName = names.ToDictionary(name => name,
            name => new HistoryTagSuggestion(name, assigned.Contains(name)), StringComparer.Ordinal);
        this.saveTag = saveTag;
        RefreshSuggestions();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddInput))]
    [NotifyPropertyChangedFor(nameof(AddInputLabel))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSuggestions))]
    private IReadOnlyList<HistoryTagSuggestion> suggestions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyPropertyChangedFor(nameof(CanAddInput))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string error = string.Empty;

    public bool CanInteract => !IsBusy;
    public bool HasNoSuggestions => Suggestions.Count == 0;
    public bool CanAddInput => !IsBusy && inputName is { } name && !assigned.Contains(name);
    public string AddInputLabel => inputName is { } name && !suggestionsByName.ContainsKey(name)
        ? Strings.HistoryTagsCreateAndAdd : Strings.HistoryTagsAdd;
    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);
    public string ValidationMessage => !string.IsNullOrEmpty(Error) ? Error
        : !string.IsNullOrWhiteSpace(Search) && inputName is null ? Strings.HistoryTagsInvalid : string.Empty;

    partial void OnSearchChanged(string value)
    {
        inputName = null;
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                inputName = HistoryTags.NormalizeName(value);
            }
            catch (ArgumentException) { }
        }
        Error = string.Empty;
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        var query = inputName ?? Search.Trim();
        var matches = names.Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(name => suggestionsByName[name]).ToArray();
        // Preserve row controls, focus and scroll position when only an assignment changes.
        if (!Suggestions.SequenceEqual(matches))
            Suggestions = matches;
    }

    public Task<bool> AddInputAsync() => AddAsync(Search);

    public Task<bool> AddAsync(string value) => SetAssignedAsync(value, true);

    public Task<bool> ToggleAsync(string value) => SetAssignedAsync(value, null);

    private async Task<bool> SetAssignedAsync(string value, bool? assign)
    {
        if (IsBusy)
            return false;
        try
        {
            var name = HistoryTags.NormalizeName(value);
            var shouldAssign = assign ?? !assigned.Contains(name);
            if (assigned.Contains(name) == shouldAssign)
                return false;
            var searchBeforeSave = Search;
            IsBusy = true;
            Error = string.Empty;
            await saveTag(name, shouldAssign);
            UpdateAssignment(name, shouldAssign);
            if (shouldAssign && Search.Length > 0 && Search == searchBeforeSave)
                Search = string.Empty;
            else
                RefreshSuggestions();
            OnPropertyChanged(nameof(AddInputLabel));
            return true;
        }
        catch (Exception ex)
        {
            Error = ex is ArgumentException ? Strings.HistoryTagsInvalid : ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateAssignment(string name, bool shouldAssign)
    {
        if (shouldAssign)
            assigned.Add(name);
        else
            assigned.Remove(name);
        if (!suggestionsByName.TryGetValue(name, out var suggestion))
        {
            var index = names.BinarySearch(pinnedCount, names.Count - pinnedCount, name, StringComparer.Ordinal);
            names.Insert(~index, name);
            suggestion = new HistoryTagSuggestion(name, shouldAssign);
            suggestionsByName.Add(name, suggestion);
        }
        suggestion.IsAssigned = shouldAssign;
    }
}
