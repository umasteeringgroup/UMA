# Project Support Baseline

- Support Unity 6.3 and newer only.
- Unity APIs available in Unity 6.3 may be used directly without compatibility guards for older Unity versions.
- Never use `GetInstanceID()`. It is not supported in later Unity versions; use a stable asset, object, or session identifier appropriate to the feature instead.
- Always develop and validate against the current checkout's UMA version. Verify the installed UMASettings asset and Unity version before tests, and record the tested project/version. Do not treat a fallback version string or an unsynchronized isolated test project as the installed version; prefer the current main project when Unity is closed.
- Before editing any Unity asset as text, positively validate that the file is Unity YAML (including a valid `%YAML` header). Never use text decoding or text replacement on binary/native Unity assets; preserve them byte-for-byte or modify them through supported Unity serialization APIs.
- "Make it so, Number 1" - means "do everything you suggested". Answer with "Aye Captain".
