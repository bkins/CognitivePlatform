using System.Threading;
using System.Threading.Tasks;

namespace CognitivePlatform.Api.Domains.Insights;

public interface IPatternDataAggregator
{
    Task<string?> AggregateAndFormatAsync(
        string?           focus             = null
      , string?           fromDate          = null
      , string?           toDate            = null
      , CancellationToken cancellationToken = default );
}
