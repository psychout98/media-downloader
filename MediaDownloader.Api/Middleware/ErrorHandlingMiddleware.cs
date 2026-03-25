using System.Text.Json;
using MediaDownloader.Api.Clients;

namespace MediaDownloader.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        // Don't handle cancelled requests
        if (ex is OperationCanceledException)
        {
            context.Response.StatusCode = 499; // Client closed request
            return;
        }

        var (statusCode, errorKey) = ex switch
        {
            RealDebridException => (502, "rd_api_error"),
            FileNotFoundException => (404, "not_found"),
            DirectoryNotFoundException => (404, "not_found"),
            ArgumentException => (422, "validation_error"),
            KeyNotFoundException => (404, "not_found"),
            InvalidOperationException => (400, "bad_request"),
            UnauthorizedAccessException => (500, "permission_error"),
            IOException => (500, "io_error"),
            HttpRequestException => (502, "external_api_error"),
            _ => (500, "server_error")
        };

        if (statusCode >= 500)
            _logger.LogError(ex, "Unhandled exception");
        else
            _logger.LogWarning(ex, "Request error ({StatusCode})", statusCode);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new { error = errorKey, detail = ex.Message };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
