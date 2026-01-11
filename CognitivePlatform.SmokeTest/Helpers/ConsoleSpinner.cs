using CognitivePlatform.Api.Avails.Extensions;

namespace CognitivePlatform.SmokeTest.Helpers;

public class ConsoleSpinner : IDisposable
{
    public static readonly string[][] SpinnerStyles =
    [
            new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" }                         // Braille dots
          , new[] { "|", "/", "-", "\\" }                                                        // Classic
          , new[] { "◴", "◷", "◶", "◵" }                                                        // Box corners
          , new[] { "◐", "◓", "◑", "◒" }                                                       // Half circles
          , new[] { "←", "↖", "↑", "↗", "→", "↘", "↓", "↙" }                                    // Arrows
          , new[] { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█", "▇", "▆", "▅", "▄", "▃", "▁" }      // Blocks
          , new[] { "▖", "▘", "▝", "▗" }                                                        // Quadrants
          , new[] { "┤", "┘", "┴", "└", "├", "┌", "┬", "┐" }                                    // Box drawing
          , new[] { "◢", "◣", "◤", "◥" }                                                      // Triangles
          , new[] { "⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷" }                                  // Braille vertical
          , new[] { "⠁", "⠂", "⠄", "⡀", "⢀", "⠠", "⠐", "⠈" }                                  // Braille dots small
          , new[] { "⢎⡰", "⢎⡡", "⢎⡑", "⢎⠱", "⠎⡱", "⢊⡱", "⢌⡱", "⢆⡱" }                        // Braille double
          , new[] { ".", "o", "O", "@", "*" }                                                  // Growing dot
          , new[] { "∙∙∙", "●∙∙", "∙●∙", "∙∙●", "∙∙∙" }                                        // Dot wave
          , new[] { "🌍", "🌎", "🌏" }                                                        // Earth spinning
          , new[] { "🌑", "🌒", "🌓", "🌔", "🌕", "🌖", "🌗", "🌘" }                         // Moon phases
          , new[] { "⠋", "⠙", "⠚", "⠞", "⠖", "⠦", "⠴", "⠲", "⠳", "⠓" }                       // Braille alt
          , new[] { "⠄", "⠆", "⠇", "⠋", "⠙", "⠸", "⠰", "⠠", "⠰", "⠸", "⠙", "⠋", "⠇", "⠆" }  // Braille pulse
          , new[] { "🧠   ", " 🧠  ", "  🧠 ", "   🧠", "  🧠 ", " 🧠  ", "🧠   " }           // Brain thinking
          , new[] { "💡", "💭", "💡", "💭" }                                                  // Lightbulb/thought
          , new[] { "🤔", "🧐", "🤔", "🧐" }                                                  // Thinking faces
          , new[] { "T", "h", "i", "n", "k", "i", "n", "g" }                                   // Thinking faces
          , new[] { "" }                                                                       // Wave text (handled specially)
    ];
    
    public static int NumberOfSpinnerStyles => SpinnerStyles.Length;

    private static readonly Random                  Random = new();
    private readonly        string[]                _frames;
    private readonly        CancellationTokenSource _cancellationTokenSource;
    private readonly        Task                    _spinnerTask;
    private readonly        string                  _message;
    private readonly        bool                    _isWaveText;

    public ConsoleSpinner(string message = "Processing", SpinnerStyle? style = null)
    {
        _message    = message;
        _isWaveText = style == SpinnerStyle.WaveText;
        
        if (_isWaveText)
        {
            _frames = GenerateWaveTextFrames(message);
        }
        else if (style.HasValue)
        {
            _frames = SpinnerStyles[(int)style.Value];
        }
        else
        {
            _frames = SpinnerStyles[Random.Next(SpinnerStyles.Length)];
        }
        
        _cancellationTokenSource = new CancellationTokenSource();
        
        Console.CursorVisible = false;
        
        _spinnerTask = Task.Run(() => Spin(_cancellationTokenSource.Token
                                         , _isWaveText.Not()));
    }
    
    private static string[] GenerateWaveTextFrames(string text)
    {
        var frames            = new List<string>();
        var textWithoutSpaces = text.Replace(" ", "");
    
        // Forward pass
        for (int i = 0; i < textWithoutSpaces.Length; i++)
        {
            frames.Add(CreateFrame(text, i));
        }
    
        // Backward pass (skip first and last to avoid duplicates at the ends)
        for (int i = textWithoutSpaces.Length - 2; i > 0; i--)
        {
            frames.Add(CreateFrame(text, i));
        }
    
        return frames.ToArray();
    }

    private static string CreateFrame(string text, int capitalizeIndex)
    {
        var frame         = new char[text.Length];
        var nonSpaceIndex = 0;
    
        for (int j = 0; j < text.Length; j++)
        {
            if (text[j] == ' ')
            {
                frame[j] = ' ';
            }
            else
            {
                frame[j] = nonSpaceIndex == capitalizeIndex 
                                   ? char.ToLower(text[j])
                                   : char.ToUpper(text[j]);
                nonSpaceIndex++;
            }
        }
    
        return new string(frame);
    }
    
    private async Task Spin(CancellationToken cancellationToken, bool showMessage = true)
    {
        var frameIndex = 0;
        
        while (cancellationToken.IsCancellationRequested
                                .Not())
        {
            var message = showMessage 
                                  ? $" {_message}..." 
                                  : string.Empty;
            Console.Write($"\r{_frames[frameIndex]}{message}");
            frameIndex = (frameIndex + 1) % _frames.Length;
            
            try
            {
                await Task.Delay(80, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _spinnerTask.Wait();
        
        Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
        Console.CursorVisible = true;
        
        _cancellationTokenSource.Dispose();
    }
}

public enum SpinnerStyle
{
    BrailleDots      = 0
  , Classic          = 1
  , BoxCorners       = 2
  , HalfCircles      = 3
  , Arrows           = 4
  , Blocks           = 5
  , Quadrants        = 6
  , BoxDrawing       = 7
  , Triangles        = 8
  , BrailleVertical  = 9
  , BrailleDotsSmall = 10
  , BrailleDouble    = 11
  , GrowingDot       = 12
  , DotWave          = 13
  , EarthSpinning    = 14
  , MoonPhases       = 15
  , BrailleAlt       = 16
  , BraillePulse     = 17
  , Brain            = 18
  , Lightbulb        = 19
  , ThinkingFace     = 20
  , WaveText         = 21
}