using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Reflection;

namespace RavenLib
{
    public class Logging
    {
        public string? loggingPath;
        private static readonly SemaphoreSlim LogSemaphore = new(1, 1);
        public Logging(string? loggingPath = "logs.txt")
        {
            this.loggingPath = loggingPath;
        }
        private string GetLogText(string message)
        {
            return $"{DateTime.Now}: {message}\n";
        }
        public async Task CreateLogAsync(string message)
        {
            await LogSemaphore.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(loggingPath ?? "logs.txt", GetLogText(message));
            }
            finally
            {
                LogSemaphore.Release();
            }
        }
    }

    public class HttpResponse : Http
    {
        public string? Body { get; set; }
        public int StatusCode { get; set; }
        public string ReasonPhrase
        {
            get
            {
                return StatusCode switch
                {
                    200 => "OK",
                    404 => "Not Found",
                    500 => "Internal Server Error",
                    400 => "Bad Request",
                    _ => "Unknown Status"
                };
            }
        }
        public Dictionary<string, string> Headers { get; set; } = new();
        public string? ContentType
        {
            get => Headers.TryGetValue("Content-Type", out var value) ? value : null;
            set
            {
                if (value != null)
                    Headers["Content-Type"] = value;
                else
                    Headers.Remove("Content-Type");
            }
        }
        public int? ContentLength
        {
            get => Headers.TryGetValue("Content-Length", out var value) && int.TryParse(value, out var len) ? len : null;
            set
            {
                if (value.HasValue)
                    Headers["Content-Length"] = value.Value.ToString();
                else
                    Headers.Remove("Content-Length");
            }
        }
        private const string Server = "Raven 1.0.0";

        public HttpResponse(string? body, int statusCode)
        {
            Body = body;
            StatusCode = statusCode;
        }

        public HttpResponse(string? body, int statusCode, Dictionary<string, string>? headers)
            : this(body, statusCode)
        {
            if (headers != null)
                Headers = new Dictionary<string, string>(headers);
        }
        // method to convert the response to a string representation
        public override string ToString()
        {
            // Ensure Server header is always present
            Headers["Server"] = Server;
            var response = $"HTTP/1.1 {StatusCode} {ReasonPhrase}\r\n";
            foreach (var header in Headers)
            {
                response += $"{header.Key}: {header.Value}\r\n";
            }
            response += "\r\n";
            if (Body != null)
            {
                response += Body;
            }
            return response;
        }
        // method to convert the reesponse to a byte array, for sockets.
        public byte[] ToBytes()
        {
            return Encoding.UTF8.GetBytes(ToString());
        }

    }

    public class Http
    {
        // Function to read from a file, from a client Path.  
        public static string ReadFile(string ClientPath, string WebDirectory = "web")
        {
            var FullPath = Path.Combine(WebDirectory, ClientPath);
            if (!File.Exists(FullPath))
            {
                throw new FileNotFoundException($"File {FullPath} does not exist.");
            }
            var FileContents = File.ReadAllText(FullPath);
            if (string.IsNullOrEmpty(FileContents))
            {
                throw new Exception($"File {FullPath} is empty.");
            }
            else
            {
                return FileContents;
            }
        }

        public class HttpRequestContext
        {
            public string Method { get; set; } = "GET";
            public string Path { get; set; } = string.Empty;
            public Dictionary<string, string> Headers { get; set; } = new();
            public string? Body { get; set; }
            public string? Query { get; set; }
            public Dictionary<string, string> Form { get; set; } = new();
        }

        public class Server : Http
        {
            int port;
            string host;
            string Webdirectory;
            TcpListener? listener;
            private readonly Dictionary<string, Func<HttpRequestContext, HttpResponse>> getRoutes = new();
            private readonly Dictionary<string, Func<HttpRequestContext, HttpResponse>> postRoutes = new();

            public Server(int port, string host = "localhost", string webdirectory = "web")
            {
                this.port = port;
                this.host = host;
                this.Webdirectory = webdirectory;
            }

            public void RegisterRoute(string path, Func<HttpRequestContext, HttpResponse> handler)
            {
                getRoutes[path.TrimStart('/')] = handler;
            }

            public void RegisterPostRoute(string path, Func<HttpRequestContext, HttpResponse> handler)
            {
                postRoutes[path.TrimStart('/')] = handler;
            }

            public void Get(string path, Func<HttpRequestContext, HttpResponse> handler) => RegisterRoute(path, handler);
            public void Post(string path, Func<HttpRequestContext, HttpResponse> handler) => RegisterPostRoute(path, handler);
            public void Get(string path, string staticHtml) => RegisterRoute(path, ctx => new HttpResponse(staticHtml, 200) { ContentType = "text/html" });
            public void Post(string path, string staticHtml) => RegisterPostRoute(path, ctx => new HttpResponse(staticHtml, 200) { ContentType = "text/html" });
            public void Get(string path, Func<HttpRequestContext, string> handler, string? contentType = null)
            {
                RegisterRoute(path, ctx =>
                {
                    var resp = new HttpResponse(handler(ctx), 200);
                    if (!string.IsNullOrEmpty(contentType))
                        resp.ContentType = contentType;
                    return resp;
                });
            }
            public void Post(string path, Func<HttpRequestContext, string> handler, string? contentType = null)
            {
                RegisterPostRoute(path, ctx =>
                {
                    var resp = new HttpResponse(handler(ctx), 200);
                    if (!string.IsNullOrEmpty(contentType))
                        resp.ContentType = contentType;
                    return resp;
                });
            }

            public void Start()
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Console.WriteLine($"Server started at {host}:{port}");
                int errorCount = 0;
                while (true)
                {
                    var client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
            }

            public void Stop()
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }

            private void HandleClient(TcpClient client)
            {
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                int StatusCode = 200;
                var logger = new Logging();
                Console.WriteLine($"Received request: {requestText}");

                // Parse request line and headers
                var lines = requestText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var requestLine = lines[0].Split(' ');
                string method = requestLine.Length > 0 ? requestLine[0] : "GET";
                string pathWithQuery = requestLine.Length > 1 ? requestLine[1] : "/";
                string path = pathWithQuery.TrimStart('/');
                string query = null;
                int qIdx = path.IndexOf('?');
                if (qIdx >= 0)
                {
                    query = path.Substring(qIdx + 1);
                    path = path.Substring(0, qIdx);
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int bodyStart = -1;
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        bodyStart = i + 1;
                        break;
                    }
                    var sep = line.IndexOf(':');
                    if (sep > 0)
                        headers[line.Substring(0, sep).Trim()] = line.Substring(sep + 1).Trim();
                }

                string? body = null;
                if (bodyStart > 0 && bodyStart < lines.Length)
                {
                    body = string.Join("\n", lines.Skip(bodyStart));
                }
                // If Content-Length is set and body is incomplete, read the rest
                if (method == "POST" && headers.TryGetValue("Content-Length", out var clStr) && int.TryParse(clStr, out var contentLength))
                {
                    int bodyBytes = Encoding.UTF8.GetByteCount(body ?? "");
                    if (bodyBytes < contentLength)
                    {
                        var extra = new byte[contentLength - bodyBytes];
                        int extraRead = stream.Read(extra, 0, extra.Length);
                        body += Encoding.UTF8.GetString(extra, 0, extraRead);
                    }
                }

                var form = new Dictionary<string, string>();
                if (method == "POST" && headers.TryGetValue("Content-Type", out var ct) && ct.StartsWith("application/x-www-form-urlencoded"))
                {
                    var formBody = body ?? string.Empty;
                    foreach (var pair in formBody.Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = pair.Split('=');
                        if (kv.Length == 2)
                            form[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
                    }
                }

                var context = new HttpRequestContext
                {
                    Method = method,
                    Path = path,
                    Headers = headers,
                    Query = query,
                    Body = body,
                    Form = form
                };

                // Routing logic: try route table first, then static file
                bool handled = false;
                if (method == "POST" && postRoutes.TryGetValue(path, out var postHandler))
                {
                    var resp = postHandler(context);
                    resp.ContentLength = resp.Body?.Length;
                    var respBytes = resp.ToBytes();
                    stream.Write(respBytes, 0, respBytes.Length);
                    logger.CreateLogAsync($"200 OK: [POST ROUTE] {path}");
                    handled = true;
                }
                else if (getRoutes.TryGetValue(path, out var handler))
                {
                    var resp = handler(context);
                    resp.ContentLength = resp.Body?.Length;
                    var respBytes = resp.ToBytes();
                    stream.Write(respBytes, 0, respBytes.Length);
                    logger.CreateLogAsync($"200 OK: [ROUTE] {path}");
                    handled = true;
                }
                else
                {
                    // If not handled by route table, try static file (default to index.html if path is empty)
                    string filePath = string.IsNullOrEmpty(path) ? "index.html" : path;
                    string fileContent;
                    try
                    {
                        fileContent = ReadFile(filePath, Webdirectory);
                        logger.CreateLogAsync($"200 OK: {filePath}");
                        StatusCode = 200;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading file {filePath}: {ex.Message}");
                        StatusCode = 404;
                        fileContent = "<h1>404 Not Found</h1>";
                        logger.CreateLogAsync($"404 Not Found: {filePath}").Wait();
                    }
                    var fileResp = new HttpResponse(fileContent, StatusCode);
                    fileResp.ContentType = MimeTypes.GetMimeType(filePath);
                    fileResp.ContentLength = fileResp.Body?.Length;
                    byte[] fileRespBytes = fileResp.ToBytes();
                    stream.Write(fileRespBytes, 0, fileRespBytes.Length);
                }
                client.Close();
            }
        }

        public class MimeTypes : Http
        {
            string? Path;
            public MimeTypes(string Path)
            {
                this.Path = Path;
            }
            static public string GetMimeType(string filePath)
            {
                string mimeType = "text/plain";
                string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                switch (extension)
                {
                    case ".aac": mimeType = "audio/aac"; break;
                    case ".abw": mimeType = "application/x-abiword"; break;
                    case ".apng": mimeType = "image/apng"; break;
                    case ".arc": mimeType = "application/x-freearc"; break;
                    case ".avif": mimeType = "image/avif"; break;
                    case ".avi": mimeType = "video/x-msvideo"; break;
                    case ".azw": mimeType = "application/vnd.amazon.ebook"; break;
                    case ".bin": mimeType = "application/octet-stream"; break;
                    case ".bmp": mimeType = "image/bmp"; break;
                    case ".bz": mimeType = "application/x-bzip"; break;
                    case ".bz2": mimeType = "application/x-bzip2"; break;
                    case ".cda": mimeType = "application/x-cdf"; break;
                    case ".csh": mimeType = "application/x-csh"; break;
                    case ".css": mimeType = "text/css"; break;
                    case ".csv": mimeType = "text/csv"; break;
                    case ".doc": mimeType = "application/msword"; break;
                    case ".docx": mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"; break;
                    case ".eot": mimeType = "application/vnd.ms-fontobject"; break;
                    case ".epub": mimeType = "application/epub+zip"; break;
                    case ".gz": mimeType = "application/gzip"; break;
                    case ".gif": mimeType = "image/gif"; break;
                    case ".htm":
                    case ".html": mimeType = "text/html"; break;
                    case ".ico": mimeType = "image/vnd.microsoft.icon"; break;
                    case ".ics": mimeType = "text/calendar"; break;
                    case ".jar": mimeType = "application/java-archive"; break;
                    case ".jpeg":
                    case ".jpg": mimeType = "image/jpeg"; break;
                    case ".js": mimeType = "text/javascript"; break;
                    case ".json": mimeType = "application/json"; break;
                    case ".jsonld": mimeType = "application/ld+json"; break;
                    case ".md": mimeType = "text/markdown"; break;
                    case ".mid":
                    case ".midi": mimeType = "audio/midi"; break;
                    case ".mjs": mimeType = "text/javascript"; break;
                    case ".mp3": mimeType = "audio/mpeg"; break;
                    case ".mp4": mimeType = "video/mp4"; break;
                    case ".mpeg": mimeType = "video/mpeg"; break;
                    case ".mpkg": mimeType = "application/vnd.apple.installer+xml"; break;
                    case ".odp": mimeType = "application/vnd.oasis.opendocument.presentation"; break;
                    case ".ods": mimeType = "application/vnd.oasis.opendocument.spreadsheet"; break;
                    case ".odt": mimeType = "application/vnd.oasis.opendocument.text"; break;
                    case ".oga": mimeType = "audio/ogg"; break;
                    case ".ogv": mimeType = "video/ogg"; break;
                    case ".ogx": mimeType = "application/ogg"; break;
                    case ".opus": mimeType = "audio/ogg"; break;
                    case ".otf": mimeType = "font/otf"; break;
                    case ".png": mimeType = "image/png"; break;
                    case ".pdf": mimeType = "application/pdf"; break;
                    case ".php": mimeType = "application/x-httpd-php"; break;
                    case ".ppt": mimeType = "application/vnd.ms-powerpoint"; break;
                    case ".pptx": mimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation"; break;
                    case ".rar": mimeType = "application/vnd.rar"; break;
                    case ".rtf": mimeType = "application/rtf"; break;
                    case ".sh": mimeType = "application/x-sh"; break;
                    case ".svg": mimeType = "image/svg+xml"; break;
                    case ".tar": mimeType = "application/x-tar"; break;
                    case ".tif":
                    case ".tiff": mimeType = "image/tiff"; break;
                    case ".ts": mimeType = "video/mp2t"; break;
                    case ".ttf": mimeType = "font/ttf"; break;
                    case ".txt": mimeType = "text/plain"; break;
                    case ".vsd": mimeType = "application/vnd.visio"; break;
                    case ".wav": mimeType = "audio/wav"; break;
                    case ".weba": mimeType = "audio/webm"; break;
                    case ".webm": mimeType = "video/webm"; break;
                    case ".webmanifest": mimeType = "application/manifest+json"; break;
                    case ".webp": mimeType = "image/webp"; break;
                    case ".woff": mimeType = "font/woff"; break;
                    case ".woff2": mimeType = "font/woff2"; break;
                    case ".xhtml": mimeType = "application/xhtml+xml"; break;
                    case ".xls": mimeType = "application/vnd.ms-excel"; break;
                    case ".xlsx": mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"; break;
                    case ".xml": mimeType = "application/xml"; break;
                    case ".xul": mimeType = "application/vnd.mozilla.xul+xml"; break;
                    case ".zip": mimeType = "application/zip"; break;
                    case ".3gp": mimeType = "video/3gpp"; break;
                    case ".3g2": mimeType = "video/3gpp2"; break;
                    case ".7z": mimeType = "application/x-7z-compressed"; break;
                }
                return mimeType;
            }
        }
    }

    public static class Template
    {
        private static readonly Regex varPattern = new(@"\{\{(.*?)\}\}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly ScriptOptions scriptOptions = ScriptOptions.Default
            .AddReferences(typeof(object).Assembly)
            .AddReferences(typeof(System.Linq.Enumerable).Assembly)
            .AddImports("System", "System.Linq", "System.Collections.Generic");

        public static string Render(string template, Dictionary<string, object?> context)
        {
            return varPattern.Replace(template, match =>
            {
                var code = match.Groups[1].Value.Trim();
                // If it's a simple variable name, just substitute
                if (Regex.IsMatch(code, @"^\w+$"))
                {
                    if (context != null && context.TryGetValue(code, out var value) && value != null)
                        return value.ToString();
                    return string.Empty;
                }
                // Otherwise, treat as C# code
                try
                {
                    var globals = new TemplateGlobals(context);
                    var result = CSharpScript.EvaluateAsync<object>(code, scriptOptions, globals).Result;
                    return result?.ToString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    return $"[template error: {ex.Message}]";
                }
            });
        }

        public static string RenderFile(string path, Dictionary<string, object?> context)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Template file not found: {path}");
            var template = File.ReadAllText(path);
            return Render(template, context);
        }

        public class TemplateGlobals
        {
            private readonly Dictionary<string, object?> _context;
            public TemplateGlobals(Dictionary<string, object?> context)
            {
                _context = context;
            }
            public object? this[string key] => _context.TryGetValue(key, out var v) ? v : null;
            public Dictionary<string, object?> Context => _context;
            public object? name => _context.TryGetValue("name", out var v) ? v : null;
            public object? path => _context.TryGetValue("path", out var v) ? v : null;
            public object? query => _context.TryGetValue("query", out var v) ? v : null;
            public object? email => _context.TryGetValue("email", out var v) ? v : null;
            public object? time => _context.TryGetValue("time", out var v) ? v : null;
            public object? now => _context.TryGetValue("now", out var v) ? v : null;
        }
    }
}