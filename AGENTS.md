# Beanrooms project guide

## What this repository contains

Beanrooms is a .NET 8 desktop game. The main client uses Silk.NET/GLFW for its window and input, OpenGL for rendering, OpenAL for sound playback, Bepu for cube physics, Vercidium Audio for room acoustics, and Photon Realtime for online rooms. Solo play simulates everything in the client. Online play elects the Photon room master to run the authoritative world simulation; every client displays its snapshots.

## Game intent

Beanrooms is a low resolution first person Backrooms exploration sandbox. Players walk procedural fluorescent rooms, avoid the monster, carry and throw physical objects, and build simple contraptions when the host enables sandbox mode. The visual style is deliberately pixelated and dithered, with path traced room lighting, while the world objects should still feel solid, readable, and physical.

Every interactive object must have a clear physical purpose: it should be visible in the ray traced renderer, receive a Bepu body when it is grabbable, and replicate through the online authority. Decorative objects are allowed only when they cannot be mistaken for something the player can use. Seeded corner pallets are grabbable wooden platforms made of several boards and must be created from the current world seed by solo, Photon, and legacy-server paths.

## Project map

| Path | Purpose |
| --- | --- |
| `Beanrooms.Silk/` | Main game client, window loop, rendering, UI, audio, and project file. |
| `Beanrooms.Silk/Program.cs` | Startup, `GameWindow`, frame update and rendering, input, and online state. |
| `Beanrooms.Silk/SilkForm.cs` | GLFW/Silk window and input adaptation. |
| `Beanrooms.Silk/Gl.cs`, `PixelDrawing.cs` | OpenGL calls and software pixel drawing helpers. |
| `Beanrooms.Silk/DebugMenu.cs` | Title screen, settings, and debug menu. |
| `Beanrooms.Silk/GameAudio.cs`, `PhysicalSounds.cs`, `LoopingSound.cs` | Game sound mix, spatial cube effects (per-vertex impacts and contact-point scrapes) and per-player footsteps, and OpenAL playback/decoding. |
| `Beanrooms.Silk/VercidiumAcoustics.cs`, `OpenAlReverb.cs` | Raytraced room and source acoustics, plus OpenAL EAX reverb slots for listener and grouped source rooms. |
| `Beanrooms.Silk/ThirdParty/VercidiumAudio/` | Vercidium Audio v1.10.1 production AnyCPU DLL, XML API documentation, and its license. |
| `Beanrooms.Silk/PhotonNetClient.cs` | Photon room connection, event relay, snapshots, and client state buffering. |
| `Beanrooms.Silk/PhotonAuthority.cs` | Room-master authoritative physics, sandbox, monster, and snapshot simulation. |
| `Beanrooms.Silk/ThirdParty/PhotonRealtime/` | Photon Realtime 5.1.20 source wrapper, production client DLL/XML, and license. |
| `Nullrooms/` | Shared game code linked into client and server projects. |
| `Nullrooms/WorldLayout.cs` | Seeded room generation and wall layout. |
| `Nullrooms/PhysicsBodies.cs` | Bepu cube simulation, spawning, ray pickup, per-player grab constraints, collisions, and server contact slip speed. |
| `Nullrooms/NetProtocol.cs` | JSON messages, player/cube snapshots, and tuning data. |
| `Nullrooms/SandboxWorld.cs` | Shared sandbox device kinds, host-gated placement, wire links, logic evaluation, and powered thrusters. |
| `Nullrooms/LocalNetClient.cs` | Shared network-client contract plus legacy TCP/WebSocket transport and state buffering. |
| `Nullrooms/MonsterBrain.cs`, `PlayerMotion.cs` | Monster behavior and local movement. |
| `Nullrooms/CameraOrientation.cs`, `LightFlicker.cs`, `SpawnTransition.cs` | Shared camera, lighting, and transition logic. |
| `Nullrooms/assets/`, `Nullrooms/shaders/` | Game assets and GLSL shaders copied into the client output. Concrete sound source and license details are in `Nullrooms/assets/SFX-LICENSES.md`. |
| `Nullrooms.Server/` | Legacy authoritative TCP server, retained for direct-server development checks. |
| `Beanrooms.RenderBridge/` | Legacy WebSocket bridge for the TCP server. |
| `Beanrooms.PortChecks/` | Headless project checks for shared behavior and protocol. |
| `Dockerfile`, `render.yaml`, `start.sh` | Server deployment configuration. |
| `packages/`, `NuGet.Config` | Local Bepu NuGet packages and package source configuration. |

## Data flow and ownership

- `GameWindow` reads input and updates camera/movement each frame. Solo cubes are `PhysicsBodies.Bodies`; online cube visuals are `networkCubes`. Sandbox devices are `localSandbox` in solo and `networkDevices` online.
- Photon Realtime carries room events. The room master owns online cube physics and decides whether a pickup succeeds. A player may hold one free cube, and multiple players may hold different cubes concurrently. `CubeState.HolderId` reports the current holder.
- The client sends pickup and release input to the room master. `PhotonAuthority` simulates all online cube motion and broadcasts snapshots. Clients smoothly follow server poses and rotations without predicting grabs or throws.
- Snapshots carry cube linear and angular velocity, orientation, grounded state, and a server-measured floor contact slip speed for scraping, player movement for each remote player's footsteps, and server collision events for impact sounds. Each cube also carries a `PhysicsMaterial` (`Concrete`, `Wood`, `Metal`, or `Plastic`) for its visual material. All physics objects use the original cube impact and scrape clips. Solo play gets collision events directly from `PhysicsBodies`. `PhysicalSounds` tracks each cube's eight corners against the floor, walls, and other cubes, clicking when a corner newly lands while a resting cube stays quiet; it can also infer a landing hit from cube motion if an online event is missing.
- `GameAudio` plays the game's clips through OpenAL. Nearby ceiling fixtures each have a spatial hum source that shares the lamp clip. `VercidiumAcoustics` traces walls, floor, and ceiling; listener EAX controls local, UI, and distant untracked sound reverb, while grouped EAX effects route nearby spatial lamps, monster, remote footsteps, and cubes through their source rooms. Vercidium's source filters also control their direct sound. Rebuild its geometry when the world seed changes.
- Network-message and snapshot changes must be reflected in `PhotonAuthority`, `PhotonNetClient`, and the client. Keep room-master simulation authoritative over pickup and physics. Update the legacy server only when its test/development path should preserve the behavior.
- Sandbox mode works in solo and online. In solo, `Tab → Session → Sandbox Mode` toggles it locally; the B-key sandbox browser only opens once sandbox mode is enabled (in any mode), and online the Photon room master controls the toggle and guests cannot change it. Cubes can always spawn; glow cubes and devices require sandbox mode. Sandbox devices have Bepu bodies and use the normal pickup path. The authority validates device placement, interaction, and wires; `PhysicsBodies` validates weld constraints for every grabbable body, evaluates the logic graph, applies powered thruster force, and broadcasts device poses and state. Device snapshots use `DevicesChanged` to avoid resending unchanged device and wire arrays every tick.
- The sandbox currently includes cubes, glow cubes, thrusters, lamps, pistons, hinges, wheels, ropes, conveyors, sensor lights, display panels, buttons, levers, pressure plates, proximity sensors, AND/OR/NOT/XOR/NAND/XNOR gates, toggles, timers, delays, counters, and random pulse sources. Devices must define their menu entry, detail text, state rules in `SandboxWorld`, callback needs in all three simulators, and a model in `pathtrace.frag`.
- `pathtrace.frag` renders the devices as composite procedural models. Fixed path tracing is the default. Realtime GI can update its probe based radiance cache when moving emissive cubes or powered sandbox lamps change, giving fixtures and portable lights dynamic soft indirect light.

## Coding rules

- Keep gameplay rules in `Nullrooms/` whenever solo and online modes share them. `Beanrooms.Silk` should gather input, present state, and render it; it should not become a second source of physics or sandbox truth.
- Treat the Photon room master as authoritative. Guests send input messages and render snapshots. Do not add client prediction for held cubes, throws, rotations, device physics, or sandbox state without an explicit reconciliation design.
- Preserve deterministic world setup: any seed-generated object must be derived from `WorldLayout` and spawned by `GameWindow.GenerateWorld`, `PhotonAuthority.ResetWorld`, and `LocalServer.ResetWorld`.
- Add a regression check in `Beanrooms.PortChecks` for every new network rule, device state machine, world prop, or input edge case. Use the live `--photon` check when changing Photon connection, event, room, authority, or snapshot behavior.
- Keep rendering data compact. `UploadObjects` packs one object into three RGBA texels, and `pathtrace.frag` decodes that layout. Update both sides together whenever an object flag or shape type changes.
- Keep input controls consistent: B opens the sandbox browser only when sandbox is enabled; O opens the local-only Ponder screen, which blocks movement and shows a 5×5×5 tutorial grid; G toggles wires; K toggles welding; Y copies an aimed sandbox cube or device; V places the copy; left click grabs or selects a tool target. Physics settings control snap-hold and its grid size, and those settings must be sent to the authority in online rooms. Menu text should describe player-facing behavior, not implementation details.
- Preserve third party licensing. Vercidium and Photon files are local dependencies; retain their license and documentation files when upgrading either SDK.

## Build and checks

```sh
dotnet build Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
dotnet build Nullrooms.Server/Nullrooms.Server.csproj -c Release
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release -- --photon
```

Run the client with `dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release`. See `README.md` and `Beanrooms.Silk/README.md` for controls, Photon room setup, and publishing. Vercidium's license terms are in `Beanrooms.Silk/ThirdParty/VercidiumAudio/LICENCE.txt`; review them before distributing or monetizing the game.

## Ambient easter eggs

The Photon room master owns timed ambient events so every player receives the same
event path. The current chicken easter egg waits a random 120–300 seconds, walks
across a short path near the first player, and expires after the walk. Clients
perform view culling independently, so a player who is not looking does not see
the chicken while another player can still see the replicated event. The source
asset and attribution are in `Nullrooms/assets/Chickensoft/`; the path tracer uses
a compact procedural rendition to keep the packed object buffer efficient.
