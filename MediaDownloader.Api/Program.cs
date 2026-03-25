using dotenv.net;
using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Configuration;
using MediaDownloader.Api.Data;
using MediaDownloader.Api.Data.Repositories;
using MediaDownloader.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Load .env before building configuration (ignore if missing)
DotEnv.Load(new DotEnvOptions(ignoreExceptions: true));

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// App data directory + required subdirectories
var appDataDir = builder.Configuration["APP_DATA_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
var logsDir = Path.Combine(appDataDir, "logs");
var postersDir = Path.Combine(appDataDir, "posters");
var stagingDir = Path.Combine(appDataDir, "staging");
Directory.CreateDirectory(appDataDir);
Directory.CreateDirectory(logsDir);
Directory.CreateDirectory(postersDir);
Directory.CreateDirectory(stagingDir);

// Serilog — console + file logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logsDir, "media-downloader-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();
builder.Host.UseSerilog();

// App settings
builder.Services.AddAppSettings(builder.Configuration);

// Database
var dbPath = Path.Combine(appDataDir, "media-downloader.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Repositories
builder.Services.AddScoped<ITitleRepository, TitleRepository>();
builder.Services.AddScoped<IMediaItemRepository, MediaItemRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IProgressRepository, ProgressRepository>();

// HTTP clients
builder.Services.AddApiHttpClients();

// API clients
builder.Services.AddScoped<TmdbClient>();
builder.Services.AddScoped<TorrentioClient>();
builder.Services.AddScoped<RealDebridClient>();
builder.Services.AddScoped<MpcClient>();

// Services
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddScoped<MediaOrganizer>();
builder.Services.AddScoped<FileDownloadService>();
builder.Services.AddSingleton<LibraryManager>();
builder.Services.AddSingleton<ProgressService>();
builder.Services.AddSingleton<JobProcessorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobProcessorService>());
builder.Services.AddHostedService<WatchTrackerService>();

// Controllers
builder.Services.AddControllers();

// Memory cache (for search results)
builder.Services.AddMemoryCache();

var app = builder.Build();

// Auto-create/migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Index library on first startup
try
{
    var libraryManager = app.Services.GetRequiredService<LibraryManager>();
    await libraryManager.IndexAsync();
    Log.Information("Library indexed on startup");
}
catch (Exception ex)
{
    Log.Warning(ex, "Library indexing failed on startup (non-fatal)");
}

// Error handling middleware (before controllers)
app.UseMiddleware<MediaDownloader.Api.Middleware.ErrorHandlingMiddleware>();

// WebSocket support
app.UseWebSockets();

// Static files (SPA)
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// SPA fallback — serve index.html for non-API, non-file routes
app.MapFallbackToFile("index.html");

var port = builder.Configuration["PORT"] ?? "8000";
var host = builder.Configuration["HOST"] ?? "0.0.0.0";
app.Urls.Add($"http://{host}:{port}");

Log.Information("Media Downloader API v{Version} starting on http://{Host}:{Port}", UpdateService.Version, host, port);
app.Run();
