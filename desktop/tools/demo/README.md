# The README animation

`docs/images/demo.webp` (and `demo-ru.webp`) show the phone and the PC side by
side, both waves moving to the same six seconds of made-up speech
(`desktop/src/PhoneMic/DemoVoice.cs`, copied in
`android/.../DemoAnimationTest.kt`). Rebuild them after changing either screen.

```bash
# 1. Phone frames, rendered on the JVM: android/app/build/demo/<lang>/
cd android && PHONEMIC_DEMO=1 ./gradlew testDebugUnitTest --tests "*DemoAnimation*"

# 2. PC frames: run the preview and capture it for a full loop
#    (PhoneMic.exe from a Debug build of desktop/src/PhoneMic)
PhoneMic.exe --preview --lang=en          # then, in PowerShell:
powershell -File desktop/tools/demo/capture-frames.ps1 -ProcessId <pid> -OutDir frames/pc-en

# 3. Put them together (needs Pillow: pip install pillow)
python desktop/tools/demo/make-demo.py android/app/build/demo/en frames/pc-en docs/images/demo.webp demo.gif
```

Repeat 2 and 3 with `--lang=ru` and `demo-ru.webp`. The GIF is for places that
do not take WebP, such as a Reddit post.
