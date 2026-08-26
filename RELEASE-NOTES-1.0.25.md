# NetPulse Monitor 1.0.25

## Public-source cleanup

- Removes the unused .NET MAUI template robot image from the Android Companion and prevents unused image/raw template assets from being bundled through wildcard declarations.
- Removes unused iOS, MacCatalyst, Tizen and Windows MAUI template scaffolding from the Android-only Companion project.
- Removes development-only MAUI launch-profile and placeholder asset documentation files.
- Retains only the Android platform implementation and the production resources required by NetPulse.

## Verification

- Re-scanned the complete public source tree for development-tool attribution markers; none remain.
