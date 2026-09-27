# Releasing

A release is one tag. GitHub Actions builds, signs and publishes everything.

## One-time setup

The Android app has to be signed with the same key for every release, or
phones cannot update from one version to the next. Generate a keystore once —
or reuse an existing one — and keep it and its passwords out of the
repository. Losing it means users have to uninstall to update.

```powershell
& "C:\Program Files\Android\Android Studio\jbr\bin\keytool.exe" -genkeypair -v -keystore release.jks -keyalg RSA -keysize 2048 -validity 10000 -alias phonemic
```

Add four repository secrets on GitHub (Settings → Secrets and variables →
Actions):

| Secret | Value |
| --- | --- |
| `KEYSTORE_BASE64` | the keystore as base64: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("release.jks"))` in PowerShell, or `base64 -w0 release.jks` |
| `KEYSTORE_PASSWORD` | the store password |
| `KEY_ALIAS` | the key alias, e.g. `phonemic` |
| `KEY_PASSWORD` | the key password |

The names match the ones Alternate uses, so the same values work if both apps
are signed with one key.

For signed builds on your own machine, put the same values in
`android/keystore.properties` (git-ignored):

```properties
storeFile=C:/path/to/release.jks
storePassword=...
keyAlias=phonemic
keyPassword=...
```

Without it, a local `assembleRelease` is signed with the debug key: fine for
trying things out, never for publishing.

## Cutting a release

1. Bump `versionCode` and `versionName` in `android/app/build.gradle.kts`.
   The PC app takes its version from the tag.
2. Add a section for the version to `CHANGELOG.md`. The release notes are
   taken from it, and the workflow stops if the section is missing.
3. Commit, then tag and push:

```bash
git tag -a v1.0.0 -m "1.0.0"
git push origin v1.0.0
```

The tag runs `.github/workflows/release.yml`, which:

1. runs the tests on both sides;
2. builds the signed APK (the keystore exists on the runner only during that
   job);
3. builds two Windows executables: a standalone one with the .NET runtime
   bundled, and a 1 MB one that needs the .NET 10 Desktop Runtime;
4. publishes a GitHub release with the CHANGELOG section, a download guide,
   and `SHA256SUMS.txt`.

## What signing does not fix

- **Android** warns about apps from outside a store no matter how they are
  signed. The warning is about where the file came from.
- **Windows SmartScreen** warns about the executable until it is signed with a
  code-signing certificate and has built up a reputation. That costs money and
  is not set up.
- **Builds signed with different keys cannot update each other.** A phone that
  has a debug build must uninstall it before installing a release, and then
  pair again.
