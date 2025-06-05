using System;

namespace SignalRMVC.CustomClasses
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context); // Continue processing the request
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception occurred");

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                var errorResponse = new
                {
                    Message = "An unexpected error occurred. Please try again later.",
                    Error = ex.Message, // Optional: remove in production
                    FullMessage = ex.InnerException != null ?  ex.InnerException.Message : ""
                };

                //var json = System.Text.Json.JsonSerializer.Serialize(errorResponse);


                var json = System.Text.Json.JsonSerializer.Serialize(errorResponse, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var logFilePath = @"D:\ChatAppLogs\JsonException"+ DateTime.Now.ToString("dd_MM_yyyy_HH_mm") + ".json";
                await File.AppendAllTextAsync(logFilePath, json + Environment.NewLine);
                //await context.Response.WriteAsync(json);
            }
        }
    }
}
