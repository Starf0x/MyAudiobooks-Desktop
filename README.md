# MyAudiobooks-Desktop

The Windows desktop client for
[my-audiobook-collection](https://github.com/Starf0x/my-audiobook-collection).

One `.exe`, one process, one file on disk. The window, the connection to the
collection server and the mp3 decoder all live in that single program: no
WebView2, no browser engine, no loopback server, nothing that starts a second
process. It talks to the server directly over HTTPS with `HttpClient`, and the
layout and behaviour of the site are the specification — `style.css`, its HTML
and its JavaScript are the source of truth for how this looks.

## Why it exists

The original was Electron, and Electron costs four Windows processes and a whole
Chromium. The rewrite is in WPF, which is the only way to get that back down to
one. The risk in that trade is the player: Chromium plays mp3, m4a, flac, ogg
and opus, and WPF does not. What that costs was measured rather than guessed, and
the measurements are in [ONDERZOEK-AUDIO.md](ONDERZOEK-AUDIO.md).

## Requirements

- Windows 10 or 11, x64
- A collection server (the one above, or your own)
- **mp3 only.** The server stores whatever the files happen to be, but this app
  decodes mp3 and nothing else. A part that is still m4a or ogg gets a message
  saying what arrived and that it can be converted in the web interface. That is
  the visible consequence of having exactly one decoder; see
  `ONDERZOEK-AUDIO.md` for the measurements that set the line.

## Building

Needs the .NET 10 SDK.

```powershell
dotnet publish MyAudiobooks.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o uit
```

That writes one `uit\MyAudiobooks.exe` of about 70 MB. The size is the .NET
runtime and the native libraries inside the file; there is nothing next to it,
and nothing to install.

There is one check to run before publishing, and it is not optional:

```powershell
.\check-resources.ps1
```

It verifies that every brush, style and font the code asks for by name actually
exists in `App.xaml`. A resource that is missing does not fail the build — it
fails at runtime, in one spot, on one screen.

## Releases

The version lives in `MyAudiobooks.csproj` and ends up in the binary, so it can
be read back off the file rather than taken on trust:

```powershell
(Get-Item .\uit\MyAudiobooks.exe).VersionInfo.ProductVersion
```

Bump `Version`, `AssemblyVersion` and `FileVersion` together, then tag with the
same number. The release notes quote the SHA-256 of the exe, because "it just
crashed" is worth very little next to a hash someone can check.

`gh release create` takes the exe as an asset, so the artefact in the release is
the artefact that was built from that tag — not one someone remembered to
rebuild afterwards.

## Tests

There is no unit-test project, and that is a decision, not an oversight: there is
no window to read. Instead there are 11 scripts that drive the real program from
outside with UI Automation, watch what the test server was asked for, and check
whether anything was written to the error log. Each one says what it saw, so any
claim in the app can be checked without trusting whoever makes it.

```powershell
.\proefjes\alle.ps1                    # everything, with a verdict per script
.\proefjes\alle.ps1 -Alleen hervatten  # one script, or several
```

They start the app **muted and minimised**, so a run does not fill the room with
sound or cover the desktop. See [proefjes/LEES-MIJ.md](proefjes/LEES-MIJ.md) for
what each one measures, and for the four ways these scripts have lied to me.

The probes need Node and a small test server:

```powershell
npm install
```

## Two things worth knowing before reading the code

**Progress comes from the server, and only from the server.** The app does not
keep a local copy of where you were. It reads the server's `progress` on every
screen that shows a book, and writes back to `/api/progress` every five seconds,
on pause, and at the end of a part. The reason is in `ONDERZOEK-AUDIO.md`: the
app once kept both, and when the two disagreed it said nothing — which presents
as "resume does nothing". The cost is honest too: if the server is unreachable,
your progress is nowhere, and the app says so on screen.

**Nothing fails silently.** If a route is missing, if a file is not mp3, if the
server cannot store your place, if the window cannot be saved to disk — each of
those says so, in a sentence that says what arrived and what to do about it.
The rule this repo is built on is that anything which does nothing must say why,
because the alternative presents as a bug in the app.
