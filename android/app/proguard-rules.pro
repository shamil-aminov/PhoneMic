# ML Kit (the QR scanner) builds its services from registrars that are named in
# the manifest and created by reflection. R8's full mode, the AGP 9 default,
# keeps those class names but drops their no-argument constructors, so the
# registry comes up empty and the scanner crashes with a NullPointerException
# the moment it is created.
-keep class * implements com.google.firebase.components.ComponentRegistrar { <init>(); }
-keep class com.google.mlkit.** { *; }
-keep class com.google.android.gms.internal.mlkit_code_scanner.** { *; }
