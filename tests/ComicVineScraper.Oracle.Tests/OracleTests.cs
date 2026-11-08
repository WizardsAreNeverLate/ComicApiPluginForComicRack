using System.Threading.Tasks;
using TUnit.Assertions.Enums;

namespace ComicVineScraper.Oracle.Tests;

public sealed class OracleTests
{
	[Test]
	public async Task Calls_the_original_filename_parser()
	{
		var parsed = (object[]?)Oracle.Call("fnameparser", "extract", "X-Men v2 #21.0000000 (1993).cbr");

		await Assert.That(parsed).IsEquivalentTo(new object[] { "X-Men", "21.0", "1993" }, CollectionOrdering.Matching);
	}
}
