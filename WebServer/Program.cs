using RavenLib;

try
{
    var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;
    var server = new Server(port);

    Console.WriteLine($"Starting Raven Web Server on port {port}...");
    server.Start();
    Console.WriteLine("Server started successfully. Press Ctrl+C to stop.");

    // Keep the application running until Ctrl+C
    Console.CancelKeyPress += (_, _) =>
    {
        Console.WriteLine("\nShutting down server...");
        Environment.Exit(0);
    };

    Thread.Sleep(Timeout.Infinite);
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to start server: {ex.Message}");
    Environment.Exit(1);
}