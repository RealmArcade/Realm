using NUnit.Framework;
using Realm.Shared;

namespace Realm.Distribution.Tests;

[TestFixture]
public class NameNormalizationTests
{
	[Test]
	public void NormalizeUsername()
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
}