# Linux / Wine Installation

CoH Analytics is a **Windows application**. It does not currently ship a native Linux executable or Linux package.

The self-contained Windows build has been successfully tested under Wine on Linux, including live monitoring of City of Heroes: Homecoming. Treat this as **experimental / community compatibility**, not official native Linux support.

Wine itself is installed from your Linux distribution or [WineHQ](https://www.winehq.org/). CoH Analytics does not bundle or redistribute Wine.

## Recommended package

Use the portable self-contained ZIP from [GitHub Releases](https://github.com/WITSLLC/CoH-Analytics/releases):

```text
CoH-Analytics-<version>-win-x64-self-contained.zip
```

Why this package:

- includes the required .NET desktop runtime
- avoids installing a separate Windows .NET runtime inside Wine
- stays portable (extract and run)
- matches the path validated for Linux / Wine

The framework-dependent ZIP may work if you already manage a .NET Desktop Runtime inside Wine, but it is not the recommended Linux path.

The Windows MSI installer was not the validated Wine installation path for CoH Analytics and is not recommended as the primary Linux / Wine option.

## Successfully tested configuration

The following configuration was validated end-to-end:

| Item | Validated value |
|------|-----------------|
| Linux | Ubuntu 24.04.5 LTS |
| Kernel | 7.0.0-31-generic |
| Wine | Wine 11 (`wine-11.0`) |
| CoH Analytics | 0.1.3 Beta, win-x64 self-contained |
| Homecoming | current Homecoming Launcher / client |
| Architecture | x86-64 |

This was not only an application-launch smoke test. The full chain worked:

```text
Homecoming under Wine
    ↓
Homecoming chat log
    ↓
CoH Analytics monitoring
    ↓
parser worker
    ↓
character / account resolution
    ↓
live analytics
```

Validated behaviors included:

- CoH Analytics launches under Wine
- Dashboard and normal navigation work
- Reference data works; recipes and badges render
- Settings and workspace navigation work
- Homecoming Launcher installs and launches under Wine
- Homecoming itself runs under Wine
- CoH Analytics detects the Homecoming installation and running client
- Homecoming account / log directories are detected
- Live log monitoring works
- Character / account identity resolution works
- Live Session tracking works
- XP, Influence, damage / DPS, and recipe / drop tracking work
- Homecoming stop / relaunch detection works through the normal Wine environment

Older Wine versions may work, but they have not been validated for this guide.

## Wine compatibility note (0.1.3 Beta and later)

CoH Analytics **0.1.3 Beta** and later include a Wine compatibility improvement for log-source continuity.

Wine may expose file creation metadata differently from native Windows while Homecoming appends to its chat log. CoH Analytics accounts for that behavior so normal log growth is not mistaken for repeated log replacement.

You do not need to patch Wine or change filesystem settings for this.

## Install Wine

Install a current Wine build from your distribution or WineHQ. Prefer a current WineHQ stable build or the current distribution-supported Wine package for your release.

Ubuntu / Debian family users typically install Wine through their package manager or the WineHQ repository for their release. Exact package names and repository steps vary by distro version; follow the current documentation for your distribution or [WineHQ](https://www.winehq.org/).

Verify:

```bash
wine --version
```

Validated during testing:

```text
wine-11.0
```

## Use one Wine prefix for both apps

Homecoming and CoH Analytics should run under the **same Wine prefix** so CoH Analytics can see the Homecoming process, installation, account directories, and logs through normal Windows paths.

The validated test used the default prefix:

```text
~/.wine
```

A custom prefix is optional. If you use one, export it for both Homecoming and CoH Analytics:

```bash
export WINEPREFIX="$HOME/.wine-coh"
```

Use that same `WINEPREFIX` every time you launch either application.

## Install Homecoming

Use the official Windows Homecoming Launcher / installer.

1. Download the Windows Homecoming installer / launcher from the official Homecoming site.
2. Run it with Wine, for example:

```bash
wine hcinstall.exe
```

3. Allow the launcher to download and install the game normally.
4. Launch Homecoming through the launcher.

The validated install location was:

```text
C:\Games\Homecoming
```

Tequila is not the recommended path for this guide.

## Install CoH Analytics

1. Download the self-contained win-x64 ZIP for your release.
2. Extract it:

```bash
mkdir -p "$HOME/coh-analytics"
unzip CoH-Analytics-0.1.3-beta-win-x64-self-contained.zip -d "$HOME/coh-analytics"
```

Replace the ZIP filename with the version you downloaded.

3. Launch:

```bash
wine "$HOME/coh-analytics/CoHAnalytics.exe"
```

Optional quieter Wine logging:

```bash
WINEDEBUG=-all wine "$HOME/coh-analytics/CoHAnalytics.exe"
```

`WINEDEBUG=-all` is optional, not required.

## Enable Homecoming chat logging

Live tracking depends on Homecoming chat logging.

This setting is **per character**:

```text
Options → Windows → Log Chat → Enabled
```

If chat logging is disabled, CoH Analytics may correctly show:

```text
Game Status: Online
App Status: Waiting
```

because the client process exists, but no active chat log is growing.

This is not Wine-specific. The same requirement applies on native Windows.

## What success looks like

When everything is working:

```text
Game Status: Online
App Status: Monitoring
```

Live Session should resolve:

- Account
- Character
- Status: Confirmed

Live metrics should begin changing as gameplay generates activity.

## Known Wine / Linux notes

### Window close control

In the tested XFCE / VNC environment, the normal title-bar close control for CoH Analytics was not reliably exposed. The application still exited normally through **File → Exit**.

Treat this as a window-manager / remote-session observation, not a confirmed universal Wine defect.

### Homecoming mouse capture under VNC

During testing through VNC, Homecoming right-mouse camera control could behave incorrectly and cause continuous camera rotation. That was attributable to VNC relative-mouse handling, not CoH Analytics.

This should not be treated as a confirmed native Linux / Wine gameplay defect. Behavior on a physical Linux desktop still needs independent validation.

### Hardware acceleration

If Homecoming performs extremely poorly, check whether Linux is using Mesa **llvmpipe** (CPU software rendering):

```bash
glxinfo -B
```

Bad example:

```text
Device: llvmpipe
Accelerated: no
```

Good example from the validation environment:

```text
Device: D3D12 (NVIDIA GeForce RTX 4060 Ti)
Accelerated: yes
```

Virtual machines need working 3D acceleration from the virtualization platform. Software-only OpenGL is not suitable for playing Homecoming.

## Diagnostics

Wine users can create the normal CoH Analytics diagnostics report:

```text
Help → Create Diagnostics Report
```

If live monitoring fails, attach that sanitized diagnostics ZIP when filing a GitHub issue. Diagnostics export worked under Wine during validation.

## Troubleshooting

### App does not launch

- Confirm Wine is installed and current (`wine --version`)
- Use the **self-contained** win-x64 ZIP
- If the app crashes immediately during startup / font rendering, try the [font replacement workaround](#app-crashes-immediately-during-startup--font-rendering)

### App crashes immediately during startup / font rendering

This was the main application-launch compatibility problem in the validated Wine 11 environment. Before font substitution, WPF could terminate during font fallback / shaping.

Only apply these replacements if your Wine environment already fails to launch the app. They are not required preemptively.

```bash
wine reg add "HKCU\Software\Wine\Fonts\Replacements" /v "Segoe UI" /t REG_SZ /d "Liberation Sans" /f
wine reg add "HKCU\Software\Wine\Fonts\Replacements" /v "Segoe UI Symbol" /t REG_SZ /d "DejaVu Sans" /f
wine reg add "HKCU\Software\Wine\Fonts\Replacements" /v "Ebrima" /t REG_SZ /d "Liberation Sans" /f
wineserver -k
```

The matching Linux fonts must exist on the system. Typical font families:

- Liberation Sans
- DejaVu Sans

Do not redistribute Microsoft fonts.

### Homecoming is detected, but App Status remains Waiting

- Enable **Log Chat** for that character
- Confirm Homecoming and CoH Analytics use the same Wine prefix
- Generate some gameplay / chat-log activity

### Homecoming installation is not detected

- Confirm both applications share the same Wine prefix
- The validated install location was `C:\Games\Homecoming`

### Extremely low Homecoming FPS

Run:

```bash
glxinfo -B
```

If the device is `llvmpipe` / `Accelerated: no`, fix GPU / OpenGL acceleration for your Linux install before expecting playable Homecoming performance.

### Still failing

1. Create **Help → Create Diagnostics Report**
2. Open a GitHub issue
3. Attach the sanitized diagnostics ZIP
4. Include your Linux distribution, Wine version (`wine --version`), and CoH Analytics package version

## Native Linux clarification

CoH Analytics does not currently ship a native Linux executable or Linux package. The Linux-compatible path uses the normal Windows self-contained release under Wine.
