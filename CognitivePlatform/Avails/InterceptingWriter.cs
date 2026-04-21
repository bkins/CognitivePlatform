using System.Text;

namespace CognitivePlatform.Api.Avails;

public class InterceptingWriter : TextWriter
{
    private readonly TextWriter _original;

    public InterceptingWriter(TextWriter original)
    {
        _original = original;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(string? value)
    {
        var stack = Environment.StackTrace;
        
        _original.WriteLine($"[RAW WRITE] {value}");
        _original.WriteLine(stack);
        _original.WriteLine(); 
    }

    public override void WriteLine(string? value)
    {
        var stack = Environment.StackTrace;
        
        _original.WriteLine($"[RAW WRITELINE] {value}");
        _original.WriteLine(stack);
        _original.WriteLine(); 
    }
}