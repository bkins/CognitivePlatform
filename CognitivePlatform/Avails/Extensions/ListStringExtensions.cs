namespace CognitivePlatform.Api.Avails.Extensions;

public static class ListStringExtensions
{
    extension(List<string> value)
    {
        public bool ContainsCaseInsensitive (string       searchTerm)
        {
            return value.Contains(searchTerm
                                , StringComparer.OrdinalIgnoreCase);
        }

        public bool DoesNotContainCaseInsensitive (string searchTerm)
        {
            return value.ContainsCaseInsensitive(searchTerm).Not();
        }
        
        public bool HasEntries ()
        {
            return value.Count > 0;
        }
    }
}