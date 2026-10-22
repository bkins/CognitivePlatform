using System.Xml.Linq;

namespace CognitivePlatform.Admin.Services.Testing;

public static class TestResultReader
{
    public static void Read(TestProjectResult    result
                          , string               path
                          , Func<string, string> redact)
    {
        if (!File.Exists(path)) { result.Outcome = "NoTests"; return; }
        var content = redact(File.ReadAllText(path));
        File.WriteAllText(path, content);
        var document = XDocument.Parse(content);
        var counters = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "Counters");
        if (counters is null) { result.Outcome = "NoTests"; return; }
        result.Passed = (int?)counters.Attribute("passed") ?? 0;
        result.Failed = (int?)counters.Attribute("failed") ?? 0;
        result.Skipped = (int?)counters.Attribute("notExecuted") ?? 0;
        result.TrxPath = path;
        result.Failures = document.Descendants().Where(element => element.Name.LocalName == "UnitTestResult" && (string?)element.Attribute("outcome") == "Failed")
            .Select(element =>
            {
                var message = redact(((string?)element.Attribute("testName") ?? "Test failure") + ": " + element.Value);
                return message[..Math.Min(4000, message.Length)];
            })
            .Take(100).ToList();
        result.Outcome = result.Failed > 0 || result.TestExitCode != 0 ? "TestFailed" : result.Passed == 0 ? "NoTests" : "Passed";
    }
}
