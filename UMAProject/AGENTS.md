# Project Support Baseline

- Support Unity 6.3 and newer only.
- Unity APIs available in Unity 6.3 may be used directly without compatibility guards for older Unity versions.
- Never use `GetInstanceID()`. It is not supported in later Unity versions; use a stable asset, object, or session identifier appropriate to the feature instead.
- Every optional UMA plugin must distribute Examples and Tests as separate companion packages, not inside its main package. List companions directly beneath their parent plugin, indented, in the installer UI.
- Always develop and validate against the current checkout's UMA version. Verify the installed UMASettings asset and Unity version before tests, and record the tested project/version. Do not treat a fallback version string or an unsynchronized isolated test project as the installed version; prefer the current main project when Unity is closed.
- Before editing any Unity asset as text, positively validate that the file is Unity YAML (including a valid `%YAML` header). Never use text decoding or text replacement on binary/native Unity assets; preserve them byte-for-byte or modify them through supported Unity serialization APIs.
- Battle buddies: vertices at the same position are "battle buddies" (the copies along the edge of a UV island, and the copies where two slots connect). Whenever normals are recalculated, battle buddies are always treated as a single vertex and end with one shared normal. Never use `Mesh.RecalculateNormals()` for slot or character normals, because it gives each copy its own normal and the seam shows; use `UMABuddyNormals`. The one exception (maintainer, 2026-11-24) is an edge marked sharp: buddies with different side ids (`UMABuddyNormals.Sides`, kept on a slot as `SlotDataAsset.normalSides`) each keep the normal of their own side, but they still stay at one position and move and track as one. Pass a slot's side ids wherever its normals are recalculated.
- "Make it so, Number 1" - means "do everything you suggested". Answer with "Aye Captain".
- Never commit or push. The maintainer commits all changes; do not offer to commit either.
- When a task is finished, end the final message with a line that says "Task complete". If work is still pending or waiting on the maintainer, say what is pending instead.
