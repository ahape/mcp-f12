# FindDef Roslyn Helper

Portable Roslyn tooling that resolves symbol definitions using the semantic model. Designed to be wrapped by an MCP command (e.g., `go-to-definition`) so agents can ask, “I am at file X, line Y, column Z—where is the definition?”

## Contents

- `FindDef.csx` – thin `dotnet-script` wrapper that forwards commands to the compiled tool (handy for MCP agents already wired to run scripts).
- `FindDefTool/` – .NET 8 console app that performs the actual Roslyn lookup with exact (`-pos`) and fuzzy (`-name`) modes.
- `README.md` – you are here; includes setup, usage, and MCP integration guidance.
- `TASK.md` – original assignment.

## Prerequisites

- .NET 8 SDK (or newer) – used to build/run `FindDefTool`
- [`dotnet-script`](https://github.com/dotnet-script/dotnet-script) if you prefer the script wrapper: `dotnet tool install -g dotnet-script`

> No additional NuGet packages are installed globally; the script references the allowed Roslyn/MSBuild assemblies.

## Usage

Run the compiled tool directly:

```ps1
dotnet run --project FindDefTool -- <SolutionPath> [-pos <FilePath> <Line> <Column> | -name <SymbolName>]
```

Or keep existing MCP workflows by using the wrapper script (which simply shells out to the console app):

```ps1
dotnet script FindDef.csx <SolutionPath> [-pos <FilePath> <Line> <Column> | -name <SymbolName>]
```

Mode details:
- `-pos` (exact) – provide the absolute/relative file that contains the call-site together with zero-based line & column of the identifier token. The tool resolves the precise overload and prints the definition location plus symbol display text.
- `-name` (fuzzy) – provide a symbol name; the tool enumerates every declaration along with the containing type and accessibility so agents can pick the right scope.

Example (fuzzy): `dotnet run --project FindDefTool -- ./DevProxy.sln -name LoadData`

Example (exact definition): `dotnet run --project FindDefTool -- ./DevProxy.sln -pos "DevProxy.Plugins/Mocking/MockRequestLoader.cs" 23 28`

## Testing With `dotnet/dev-proxy`

1. `git clone https://github.com/dotnet/dev-proxy.git`
2. `cd dev-proxy`
3. From this repository root, run the tool against the cloned solution:
   ```ps1
   dotnet run --project FindDefTool -- external/dev-proxy/DevProxy.sln -name LoadData
   ```
   You should see multiple contexts, including `DevProxy.Plugins.Mocking.MockRequestLoader.LoadData` at `DevProxy.Plugins/Mocking/MockRequestLoader.cs:23`.
4. Validate exact resolution (zero-based line/column) with:
   ```ps1
   dotnet run --project FindDefTool -- external/dev-proxy/DevProxy.sln -pos external/dev-proxy/DevProxy.Plugins/Mocking/MockRequestLoader.cs 23 28
   ```
   Output mirrors Visual Studio’s F12 experience:
   ```
   DEFINITION_FOUND:
   Symbol: void MockRequestLoader.LoadData(string fileContents)
   File: .../DevProxy.Plugins/Mocking/MockRequestLoader.cs
   Line: 23
   ```

## Wrapping as an MCP Command

Embed the following guidance inside your agent/system prompt so it always uses the script instead of guessing:

> I have provided a script `FindDef.csx` (backed by `FindDefTool`) to help you navigate code.
> - Scenario 1 (exact): `dotnet script FindDef.csx ./MySolution.sln -pos "/abs/path/File.cs" 45 12`
> - Scenario 2 (fuzzy): `dotnet script FindDef.csx ./MySolution.sln -name "MyPrivateMethod"`

Use the context (containing type, accessibility) or the resolved file path to select the correct symbol before editing code.

## Notes

- `FindDefTool` bootstraps MSBuild via `Microsoft.Build.Locator`, so as long as a recent .NET SDK or VS Build Tools is installed, Roslyn can parse the solution.
- The repo keeps the workflow agent-friendly: invoke the wrapper script if your MCP command expects a `.csx`, or call the console directly when embedding in other tooling.
