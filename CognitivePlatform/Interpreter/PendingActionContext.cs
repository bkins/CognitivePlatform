namespace CognitivePlatform.Interpreter
{
    public class PendingActionContext
    {
        public string ActionName { get; set; } = "";
        
        public Dictionary<string, string?> RequiredParameters  { get; set; } = new();
        public Dictionary<string, string?> CollectedParameters { get; set; } = new();

        public bool IsComplete => RequiredParameters.All(kvp => CollectedParameters.ContainsKey(kvp.Key) 
                                                              && ! string.IsNullOrWhiteSpace(CollectedParameters[kvp.Key]));
    }
}