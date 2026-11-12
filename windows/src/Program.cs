namespace Snowfall;

internal static class Program
{
    private static readonly string[] Commands = { "toggle", "on", "off", "status", "shake", "snow", "snow-on", "snow-off" };

    /// <summary>
    /// <c>Snowfall.exe</c> starts the app. <c>Snowfall.exe --toggle|--on|--off|--shake|--status</c> talks to the
    /// running instance over the pipe; if none is running, <c>--toggle</c>/<c>--on</c> start it with snow on.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        SnowApp.DebugDump = args.Contains("--debug");
        var command = args.Select(a => a.TrimStart('-', '/').ToLowerInvariant()).FirstOrDefault(Commands.Contains);

        using var mutex = new Mutex(true, @"Local\Snowfall.SingleInstance", out bool first);
        if (!first)
        {
            if (command != null) CommandServer.Send(command);
            return;
        }
        if (command is "off" or "status" or "shake" or "snow-off") return; // nothing running, nothing to do

        ApplicationConfiguration.Initialize();
        Application.Run(new SnowApp(forceEnable: command is "toggle" or "on" or "snow" or "snow-on"));
    }
}
