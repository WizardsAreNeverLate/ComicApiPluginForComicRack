using System;
using System.Threading;
using System.Threading.Tasks;

namespace ComicVineScraper.Engine;

/// <summary>Time, injected so MatchScore's recency term, note dates, and the scrape delay are testable.</summary>
public interface IClock
{
	DateTime Now { get; }

	Task Delay(TimeSpan delay, CancellationToken ct);
}
