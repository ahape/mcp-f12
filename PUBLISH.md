Start by running this:

```sh
cd FindDef
dotnet pack -c Release
```
This will create a file named `Brightmetrics.Mcp.F12.1.0.0.nupkg` inside the `FindDef/nupkg` folder.

Before publishing to the world, test it on your own machine. You can install a tool from a specific folder (folder source) instead of NuGet.org.

### Install:

```sh
dotnet tool install --global --add-source ./nupkg Brightmetrics.Mcp.F12
```

### Run: You can now run the command anywhere in your terminal:

```sh
f12 --help
```
### Uninstall (when done testing):

```sh
dotnet tool uninstall -g Brightmetrics.Mcp.F12
```

To allow other developers to run `dotnet tool install -g Brightmetrics.Mcp.F12`, you need to push the package to NuGet.org.

```sh
dotnet nuget push ./nupkg/Brightmetrics.Mcp.F12.1.0.0.nupkg --api-key <YOUR_API_KEY> --source https://api.nuget.org/v3/index.json
```
