using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RavenLib
{
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
        public string Server = "Raven 1.0.0";

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
            return System.Text.Encoding.UTF8.GetBytes(ToString());
        }

    }

    public class Http
    {
        // Function to read from a file, from a client path.  
        public static string ReadFile(string clientPath, string webDirectory = "web")
        {
            try
            {
                var fullPath = Path.Combine(webDirectory, clientPath);
                return System.IO.File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file {clientPath}: {ex.Message}");
                return string.Empty;
            }
        }
    }

    public class Server : Http
    {
        int port;
        string host;
        TcpListener? listener;
        public Server(int port, string host = "localhost")
        {
            this.port = port;
            this.host = host;
        }
        public void Start()
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Server started at {host}:{port}");
            while (true)
            {
                var client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
        }
        private void HandleClient(TcpClient client)
        {
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            int StatusCode = 200;
            // Get client info. string path  
            Console.WriteLine($"Received request: {requestText}");
            // ReadFile() from the request, and get the file.  
            string path = requestText.Split(' ')[1].TrimStart('/');
            if (string.IsNullOrEmpty(path))
            {
                path = "index.html"; // Default file  
            }
            string fileContent;
            try
            {
                fileContent = ReadFile(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file {path}: {ex.Message}");
                StatusCode = 404;
                fileContent = "<h1>404 Not Found</h1>";
            }
            var response = new HttpResponse(fileContent, StatusCode);

            response.ContentType = MimeTypes.GetMimeType(path);
            response.ContentLength = response.Body?.Length;

            byte[] responseBytes = response.ToBytes();
            stream.Write(responseBytes, 0, responseBytes.Length);
            client.Close();
        }
    }

    public class MimeTypes : Http
    {
        string? path;
        public MimeTypes(string path)
        {
            this.path = path;
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
                case ".htm": case ".html": mimeType = "text/html"; break;
                case ".ico": mimeType = "image/vnd.microsoft.icon"; break;
                case ".ics": mimeType = "text/calendar"; break;
                case ".jar": mimeType = "application/java-archive"; break;
                case ".jpeg": case ".jpg": mimeType = "image/jpeg"; break;
                case ".js": mimeType = "text/javascript"; break;
                case ".json": mimeType = "application/json"; break;
                case ".jsonld": mimeType = "application/ld+json"; break;
                case ".md": mimeType = "text/markdown"; break;
                case ".mid": case ".midi": mimeType = "audio/midi"; break;
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
                case ".tif": case ".tiff": mimeType = "image/tiff"; break;
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
