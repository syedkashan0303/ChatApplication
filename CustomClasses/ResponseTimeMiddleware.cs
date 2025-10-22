namespace SignalRMVC.CustomClasses
{
    using Microsoft.AspNetCore.Http;
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;

    public class ResponseTimeMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly string[] _skipExt = { ".html", ".js", ".css" };

        public ResponseTimeMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 1⃣ Skip static files we don’t care about
            var path = context.Request.Path.Value;
            if (path != null)
            {
                foreach (var ext in _skipExt)
                {
                    if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        await _next(context);
                        return;
                    }
                }
            }

            // 2⃣ Measure the request (incl. SignalR negotiate / WS handshake)
            var sw = Stopwatch.StartNew();
            await _next(context);
            sw.Stop();

            var ms = sw.ElapsedMilliseconds;

            // 3⃣ Log + expose in a custom header
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context.Request.Method} {context.Request.Path} took {ms} ms");
            //context.Response.Headers["X-Response-Time-ms"] = ms.ToString();
        }
    }

}
