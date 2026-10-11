# UI rework: player-facing content audit

This is the first planning pass on `feature/ui-rework`. It changes no gameplay, settings values, network rules, or rendering. The goal is to keep the spare text-only camcorder look while making normal play readable at 320×240 and 4:3.

## Current surfaces

| Surface | What it currently shows | Decision |
| --- | --- | --- |
| Launch/title | Play Solo, Join Online, Settings, Exit; separate online room/key entry and status | Keep. Make this the clear starting point and show connection errors here. |
| In-game Tab menu | Ten tabs: Session, Image, Post FX, Lights, Flicker, Camera, Movement, Physics, Monster, Network | Replace the flat tab strip with a small pause menu and grouped settings. Keep all existing tunables reachable in an Advanced section. |
| Sandbox browser (B) | Object/device/logic lists, spawn actions, cube color/material, clear cubes, selected item description, tool shortcuts | Keep when sandbox is enabled. Separate choosing an item from object appearance and destructive world actions. |
| Ponder screen (O) | Four static building lessons with a grid demonstration | Keep as optional Help / Building Guide, reachable from pause and the builder; do not present it during exploration. |
| Normal gameplay overlay | Player name tags, aimed object corner brackets, wires, tool state; optional debug text and monster vectors | Keep only context-sensitive interaction cues by default. Developer diagnostics remain behind a separate toggle. |
| Window title | Renderer, resolution, FPS, player count, and a long key list | Reduce to game name and perhaps short connection state. Fullscreen players cannot see this title, so controls must be discoverable in Help. |

## Show during normal play

- The world, with no persistent text HUD by default. This preserves the claustrophobic atmosphere.
- A thin white corner bracket with a black shadow around an object only when it is in pickup range and the camera is aimed at it. Keep it on the held object while carried. Do not add a center crosshair.
- A short prompt next to the focused object or device for the action available now, such as `LMB GRAB`, `E USE`, or `G CONNECT`. Hide it when there is no valid target. Use the existing pixel font and no opaque backing panel.
- A brief status line for actions that need confirmation: copied item, wire source chosen, weld target chosen, connection loss, or host restriction. Let it fade instead of occupying the bottom edge indefinitely.
- Other players' names only when their avatars are visible and close enough to read. Keep the current face and object visuals untouched.
- Wires when the player is using the wire tool or the builder view; avoid covering ordinary exploration with every connection.

## Show in the pause/menu flow

1. **Resume** (or **Play Solo** from launch).
2. **Online**: join/create a room, show connecting/connected/error state, player count, and leave room. Keep room credentials masked and editable only on this screen. Do not repeat connection/player/role status in multiple tabs.
3. **Settings** with an understandable first layer:
   - **Display**: fullscreen, internal resolution, FPS limit, VSync, renderer choice, and a simple quality preset. Preserve detailed sample, bounce, and probe values under Advanced Graphics.
   - **Picture**: brightness/exposure, temperature, saturation, palette reduction, dither, bloom, and tonemap. Preserve channel mixing and auto-exposure timing under Advanced Picture.
   - **Controls & Camera**: mouse sensitivity, look smoothing, head bob, sway, shake strengths, and camera motion. Keep movement speed/acceleration tuning in solo or host-only Advanced Gameplay so guests cannot use it to cheat.
   - **Audio**: reserve a place for volume controls if added later; do not add an empty page now.
   - **Accessibility**: show/hide pickup brackets, player names, motion effects, and optional FPS counter. These can be implemented as the UI is built; no current settings should silently change.
   - **Advanced**: the existing precise graphics, GI, physics, monster, flicker, and sandbox parameters. Preserve numeric precision and keyboard adjustment. Mark host-controlled settings visibly and disable them for guests.
4. **Help**: concise controls and optional Ponder building lessons.
5. **Leave room / Return to title / Quit**, with labels that reflect solo versus online state.

## Keep, but remove from the ordinary player's view

| Current item | Placement in rework |
| --- | --- |
| FPS and frame time | Optional performance readout, off by default. FPS alone may be useful to a player; frame time belongs in diagnostics. |
| Seed and XYZ coordinates | Developer diagnostics only. |
| Render mode, resolution, SPP, GI bounce count, probe grid/steps | Advanced Graphics and optional diagnostics, not permanent HUD text. |
| Cube/body count | Sandbox browser or developer diagnostics only. |
| Monster mode, target, search points, vectors | Developer diagnostics only; keep the existing H/M toggles accessible through Advanced/Debug. |
| Fog steps and raw flicker/chase probabilities | Advanced Graphics/World controls only. |
| Physics spring, damping, smoothing, slide resistance, snap step | Builder/Advanced Physics only. Retain the values and host rules. |
| Host/guest role | Online screen and a brief status when a restricted action is attempted. |
| `DEBUG UI` and `OBJECT FRAMES` rows inside Physics | Move to Accessibility/Diagnostics; they are display controls, not physics. |
| Reset to defaults | Settings action with a clear scope; preserve the existing values until the new flow defines per-section reset behavior. |

## Remove or consolidate

- The always-visible window-title key list and the bottom-of-menu seed/FPS footer. Help and the optional performance readout cover these.
- Duplicate online connection/player/role rows in Session and Network. One Online screen should own them.
- The ten-tab strip on the 320×240 menu. With 17–18 rows on Lights/Flicker, the final rows reach the footer area and are hard to scan or click.
- Repeated `BEANROOMS MENU` headings on every overlay. Use a short context title: `PAUSED`, `SETTINGS`, `BUILD`, or `ONLINE`.
- Technical labels such as `PATH SAMPLES`, `AO`, `PROBE GRID`, and `PID SMOOTH` from the first settings layer. Preserve them in Advanced with short explanations.
- Persistent tool instructions after the tool is no longer active. Keep only the current action and a brief confirmation.

## Layout and behavior rules for implementation

- Draw menus in the existing 320×240 overlay and preserve the 4:3 presentation and low-resolution pixel font. Avoid opaque full-screen backing layers; keep the user's text-only camcorder style, with a small shadow for legibility.
- Keep critical choices within a consistent safe area, away from the bottom 12 pixels and the outer edges. Long lists must scroll or paginate; no item should overlap another surface.
- The title, pause, settings, online, builder, and help screens need a predictable back path. Tab opens/closes pause; Escape backs out one level or releases the mouse in play. Preserve mouse and keyboard navigation.
- Do not show unusable options as if they were active. A guest should see `HOST ONLY` for restricted world controls and should still be able to change personal graphics/camera settings.
- Context prompts must not reveal developer state or block the view. The optional diagnostics overlay can retain the current terse text treatment.
- Keep network credentials out of debug text and screenshots; the UI already masks the join key and should continue to do so.

## Implementation order

1. Extract a screen/navigation model from `DebugMenu` while retaining the existing settings values and callbacks.
2. Rebuild launch, pause, online, and back navigation; test solo and Photon host/guest transitions.
3. Reorganize settings with grouped player-facing and advanced pages, scrolling, precise value changes, and reset behavior.
4. Rework gameplay prompts, object brackets, name tags, builder browser, and Ponder placement.
5. Test 320×240 and all larger internal resolutions in fullscreen/windowed 4:3, mouse and keyboard focus, guest restrictions, and no text overlap.

`Beanrooms.Silk/DebugMenu.cs` currently owns drawing and input hit tests for all these surfaces. `Beanrooms.Silk/Program.cs` supplies gameplay/debug data, menu actions, and the window title. The eventual rework should keep the appearance and state model separate enough to test layout and navigation without launching OpenGL.
