# About

`KitX Dashboard` is a desktop client for KitX Project.

# Architecture

Based on `.NET 8` platform.

Using [Avalonia UI](https://avaloniaui.net) as UI framework, using `Avalonia 11.0` .

# Clone

We strongly suggest you to develop `KitX Dashboard` in `KitX Project` folder structure, visit [dependencies](#dependencies) for more.

Instead of cloning almost total `KitX Project`, you can only clone `KitX Dashboard`.

```shell
git clone git@github.com:Crequency/KitX=Dashboard.git
# or
git clone https://github.com/Crequency/KitX-Dashboard.git
```

# Build

Make sure you have `dotnet 8` sdk installed on your machine firstly.

> Execute command `dotnet --list-sdks` to check if you have installed `dotnet 8` sdk.

Then,

```shell
cd 'KitX Dashboard/KitX Dashboard'

dotnet build
```

this will only build the project.

output is in `./bin/Debug/net8.0/` folder.

or

```shell
dotnet run
```

this will build and run in current folder.

suggest using `dotnet run --project ../../..` command in `KitX Dashboard/bin/Debug/net8.0` folder, in order to avoid workspace error.

# Dependencies

`KitX Dashboard` rely on a lot of other projects.

We suggest you to build `KitX Dashboard` in `KitX` main repo, lots of dependencies are imported as submodules.

```shell
# Fetch source code for KitX Project
git clone git@github.com:Crequency/KitX.git

cd KitX

# Init submodules
git submodule init

# Update submodules
# If you are developing on Windows OS, replace `start.sh` with `start.ps1`
# which requires powershell installed
# WARN: We're developing new `cheese` program to replace `start.sh` and `start.ps1`
./ToolKits/start.sh dashboard
./ToolKits/start.sh reference
```

Local dependencies are located at `KitX/Reference/` folder.

Remote dependencies will be downloaded while running `dotnet build` or `dotnet run`

You can execute `dotnet restore` to restore remote dependencies (NuGet packages).


