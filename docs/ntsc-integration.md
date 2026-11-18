# NTSC integration and final scope

Target branch: rewrite/player-experience, based on c8722b9.

Removed: the raised-ceiling/pit-room change introduced in c8722b9, including
its room geometry, GI probe/shadow changes, collision and fall recovery, audio
occlusion changes, and dependent world/authority code. Restored the fixed
ceiling and continuous floor from 95c6858.

Retained at the user's final request: the CMD terminal, framed menus, scrollback,
clickable settings, chat/network screen, physics bean menu background, current
movement/pickup profiles, cube spawning commands and fixed world-object spawning,
current exposure rate and color/tonemap pipeline, and existing pallet-code cleanup.

Added: the pinned complete ntsc-rs Rust core, a C ABI wrapper, all 60 upstream
controls from live descriptors, master enable/strength, and a native-resolution
color pass before NTSC. NTSC precedes display pixel enlargement, Bayer dithering,
posterization, transitions, and the HUD. The ray tracer still samples at the
selected low game resolution; this does not add a high-resolution ray-tracing pass.
The terminal/menu background also receives NTSC, while terminal text stays sharp.

The effect uses a native CPU round-trip (RGBA float OpenGL readback, reusable
YIQ buffers, texture upload). Disabled/zero-strength NTSC skips this round-trip.
No network or physics rules are changed by NTSC. The core's defaults are retained
and blended at 0.65. Application/export/player preferences from the upstream GUI
are outside the effect settings.

Validation: build, complete PortChecks, all 60 controls mapped once, typed values
and disabled groups, deterministic seed/field output, zero-strength identity,
image orientation, all three game resolutions, real OpenGL compile/link of every
shader, and offscreen grade/NTSC/enlargement/dither rendering. Use --gpu with
PortChecks for the OpenGL checks and --menu-preview <folder> for raw BGRA menus.
