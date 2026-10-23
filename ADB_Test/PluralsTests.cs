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
    public void Lookup_FallsBackToOtherThenSourceLanguage()
    {
        var ru = Plurals.Table("ru")!;

        Assert.AreEqual(ru["Items_one"], Plurals.Lookup(Culture("ru"), "Items", 21));

        // ru.json has no "few" form yet
        Assert.IsFalse(ru.ContainsKey("Items_few"));
        Assert.AreEqual(ru["Items_other"], Plurals.Lookup(Culture("ru"), "Items", 2));

        Assert.AreEqual(Plurals.Table("de")!["Items_one"], Plurals.Lookup(Culture("de-AT"), "Items", 1));
        Assert.AreEqual("{{count}} item", Plurals.Lookup(Culture("ro"), "Items", 1));
        Assert.AreEqual("{{count}} items", Plurals.Lookup(Culture("en-US"), "Items", null));
    }

    [TestMethod]
    public void Format_FillsPlaceholders()
    {
        var culture = Resources.Culture;
        try
        {
            Resources.Culture = Culture("en");

            Assert.AreEqual("1 item", Plurals.Items(1));
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
            foreach (var (key, text) in table)
            {
                var baseKey = Plurals.BaseKey(key);
                var category = key[(baseKey.Length + 1)..];

                Assert.IsTrue(keys.Contains(baseKey), $"{language}: unknown key {key}");
                Assert.IsTrue(categories.Contains(category), $"{language}: {key} is not a {language} plural category");

                var known = Placeholders(source[$"{baseKey}_other"]);
                var actual = Placeholders(text);
                Assert.IsTrue(actual.IsSubsetOf(known), $"{language}: {key} has an unknown placeholder");

                // a form for one exact number may spell the number out
                if (category is "zero" or "one" or "two")
                    known.Remove("count");

                Assert.IsTrue(known.IsSubsetOf(actual), $"{language}: {key} is missing a placeholder");
            }
        }
    }

    private static HashSet<string> Placeholders(string text)
        => Placeholder.Matches(text).Select(m => m.Groups[1].Value).ToHashSet();
}
