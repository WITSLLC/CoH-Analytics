# Third-Party Notices

This document lists third-party components and related notices for the CoH Analytics public source tree.

Third-party components remain under their own licenses. Nothing in the CoH Analytics license replaces those terms.

## Direct NuGet dependencies (application)

| Component | Version (as referenced) | License family | Identity |
|-----------|-------------------------|----------------|----------|
| CommunityToolkit.Mvvm | 8.4.2 | MIT | [NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm) |
| Microsoft.Data.Sqlite.Core | 10.0.10 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core) |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | Apache-2.0 (package); native SQLite under its own terms | [NuGet](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3) |
| SharpVectors.Wpf | 1.8.5 | BSD-3-Clause | [NuGet](https://www.nuget.org/packages/SharpVectors.Wpf) |

## Direct NuGet dependencies (tests)

| Component | Version (as referenced) | License family | Identity |
|-----------|-------------------------|----------------|----------|
| xunit | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit) |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio) |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk) |
| coverlet.collector | 6.0.4 | MIT | [NuGet](https://www.nuget.org/packages/coverlet.collector) |

Test tooling is used to build and verify the project. It is not required in end-user application packages.

## Transitive dependencies

NuGet packages may pull additional transitive dependencies. Consult restored package license metadata (`dotnet restore` / package license expressions) for a complete inventory when preparing a formal redistribution package.

## City of Heroes / Homecoming

- **City of Heroes** and related names, marks, and game content belong to their respective rights holders.
- **Homecoming** is an independent City of Heroes server project/community and is not affiliated with CoH Analytics.
- CoH Analytics is an independent companion tool. It is not endorsed by Homecoming or the City of Heroes rights holders.

## Homecoming-derived reference data

CoH Analytics maintains authored catalog inputs and generates a runtime SQLite catalog (`reference.db`) used by the application.

Maintenance tooling can import or promote facts derived from a local Homecoming client installation. The exact redistribution status of Homecoming-derived reference data is **not asserted** by this notice beyond: the data is provided for use with CoH Analytics; game content and names remain owned by their respective rights holders. Do not treat the catalog as a grant of rights to extract or republish game assets independently.

## First-party production assets

Application icons, UI artwork, the character-icon bundle (`character-icons.cohicons`), and related CoH Analytics chrome shipped under `src/CoHAnalytics/Assets/` are treated as first-party project assets unless a specific file states otherwise.

Optional support/donation imagery (for example PayPal QR artwork) is included for product support links and remains subject to the terms of the payment provider and the CoH Analytics license.

PayPal and the PayPal marks are trademarks of PayPal, Inc. The PayPal monogram used for the optional Support the App link is sourced from PayPal's official media resources and remains the property of its respective owner. Use of those marks does not imply that PayPal endorses or sponsors CoH Analytics.
