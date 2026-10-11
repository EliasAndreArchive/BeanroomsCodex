# Beanrooms project guide

## What this repository contains

Beanrooms is a .NET 8 desktop game. The main client uses Silk.NET/GLFW for its window and input, OpenGL for rendering, OpenAL for sound playback, Bepu for cube physics, Vercidium Audio for room acoustics, and Photon Realtime for online rooms. Solo play simulates everything in the client. Online play elects the Photon room master to run the authoritative world simulation; every client displays its snapshots.

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
- Snapshots carry cube linear and angular velocity, orientation, grounded state, and a server-measured floor contact slip speed for scraping, player movement for each remote player's footsteps, and server collision events for impact sounds. Solo play gets collision events directly from `PhysicsBodies`. `PhysicalSounds` tracks each cube's eight corners against the floor, walls, and other cubes, clicking when a corner newly lands while a resting cube stays quiet; it can also infer a landing hit from cube motion if an online event is missing.
- `GameAudio` plays the game's clips through OpenAL. Nearby ceiling fixtures each have a spatial hum source that shares the lamp clip. `VercidiumAcoustics` traces walls, floor, and ceiling; listener EAX controls local, UI, and distant untracked sound reverb, while grouped EAX effects route nearby spatial lamps, monster, remote footsteps, and cubes through their source rooms. Vercidium's source filters also control their direct sound. Rebuild its geometry when the world seed changes.
- Network-message and snapshot changes must be reflected in `PhotonAuthority`, `PhotonNetClient`, and the client. Keep room-master simulation authoritative over pickup and physics. Update the legacy server only when its test/development path should preserve the behavior.
- Sandbox mode works in solo and online. In solo, `Tab → Session → Sandbox Mode` toggles it locally; the B-key sandbox browser only opens once sandbox mode is enabled (in any mode), and online the Photon room master controls the toggle and guests cannot change it. Cubes can always spawn; glow cubes and devices require sandbox mode. Sandbox devices have Bepu bodies and use the normal pickup path. The authority validates device placement, interaction, wires, and `Weld` constraints, evaluates the logic graph, applies powered thruster force, and broadcasts device poses and state.
- `pathtrace.frag` renders the devices as composite procedural models. Fixed path tracing is the default. Realtime GI can update its probe based radiance cache when moving emissive cubes or powered sandbox lamps change, giving fixtures and portable lights dynamic soft indirect light.

## Build and checks

```sh
dotnet build Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
dotnet build Nullrooms.Server/Nullrooms.Server.csproj -c Release
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release -- --photon
```

Run the client with `dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release`. See `README.md` and `Beanrooms.Silk/README.md` for controls, Photon room setup, and publishing. Vercidium's license terms are in `Beanrooms.Silk/ThirdParty/VercidiumAudio/LICENCE.txt`; review them before distributing or monetizing the game.
