using TxtConverter.Cli;

namespace TxtConverter;

/// <summary>
/// Application entry point. The executable is dual mode:
/// no arguments (or the explicit "gui" command) starts the WPF interface,
/// any other argument switches the process to the command line interface.
/// </summary>
public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        if (CliLauncher.IsCliInvocation(args))
            return CliLauncher.RunAsync(args).GetAwaiter().GetResult();

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}