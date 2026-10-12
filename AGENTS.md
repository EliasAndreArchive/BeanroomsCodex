# Beanrooms project guide

Beanrooms is a .NET 8 desktop game. The main client uses Silk.NET/GLFW, OpenGL 4.3, OpenAL Soft, Bepu rigidbodies, Vercidium raytraced acoustics, and Photon Realtime. Solo play runs the simulation locally. In an online room, the Photon room master simulates shared physics and monster behavior; guests render snapshots.

## Source map

| Path | Responsibility |
| --- | --- |
| `Beanrooms.Silk/Program.cs` | Startup, window, input, local movement, and game update |
| `Beanrooms.Silk/GameWindow.Rendering.cs` | Render targets, textures, object data, and GI probes |
| `Beanrooms.Silk/GameWindow.Presentation.cs` | Scene draw, post processing, and overlay projection |
| `Beanrooms.Silk/DebugMenu.cs`, `DebugMenu.Rendering.cs` | Menu state/input and pixel rendering |
| `Beanrooms.Silk/PhotonNetClient.cs` | Photon transport and snapshot buffering |
| `Beanrooms.Silk/PhotonAuthority.cs` | Room master physics, monster, and flicker authority |
| `Beanrooms.Silk/GameAudio.cs`, `PhysicalSounds.cs`, `VercidiumAcoustics.cs` | Sound playback, physics contact audio, and raytraced acoustics |
| `Nullrooms/PhysicsBodies.cs` | Shared Bepu cube and pallet simulation, collisions, and pickup |
| `Nullrooms/NetworkClient.cs`, `NetProtocol.cs` | Solo/online transport contract and messages |
| `Nullrooms/WorldLayout.cs`, `MonsterBrain.cs`, `PlayerMotion.cs`, `LightFlicker.cs` | Shared game behavior |
| `Nullrooms/shaders/`, `Nullrooms/assets/` | Rendering source and licensed game assets |
| `Beanrooms.PortChecks/` | Headless checks for retained behavior |

## Ownership rules

- Keep shared game rules in `Nullrooms/`. The client presents state and gathers input; `PhotonAuthority` owns online shared physics and host controls.
- Do not predict held cube movement or throws on guests without reconciliation. `CubeState.HolderId` identifies a current holder.
- Keep seeded world props deterministic across solo and Photon authority setup.
- Changes to object encoding must update both `GameWindow.Rendering.cs` and `pathtrace.frag`.
- Settings are for player preferences. Developer controls contain world/AI behavior, physics tuning, detailed GI/post parameters, and diagnostics. Host-only controls must stay host-only online.
- Preserve raytraced audio and original impact/scrape sounds. Retain Photon, Vercidium, and sound asset licenses.
- The sandbox device, wire, weld, copy/place, Ponder, and legacy dedicated-server systems were removed. Avoid reintroducing their controls or protocol fields unintentionally.

## Build

```sh
dotnet build Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```
