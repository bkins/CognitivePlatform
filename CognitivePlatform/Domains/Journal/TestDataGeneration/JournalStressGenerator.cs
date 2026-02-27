using System.Text;

namespace CognitivePlatform.Api.Domains.Journal.TestDataGeneration;

public static class JournalStressGenerator
{
    public static string Generate( int  paragraphs
                                 , int  lineLength
                                 , bool includeUnicode = false )
    {
        var sb = new StringBuilder();

        var baseLine = includeUnicode
                               ? "🚀⚙️🧠 Unicode Test — "
                               : "Lorem ipsum dolor sit amet — ";

        for (int i = 0; i < paragraphs; i++)
        {
            sb.AppendLine($"Paragraph {i + 1}");

            var line = new StringBuilder(baseLine);

            while (line.Length < lineLength)
            {
                line.Append(baseLine);
            }

            sb.AppendLine(line.ToString());
            sb.AppendLine();
        }

        return sb.ToString();
    }
}