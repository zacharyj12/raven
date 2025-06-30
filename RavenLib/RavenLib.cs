using System.Net;
using System.Net.Sockets;
using System.Text;

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


        public class Server : Http
        {
            private readonly TcpListener listener;
            private volatile bool isRunning = false;

            int port;
            string host;
            string Webdirectory;

            public Server(int port, string host = "localhost", string webdirectory = "web")
            {
                this.port = port;
                this.host = host;
                this.Webdirectory = webdirectory;

                listener = new TcpListener(IPAddress.Any, port);
            }

            public void Start()
            {
                isRunning = true;
                listener.Start();
                Console.WriteLine($"Server started at {host}:{port}");
                while (isRunning)
                {
                    if (!listener.Pending())
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    var client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                listener.Stop();
            }

            public void Stop()
            {
                isRunning = false;
                listener.Stop();
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

                string Path = "index.html";
                var parts = requestText.Split(' ');
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    Path = parts[1].TrimStart('/');
                    if (string.IsNullOrEmpty(Path))
                        Path = "index.html";
                }
                else
                {
                    StatusCode = 400;
                    var response = new HttpResponse("<h1>400 Bad Request</h1>", StatusCode);
                    response.ContentType = "text/html";
                    response.ContentLength = response.Body?.Length;
                    var responseBytes = response.ToBytes();
                    stream.Write(responseBytes, 0, responseBytes.Length);
                    logger.CreateLogAsync($"400 Bad Request: Malformed request").Wait();
                    client.Close();
                    return;
                }

                string fileContent;
                try
                {
                    fileContent = ReadFile(Path, Webdirectory);
                    logger.CreateLogAsync($"200 OK: {Path}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading file {Path}: {ex.Message}");
                    StatusCode = 404;
                    fileContent = "<h1>404 Not Found</h1>";
                    logger.CreateLogAsync($"404 Not Found: {Path}").Wait();
                }
                var resp = new HttpResponse(fileContent, StatusCode);
                resp.ContentType = MimeTypes.GetMimeType(Path);
                resp.ContentLength = resp.Body?.Length;
                byte[] respBytes = resp.ToBytes();
                stream.Write(respBytes, 0, respBytes.Length);
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
}