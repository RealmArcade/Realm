using System.Collections.Generic;
using NUnit.Framework;
using Realm.Shared;

namespace Realm.Distribution.Tests;

[TestFixture]
public class NameNormalizationTests
{
    [Test]
    public void ToAscii_ConvertsDiacriticsAndHomoglyphs()
    {
        Assert.That(NameNormalizationHelper.ToAscii("Müller"), Is.EqualTo("Muller"));
        Assert.That(NameNormalizationHelper.ToAscii("Éléonore"), Is.EqualTo("Eleonore"));
        Assert.That(NameNormalizationHelper.ToAscii("Groß"), Is.EqualTo("Gross"));
        Assert.That(NameNormalizationHelper.ToAscii("Пётр"), Is.EqualTo("Petr"));
        Assert.That(NameNormalizationHelper.ToAscii("Аdmin"), Is.EqualTo("Admin"));
        Assert.That(NameNormalizationHelper.ToAscii("Łódź"), Is.EqualTo("Lodz"));
        Assert.That(NameNormalizationHelper.ToAscii("Æthelred"), Is.EqualTo("AEthelred"));
    }

    [Test]
    public void NormalizeUsername_IgnoresPunctuationSymbolsAndCase()
    {
        var baseUsername = "username";

        Assert.That(NameNormalizationHelper.NormalizeUsername("UserName"), Is.EqualTo(baseUsername));
        Assert.That(NameNormalizationHelper.NormalizeUsername("user_name"), Is.EqualTo(baseUsername));
        Assert.That(NameNormalizationHelper.NormalizeUsername("user-name"), Is.EqualTo(baseUsername));
        Assert.That(NameNormalizationHelper.NormalizeUsername("u.s.e.r.n.a.m.e"), Is.EqualTo(baseUsername));
        Assert.That(NameNormalizationHelper.NormalizeUsername("!User#Name$"), Is.EqualTo(baseUsername));
        Assert.That(NameNormalizationHelper.NormalizeUsername("[User]Name`"), Is.EqualTo(baseUsername));
        Assert.That(NameNormalizationHelper.NormalizeUsername("ÜserName"), Is.EqualTo(baseUsername));
        Assert.That(NameNormalizationHelper.NormalizeUsername("  user   name  "), Is.EqualTo(baseUsername));
    }

    [Test]
    public void AreUsernamesConflicting_DetectsConflictingVariations()
    {
        Assert.That(NameNormalizationHelper.AreUsernamesConflicting("CoolGamer", "cool_gamer"), Is.True);
        Assert.That(NameNormalizationHelper.AreUsernamesConflicting("CoolGamer", "cool-gamer!"), Is.True);
        Assert.That(NameNormalizationHelper.AreUsernamesConflicting("CöölGämer", "coolgamer"), Is.True);
        Assert.That(NameNormalizationHelper.AreUsernamesConflicting("DifferentUser", "CoolGamer"), Is.False);
    }

    [Test]
    public void NormalizeMapName_NormalizesAlphanumericChars()
    {
        Assert.That(NameNormalizationHelper.NormalizeMapName("Winter Survival"), Is.EqualTo("wintersurvival"));
        Assert.That(NameNormalizationHelper.NormalizeMapName("Winter-Survival!"), Is.EqualTo("wintersurvival"));
        Assert.That(NameNormalizationHelper.NormalizeMapName("[Winter] Survival v1.0"), Is.EqualTo("wintersurvivalv10"));
    }

    [Test]
    public void ComputeLevenshteinDistance_CalculatesAccurateDistance()
    {
        Assert.That(NameNormalizationHelper.ComputeLevenshteinDistance("kitten", "sitting"), Is.EqualTo(3));
        Assert.That(NameNormalizationHelper.ComputeLevenshteinDistance("wintersurvival", "wintersurviva1"), Is.EqualTo(1));
        Assert.That(NameNormalizationHelper.ComputeLevenshteinDistance("map", "map"), Is.EqualTo(0));
        Assert.That(NameNormalizationHelper.ComputeLevenshteinDistance("", "test"), Is.EqualTo(4));
    }

    [Test]
    public void IsMapNameTooSimilar_IdentifiesTypoSquatsAndCollisions()
    {
        var existingMaps = new List<string> { "Winter Survival", "Desert Fortress", "Lost Temple" };

        bool isSimilar1 = NameNormalizationHelper.IsMapNameTooSimilar("Winter Surviva1", existingMaps, 2, out var conflict1);
        Assert.That(isSimilar1, Is.True);
        Assert.That(conflict1, Is.EqualTo("Winter Survival"));

        bool isSimilar2 = NameNormalizationHelper.IsMapNameTooSimilar("Winter-Survival", existingMaps, 2, out var conflict2);
        Assert.That(isSimilar2, Is.True);
        Assert.That(conflict2, Is.EqualTo("Winter Survival"));

        bool isSimilar3 = NameNormalizationHelper.IsMapNameTooSimilar("Mystic Forest", existingMaps, 2, out var conflict3);
        Assert.That(isSimilar3, Is.False);
        Assert.That(conflict3, Is.Null);
    }

    [Test]
    public void ValidateUsername_EnforcesValidationRules()
    {
        Assert.That(NameNormalizationHelper.ValidateUsername("ValidUser123", out var err1), Is.True);
        Assert.That(err1, Is.Null);

        Assert.That(NameNormalizationHelper.ValidateUsername("ab", out var err2), Is.False);
        Assert.That(err2, Is.Not.Null);

        Assert.That(NameNormalizationHelper.ValidateUsername("!!!", out var err3), Is.False);
        Assert.That(err3, Is.Not.Null);

        Assert.That(NameNormalizationHelper.ValidateUsername("", out var err4), Is.False);
        Assert.That(err4, Is.Not.Null);
    }

    [Test]
    public void ValidateMapName_EnforcesValidationRules()
    {
        Assert.That(NameNormalizationHelper.ValidateMapName("Awesome Map", out var err1), Is.True);
        Assert.That(err1, Is.Null);

        Assert.That(NameNormalizationHelper.ValidateMapName("a", out var err2), Is.False);
        Assert.That(err2, Is.Not.Null);

        Assert.That(NameNormalizationHelper.ValidateMapName("---", out var err3), Is.False);
        Assert.That(err3, Is.Not.Null);
    }
}
