using CognitivePlatform.Api.Attributes;

namespace CognitivePlatform.Api.Demo;

public class DemoActions
{
    [NaturalLanguageAction( Description = "A test action for Phase 1."
                          , Examples    = new[]
                                          {
                                              "test"
                                            , "run test"
                                            , "invoke demo"
                                          }
    )]
    public void TestAction()
    {
        Console.WriteLine("Demo action executed!");
    }
}