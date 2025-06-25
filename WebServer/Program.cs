using RavenLib;

try
{
    var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;
    var server = new Http.Server(port);
    Console.WriteLine($"Starting Raven Web Server on port {port}...");

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        Console.WriteLine("\nShutting down server...");
        eventArgs.Cancel = true; // Ensure process termination is handled cleanly.
    };

    server.Start();
    Console.WriteLine("Server stopped. Press any key to exit.");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to start server: {ex.Message}");
    Environment.Exit(1);
}