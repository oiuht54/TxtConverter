using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TxtConverter.Cli;

/// <summary>
/// Console writer. Results go to stdout (pipe friendly), everything else goes to stderr.
/// In JSON mode stdout carries exactly one JSON document and all human readable text is muted.
/// </summary>
internal sealed class ConsoleLog {
    private bool _colorBroken;

    public bool JsonMode { get; set; }
    public bool Quiet { get; set; }
    public bool Verbose { get; set; }
    public bool UseColor { get; set; }

    /// <summary>Primary result line (stdout).</summary>
    public void Result(string message) {
        if (JsonMode) return;
        Write(Console.Out, message, null);
    }

    /// <summary>Secondary result line (stdout).</summary>
    public void Line(string message) {
        if (JsonMode) return;
        Write(Console.Out, message, null);
    }

    /// <summary>Progress / informational message (stderr).</summary>
    public void Detail(string message) {
        if (JsonMode || Quiet) return;
        Write(Console.Error, message, null);
    }

    /// <summary>Extra information shown only with --verbose (stderr).</summary>
    public void Trace(string message) {
        if (JsonMode || Quiet || !Verbose) return;
        Write(Console.Error, message, ConsoleColor.DarkGray);
    }

    /// <summary>Warning (stderr).</summary>
    public void Warn(string message) {
        if (JsonMode || Quiet) return;
        Write(Console.Error, "warning: " + message, ConsoleColor.Yellow);
    }

    /// <summary>Error (stderr, never suppressed).</summary>
    public void Error(string message) {
        Write(Console.Error, "error: " + message, ConsoleColor.Red);
    }

    /// <summary>Success marker (stderr, suppressed in quiet mode).</summary>
    public void Success(string message) {
        if (JsonMode || Quiet) return;
        Write(Console.Error, message, ConsoleColor.Green);
    }

    /// <summary>Section heading (stdout).</summary>
    public void Section(string title) {
        if (JsonMode) return;
        Write(Console.Out, title, ConsoleColor.Cyan);
    }

    /// <summary>Writes the single JSON document produced by a command.</summary>
    public void EmitJson(IDictionary<string, object?> payload) {
        var options = new JsonSerializerOptions {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        Console.WriteLine(JsonSerializer.Serialize(payload, options));
    }

    private void Write(TextWriter writer, string message, ConsoleColor? color) {
        if (color.HasValue && UseColor && !_colorBroken) {
            try {
                Console.ForegroundColor = color.Value;
                writer.WriteLine(message);
                Console.ResetColor();
                return;
            }
            catch {
                // No console attached (redirected/piped) - fall back to plain text.
                _colorBroken = true;
            }
        }
        writer.WriteLine(message);
    }
}