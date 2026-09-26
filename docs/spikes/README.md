# Spikes

A spike is a short, time-boxed investigation that answers one question before we build on the answer.
Each note gives the question, the answer, the evidence, and what still has to be confirmed on a device.

| Spike | Question | Answer | Device check |
|---|---|---|---|
| [S1](S1-quest-room-data.md) | Can Unity OpenXR: Meta trigger Space Setup and read room data, or do we need Meta's SDK? | Unity's package covers it | Pending (M1) |
| [S2](S2-quest-persistent-anchors.md) | Do anchors persist across app restarts and headset off/on on Quest? | API supported by Unity's package | Pending (M1) |
| [S3](S3-quest-boundaryless.md) | Can a passthrough app run without the boundary prompt? | Yes: Boundary Visibility feature | Pending (M1) |
| [S4](S4-editor-testing.md) | Can the full flow run in the editor without a headset? | Mostly: everything except native anchor persistence | Not needed |

Sources were read from the exact pinned versions: `com.unity.xr.meta-openxr` 2.3.2, `com.unity.xr.arfoundation` 6.3.5 and `com.unity.xr.interaction.toolkit` 3.3.2.
