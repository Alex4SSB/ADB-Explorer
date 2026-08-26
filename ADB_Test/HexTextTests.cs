using ADB_Explorer.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

[TestClass]
public class HexTextTests
{
    [TestMethod]
    public void Format_SpacesUppercaseBytes()
    {
        Assert.AreEqual("", HexText.Format([]));
        Assert.AreEqual("DE", HexText.Format([0xDE]));
        Assert.AreEqual("DE AD BE EF", HexText.Format([0xDE, 0xAD, 0xBE, 0xEF]));
    }

    [TestMethod]
    public void Parse_AcceptsWhitespaceAndMixedCase()
    {
        CollectionAssert.AreEqual(new byte[] { }, HexText.Parse(""));
        CollectionAssert.AreEqual(new byte[] { }, HexText.Parse(null));

        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00 }, HexText.Parse("DE AD be ef\n00"));
    }

    [TestMethod]
    public void FilterInput_KeepsHexAndWhitespace()
    {
        Assert.AreEqual("", HexText.FilterInput(null));
        Assert.AreEqual("", HexText.FilterInput(""));
        Assert.AreEqual("DEADBEEF", HexText.FilterInput("de-ad-be-ef"));
        Assert.AreEqual("DE AD BE EF", HexText.FilterInput("DE AD BE EF"));
        Assert.AreEqual("00\nFF", HexText.FilterInput("00\nFF"));
        Assert.AreEqual("", HexText.FilterInput("!@#"));
    }

    [TestMethod]
    public void Normalize_GroupsDigitsWithSpaces()
    {
        Assert.AreEqual("", HexText.Normalize(""));
        Assert.AreEqual("D", HexText.Normalize("d"));
        Assert.AreEqual("DE", HexText.Normalize("de"));
        Assert.AreEqual("DE A", HexText.Normalize("dea"));
        Assert.AreEqual("DE AD BE EF", HexText.Normalize("de-ad-be-ef"));
    }

    [TestMethod]
    public void Caret_SkipsSpaces()
    {
        const string text = "DE AD";
        Assert.AreEqual(0, HexText.NibbleIndex(text, 0));
        Assert.AreEqual(1, HexText.NibbleIndex(text, 1));
        Assert.AreEqual(2, HexText.NibbleIndex(text, 2));
        Assert.AreEqual(2, HexText.NibbleIndex(text, 3));
        Assert.AreEqual(4, HexText.NibbleIndex(text, 5));

        Assert.AreEqual(0, HexText.CaretOffsetFromNibble(text, 0));
        Assert.AreEqual(1, HexText.CaretOffsetFromNibble(text, 1));
        Assert.AreEqual(3, HexText.CaretOffsetFromNibble(text, 2));
        Assert.AreEqual(4, HexText.CaretOffsetFromNibble(text, 3));
        Assert.AreEqual(5, HexText.CaretOffsetFromNibble(text, 4));
    }

    [TestMethod]
    public void Selection_IncludesInternalSpaceOnly()
    {
        const string text = "DE AD";

        var de = HexText.SelectionForNibbles(text, 0, 2);
        Assert.AreEqual(0, de.Anchor);
        Assert.AreEqual(2, de.Caret);
        Assert.AreEqual("DE", text[de.Anchor..de.Caret]);

        var ea = HexText.SelectionForNibbles(text, 1, 3);
        Assert.AreEqual(1, ea.Anchor);
        Assert.AreEqual(4, ea.Caret);
        Assert.AreEqual("E A", text[ea.Anchor..ea.Caret]);

        var ad = HexText.SelectionForNibbles(text, 2, 4);
        Assert.AreEqual(3, ad.Anchor);
        Assert.AreEqual(5, ad.Caret);
        Assert.AreEqual("AD", text[ad.Anchor..ad.Caret]);

        var adBack = HexText.SelectionForNibbles(text, 4, 2);
        Assert.AreEqual(5, adBack.Anchor);
        Assert.AreEqual(3, adBack.Caret);
        Assert.AreEqual("AD", text[adBack.Caret..adBack.Anchor]);
    }

    [TestMethod]
    public void Insert_AddsSpacesAutomatically()
    {
        var typed = HexText.Insert("", 0, 0, "D");
        Assert.AreEqual("D", typed.Text);
        Assert.AreEqual(1, typed.Caret);

        typed = HexText.Insert(typed.Text, typed.Caret, 0, "E");
        Assert.AreEqual("DE", typed.Text);
        Assert.AreEqual(2, typed.Caret);

        typed = HexText.Insert(typed.Text, typed.Caret, 0, "A");
        Assert.AreEqual("DE A", typed.Text);
        Assert.AreEqual(4, typed.Caret);

        typed = HexText.Insert(typed.Text, typed.Caret, 0, "D");
        Assert.AreEqual("DE AD", typed.Text);
        Assert.AreEqual(5, typed.Caret);
    }

    [TestMethod]
    public void Insert_OverwriteReplacesDigits()
    {
        var state = HexText.Insert("DE AD", 0, 0, "F", overwrite: true);
        Assert.AreEqual("FE AD", state.Text);
        Assert.AreEqual(1, state.Caret);

        state = HexText.Insert("DE AD", 1, 0, "F", overwrite: true);
        Assert.AreEqual("DF AD", state.Text);
        Assert.AreEqual(3, state.Caret);

        state = HexText.Insert("DE", 2, 0, "A", overwrite: true);
        Assert.AreEqual("DE A", state.Text);
        Assert.AreEqual(4, state.Caret);
    }

    [TestMethod]
    public void Backspace_RemovesSpacesWithDigits()
    {
        var state = HexText.Backspace("DE AD", 5, 0);
        Assert.AreEqual("DE A", state.Text);
        Assert.AreEqual(4, state.Caret);

        state = HexText.Backspace(state.Text, state.Caret, 0);
        Assert.AreEqual("DE", state.Text);
        Assert.AreEqual(2, state.Caret);
    }

    [TestMethod]
    public void Parse_PadsOddNibbleAsMsb()
    {
        CollectionAssert.AreEqual(new byte[] { 0x70 }, HexText.Parse("7"));
        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xE0 }, HexText.Parse("DE AD BE E"));
        CollectionAssert.AreEqual(new byte[] { 0xDE, 0xA0 }, HexText.Parse("DE A"));
    }
}
