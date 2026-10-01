# Contributing

Thanks for taking an interest. Bug reports and pull requests are both welcome.

## Reporting a problem

Open an issue using the bug report form. The single most useful thing to
attach is a piece of the PC log, `%APPDATA%\PhoneMic\log.txt` (the *Open log*
link in the window opens it). Every 10 seconds of streaming it records the
network jitter and the buffer level, so it usually shows whether a problem is
the network, the phone or the PC.

## Setting up

- **Phone app:** Android Studio, or JDK 21+ with the Android SDK. Open the
  `android/` folder, not the repository root.
- **PC app:** the .NET 10 SDK. `desktop/PhoneMic.sln` opens in Visual Studio or
  Rider, and everything also works from the command line.

```bash
cd android && ./gradlew assembleDebug test lintDebug
dotnet build desktop/PhoneMic.sln
dotnet test desktop/tests/PhoneMic.Core.Tests
```

CI runs exactly these on every push and pull request.

## Looking at the UI without a device

- **Phone:** `./gradlew recordRoborazziDebug` renders every state of the main
  screen on the JVM, in both languages, into `android/app/screenshots/`. Changing the design means
  re-recording them and committing the images with the code.
- **Launcher icon:** `./gradlew test` also draws it with Android 11's and
  Android 16's renderers, monochrome themed layer included, into
  `android/app/build/launcher-icon/`.
- **PC:** `PhoneMic.exe --preview` opens the window streaming a made-up phone,
  and `--preview=pairing` opens it waiting for one, each with the matching
  tray icon. The preview does no audio or networking and never saves settings,
  so it runs safely next to a real instance.

## Translations

Both apps are in English and Russian, and every piece of text exists in both.

- **Phone:** `android/app/src/main/res/values/strings.xml` is English, the
  default; `values-ru/strings.xml` is Russian. Lint fails the build on a string
  missing from either.
- **PC:** `desktop/src/PhoneMic/Text.cs` holds each string as an English and
  Russian pair on one line. Run with `--lang=en` or `--lang=ru` to see either
  regardless of the Windows language.

The screenshot tests render both languages, which is the quickest way to see
whether a longer translation overflows.

## Guidelines

- **Match the code around you.** Comments explain *why*, not *what*; names say
  what things are. The existing files are the style guide.
- **The protocol has two implementations.** A change to the wire format must
  land in `android/.../net/`, `desktop/src/PhoneMic.Core/Protocol.cs` and
  `docs/protocol.md` together, with the test vectors updated on both sides.
  Anything that breaks compatibility between versions needs a new protocol
  version number.
- **Buffer changes need numbers.** `JitterBufferSimulationTests` prints
  underruns and latency for each scenario; include the before and after in the
  pull request.
- **Check it end to end.** For anything touching audio or networking, run the
  tone test from the README over both Wi-Fi and USB, and say what `probe`
  reported.
- **Release builds are minified.** R8 has broken the QR scanner before; if you
  add a library that uses reflection, try the release build on a phone.

## Commit messages

A short summary line in the imperative ("Keep the scanner's registrars under
R8"), then a paragraph on why, if it is not obvious.
