using System;

namespace TxtConverter.Cli;

/// <summary>Process exit codes used by every CLI command.</summary>
public static class ExitCodes {
    /// <summary>Command finished successfully.</summary>
    public const int Success = 0;
    /// <summary>Unexpected runtime failure.</summary>
    public const int Failure = 1;
    /// <summary>Invalid or unknown command line arguments.</summary>
    public const int UsageError = 2;
    /// <summary>The scan produced no files, so there is nothing to convert.</summary>
    public const int NothingToConvert = 3;
    /// <summary>Requested preset / config key / directory does not exist.</summary>
    public const int NotFound = 4;
    /// <summary>AI provider or remote API call failed.</summary>
    public const int ApiError = 5;
}

/// <summary>Error with a well defined CLI exit code and user facing message.</summary>
public sealed class CliException : Exception {
    public int ExitCode { get; }

    public CliException(string message, int exitCode = ExitCodes.Failure) : base(message) {
        ExitCode = exitCode;
    }

    public CliException(string message, Exception inner, int exitCode = ExitCodes.Failure) : base(message, inner) {
        ExitCode = exitCode;
    }

    public static CliException Usage(string message) => new(message, ExitCodes.UsageError);
    public static CliException NotFound(string message) => new(message, ExitCodes.NotFound);
    public static CliException Api(string message, Exception? inner = null) =>
        inner == null ? new CliException(message, ExitCodes.ApiError) : new CliException(message, inner, ExitCodes.ApiError);
}