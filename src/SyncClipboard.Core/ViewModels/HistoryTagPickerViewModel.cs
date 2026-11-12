using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.ViewModels;

public sealed partial class HistoryTagSuggestion(string name, bool isAssigned, bool isNew = false) : ObservableObject
{
    public string Name { get; } = name;
    public bool IsNew { get; } = isNew;

    [ObservableProperty]
    private bool isAssigned = isAssigned;

    [ObservableProperty]
    private bool isCurrent;
}

public sealed partial class HistoryTagPickerViewModel : ObservableObject
{
    private readonly List<string> names;
    private readonly int pinnedCount;
    private readonly Dictionary<string, HistoryTagSuggestion> suggestionsByName;
    private readonly Func<string, bool, Task> saveTag;
    private string? inputName;
    private HistoryTagSuggestion? newSuggestion;

    public HistoryTagPickerViewModel(IEnumerable<string> knownNames, IEnumerable<string> assignedNames,
        Func<string, bool, Task> saveTag, IEnumerable<string>? pinnedNames = null)
    {
        var assigned = assignedNames.ToHashSet(StringComparer.Ordinal);
        var pinned = (pinnedNames ?? []).Distinct(StringComparer.Ordinal).ToArray();
        pinnedCount = pinned.Length;
        names = HistoryTagOrdering.Order(knownNames.Concat(assigned), pinned).ToList();
        suggestionsByName = names.ToDictionary(name => name,
            name => new HistoryTagSuggestion(name, assigned.Contains(name)), StringComparer.Ordinal);
        this.saveTag = saveTag;
        RefreshSuggestions();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSuggestions))]
    private IReadOnlyList<HistoryTagSuggestion> suggestions = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string error = string.Empty;

    [ObservableProperty]
    private HistoryTagSuggestion? selectedSuggestion;

    partial void OnSelectedSuggestionChanged(HistoryTagSuggestion? oldValue, HistoryTagSuggestion? newValue)
    {
        if (oldValue is not null)
            oldValue.IsCurrent = false;
        if (newValue is not null)
            newValue.IsCurrent = true;
    }

    public void MoveSelection(bool forward)
    {
        if (Suggestions.Count == 0)
            return;
        var index = SelectedSuggestion is null ? -1 : Suggestions.ToList().IndexOf(SelectedSuggestion);
        SelectedSuggestion = Suggestions[(index + (forward ? 1 : -1) + Suggestions.Count) % Suggestions.Count];
    }

    public bool HasNoSuggestions => Suggestions.Count == 0;
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
        SelectedSuggestion = Suggestions.FirstOrDefault();
    }

    private void RefreshSuggestions()
    {
        var query = inputName ?? Search.Trim();
        var matches = names.Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(name => string.Equals(name, query, StringComparison.Ordinal))
            .ThenByDescending(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Select(name => suggestionsByName[name]).ToList();
        if (inputName is { } candidate && !suggestionsByName.ContainsKey(candidate))
        {
            if (newSuggestion?.Name != candidate)
                newSuggestion = new HistoryTagSuggestion(candidate, false, isNew: true);
            matches.Insert(0, newSuggestion);
        }
        else
        {
            newSuggestion = null;
        }
        // Preserve row controls and scroll position when only an assignment changes.
        if (!Suggestions.SequenceEqual(matches))
            Suggestions = matches.ToArray();
        if (SelectedSuggestion is null || !matches.Contains(SelectedSuggestion))
            SelectedSuggestion = matches.FirstOrDefault();
    }

    public Task<bool> ToggleSelectedAsync() => SelectedSuggestion is { } selected
        ? ToggleAsync(selected.Name) : Task.FromResult(false);

    public async Task<bool> ToggleAsync(string value)
    {
        if (IsBusy)
            return false;
        try
        {
            var name = HistoryTags.NormalizeName(value);
            var isNewTag = !suggestionsByName.TryGetValue(name, out var suggestion);
            var shouldAssign = suggestion?.IsAssigned != true;
            var searchBeforeSave = Search;
            var selectionBeforeSave = SelectedSuggestion;
            IsBusy = true;
            Error = string.Empty;
            await saveTag(name, shouldAssign);
            UpdateAssignment(name, shouldAssign);
            var interactionUnchanged = Search == searchBeforeSave && ReferenceEquals(SelectedSuggestion, selectionBeforeSave);
            if (shouldAssign && isNewTag && Search.Length > 0 && interactionUnchanged)
                Search = string.Empty;
            else
                RefreshSuggestions();
            if (interactionUnchanged)
                SelectedSuggestion = Suggestions.FirstOrDefault(tag => tag.Name == name) ?? SelectedSuggestion;
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
