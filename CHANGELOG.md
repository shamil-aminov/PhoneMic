# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.0] — 2026-09-28

First release.

### Added

- **The phone app.** One button that turns the microphone on and off, a level
  meter, the paired PC and how the link is doing (Wi-Fi or USB, round-trip
  time). Recording runs in a foreground service with wake and Wi-Fi locks, so
  it carries on with the screen off.
- **The PC app.** Shows the pairing QR code, receives the audio and plays it
  into VB-Audio Virtual Cable, where other programs see it as the microphone
  *CABLE Output*. Level meter, volume up to 400%, output device choice, start
  with Windows, and a tray icon that turns green while a phone is streaming.
- **One-time pairing by QR code**, scanned in the app or opened from any QR
  reader as a `phonemic://` link. The phone finds the PC again after its IP
  address changes by also broadcasting its hello.
- **USB without setup.** The PC runs `adb reverse` for every phone with USB
  debugging and repairs it if another tool restarts the adb server.
- **Switching on the fly.** Pulling the cable moves the stream to Wi-Fi at
  once; plugging it back in returns it to USB after a few seconds of the cable
  staying put. The microphone never stops in between.
- **Adaptive jitter buffer** that sizes itself from measured packet lateness
  and steers its fill level by skipping or stretching pauses in speech, with
  *Low latency*, *Balanced* and *Stable* modes.
- **Automatic reconnection** on either side after any dropout, and a clear
  message when Android refuses the microphone.
- **A wire protocol** documented in `docs/protocol.md`, with byte-exact test
  vectors checked by both the Kotlin and the C# implementation.
- **Tests**: protocol tests on both sides, and simulations of the jitter
  buffer against USB, bad Wi-Fi and clock drift on a virtual clock.
- **`probe`**, a tool that checks the whole chain without anyone speaking: the
  phone plays a test tone and `probe` analyses what comes out of the virtual
  cable.

[1.0.0]: https://github.com/shamil-aminov/PhoneMic/releases/tag/v1.0.0
