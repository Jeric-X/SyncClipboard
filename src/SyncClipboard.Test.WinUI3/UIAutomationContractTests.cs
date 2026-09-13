extern alias WinUIProduct;

using Interop.UIAutomationClient;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Runtime.InteropServices;
using WinUIProduct::SyncClipboard.WinUI3.Win32;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
[TestCategory("NonUI")]
public class UIAutomationContractTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void SelectedText_UnavailablePatternReturnsNull(int scenario)
    {
        var element = CreateElement();
        var setup = element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId));
        if (scenario == 2)
            setup.Throws(new COMException("Pattern unavailable"));
        else
            setup.Returns(scenario == 0 ? null! : new object());

        Assert.IsNull(CurrentSelectedContentProvider.TryGetSelectedText(element.Object));
        element.Verify(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId), Times.Once());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void SelectedText_EmptySelectionReturnsNull(int scenario)
    {
        var element = CreateElement();
        var pattern = new Mock<IUIAutomationTextPattern>(MockBehavior.Strict);
        var emptyRange = CreateRange();
        emptyRange.Setup(x => x.GetText(-1)).Returns(string.Empty);
        var missingRange = CreateRange();
        missingRange.Setup(x => x.GetText(-1)).Returns((string)null!);
        var selection = scenario == 2
            ? CreateSelection(emptyRange.Object, missingRange.Object)
            : CreateSelection();
        pattern.Setup(x => x.GetSelection()).Returns(scenario == 0 ? null! : selection.Object);
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId)).Returns(pattern.Object);

        Assert.IsNull(CurrentSelectedContentProvider.TryGetSelectedText(element.Object));
    }

    [TestMethod]
    public void SelectedText_JoinsNonemptyRangesWithoutTrimmingUnicodeOrWhitespace()
    {
        string?[] parts = ["第一段 ✓", "", null, "second\nline", " "];
        var ranges = parts.Select(part =>
        {
            var range = CreateRange();
            range.Setup(x => x.GetText(-1)).Returns(part!);
            return range;
        }).ToArray();
        var selection = CreateSelection(ranges.Select(x => x.Object).ToArray());
        var pattern = new Mock<IUIAutomationTextPattern>(MockBehavior.Strict);
        pattern.Setup(x => x.GetSelection()).Returns(selection.Object);
        var element = CreateElement();
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId)).Returns(pattern.Object);

        var actual = CurrentSelectedContentProvider.TryGetSelectedText(element.Object);

        Assert.AreEqual($"第一段 ✓{Environment.NewLine}second\nline{Environment.NewLine} ", actual);
        foreach (var range in ranges)
            range.Verify(x => x.GetText(-1), Times.Once());
        Assert.IsFalse(Marshal.IsComObject(element.Object));
    }

    [TestMethod]
    public void Caret_TextPattern2UsesFirstRectangleAndPreservesSignedCoordinates()
    {
        var range = CreateRange(-15.75, 20.8, 1.9, 14.2, 100, 200, 4, 8);
        var pattern = new Mock<IUIAutomationTextPattern2>(MockBehavior.Strict);
        var active = 1;
        pattern.Setup(x => x.GetCaretRange(out active)).Returns(range.Object);
        var element = CreateElement();
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPattern2Id)).Returns(pattern.Object);

        var actual = CreateCaretProvider().TryGetCaretFromElement(element.Object);

        AssertPosition(actual, -15, 20, 1, 14);
        element.Verify(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId), Times.Never());
        Assert.IsFalse(Marshal.IsComObject(range.Object));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void Caret_UnavailableCaretRangeFallsBackToTextSelection(int scenario)
    {
        var element = CreateElement();
        var pattern2 = new Mock<IUIAutomationTextPattern2>(MockBehavior.Strict);
        var active = 1;
        var setup = pattern2.Setup(x => x.GetCaretRange(out active));
        if (scenario == 4)
            setup.Throws(new COMException("Caret range unavailable"));
        else
            setup.Returns(scenario == 2 ? null! : CreateRange().Object);
        object? value = scenario switch
        {
            0 => null,
            1 => new object(),
            _ => pattern2.Object
        };
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPattern2Id)).Returns(value!);
        var selection = CreateSelection(CreateRange(10, 20, 3, 8).Object);
        var pattern1 = new Mock<IUIAutomationTextPattern>(MockBehavior.Strict);
        pattern1.Setup(x => x.GetSelection()).Returns(selection.Object);
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId)).Returns(pattern1.Object);

        AssertPosition(CreateCaretProvider().TryGetCaretFromElement(element.Object), 10, 20, 3, 8);
        pattern1.Verify(x => x.GetSelection(), Times.Once());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Caret_UnavailableSelectionReturnsNull(int scenario)
    {
        var element = CreateElement();
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPattern2Id)).Returns((object)null!);
        var pattern = new Mock<IUIAutomationTextPattern>(MockBehavior.Strict);
        var setup = pattern.Setup(x => x.GetSelection());
        if (scenario == 2)
            setup.Throws(new COMException("Selection unavailable"));
        else
            setup.Returns(scenario == 0 ? null! : CreateSelection().Object);
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId)).Returns(pattern.Object);

        Assert.IsNull(CreateCaretProvider().TryGetCaretFromElement(element.Object));
    }

    [TestMethod]
    public void Caret_DegenerateSelectionExpandsCloneWithoutChangingOriginalRange()
    {
        var original = CreateRange();
        var expanded = CreateRange(30, 40, 2, 12);
        original.Setup(x => x.Clone()).Returns(expanded.Object);
        expanded.Setup(x => x.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            TextUnit.TextUnit_Character, 1)).Returns(1);
        var element = CreateSelectionElement(original.Object);

        AssertPosition(CreateCaretProvider().TryGetCaretFromElement(element.Object), 30, 40, 2, 12);
        original.Verify(x => x.Clone(), Times.Once());
        original.Verify(x => x.MoveEndpointByUnit(It.IsAny<TextPatternRangeEndpoint>(), It.IsAny<TextUnit>(),
            It.IsAny<int>()), Times.Never());
        expanded.Verify(x => x.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            TextUnit.TextUnit_Character, 1), Times.Once());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Caret_UnavailableExpansionReturnsNull(int scenario)
    {
        var original = CreateRange();
        var expanded = CreateRange(1, 2, 3);
        original.Setup(x => x.Clone()).Returns(expanded.Object);
        var setup = expanded.Setup(x => x.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
            TextUnit.TextUnit_Character, 1));
        if (scenario == 1)
            setup.Throws(new COMException("Expansion unavailable"));
        else
            setup.Returns(scenario == 0 ? 0 : 1);

        Assert.IsNull(CreateCaretProvider().TryGetCaretFromElement(CreateSelectionElement(original.Object).Object));
        if (scenario != 2)
            expanded.Verify(x => x.GetBoundingRectangles(), Times.Never());
    }

    [TestMethod]
    public void Caret_IncompleteNonemptyRectangleDoesNotProducePositionOrExpand()
    {
        var range = CreateRange(10, 20, 30);

        Assert.IsNull(CreateCaretProvider().TryGetCaretFromElement(CreateSelectionElement(range.Object).Object));
        range.Verify(x => x.Clone(), Times.Never());
    }

    private static CaretPositionProvider CreateCaretProvider() => new(Mock.Of<ILogger>());

    private static Mock<IUIAutomationElement> CreateElement()
    {
        var element = new Mock<IUIAutomationElement>(MockBehavior.Strict);
        element.SetupGet(x => x.CurrentName).Returns("mock element");
        element.SetupGet(x => x.CurrentClassName).Returns("mock class");
        return element;
    }

    private static Mock<IUIAutomationTextRange> CreateRange(params double[] rectangles)
    {
        var range = new Mock<IUIAutomationTextRange>(MockBehavior.Strict);
        range.Setup(x => x.GetBoundingRectangles()).Returns(rectangles);
        return range;
    }

    private static Mock<IUIAutomationTextRangeArray> CreateSelection(params IUIAutomationTextRange[] ranges)
    {
        var selection = new Mock<IUIAutomationTextRangeArray>(MockBehavior.Strict);
        selection.SetupGet(x => x.Length).Returns(ranges.Length);
        for (var index = 0; index < ranges.Length; index++)
            selection.Setup(x => x.GetElement(index)).Returns(ranges[index]);
        return selection;
    }

    private static Mock<IUIAutomationElement> CreateSelectionElement(IUIAutomationTextRange range)
    {
        var element = CreateElement();
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPattern2Id)).Returns((object)null!);
        var pattern = new Mock<IUIAutomationTextPattern>(MockBehavior.Strict);
        pattern.Setup(x => x.GetSelection()).Returns(CreateSelection(range).Object);
        element.Setup(x => x.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId)).Returns(pattern.Object);
        return element;
    }

    private static void AssertPosition(ScreenPosition? position, int x, int y, int width, int height)
    {
        Assert.IsNotNull(position);
        Assert.AreEqual(x, position.X);
        Assert.AreEqual(y, position.Y);
        Assert.AreEqual(width, position.Width);
        Assert.AreEqual(height, position.Height);
    }
}
