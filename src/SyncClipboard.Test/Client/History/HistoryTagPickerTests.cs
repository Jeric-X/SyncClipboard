using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryTagPickerTests
{
    private static readonly string[] LeadingNames = ["pinned"];
    private static readonly string[] AssignedNames = ["recent", "work"];
    private static readonly string[] NormalizedAddedNames = ["é", "新标签"];
    private static readonly string[] SortedNames = ["abc", "pinned", "work", "é", "新标签"];
    private static readonly string[] RemainingNames = ["keep"];
    private static readonly string[] OriginalNames = ["work", "keep"];
    private static readonly string[] PrefixRankedNames = ["wor", "workplace", "workbench", "work", "workbook", "homework", "network"];

    [TestMethod]
    [DataRow("code", "code", "decode", "CODE", "code review")]
    [DataRow(" code ", "code", "decode", "CODE", "code review")]
    [DataRow(" e\u0301 ", "é", "café", "É", "éclair")]
    public void Search_RanksExactThenPrefixThenSubstringMatches(
        string query, string exact, string pinnedPartial, string caseVariant, string partial)
    {
        var picker = new HistoryTagPickerViewModel([exact, partial], [], (_, _) => Task.CompletedTask,
            [pinnedPartial, caseVariant]);
        Assert.AreEqual(pinnedPartial, picker.SelectedSuggestion?.Name);
        picker.Search = query;
        CollectionAssert.AreEqual(new[] { exact, caseVariant, partial, pinnedPartial },
            picker.Suggestions.Select(tag => tag.Name).ToArray());
        Assert.AreEqual(exact, picker.SelectedSuggestion?.Name);
        picker.MoveSelection(true);
        Assert.AreEqual(caseVariant, picker.SelectedSuggestion?.Name);
        picker.Search = "";
        CollectionAssert.AreEqual(new[] { pinnedPartial, caseVariant, exact, partial },
            picker.Suggestions.Select(tag => tag.Name).ToArray());
    }

    [TestMethod]
    public void Search_WithoutExactMatchPrioritizesPrefixesAndKeepsPinnedOrderWithinEachGroup()
    {
        var picker = new HistoryTagPickerViewModel(["work", "workbook", "network"], [], (_, _) => Task.CompletedTask,
            ["homework", "workplace", "workbench"])
        {
            Search = "wor"
        };
        CollectionAssert.AreEqual(PrefixRankedNames,
            picker.Suggestions.Select(tag => tag.Name).ToArray());
        Assert.AreEqual("wor", picker.SelectedSuggestion?.Name);
        Assert.IsTrue(picker.Suggestions[0].IsNew);
        picker.Search = "work";
        Assert.AreEqual("work", picker.Suggestions[0].Name);
        Assert.AreEqual("work", picker.SelectedSuggestion?.Name);
    }

    [TestMethod]
    public void Selection_DefaultsToFirstAndCyclesWithinFilteredTags()
    {
        var picker = new HistoryTagPickerViewModel(["code", "work", "week"], [], (_, _) => Task.CompletedTask);
        Assert.AreEqual("code", picker.SelectedSuggestion?.Name);
        picker.MoveSelection(false);
        Assert.AreEqual("work", picker.SelectedSuggestion?.Name);
        picker.MoveSelection(true);
        Assert.AreEqual("code", picker.SelectedSuggestion?.Name);
        picker.Search = "w";
        Assert.AreEqual("w", picker.SelectedSuggestion?.Name);
        picker.MoveSelection(true);
        Assert.AreEqual("week", picker.SelectedSuggestion?.Name);
        picker.MoveSelection(true);
        Assert.AreEqual("work", picker.SelectedSuggestion?.Name);
        Assert.HasCount(1, picker.Suggestions.Where(tag => tag.IsCurrent));
        picker.Search = "missing";
        picker.MoveSelection(true);
        Assert.AreEqual("missing", picker.SelectedSuggestion?.Name);
        Assert.IsNotNull(picker.SelectedSuggestion);
        Assert.IsTrue(picker.SelectedSuggestion.IsNew);
        picker.Search = "";
        Assert.AreEqual("code", picker.SelectedSuggestion?.Name);
    }

    [TestMethod]
    public void Search_FindsAllTagsAndPreservesOrdinalOrder()
    {
        var names = LeadingNames.Concat(Enumerable.Range(0, 30).Select(i => $"tag{i:D2}"));
        var picker = new HistoryTagPickerViewModel(names, ["tag25"], (_, _) => Task.CompletedTask);
        CollectionAssert.AreEqual(names.ToArray(), picker.Suggestions.Select(t => t.Name).ToArray());
        picker.Search = " TAG25 ";
        Assert.HasCount(2, picker.Suggestions);
        Assert.AreEqual("TAG25", picker.Suggestions[0].Name);
        Assert.IsTrue(picker.Suggestions[0].IsNew);
        Assert.IsTrue(picker.Suggestions[1].IsAssigned);
        picker.Search = "tag25";
        Assert.IsTrue(picker.Suggestions.Single().IsAssigned);
        Assert.IsFalse(picker.Suggestions.Single().IsNew);
        picker.Search = "unknown";
        Assert.AreEqual("unknown", picker.Suggestions.Single().Name);
        Assert.IsTrue(picker.Suggestions.Single().IsNew);
    }

    [TestMethod]
    public void Suggestions_IncludeAssignedTagsMissingFromKnownNames()
    {
        var picker = new HistoryTagPickerViewModel(["work", "work"], ["recent", "work"], (_, _) => Task.CompletedTask);
        CollectionAssert.AreEqual(AssignedNames, picker.Suggestions.Select(tag => tag.Name).ToArray());
        Assert.IsTrue(picker.Suggestions.All(tag => tag.IsAssigned));
    }

    [TestMethod]
    public async Task InputCandidate_IsOnlyPersistedOnActivationAndPinnedTagsAreNotNew()
    {
        var saved = new List<string>();
        var picker = new HistoryTagPickerViewModel([], [], (name, _) =>
        {
            saved.Add(name);
            return Task.CompletedTask;
        }, ["pinned"])
        {
            Search = " e\u0301 "
        };
        Assert.AreEqual("é", picker.Suggestions[0].Name);
        Assert.IsTrue(picker.Suggestions[0].IsNew);
        Assert.IsEmpty(saved);
        picker.Search = " pinned ";
        Assert.IsFalse(picker.Suggestions.Single().IsNew);
        Assert.IsTrue(await picker.ToggleSelectedAsync());
        Assert.AreEqual(" pinned ", picker.Search);
        Assert.AreEqual("pinned", saved.Single());
    }

    [TestMethod]
    public async Task Activation_NormalizesNamesAndOnlyClearsInputForNewTags()
    {
        var saved = new List<string>();
        var picker = new HistoryTagPickerViewModel(["é", "work", "pinned"], [], (name, _) =>
        {
            saved.Add(name);
            return Task.CompletedTask;
        })
        {
            Search = " e\u0301 "
        };
        Assert.IsTrue(await picker.ToggleSelectedAsync());
        Assert.AreEqual(" e\u0301 ", picker.Search);
        Assert.AreEqual("é", picker.SelectedSuggestion?.Name);
        picker.Search = " 新标签 ";
        Assert.IsTrue(await picker.ToggleSelectedAsync());
        Assert.IsEmpty(picker.Search);
        CollectionAssert.AreEqual(NormalizedAddedNames, saved);
        Assert.AreEqual("新标签", picker.SelectedSuggestion?.Name);
        Assert.IsTrue(picker.Suggestions.Single(t => t.Name == "新标签").IsAssigned);
        Assert.IsFalse(picker.Suggestions.Single(t => t.Name == "work").IsAssigned);
        Assert.IsTrue(await picker.ToggleAsync("abc"));
        CollectionAssert.AreEqual(SortedNames,
            picker.Suggestions.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task FailedSave_KeepsInputAndAllowsRetryWithoutMarkingAssigned()
    {
        var fail = true;
        var picker = new HistoryTagPickerViewModel([], [], (_, _) => fail
            ? Task.FromException(new InvalidOperationException("Save failed")) : Task.CompletedTask)
        {
            Search = "work"
        };
        Assert.IsFalse(await picker.ToggleSelectedAsync());
        Assert.AreEqual("work", picker.Search);
        Assert.AreEqual("Save failed", picker.ValidationMessage);
        Assert.IsTrue(picker.Suggestions.Single().IsNew);
        Assert.IsFalse(picker.Suggestions.Single().IsAssigned);
        fail = false;
        Assert.IsTrue(await picker.ToggleSelectedAsync());
        Assert.IsEmpty(picker.ValidationMessage);
        Assert.IsTrue(picker.Suggestions.Single().IsAssigned);
        Assert.IsFalse(picker.Suggestions.Single().IsNew);
    }

    [TestMethod]
    public async Task PendingSave_BlocksDuplicateRequestsAndInvalidNamesNeverSave()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var picker = new HistoryTagPickerViewModel([], [], (_, _) =>
        {
            calls++;
            return gate.Task;
        })
        {
            Search = new string('a', 65)
        };
        Assert.IsTrue(picker.HasValidationMessage);
        Assert.IsTrue(picker.HasNoSuggestions);
        Assert.IsFalse(await picker.ToggleSelectedAsync());
        Assert.IsFalse(await picker.ToggleAsync(picker.Search));
        Assert.AreEqual(0, calls);
        picker.Search = "work";
        var saving = picker.ToggleSelectedAsync();
        Assert.IsTrue(picker.IsBusy);
        Assert.IsFalse(await picker.ToggleAsync("work"));
        Assert.AreEqual(1, calls);
        picker.Search = "next";
        gate.SetResult();
        Assert.IsTrue(await saving);
        Assert.AreEqual("next", picker.Search);
        Assert.AreEqual("next", picker.SelectedSuggestion?.Name);
        Assert.IsNotNull(picker.SelectedSuggestion);
        Assert.IsTrue(picker.SelectedSuggestion.IsNew);
        Assert.IsFalse(picker.IsBusy);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("tag")]
    public async Task PendingSave_PreservesNavigationAndActivatesTheNewSelection(string query)
    {
        var pending = new TaskCompletionSource();
        var saved = new List<(string Name, bool Add)>();
        var picker = new HistoryTagPickerViewModel(["code", "tag1", "tag2"], [], (name, add) =>
        {
            saved.Add((name, add));
            return saved.Count == 1 ? pending.Task : Task.CompletedTask;
        })
        {
            Search = query
        };
        var saving = picker.ToggleSelectedAsync();
        picker.MoveSelection(true);
        Assert.AreEqual("tag1", picker.SelectedSuggestion?.Name);
        pending.SetResult();
        Assert.IsTrue(await saving);
        Assert.AreEqual(query, picker.Search);
        Assert.AreEqual("tag1", picker.SelectedSuggestion?.Name);
        Assert.IsTrue(await picker.ToggleSelectedAsync());
        Assert.AreEqual(("tag1", true), saved[1]);
        Assert.IsTrue(picker.Suggestions.Single(tag => tag.Name == "tag1").IsAssigned);
    }

    [TestMethod]
    public async Task Toggle_RemovesAndReaddsAssignedTagWithoutChangingOtherTags()
    {
        var assignments = new HashSet<string> { "work", "keep" };
        var picker = new HistoryTagPickerViewModel(["work", "keep"], assignments, (name, add) =>
        {
            if (add)
                assignments.Add(name);
            else
                assignments.Remove(name);
            return Task.CompletedTask;
        })
        {
            Search = "work"
        };
        Assert.IsTrue(await picker.ToggleAsync("work"));
        Assert.AreEqual("work", picker.Search);
        Assert.IsFalse(picker.Suggestions.Single().IsAssigned);
        CollectionAssert.AreEquivalent(RemainingNames, assignments.ToArray());
        Assert.IsTrue(await picker.ToggleAsync("work"));
        Assert.AreEqual("work", picker.Search);
        Assert.IsTrue(picker.Suggestions.Single(t => t.Name == "work").IsAssigned);
        CollectionAssert.AreEquivalent(OriginalNames, assignments.ToArray());
    }

    [TestMethod]
    public async Task FailedRemoval_PreservesAssignmentAndCanBeRetried()
    {
        var fail = true;
        var picker = new HistoryTagPickerViewModel(["work"], ["work"], (_, add) =>
        {
            Assert.IsFalse(add);
            return fail ? Task.FromException(new InvalidOperationException("Remove failed")) : Task.CompletedTask;
        });
        Assert.IsFalse(await picker.ToggleAsync("work"));
        Assert.IsTrue(picker.Suggestions.Single().IsAssigned);
        Assert.AreEqual("Remove failed", picker.ValidationMessage);
        fail = false;
        Assert.IsTrue(await picker.ToggleAsync("work"));
        Assert.IsFalse(picker.Suggestions.Single().IsAssigned);
    }
}
