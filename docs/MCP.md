# Daynote MCP server

`Daynote.Mcp` is a headless **stdio [Model Context Protocol](https://modelcontextprotocol.io) server**
that exposes your local Daynote notes to MCP clients (Claude Desktop, Claude Code, and other MCP
hosts) with read **and** write tools. It reads the same database the app uses —
`%LocalAppData%\Daynote\daynote.db` — so notes created or edited through an AI assistant show up in
the Daynote app, and vice versa.

It is a separate console app (`src/Daynote.Mcp`) that **ships inside the Store package** as a second,
hidden entry point, so an ordinary user never builds anything. It uses the official
`ModelContextProtocol` .NET SDK (2.0.0) over stdio.

## Tools

| Tool | Parameters (required\*) | What it does |
|------|--------------------------|--------------|
| `search_notes` | `query`\*, `limit` (default 20) | Full-text search across your notes; returns date, id, title, snippet. |
| `get_notes_for_date` | `date`\* (`YYYY-MM-DD`) | Lists the notes on one day (id, title, body, favorite). |
| `list_recent_notes` | `limit` (default 20) | Most recent notes first (id, date, title, snippet, favorite). |
| `create_note` | `date`\*, `body`\*, `title` | Creates a note on a date; returns the new note id. |
| `update_note` | `noteId`\*, `body`, `title` | Updates a note's body and/or title (unspecified fields keep their value). |
| `delete_note` | `noteId`\* | Deletes a note. |

Dates are ISO `YYYY-MM-DD`. `noteId` is the GUID returned by `create_note` / `search_notes` /
`list_recent_notes`. Invalid input and save conflicts come back as tool errors, not crashes.

## Registering it (what a user does)

Open **Settings → AI integration (MCP)** and press **Claude Desktop에 등록 / Register with Claude
Desktop**. That adds one entry to `%AppData%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "daynote": {
      "command": "daynote-mcp.exe"
    }
  }
}
```

Restart Claude Desktop and "daynote" appears in the tools list. The write is additive — other MCP
servers and unrelated settings in that file are left alone — and a config the app cannot parse is
reported as a failure rather than overwritten.

For **Claude Code**, the same settings row shows a copyable one-liner:

```powershell
claude mcp add daynote -- "daynote-mcp.exe"
```

### Why the command is a bare `daynote-mcp.exe`

`daynote-mcp.exe` is the package's **app execution alias** (declared in `Package.appxmanifest`), which
Windows puts on the user's PATH. Launching through it is what makes the integration work at all:

- **It gives the server the package identity.** The Store package leaves MSIX file-system
  virtualization enabled, so the app's writes to `%LocalAppData%\Daynote` are redirected into
  `%LocalAppData%\Packages\<PackageFamilyName>\LocalCache\Local\Daynote`. A server started from
  *outside* the package gets no redirection, so it would open an empty second database and show none
  of the user's notes.
- **It is reachable.** The real executable lives under `%ProgramFiles%\WindowsApps`, whose ACLs a
  client process cannot traverse. The alias can be launched by anyone.

## Build (developers)

```powershell
# Framework-dependent (needs the .NET 10 runtime on the machine):
dotnet build src\Daynote.Mcp\Daynote.Mcp.csproj -c Release
#   -> src\Daynote.Mcp\bin\Release\net10.0-windows10.0.19041.0\Daynote.Mcp.exe

# Or a self-contained single folder (no runtime dependency):
dotnet publish src\Daynote.Mcp\Daynote.Mcp.csproj -c Release -r win-x64 --self-contained true -o dist\daynote-mcp
#   -> dist\daynote-mcp\Daynote.Mcp.exe
```

The MSIX build needs neither: `Daynote.Package.wapproj` references `Daynote.Mcp.csproj`, so packaging
publishes the server and lays it into the package as `Daynote.App\Daynote.Mcp.exe`.

### Why it sits in the app's folder

Both projects publish self-contained, and of the server's ~113 MB of output ~109 MB is byte-identical
to what is already in `Daynote.App\` - the same copy of the .NET runtime. Giving it its own folder
therefore doubled the download for nothing (131 MB vs 86 MB measured on the Store bundle), so the
`_DaynoteCoLocateMcpServer` target in the `.wapproj` keeps only the three files that are genuinely the
server's (`Daynote.Mcp.exe` and its `deps.json` / `runtimeconfig.json`) and drops the 239 duplicates.

Two apphosts sharing one folder is only safe while the folder holds exactly one build of each assembly
name, so two things enforce that:

- **`Daynote.App` has a `ProjectReference` on `Daynote.Mcp`.** Restore then resolves a single graph for
  the union of both, and the app's folder gets `Daynote.Mcp.dll` plus the MCP-only packages. Without
  that reference the two independent restores disagreed on 14 shared assemblies (for instance
  `System.Diagnostics.EventLog.dll`, 366 KB in the app's publish against 176 KB in the server's).
- **`Build-Package.ps1` re-checks the produced package.** It reads `Daynote.Mcp.deps.json` out of the
  package and fails the build unless every assembly it names is present in `Daynote.App/`, and unless
  the `Daynote.Mcp/` folder is gone. A missing assembly would otherwise show up as a server that dies
  on first use, long after release.

An unpackaged (dev) run of Daynote has no alias, so Settings offers registration only when a built
`Daynote.Mcp.exe` sits next to `Daynote.App.exe`; otherwise the row says there is nothing to register.
To point a client at a dev build by hand, give it the full path:

```json
{
  "mcpServers": {
    "daynote": {
      "command": "C:\\path\\to\\Daynote.Mcp.exe"
    }
  }
}
```

**Run via the SDK host instead of the apphost exe** (if launching the `.exe` is blocked by an endpoint
policy), point the client at `dotnet` with the DLL:

```json
{
  "mcpServers": {
    "daynote": {
      "command": "dotnet",
      "args": ["C:\\path\\to\\Daynote.Mcp.dll"]
    }
  }
}
```

## Notes

- **Data location** — defaults to `%LocalAppData%\Daynote`, which for the packaged server the OS
  redirects into the package's `LocalCache\Local\Daynote` — the same file the app uses. Set the
  `DAYNOTE_DATA_ROOT` environment
  variable (in the client's server config `env`) to point at a different root, e.g. a disposable test
  database.
- **Running alongside the app** — safe. SQLite (WAL mode) allows the MCP server to read while the app
  is open, and serializes writers, so concurrent edits don't corrupt data. A rare write that races the
  app is returned as a tool error; just retry.
- **stdio hygiene** — the server logs only to stderr; stdout carries the JSON-RPC stream, so never add
  `Console.WriteLine` to stdout in this project.
