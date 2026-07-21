# Privacy Policy

Last updated: July 2026 (Grafana crash reporting regards versions **after** v1.0.26061)

ADB Explorer is developed by Alex B. This policy describes what data the application handles and what may be sent over the internet.

## Summary

- ADB Explorer does **not** run background analytics or telemetry.
- The app stores settings and operational data **locally on your PC**.
- **Optional crash reporting** is available in some builds. A report is sent **only if you click "Send Report"** after a crash.
- Crash reports for published builds are processed by **Grafana Labs** (Grafana Cloud). See [Third-party services](#third-party-services) below.

## Data stored locally

ADB Explorer keeps data on your computer, including:

- Application settings (`settings.json` under your local AppData folder)
- Saved device connections, thumbnails, logs, and other files you create or that the app generates during normal use

This data stays on your device unless you copy it elsewhere or use features that contact other services (for example, checking GitHub for updates in non-Store builds).

## Optional crash reporting

### When it is available

Crash reporting is enabled only in builds that include a Grafana Faro collector configuration:

- **Microsoft Store** builds of ADB Explorer
- Some **release** builds distributed outside the Store when crash reporting was included at compile time

**Debug** builds used for development send reports only to a **local Grafana Alloy** instance on the developer's machine, not to Grafana Cloud.

Plain **Deploy** packages (GitHub Releases) that are not installed from the Microsoft Store do not include crash reporting.

### Your choice

Crash reporting is **opt-in per crash**:

1. After an unhandled exception, the app may show a crash dialog (if enabled in Settings).
2. You may click **Dismiss** — **no data is sent**.
3. You may click **Send Report** — a single diagnostic payload is sent for that crash.

There is no automatic or background crash upload.

### What a crash report contains

If you choose **Send Report**, the following diagnostic information is transmitted:

| Data | Purpose |
|------|---------|
| Exception type and message | Identify the failure |
| Stack trace (including source file names and line numbers when available) | Locate the bug |
| Inner exception details | Understand chained errors |
| App name and version | Know which build failed |
| Windows version, OS description, CPU architecture, .NET runtime version | Reproduce environment issues |
| A random session identifier (per app launch) | Group events from the same session |
| The in-app view/page active at crash time (for example, Settings, Explorer) | Know where the crash occurred |
| Whether the build is a Store or portable install | Distinguish distribution channels |

**What we do not intentionally collect:** account names, email addresses, advertising identifiers, contacts, photos, or the contents of files on your Android device or PC.

**Please note:** exception messages and stack traces can sometimes include **file paths** (which may contain your Windows user name) or other text that reflected what the app was doing at the time of the crash. Only send a report if you are comfortable sharing that diagnostic text.

### Where reports go

- **Store and configured release builds:** HTTPS to **Grafana Cloud** (operated by Grafana Labs).
- **Developer debug builds:** HTTP to `127.0.0.1` on the developer's machine only.

Reports are used solely to investigate and fix application defects.

### Retention

Crash data retained in Grafana Cloud is subject to [Grafana Labs' privacy policy](https://grafana.com/legal/privacy-policy/) and our Grafana Cloud account configuration (free tier - 14 days retention). We do not use crash reports for advertising or profiling.

## Third-party services

### Grafana Labs (crash reporting)

When you send a crash report from an applicable build, Grafana Labs acts as a **service provider** that receives and stores the diagnostic payload on our behalf.

- Grafana Labs: [https://grafana.com/](https://grafana.com/)
- Grafana privacy policy: [https://grafana.com/legal/privacy-policy/](https://grafana.com/legal/privacy-policy/)

Grafana Labs is also listed under **Attributions & Third Party Licenses** in the app's Settings.

### Microsoft Store

If you installed ADB Explorer from the Microsoft Store, Microsoft may collect installation, update, and usage data according to the [Microsoft Store terms](https://www.microsoft.com/store/b/terms-of-sale) and [Microsoft privacy statement](https://privacy.microsoft.com/). That collection is separate from optional crash reporting described above.

### Other network use

Depending on your settings and build, the app may also contact:

- **GitHub** — check for application updates and, in **Update** mode, download the new release archive to your app folder and install it locally (non-Store builds, when enabled)

These requests only fetch update information and files from GitHub; they do not send crash diagnostics or any personal data.

## Your choices

| Action | Effect |
|--------|--------|
| Click **Dismiss** on the crash dialog | No report is sent |
| Turn off **Show crash report dialog** in **Settings → About** | The crash dialog (and ability to send from the app) is hidden; the app still terminates on an unhandled exception |
| Set `"ShowMessageOnCrash": false` in `settings.json` | Same as disabling the setting above |

## Changes

We may update this policy when the application or its data practices change. The current version is always available in this repository and linked from the app (**Settings → About → Privacy Policy**).
