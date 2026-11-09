using System.Threading.Tasks;
using ComicApiPlugin.Text;

namespace ComicApiPlugin.Tests.Text;

public sealed class NaturalOrderTests
{
	[Test]
	public async Task Text_after_a_fraction_decides_the_order()
	{
		await Assert.That(NaturalOrder.KeysEqual("5½a", "5½b")).IsFalse();
		await Assert.That(NaturalOrder.Instance.Compare("5½a", "5½b")).IsEqualTo(-1);
		await Assert.That(NaturalOrder.Instance.Compare("5½b", "5½a")).IsEqualTo(1);
		await Assert.That(NaturalOrder.Instance.Compare("5½", "5½a")).IsEqualTo(-1);
		await Assert.That(NaturalOrder.Instance.Compare("5½a", "6")).IsEqualTo(-1);
		await Assert.That(NaturalOrder.KeysEqual("5½ A", "5½a")).IsTrue();
	}

	[Test]
	public async Task A_bare_fraction_still_equals_the_same_decimal()
	{
		await Assert.That(NaturalOrder.KeysEqual("5½", "5.5")).IsTrue();
		await Assert.That(NaturalOrder.KeysEqual("0.5000", "½")).IsTrue();
	}
}
