# 0011. Host whole apps as bare metal content

- **Status:** Accepted
- **Date:** 2026-10-23

## Context
Every kind of content so far plays inside ImmersiveX: ImmersiveX maps the room first, places the content in front of the user, lists it in the content menu and gives it controls. That suits media. It gets in the way of specialised apps, such as a mechanic's assistant that recognises parts with a vision model, which need the headset (passthrough, cameras, depth, anchors, input) but must own their menus, layout and flow, and must keep working wherever the user walks.

## Decision
A scene with a `BareMetalContent` component is a bare metal app. The `ImmersiveXSession` still runs, but as a host: it resolves the platform, asks for permissions, turns on the see-through view, hides the boundary and sets up input, then calls the app's `IBareMetalApp.OnHostReady`. It leaves out everything of its own (content menu, room-mapping prompt, placement, MR/VR switch) unless the app opts in. What the session sets up is computed once, as a `SessionPlan`.

The app reaches the headset through one static facade, `ImmersiveHost`, which wraps the existing services and AR Foundation managers rather than replacing them. New device capabilities come in the usual way: a provider interface in core (`IPassthroughCameraProvider`), implemented by the platform adapter (`MetaQuestPassthroughCamera`), with an editor fallback.

## Consequences
- An app is plain Unity code on top of ImmersiveX; it isn't tied to a mapped room or to ImmersiveX's UI, and it can still use Immersive Media, the content menu or the room flow when it wants them.
- Scenes without bare metal content behave exactly as before.
- Permissions follow what the build's scenes ask for: only apps whose bare metal content reads the cameras get the camera permissions.
- `ImmersiveHost` is a public API: changes to it must stay backwards-compatible or be called out in the changelog.
- The editor can't see the real room, so camera features are tested with a simulated camera that renders the app's own scene; depth, Space Setup and anchor loads still need a headset.
