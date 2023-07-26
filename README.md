# About

`KitX Dashboard` is a desktop client for KitX project.

# Architecture

Based on `.NET 6` platform.

Using `Avalonia UI` as UI framework.

# Build

Make sure you have `dotnet 6` sdk installed on your maching and added to `PATH` environment variable first.

Then,

```shell
cd 'KitX Dashboard'
dotnet build
```

this will only build the project.

output is in `./bin/Debug/net6.0/` folder.

or

```shell
dotnet run
```

this will build and run in current folder.

suggest using `dotnet run --project ../../..` command in `KitX Dashboard/bin/Debug/net6.0` folder.

# Dependencies

`KitX Dashboard` rely on a lot of other projects.

You need build `KitX Dashboard` in `KitX` main repo, lots of dependencies are imported as submodules.

```shell
git clone git@github.com:Crequency/KitX.git
cd KitX
git submodule init
ToolKits/start.sh dashboard
ToolKits/start.sh reference
```

Local dependencies are located at `KitX/Reference/` folder.

Remote dependencies will be downloaded while running `dotnet build` or `dotnet run`

You can execute `dotnet restore` to restore remote dependencies (NuGet packages).


