# iOS native speaking-audio — deferred

Placeholder for the Capacitor iOS implementation of `@vni/speaking-audio`.

Required before this folder holds code:

- AVAudioSession category suitable for background capture
- Background Modes → Audio
- Interruption notifications distinguishing system interrupt from user pause
- Persist to app storage before upload (`audio/m4a`)

`[NEEDS VALIDATION]` Device testing is blocked — Xcode is not installed on the current engineering hosts.

See [ADR-0006](../../../docs/decisions/0006-speaking-audio-capture-native-plugin.md) and the package [README](../README.md).
