using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Time;
using Daynote.Infrastructure.Mcp;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Profiles;
using Daynote.Infrastructure.Search;
using Daynote.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// The stdout stream carries the MCP JSON-RPC messages, so every log record must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Resolve the same per-user data root the desktop apps use (DaynoteAppOptions.ForCurrentUser), then
// the profile active in it (docs/PROFILES.md §8): the server reads and writes the notes the app shows.
// Resolve only, never migrate: the app owns the one-time migration, and two processes moving the same
// files would race. Until the app has run it the pointer resolves to the base root, which is where the
// legacy data still is. A profile switch while a client holds the server open takes effect the next
// time the client starts it.
string root = new ProfileStore(DaynoteDataRoot.Resolve()).ResolveActiveFolder();
string dbPath = Path.Combine(root, ProfileStore.DatabaseFileName);

// Say which database this is and whether the process has a package identity. Both are invisible
// otherwise, and both decide whether a client is talking to the notes the user can see: an MSIX
// build reaches the server through its app execution alias, and identity is what makes the app
// register that alias instead of a path under WindowsApps no client can traverse. stderr, because
// stdout carries the JSON-RPC messages.
Console.Error.WriteLine(
    $"daynote-mcp: database {dbPath}; packaged {McpServerCommand.IsPackaged()}; "
    + $"registered command {McpServerCommand.Current ?? "(none)"}");

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
