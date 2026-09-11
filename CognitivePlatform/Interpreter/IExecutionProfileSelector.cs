namespace CognitivePlatform.Api.Interpreter;

public interface IExecutionProfileSelector
{
    ExecutionProfile SelectProfile(string userMessage);
}
