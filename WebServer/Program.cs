using RavenLib;
using System.Threading;

var cts = new CancellationTokenSource();
try
{
    var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;
    var server = new RavenLib.Http.Server(port);
    Console.WriteLine($"Starting Raven Web Server on port {port}...");
    Console.CancelKeyPress += (_, _) =>
    {
        Console.WriteLine("\nShutting down server...");
        cts.Cancel();
    };
    server.Start(cts.Token);
    Console.WriteLine("Server stopped. Press any key to exit.");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to start server: {ex.Message}");
    Environment.Exit(1);
}