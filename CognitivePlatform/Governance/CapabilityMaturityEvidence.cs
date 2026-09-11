namespace CognitivePlatform.Api.Governance;

public sealed record CapabilityMaturityEvidence
{
    public bool RequirementsReviewed          { get; init; }
    public bool AcceptanceCriteriaDefined     { get; init; }
    public bool RisksIdentified                { get; init; }
    public bool FeasibilityReviewed            { get; init; }
    public bool BuildSucceeded                 { get; init; }
    public bool StaticAnalysisPassed           { get; init; }
    public bool BasicDocumentationCreated      { get; init; }
    public bool UnitTestsPassed                { get; init; }
    public bool IntegrationTestsPassed         { get; init; }
    public bool UiAutomationPassed             { get; init; }
    public bool AcceptanceTestsPassed          { get; init; }
    public bool RegressionTestsPassed          { get; init; }
    public bool ConfigurationValidationPassed  { get; init; }
    public bool PolicyChecksPassed             { get; init; }
    public bool ExtendedRegressionPassed       { get; init; }
    public bool ObservabilityPlanDefined       { get; init; }
    public bool RollbackValidated              { get; init; }
    public int  SuccessfulVerifiedRunCount     { get; init; }

    public static CapabilityMaturityEvidence ForProposal()
    {
        return new CapabilityMaturityEvidence
               {
                   RequirementsReviewed      = true
                 , AcceptanceCriteriaDefined = true
                 , RisksIdentified           = true
                 , FeasibilityReviewed       = true
               };
    }

    public static CapabilityMaturityEvidence ForImplementation()
    {
        return ForProposal() with
               {
                   BuildSucceeded            = true
                 , StaticAnalysisPassed      = true
                 , BasicDocumentationCreated = true
               };
    }

    public static CapabilityMaturityEvidence ForVerified()
    {
        return ForImplementation() with
               {
                   UnitTestsPassed               = true
                 , IntegrationTestsPassed        = true
                 , UiAutomationPassed            = true
                 , AcceptanceTestsPassed         = true
                 , RegressionTestsPassed         = true
                 , ConfigurationValidationPassed = true
                 , PolicyChecksPassed            = true
               };
    }

    public static CapabilityMaturityEvidence ForTrusted(int successfulVerifiedRunCount)
    {
        return ForVerified() with
               {
                   ExtendedRegressionPassed   = true
                 , ObservabilityPlanDefined   = true
                 , RollbackValidated          = true
                 , SuccessfulVerifiedRunCount = successfulVerifiedRunCount
               };
    }
}
