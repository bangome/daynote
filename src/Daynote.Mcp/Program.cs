using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Time;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Daynote.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// The stdout stream carries the MCP JSON-RPC messages, so every log record must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Resolve the same per-user data root the desktop apps use (DaynoteAppOptions.ForCurrentUser).
string root = DaynoteDataRoot.Resolve();
string dbPath = Path.Combine(root, "daynote.db");

var database = new SqliteDatabase(new SqliteDatabaseOptions(dbPath));
database.Initialize();

builder.Services.AddSingleton(database);
builder.Services.AddSingleton<INoteRepository>(sp => new SqliteNoteRepository(sp.GetRequiredService<SqliteDatabase>()));
builder.Services.AddSingleton<ISearchRepository>(sp => new SqliteSearchRepository(sp.GetRequiredService<SqliteDatabase>()));
builder.Services.AddSingleton(sp => new SearchService(sp.GetRequiredService<ISearchRepository>()));
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name = "daynote",
            Version = "1.0.0",
        };
        options.ServerInstructions = "Read and write the user's local Daynote daily notes.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
