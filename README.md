# ChatGPT & Codex Account Switcher

An independent Windows launcher for switching between multiple native ChatGPT / Codex account profiles, with separate saved logins and shared local Codex chats and projects.

**[Download v5.1.0](https://github.com/harshjamdar/chatgpt-codex-account-switcher/releases/tag/v5.1.0)** · [Test report](Test-report.txt) · [Feature guide](FEATURES-V5.txt)

## Install and use

1. Download `OpenAIProfiles-Setup.exe` from Releases for a per-user install, or run the portable `OpenAIProfiles-V5.1.exe`.
2. Close older launcher windows. Fully quit your native ChatGPT/Codex app, including its tray icon and any Codex CLI or IDE sessions.
3. Open the launcher and click **+** to add a profile.
4. Choose the profile and sign in yourself on its first launch.
5. To switch, choose another profile. The launcher queues it until the current native sessions exit. **Request close & switch** sends a normal close request; a tray app may still require Quit.

Requires Windows, .NET Framework/WPF and a compatible installed native app. This is a launcher for your existing native installation, not a web wrapper. It does not bundle ChatGPT or Codex or provide a subscription. A changed native installation may require a compatibility review before launch.

## Features

- Add profiles without a fixed account limit; rename, remove and restore them.
- Separate native login homes and browser sessions for each profile.
- One active account through the launcher, with queued switching and cancellation.
- Shared local SQLite chat/project state, while preserving profile folders and rollout files.
- Search, favorites, colors, custom logos, reorder and stable desktop shortcuts.
- Optional expected-account/workspace guards and native-service identity checks.
- Launch progress, window detection, retry and compatibility checks.
- SQLite snapshots, validated restore and failure recovery.
- Tray access, keyboard shortcuts and privacy-filtered diagnostics.
- Per-user installer with versioned rollback after an upgrade.

## What sharing means

Profiles point to the same local SQLite store, normally your existing user `.codex` directory. Login homes and browser sessions remain separate. Local rollout files and attachments stay in their original folders, so **do not manually delete profile folders**. Removing a profile in the launcher hides it and retains its data.

**Cloud ChatGPT chats and cloud Projects remain account-bound.** This utility does not merge cloud accounts or change company permissions. Sharing local state makes that state available to the selected account; only use it where your organizations permit this. A conversation's visibility and continuation can still depend on the account/workspace.

Preexisting private history or a conflicting storage configuration blocks launch rather than silently hiding that data. Backups cover SQLite databases, not a complete archive of attachments, rollouts or saved credentials.

The single-account guard applies to this launcher. It cannot prevent someone starting another native session directly through Windows or another tool.

## v5.1.0 fixes and validation

**88 checks passed** across core regression, native integration, launcher UI and installer suites, plus final release checks. Fixes cover orphan-worker launch blocking, profile detection after native relaunch, stale identity captions, shortcut collisions, paginated history verification, helper timeouts and WAL-safe restore recovery.

Native signed-out test profiles opened in A → B → A order. The installed native backend read and resumed the same synthetic local chat across distinct test credential homes, including project-list pagination beyond 100 entries. Existing user app processes were retained.

Real company sign-in, SSO and company-specific conversation access still require manual verification. No company sign-in or real model request was made during these tests. See [the full test report](Test-report.txt) for scope and limits.

## Build from source

Run in Windows PowerShell from the repository directory:

```powershell
.\Build-Launcher.ps1
```

The script uses the Windows .NET Framework C# compiler and WPF assemblies. It produces `OpenAIProfiles-V5.exe` (file version 5.1.0.0) and `OpenAIProfiles-Setup.exe`. Release downloads name the portable binary `OpenAIProfiles-V5.1.exe`; its contents match the build output.

The `tests/` directory contains the test harness source used during development. Some tests require isolated database fixtures and the compatible installed native app; they are not a standalone one-command test suite. Never point destructive restore tests at a live store.

## Signing and updates

Release binaries are **unsigned**. A publisher certificate and HTTPS release host are needed for trusted update downloads. `Sign-Release.ps1` supports signing with your existing certificate; no certificate is bundled or installed. Downloads must pass hash and pinned-publisher checks before acceptance and are not executed automatically. The positive trusted-download path has not been tested without that infrastructure.

## Independence

This project is not affiliated with or endorsed by OpenAI. OpenAI, ChatGPT and Codex names and branding belong to their respective owners. Included branding assets originate from the installed native app and are not covered by a project license grant. Desktop profile startup integration includes internal behavior that may change in future app releases.
