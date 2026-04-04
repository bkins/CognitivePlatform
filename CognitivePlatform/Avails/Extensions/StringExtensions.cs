namespace CognitivePlatform.Api.Avails.Extensions;

[Obsolete("Use CP.Shared.Primitives.Avails.Extensions instead")]
public static class StringExtensions
{
    extension(string? value)
    {
        public bool HasValue ()
        {
            return string.IsNullOrEmpty(value).Not() 
                || string.IsNullOrWhiteSpace(value).Not();
        }

        public bool? DoesNotContain (string searchTerm)
        {
            return value?.Contains(searchTerm).Not();
        }
        
        public bool DoesNotContainOrNull (string? searchTerm)
        {
            return searchTerm != null 
                && value != null 
                && value.Contains(searchTerm).Not();
        }

        public bool EqualsIgnoreCase (string? searchTerm)
        {
            return value?.Equals(searchTerm
                              , StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }

    extension (string? value)
    {
        public bool DoesNotHaveValueOrIsNullOrEmpty ()
        {
            return value?.HasValue().Not() ?? true;
        }

        public bool DoesNotStartWithOrdinal(string prefix)
        {
            return value.StartsWithOrdinal(prefix)
                        .Not();
        }

        public bool StartsWithOrdinal (string prefix)
        {
            return value?.StartsWith(prefix) ?? false;
        }

        public bool EndsWithOrdinalIgnoreCase (string prefix)
        {
            return value?.EndsWith(prefix) ?? false;
        }

        public bool DoesNotEndWith (string prefix)
        {
            return value?.EndsWith(prefix).Not() ?? false;
        }
    }

}