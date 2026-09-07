<p align="center">
  <img src="src/CoHAnalytics/Assets/Images/Header/logo-hero.png" alt="CoH Analytics" width="360" />
</p>

# CoH Analytics

CoH Analytics is a Windows desktop companion for **City of Heroes: Homecoming**. It helps you follow live play, rewards, characters, builds, and reference data from local Homecoming installs and logs.

This repository is an **initial beta**. Features and packaging will continue to evolve. Treat releases as early-access software.

## Why I Built This

I’m an old City of Heroes player from Freedom back on Live. I started playing when the game released and stayed with it right up until sunset.

Like a lot of people who played that long, there were always things I wished the game did a little better outside the game itself. Better tracking. Better visibility into what a character was doing. A way to look back at rewards, builds, badges, and everything else without trying to keep half of it in my head or spread across a pile of different tools.

Back then I didn’t really have the time to build it, and honestly I didn’t have the skillset I have now.

Now I do.

So I built CoH Analytics.

Originally, I built it for myself. It does a lot of the things I wanted when I was playing on Freedom twenty years ago, along with quite a few things I didn’t know I wanted until I started building it.

At some point it became pretty obvious that if I wanted this stuff, other players probably would too. So rather than keep it sitting on my machine, I’m putting it out here for everybody.

This is the initial beta. I’ve tested the hell out of it, but I’m also one person with a handful of characters and machines. The point of the beta is to get it into the hands of people who are going to use it in ways I haven’t thought of yet and find the bugs I haven’t managed to find myself.

CoH Analytics is a Windows desktop companion for **City of Heroes: Homecoming**. It watches the local information Homecoming already produces and uses it to give you a better picture of your characters, sessions, rewards, builds, badges, and reference data while you play.

## Capabilities

At a high level, the current beta includes:

- Live session tracking and monitoring status
- Rewards and gameplay activity tracking from chat/log sources
- Character identity, accounts, and build-file integration
- Badge and accolade acquisition views, including sync from character builds
- Reference data browsing for enhancements, recipes, badges, and related catalog content
- Diagnostics and troubleshooting support for monitoring and local environment issues

## Local operation

CoH Analytics does its normal game tracking locally. It reads the Homecoming files and logs already on your computer; you do not need to create an online account or send your game data to a service just to use it.

**Important:** Homecoming chat logging must be enabled for CoH Analytics live tracking to work. In Homecoming, go to **Options → Windows → Log Chat** and set it to **Enabled** before using live tracking.

The app does contain a few optional external links for things like GitHub, bug reports, and support, but those are separate from the normal tracking and analysis features.

## Running a release

Release builds are published on the [CoH Analytics Releases](https://github.com/WITSLLC/CoH-Analytics/releases) page.

For most people, use the **Windows Installer**.

### Windows Installer — Recommended

This is the normal installable version of CoH Analytics and is the best choice for most users.

1. Open the [CoH Analytics Releases](https://github.com/WITSLLC/CoH-Analytics/releases) page.
2. Download the latest Windows x64 installer.
3. Run the installer.
4. Follow the installation prompts.
5. Launch CoH Analytics after installation.

The installer includes the required .NET runtime, so you do not need to install .NET separately. You also do **not** need the .NET 10 SDK.

### Portable Self-contained ZIP

Use this version if you do not want to install CoH Analytics and would rather keep it in a folder of your choice.

1. Open the [CoH Analytics Releases](https://github.com/WITSLLC/CoH-Analytics/releases) page.
2. Download the latest Portable Self-contained Windows x64 ZIP.
3. Create a folder for CoH Analytics. For example:

```text
C:\CoHAnalytics
```

4. Extract the ZIP into that folder.
5. Run:

```text
C:\CoHAnalytics\CoHAnalytics.exe
```

You can also launch it from PowerShell:

```powershell
cd C:\CoHAnalytics
.\CoHAnalytics.exe
```

This package includes the required .NET runtime, so no separate .NET installation is required. You do **not** need the .NET 10 SDK.

### Portable Framework-dependent ZIP — Advanced

This is the smallest download and is intended for users who already have, or prefer to install, the required .NET runtime separately.

If you don’t already know why you would want this version, use the Windows Installer instead.

1. Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) for Windows x64 if it is not already installed.

   Make sure you install the **Desktop Runtime**, not the SDK and not the console-only runtime.

2. Open the [CoH Analytics Releases](https://github.com/WITSLLC/CoH-Analytics/releases) page.
3. Download the latest Portable Framework-dependent Windows x64 ZIP.
4. Create a folder for CoH Analytics. For example:

```text
C:\CoHAnalytics
```

5. Extract the ZIP into that folder.
6. Run:

```text
C:\CoHAnalytics\CoHAnalytics.exe
```

Or from PowerShell:

```powershell
cd C:\CoHAnalytics
.\CoHAnalytics.exe
```

All three packages require Windows x64 and a local City of Heroes: Homecoming installation.

CoH Analytics uses your local Homecoming installation for live game tracking and for artwork such as badge and enhancement images. Those images are read directly from your game files rather than being included with CoH Analytics.

Only people building CoH Analytics from source need the **.NET 10 SDK**.

## Building from source

Building from source requires the **.NET 10 SDK**. End users who only run a packaged release do **not** need the SDK.

These steps assume you are starting with no existing checkout and use this example source root:

```text
C:\CoHAnalyticsSource
```

### Create the source folder

```powershell
mkdir C:\CoHAnalyticsSource
cd C:\CoHAnalyticsSource
```

### Clone the repository

```powershell
git clone https://github.com/WITSLLC/CoH-Analytics.git
```

That creates:

```text
C:\CoHAnalyticsSource\CoH-Analytics
```

Then move into the repository root:

```powershell
cd C:\CoHAnalyticsSource\CoH-Analytics
```

### Restore

From `C:\CoHAnalyticsSource\CoH-Analytics`:

```powershell
dotnet restore src/CoHAnalytics.slnx
```

### Build

Still from `C:\CoHAnalyticsSource\CoH-Analytics`:

```powershell
dotnet build src/CoHAnalytics.slnx -c Release
```

### Where the built application goes

After a successful Release build, the application output is here:

```text
C:\CoHAnalyticsSource\CoH-Analytics\src\CoHAnalytics\bin\Release\net10.0-windows
```

`CoHAnalytics.exe` is in that folder.

### Generated reference database

The build automatically creates:

```text
C:\CoHAnalyticsSource\CoH-Analytics\src\CoHAnalytics\bin\Release\net10.0-windows\ReferenceData\reference.db
```

You do not need to create or download `reference.db` separately for a normal source build.

The maintained catalog inputs live under:

```text
src/CoHAnalytics/ReferenceData/
```

Homecoming client data is the authority used by the project's maintenance/import tooling when canonical reference information needs to be regenerated or updated.

### Run the source-built application

In PowerShell:

```powershell
cd C:\CoHAnalyticsSource\CoH-Analytics\src\CoHAnalytics\bin\Release\net10.0-windows
.\CoHAnalytics.exe
```

Or open that same folder in File Explorer and double-click `CoHAnalytics.exe`.

### Updating an existing checkout

If you already cloned the repository earlier:

```powershell
cd C:\CoHAnalyticsSource\CoH-Analytics
git switch main
git pull --ff-only origin main
dotnet restore src/CoHAnalytics.slnx
dotnet build src/CoHAnalytics.slnx -c Release
```

The updated executable is again at:

```text
C:\CoHAnalyticsSource\CoH-Analytics\src\CoHAnalytics\bin\Release\net10.0-windows\CoHAnalytics.exe
```

### Running the tests

CoH Analytics includes an automated test suite that checks the application’s core behavior without requiring you to click through every part of the UI manually.

The tests cover things such as:

- log parsing and session tracking
- rewards and badge/accolade behavior
- character/build resolution
- reference-data generation and lookup
- replay fixtures and parser edge cases
- view-model and workspace behavior
- privacy/sanitization checks
- regression tests for previously fixed bugs

If you change the source code, running the test suite is the quickest way to make sure you didn’t break something somewhere else. Ordinary users do **not** need to run the tests just to use CoH Analytics.

From the repository root (`C:\CoHAnalyticsSource\CoH-Analytics`):

```powershell
dotnet test src/CoHAnalytics.Tests/CoHAnalytics.Tests.csproj -c Release --settings tests/CoHAnalytics.runsettings
```

A normal successful run should finish with all default tests passing.

The default runsettings leave out two opt-in categories that are **not** required to build CoH Analytics, run CoH Analytics, pass the normal public suite, or contribute ordinary code changes:

- `LiveInstall` — these tests interact with or validate against a real configured local Homecoming installation. They are for environment-specific checks on a machine that already has Homecoming set up for that purpose.
- `PrivateResearch` — these tests depend on development/research fixtures that are intentionally not included in the public repository.

## Reference data

CoH Analytics includes the reference data it needs for things such as badges, enhancements, recipes, accolades, and related catalog information.

If you are using a packaged release, you do **not** need to build, download, or configure the reference database yourself. It is included with the application.

Developers building from source do not need to create it manually either; the normal build process generates the runtime reference database automatically from the maintained catalog data in the repository.

## Releases

Beta builds are published on the [GitHub Releases](https://github.com/WITSLLC/CoH-Analytics/releases) page.

If you are not sure which package to download, use the **Windows Installer**.

For the differences between the Installer, Portable Self-contained ZIP, and Portable Framework-dependent ZIP—and step-by-step instructions for each—see [Running a release](#running-a-release).

## Bug reports

Use the issue tracker:

https://github.com/WITSLLC/CoH-Analytics/issues/new

Please include:

- Application version
- A short reproduction description
- Diagnostic export when relevant

Do **not** paste private chat logs, account credentials, or other personal play data into public issues.

## License

Source is published under a custom **source-available** license. See [`LICENSE`](LICENSE) for the complete terms.

Personal use, evaluation, personal modification, and free redistribution of complete, unmodified official releases are permitted under the license. Modified redistribution, commercial use, rebranding, and alteration of the app's support functionality require permission or are otherwise restricted by the license.

Third-party components remain under their own licenses. See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Disclaimer

CoH Analytics is an independent fan companion tool. It is **not affiliated with, endorsed by, or sponsored by** Homecoming, NCSoft, or the City of Heroes rights holders. City of Heroes and related names and marks belong to their respective owners.
