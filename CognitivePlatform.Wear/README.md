# Cognitive Platform Wear Capture

Native Kotlin/Wear Compose companion for the Galaxy Watch Classic.

## V1 boundary

- The watch requests its own microphone permission and asks the system recognizer for offline-preferred speech recognition.
- Only final recognized text is sent to `POST /api/conversation/converse`.
- Raw audio is never uploaded, stored, or forwarded to the phone.
- The watch does not execute actions locally; Cognitive Platform retains validation, confirmation, routing, and audit behavior.

## Configure and run

Open `CognitivePlatform.Wear` in Android Studio. Set `CP_WEAR_API_BASE_URL` in your user-level `gradle.properties` to the HTTPS address reachable from the watch, for example:

```properties
CP_WEAR_API_BASE_URL=https://cp.example.net/
```

The placeholder value intentionally cannot address a real server. Install the `app` module on the watch after configuring the URL. The app requires the watch-side microphone permission.

## Verification boundary

This workspace has Android platform SDK files but no Gradle distribution or Android Studio installation available to execute a Wear build. Open the module in Android Studio before considering it deployable; verify microphone permission, offline-recognition availability, HTTPS reachability, and an end-to-end text capture on the Galaxy Watch Classic.
