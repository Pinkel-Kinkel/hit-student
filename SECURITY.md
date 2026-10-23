# Security

HIT Student runs entirely on the student's own PC. There is no server, account or telemetry of our own.

## What was checked (v2.0.0)

| Area | How it is protected | Verified by |
|---|---|---|
| Network exposure | The app opens no listening TCP port. The only UDP sockets are the embedded browser's outgoing connections. | `Get-NetTCPConnection` on the app and all its WebView2 processes: 0 listeners |
| App page isolation | The UI is served from `web\` only (virtual host `app.local`). It can't read `browser-data`, `settings.json` or anything else, and can't navigate away; a strict Content-Security-Policy allows only its own scripts. | Self test: `/../settings.json`, `%2e%2e`, `browser-data/...` all blocked |
| Messages | Messages are accepted only from the app page (`https://app.local/`), and page-script results only from `*.hit.ac.il` / `*.zoom.us`. | Code review |
| Downloads | Moodle files are fetched only from `https://md.hit.ac.il`; a redirect elsewhere is dropped. Recording links must be `https://*.zoom.us/rec/...`. File names are sanitised (no `..`, `\`, `/`, reserved names). | Self test (URL and file-name checks) |
| Opening files | Only files inside the app's folders, and only document/media types. An `.exe`, `.bat` or script is shown in Explorer instead of run. | Code review |
| AI tools | Claude may read and write in the course folder and run Python (the study skill needs it). Web access, `curl`, `wget`, PowerShell, `cmd`, `rm` and `del` are blocked, and the prompt tells it to treat file contents as material, never as instructions. | Self test: asked to `curl` a site, Claude was refused |
| Secrets | A Gemini API key is stored encrypted with Windows DPAPI (current user only). Zoom cookies for yt-dlp go to a one-off file that is deleted right after; yt-dlp runs with `--no-exec`. | Self test (DPAPI round trip) |
| Updates | The app only checks GitHub for a newer release and shows a link. It never downloads or runs code by itself. | Code review |

## What it can't protect against

- Anyone who can use your Windows account can use your HIT sign-in from `browser-data`. Lock your PC.
- The executable isn't code-signed, so Windows SmartScreen warns on first run.
- The AI is allowed to run Python for the study skill. Claude Code's own safeguards and the instructions above limit this, but a crafted document could still try to influence it. Only add course material from sources you trust.

## Reporting

Open an issue on GitHub (without personal data), or contact the maintainer privately.
