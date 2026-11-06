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

    [TestMethod]
    public void Search_FindsAllTagsAndPreservesOrdinalOrder()
    {
        var names = LeadingNames.Concat(Enumerable.Range(0, 30).Select(i => $"tag{i:D2}"));
        var picker = new HistoryTagPickerViewModel(names, ["tag25"], (_, _) => Task.CompletedTask);
        CollectionAssert.AreEqual(names.ToArray(), picker.Suggestions.Select(t => t.Name).ToArray());
        picker.Search = " TAG25 ";
        Assert.HasCount(1, picker.Suggestions.ToArray());
        Assert.IsTrue(picker.Suggestions.Single().IsAssigned);
        picker.Search = "tag25";
        Assert.IsFalse(picker.CanAddInput);
        picker.Search = "unknown";
        Assert.IsTrue(picker.HasNoSuggestions);
        Assert.IsTrue(picker.CanAddInput);
    }

    [TestMethod]
    public void Suggestions_IncludeAssignedTagsMissingFromKnownNames()
    {
        var picker = new HistoryTagPickerViewModel(["work", "work"], ["recent", "work"], (_, _) => Task.CompletedTask);
        CollectionAssert.AreEqual(AssignedNames, picker.Suggestions.Select(tag => tag.Name).ToArray());
        Assert.IsTrue(picker.Suggestions.All(tag => tag.IsAssigned));
    }

    [TestMethod]
    public async Task Add_NormalizesNamesAndPreventsDuplicateAssignments()
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
        Assert.IsTrue(await picker.AddInputAsync());
        Assert.IsEmpty(picker.Search);
        Assert.IsFalse(await picker.AddAsync("é"));
        picker.Search = " 新标签 ";
        Assert.IsTrue(await picker.AddInputAsync());
        CollectionAssert.AreEqual(NormalizedAddedNames, saved);
        Assert.IsTrue(picker.Suggestions.Single(t => t.Name == "新标签").IsAssigned);
        Assert.IsFalse(picker.Suggestions.Single(t => t.Name == "work").IsAssigned);
        Assert.IsTrue(await picker.AddAsync("abc"));
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
        Assert.IsFalse(await picker.AddInputAsync());
        Assert.AreEqual("work", picker.Search);
        Assert.AreEqual("Save failed", picker.ValidationMessage);
        Assert.IsEmpty(picker.Suggestions);
        Assert.IsTrue(picker.CanAddInput);
        fail = false;
        Assert.IsTrue(await picker.AddInputAsync());
        Assert.IsEmpty(picker.ValidationMessage);
        Assert.IsTrue(picker.Suggestions.Single().IsAssigned);
    }

    [TestMethod]
    public async Task PendingSave_BlocksDuplicateRequestsAndInvalidNamesNeverSave()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var picker = new HistoryTagPickerViewModel(["work"], [], (_, _) =>
        {
            calls++;
            return gate.Task;
        })
        {
            Search = new string('a', 65)
        };
        Assert.IsFalse(picker.CanAddInput);
        Assert.IsFalse(await picker.AddInputAsync());
        Assert.AreEqual(0, calls);
        picker.Search = "work";
        var saving = picker.AddInputAsync();
        Assert.IsFalse(picker.CanInteract);
        Assert.IsFalse(await picker.AddAsync("work"));
        Assert.AreEqual(1, calls);
        picker.Search = "next";
        Assert.IsFalse(picker.CanAddInput);
        gate.SetResult();
        Assert.IsTrue(await saving);
        Assert.AreEqual("next", picker.Search);
        Assert.IsTrue(picker.CanAddInput);
        Assert.IsTrue(picker.CanInteract);
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
        Assert.IsTrue(picker.CanAddInput);
        Assert.IsTrue(await picker.ToggleAsync("work"));
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
