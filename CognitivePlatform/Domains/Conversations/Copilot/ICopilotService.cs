using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CognitivePlatform.Api.Domains.Conversations.Copilot;

public interface ICopilotService
{
    Task<CopilotSliceResult> ProcessSliceAsync( Guid conversationId
                                              , Stream audioStream
                                              , CopilotSliceRequest request
                                              , CancellationToken cancellationToken = default );

    Task<LiveStreamChunkResult> ProcessLiveStreamChunkAsync( Guid conversationId
                                                           , Stream audioChunkStream
                                                           , LiveStreamChunkRequest request
                                                           , CancellationToken cancellationToken = default );

    Task<List<CopilotInsight>> GetInsightsAsync( Guid conversationId
                                               , CancellationToken cancellationToken = default );

    Task<bool> DismissInsightAsync( Guid conversationId
                                  , Guid insightId
                                  , CancellationToken cancellationToken = default );
}
