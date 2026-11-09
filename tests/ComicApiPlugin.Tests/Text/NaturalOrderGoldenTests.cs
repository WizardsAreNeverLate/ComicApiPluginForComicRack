using System.Linq;
using System.Threading.Tasks;
using ComicApiPlugin.Text;
using TUnit.Assertions.Enums;

namespace ComicApiPlugin.Tests.Text;

/// <summary>The cases in the Python project's tests/test_utils.py.</summary>
public sealed class NaturalOrderGoldenTests
{
	[Test]
	[Arguments(new[] { "10", "1", "23", "5", "1.2", "11", "1.01", "55" }, new[] { "1", "1.01", "1.2", "5", "10", "11", "23", "55" })]
	[Arguments(new[] { "10a", "1b", "2", "1c", "1a", "1" }, new[] { "1", "1a", "1b", "1c", "2", "10a" })]
	[Arguments(new[] { "a1", "a10", "a1.1", "aa2", "aa2.3" }, new[] { "a1", "a1.1", "a10", "aa2", "aa2.3" })]
	[Arguments(new[] { "-5", "-6", "-0.1", "-0.2", "-.11", ".3", "0.31" }, new[] { "-6", "-5", "-0.2", "-.11", "-0.1", ".3", "0.31" })]
	[Arguments(new[] { "⅞", "⅝", "⅜", "⅛", "⅚", "⅙", "⅘", "⅗", "⅖", "⅕", "⅔", "⅓", "¾", "½", "¼" }, new[] { "⅛", "⅙", "⅕", "¼", "⅓", "⅜", "⅖", "½", "⅗", "⅝", "⅔", "¾", "⅘", "⅚", "⅞" })]
	[Arguments(new[] { ".4", "0.6", "½", "6", "5", "5½", "5 ¾", " 5 ¼ " }, new[] { ".4", "½", "0.6", "5", " 5 ¼ ", "5½", "5 ¾", "6" })]
	[Arguments(new[] { "-.4", "-0.6", "-½", "-6", "-5", "-5½", "-5 ¾", "- 5 ¼ " }, new[] { "-6", "-5 ¾", "-5½", "- 5 ¼ ", "-5", "-0.6", "-½", "-.4" })]
	public async Task Sorts_like_natural_compare(string[] unsorted, string[] expected)
	{
		await Assert.That(unsorted.OrderBy(s => s, NaturalOrder.Instance).ToArray()).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("0.", "0")]
	[Arguments("0.a", "0a")]
	[Arguments("3.0", "3.00")]
	[Arguments("3.0", "3")]
	[Arguments("003", "3")]
	[Arguments("003a", "3a  ")]
	[Arguments("003", "3  ")]
	[Arguments("½", "0.5000")]
	[Arguments("3½", "0003.5")]
	[Arguments("6 au", "6au")]
	[Arguments("0.0 final", "0 final")]
	[Arguments(".5", " 0 ½")]
	[Arguments("000.5", "0½")]
	public async Task Gives_equal_keys_like_natural_key(string a, string b)
	{
		await Assert.That(NaturalOrder.KeysEqual(a, b)).IsTrue();
	}
}
