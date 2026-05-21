using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Api.Domains.Identity;

public interface IIdentityAnalysisService
{
    Task<IReadOnlyList<DerivedInsight>> GenerateInsightsAsync( string              partitionKey
                                                             , CancellationToken   ct
                                                             , ConversationContext? sessionContext = null );

    Task<PersonalitySnapshot>           GenerateSnapshotAsync( string              partitionKey
                                                             , CancellationToken   ct
                                                             , ConversationContext? sessionContext = null );
}
