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
