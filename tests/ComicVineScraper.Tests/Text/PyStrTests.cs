using System.Threading.Tasks;
using ComicVineScraper.Text;

namespace ComicVineScraper.Tests.Text;

public sealed class PyStrTests
{
	[Test]
	[Arguments(1.0, "1.0")]
	[Arguments(0.1, "0.1")]
	[Arguments(1e16, "1e+16")]
	[Arguments(123456789012.0, "123456789012.0")]
	[Arguments(1234567890123.0, "1.23456789012e+12")]
	[Arguments(0.30000000000000004, "0.3")]
	[Arguments(1e-5, "1e-05")]
	[Arguments(0.0001, "0.0001")]
	[Arguments(-0.0, "-0.0")]
	[Arguments(double.NaN, "nan")]
	[Arguments(double.PositiveInfinity, "inf")]
	[Arguments(double.NegativeInfinity, "-inf")]
	[Arguments(3.14159265358979, "3.14159265359")]
	[Arguments(1e100, "1e+100")]
	[Arguments(123456789.123456789, "123456789.123")]
	[Arguments(5e-324, "4.94065645841e-324")]
	[Arguments(999999999999.5, "1000000000000.0")]
	public async Task Formats_like_IronPython_str(double value, string expected)
	{
		await Assert.That(PyStr.Format(value)).IsEqualTo(expected);
	}
}
