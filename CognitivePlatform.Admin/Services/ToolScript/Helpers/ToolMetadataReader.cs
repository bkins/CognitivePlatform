using CognitivePlatform.Admin.Services.ToolScript.Interfaces;
using CognitivePlatform.Admin.Services.ToolScript.Models;
using CP.Client.Core.Avails;

namespace CognitivePlatform.Admin.Services.ToolScript.Helpers;

public sealed class ToolMetadataReader : IToolMetadataReader
{
    public ToolMetadataReadResult Read( string scriptPath )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        var metadata        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var insideToolBlock = false;

        foreach (var rawLine in File.ReadLines(scriptPath))
        {
            var line = rawLine.Trim();

            if (!insideToolBlock)
            {
                if (line.EqualsIgnoreCase("@tool"))
                {
                    insideToolBlock = true;
                }

                continue;
            }

            // End of the comment block
            if (line == "#>")
            {
                break;
            }

            if (line.HasNoValue())
            {
                continue;
            }

            var equalsIndex = line.IndexOf('=');

            if (equalsIndex < 0)
            {
                continue;
            }

            var key   = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();

            metadata[key] = value;
        }
        
        if (insideToolBlock.Not() 
         && metadata.Count == 0)
        {
            return new ToolMetadataReadResult
                   {
                           Success = false
                         , ErrorMessage = "Missing @tool metadata block."
                   };
        }

        if (metadata.TryGetValue("Name", out var name)
                    .Not() 
         || name is not null 
         && name.HasNoValue())
        {
            return new ToolMetadataReadResult
                   {
                           Success = false
                         , ErrorMessage = "Missing required metadata: Name."
                   };
        }

        if (metadata.TryGetValue("Category", out var category)
                    .Not() 
          || category is not null
         && category.HasNoValue())
        {
            return new ToolMetadataReadResult
                   {
                           Success      = false
                         , ErrorMessage = "Missing required metadata: Category."
                   };
        }

        return new ToolMetadataReadResult
               {
                       Success = true
                     , Metadata = new ToolMetadata
                                                  {
                                                          Name                 = Get(metadata, "Name")
                                                        , Category             = Get(metadata, "Category")
                                                        , Description          = Get(metadata, "Description")
                                                        , Icon                 = Get(metadata, "Icon")
                                                        , Order                = GetInt(metadata, "Order")
                                                        , Hidden               = GetBool(metadata, "Hidden")
                                                        , RequiresConfirmation = GetBool(metadata, "RequiresConfirmation")
                                                  }
               };
    }

    private static string Get( IDictionary<string, string> values
                             , string                      key )
    {
        return values.TryGetValue(key, out var value)
                       ? value
                       : string.Empty;
    }

    private static int GetInt( IDictionary<string, string> values
                             , string                      key )
    {
        return values.TryGetValue(key, out var value)
            && int.TryParse(value
                          , out var result)
                       ? result
                       : 0;
    }

    private static bool GetBool( IDictionary<string, string> values
                               , string                      key )
    {
        return values.TryGetValue(key, out var value)
            && bool.TryParse(value, out var result)
            && result;
    }
}