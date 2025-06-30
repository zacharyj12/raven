using RavenLib;
using System.Globalization;

try
{
    var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;
    var server = new Http.Server(port);
    Console.WriteLine($"Starting Raven Web Server on port {port}...");

    // List of example routes and descriptions
    var examples = new (string Path, string Description)[]
    {
        ("time", "Current Server Time"),
        ("timezone", "Time by Timezone (with form)"),
        ("form", "Form Example (POST)"),
        ("code", "Code Template Example"),
        ("filetemplate", "File Template Example"),
        ("json", "JSON Example"),
        ("plaintext", "Plain Text Example"),
        ("echo?query=hello", "Echo Query String Example")
    };

    // Home page: dynamically list all examples
    server.Get("/", ctx => $"<h1>Welcome to the Time Info Website</h1><ul>{string.Join("\n", examples.Select(e => $"<li><a href='/{e.Path}'>{e.Description}</a></li>"))}</ul>", "text/html");

    // Current server time
    server.Get("time", ctx => Template.Render("<h1>Server Time</h1><p>{{ DateTime.Now.ToString() }}</p>", new()), "text/html");

    // Timezone info page (GET: show form, POST: show result)
    server.Get("timezone", ctx => Template.Render(@"<h1>Get Time by Timezone</h1>
<form method='post' action='/timezone'>
  <input name='tz' placeholder='e.g. America/New_York'><br>
  <button type='submit'>Get Time</button>
</form>", new()), "text/html");

    server.Post("timezone", ctx => {
        var tz = ctx.Form.TryGetValue("tz", out var t) ? t : null;
        string result;
        try
        {
            if (!string.IsNullOrWhiteSpace(tz))
            {
                var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tz);
                var tzTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tzInfo);
                result = $"<h1>Time in {tz}:</h1><p>{tzTime}</p>";
            }
            else
            {
                result = "<p>Please enter a timezone ID.</p>";
            }
        }
        catch (Exception ex)
        {
            result = $"<p>Error: {ex.Message}</p>";
        }
        return result + "<p><a href='/timezone'>Back</a></p>";
    }, "text/html");

    // JSON example
    server.Get("json", ctx => $"{{\"message\":\"This is a JSON response\",\"path\":\"{ctx.Path}\"}}", "application/json");

    // Plain text example
    server.Get("plaintext", ctx => $"This is plain text. You requested: {ctx.Path}", "text/plain");

    // Echo query string
    server.Get("echo", ctx => Template.Render("<h1>Query: {{ query }}</h1>", new() {
        ["query"] = ctx.Query ?? "(none)"
    }), "text/html");

    // POST endpoint /submit that echoes form fields
    server.Post("submit", ctx => {
        var name = ctx.Form.TryGetValue("name", out var n) ? n : "(none)";
        var email = ctx.Form.TryGetValue("email", out var e) ? e : "(none)";
        return Template.Render("<h1>Form Submitted</h1><p>Name: {{ name }}</p><p>Email: {{ email }}</p>", new() {
            ["name"] = name,
            ["email"] = email
        });
    }, "text/html");

    // GET endpoint to show a form
    server.Get("form", ctx => "<form method='post' action='/submit'>" +
        "<input name='name' placeholder='Name'><br>" +
        "<input name='email' placeholder='Email'><br>" +
        "<button type='submit'>Submit</button>" +
        "</form>", "text/html");

    // Code execution in template
    server.Get("code", ctx => Template.Render(
        "<h1>2 + 2 = {{ 2 + 2 }}</h1>" +
        "<p>Uppercase name: {{ name is string s ? s.ToUpper() : \"\" }}</p>" +
        "<p>Current year: {{ DateTime.Now.Year }}</p>",
        new() { ["name"] = "dynamic user" }), "text/html");

    // Render a template file
    server.Get("filetemplate", ctx => {
        var context = new Dictionary<string, object?> {
            ["name"] = "File User",
            ["now"] = DateTime.Now.ToString()
        };
        return Template.RenderFile("web/example.tmpl", context);
    }, "text/html");

    bool shuttingDown = false;
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        if (!shuttingDown)
        {
            shuttingDown = true;
            Console.WriteLine("\nShutting down server...");
            server.Stop();
        }
        eventArgs.Cancel = true; // Prevent abrupt process termination.
    };

    server.Start();
    Console.WriteLine("Server stopped. Press any key to exit.");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to start server: {ex.Message}");
    Environment.Exit(1);
}