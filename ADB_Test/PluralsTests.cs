using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ADB_Explorer.Services;
using ADB_Explorer.Strings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADB_Test;

[TestClass]
public class PluralsTests
{
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}");

    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    [TestMethod]
    public void PluralRules_SelectsCldrCategories()
    {
        Assert.AreEqual("one", NativeMethods.PluralRules.Select(Culture("ru"), 21));
        Assert.AreEqual("few", NativeMethods.PluralRules.Select(Culture("ru"), 22));
        Assert.AreEqual("many", NativeMethods.PluralRules.Select(Culture("ru"), 11));
        Assert.AreEqual("few", NativeMethods.PluralRules.Select(Culture("pl"), 3));
        Assert.AreEqual("many", NativeMethods.PluralRules.Select(Culture("pl"), 21));
        Assert.AreEqual("zero", NativeMethods.PluralRules.Select(Culture("ar"), 0));
        Assert.AreEqual("two", NativeMethods.PluralRules.Select(Culture("ar"), 2));
        Assert.AreEqual("one", NativeMethods.PluralRules.Select(Culture("fr"), 0));
        Assert.AreEqual("one", NativeMethods.PluralRules.Select(Culture("pt-BR"), 0));
        Assert.AreEqual("other", NativeMethods.PluralRules.Select(Culture("en"), 0));
        Assert.AreEqual("other", NativeMethods.PluralRules.Select(Culture("ja"), 1));
    }

    [TestMethod]
    public void PluralRules_ListsLanguageCategories()
    {
        CollectionAssert.AreEquivalent(new[] { "one", "few", "many", "other" }, NativeMethods.PluralRules.Categories(Culture("ru")).ToArray());
        CollectionAssert.AreEquivalent(new[] { "zero", "one", "two", "few", "many", "other" }, NativeMethods.PluralRules.Categories(Culture("ar")).ToArray());
        CollectionAssert.AreEquivalent(new[] { "other" }, NativeMethods.PluralRules.Categories(Culture("ja")).ToArray());
    }

    [TestMethod]
    public void Lookup_FallsBackToGeneralFormThenSourceLanguage()
    {
        var ru = Plurals.Table("ru")!;

        Assert.AreEqual(ru["Items_one"], Plurals.Lookup(Culture("ru"), "Items", 21));

        // Weblate gives ru no "other" form, so an untranslated "few" falls back to "many"
        Assert.IsFalse(ru.ContainsKey("Items_other"));
        var few = ru.GetValueOrDefault("Items_few") is { Length: > 0 } text ? text : ru["Items_many"];
        Assert.AreEqual(few, Plurals.Lookup(Culture("ru"), "Items", 2));

        // a form that spells out its number never stands in for another count
        var he = Plurals.Table("he-IL")!;
        Assert.AreEqual(he["Items_other"], Plurals.Lookup(Culture("he-IL"), "Items", 5));

        Assert.AreEqual(Plurals.Table("de")!["Items_one"], Plurals.Lookup(Culture("de-AT"), "Items", 1));

        // ro has no Items forms, so it gets the source language's
        var source = Plurals.Table(Plurals.SourceLanguage)!;
        Assert.AreEqual(source["Items_one"], Plurals.Lookup(Culture("ro"), "Items", 1));
        Assert.AreEqual(source["Items_other"], Plurals.Lookup(Culture("en-US"), "Items", null));
    }

    [TestMethod]
    public void Format_FillsPlaceholders()
    {
        var culture = Resources.Culture;
        try
        {
            Resources.Culture = Culture("en");

            // The source's one forms spell out the number, so 2 is the count that shows the placeholder filled
            Assert.AreEqual("2 items", Plurals.Items(2));
            Assert.AreEqual("?? items", Plurals.Items(null));
            Assert.AreEqual("Copy 3 items to Download", Plurals.CopyItemsTo(3, "Download"));
        }
        finally
        {
            Resources.Culture = culture;
        }
    }

    [TestMethod]
    public void Accessors_MatchSourceKeys()
    {
        var accessors = typeof(Plurals).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string))
            .Select(m => m.Name);

        CollectionAssert.AreEquivalent(accessors.ToArray(), Plurals.Keys().ToArray());

        var source = Plurals.Table(Plurals.SourceLanguage)!;
        foreach (var key in Plurals.Keys())
        {
            Assert.IsTrue(source.ContainsKey($"{key}_one"), key);
            Assert.IsTrue(source.ContainsKey($"{key}_other"), key);
        }
    }

    /// <summary>Guards the files Weblate writes back: known keys, the language's own categories, matching placeholders.</summary>
    [TestMethod]
    public void Translations_AreValid()
    {
        var source = Plurals.Table(Plurals.SourceLanguage)!;
        var keys = Plurals.Keys();

        foreach (var language in Plurals.Languages())
        {
            var table = Plurals.Table(language);
            Assert.IsNotNull(table, language);

            var categories = NativeMethods.PluralRules.Categories(Culture(language));
            var singleNumber = categories.Where(category => IsSingleNumber(Culture(language), category)).ToHashSet();

            foreach (var (key, text) in table)
            {
                // Weblate writes untranslated forms as empty strings
                if (text == "")
                    continue;

                var baseKey = Plurals.BaseKey(key);
                var category = key[(baseKey.Length + 1)..];

                Assert.IsTrue(keys.Contains(baseKey), $"{language}: unknown key {key}");
                Assert.IsTrue(categories.Contains(category), $"{language}: {key} is not a {language} plural category");

                var known = Placeholders(source[$"{baseKey}_other"]);
                var actual = Placeholders(text);
                Assert.IsTrue(actual.IsSubsetOf(known), $"{language}: {key} has an unknown placeholder");

                // a form for one exact number may spell the number out (he: "פריט אחד")
                if (singleNumber.Contains(category))
                    known.Remove("count");

                Assert.IsTrue(known.IsSubsetOf(actual), $"{language}: {key} is missing a placeholder");
            }
        }
    }

    [TestMethod]
    public void WholeNumberCategories_MatchWeblateForms()
    {
        CollectionAssert.AreEquivalent(new[] { "one", "few", "many" }, NativeMethods.PluralRules.WholeNumberCategories(Culture("ru")).ToArray());
        CollectionAssert.AreEquivalent(new[] { "one", "few", "many" }, NativeMethods.PluralRules.WholeNumberCategories(Culture("pl")).ToArray());
        CollectionAssert.AreEquivalent(new[] { "one", "two", "other" }, NativeMethods.PluralRules.WholeNumberCategories(Culture("he-IL")).ToArray());
        CollectionAssert.AreEquivalent(new[] { "one", "few", "other" }, NativeMethods.PluralRules.WholeNumberCategories(Culture("cs")).ToArray());
    }

    [TestMethod]
    public void IsSingleNumber_OnlyForCategoriesOfOneWholeNumber()
    {
        Assert.IsTrue(IsSingleNumber(Culture("he"), "one"));
        Assert.IsTrue(IsSingleNumber(Culture("he"), "two"));
        Assert.IsTrue(IsSingleNumber(Culture("ar"), "zero"));
        Assert.IsTrue(IsSingleNumber(Culture("de"), "one"));

        // one also covers 21, 31... (ru) or 0 (fr, pt-BR)
        Assert.IsFalse(IsSingleNumber(Culture("ru"), "one"));
        Assert.IsFalse(IsSingleNumber(Culture("fr"), "one"));
        Assert.IsFalse(IsSingleNumber(Culture("pt-BR"), "one"));
        Assert.IsFalse(IsSingleNumber(Culture("ar"), "few"));
    }

    /// <summary>
    /// Whether <paramref name="category"/> covers exactly one whole number - CLDR integer rules repeat
    /// within 0-999. Weblate also lets such forms drop the count, but accepts 0-and-1 too, which this doesn't.
    /// </summary>
    private static bool IsSingleNumber(CultureInfo culture, string category)
        => Enumerable.Range(0, 1000).Count(n => NativeMethods.PluralRules.Select(culture, n) == category) == 1;

    private static HashSet<string> Placeholders(string text)
        => Placeholder.Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
}
