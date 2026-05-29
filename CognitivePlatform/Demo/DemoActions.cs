using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Demo;

[Domain(typeof(SystemDomain))]
public class DemoActions
{
    [NaturalLanguageAction( Description = "A test action for Phase 1."
                          , Examples    = new[]
                                          {
                                              "test"
                                            , "run test"
                                            , "invoke demo"
                                          }
                           , Category = "general")]
    public void TestAction()
    {
        Console.WriteLine("Demo action executed!");
    }
}