# Beanrooms

Beanrooms is a low resolution 4:3 desktop game with procedural rooms, path traced and hybrid lighting, Bepu physics, and online multiplayer. The client uses Silk.NET/GLFW, OpenGL 4.3, OpenAL Soft for playback, and Vercidium Audio v1.10.1 for raytraced source filtering and EAX room reverb on every sound. Nearby spatial sounds use Vercidium's grouped room effects; local, UI, and untracked distant sounds use the listener room. The dedicated server and Render WebSocket bridge are included. Vercidium's DLL, API XML, and license are in `Beanrooms.Silk/ThirdParty/VercidiumAudio/`.

Varied concrete footsteps play for the local and online players. Cubes make spatial wood impact and floor scraping sounds; each cube corner is tracked against the floor, walls, and other cubes, so a landing corner clicks while a resting cube stays quiet, and scrapes follow the floor contact points, so pivoting on an edge stays quiet. Online cube scraping uses a server-measured contact slip speed. A landing sound is inferred from cube motion if an online impact message is missing. Nearby ceiling lights each emit a spatial hum. Sound source licenses are in `Nullrooms/assets/SFX-LICENSES.md`. The server simulates online cube grabs and throws, and clients display its snapshots. Sandbox mode works in solo and is host-controlled online. Deploy the updated client and server together for full online sound state.

## Run the client

Install the .NET 8 SDK and run:

```powershell
dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
```

The game starts fullscreen. Press **F11** for windowed mode, **Tab** for settings, and **Escape** to release the mouse. The title menu offers solo and online play. A GPU driver with OpenGL 4.3 support is required.

To make a standalone Windows build:

```powershell
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r win-x64 --self-contained true -o publish/windows
```

Launch `publish/windows/Beanrooms.exe`. The Windows executable uses the GUI subsystem, so it opens without a command prompt.

For Linux, use `-r linux-x64` and a separate output folder. Run the published `Beanrooms` executable directly; Proton is not required.

## Server

`Nullrooms.Server` is the game server and `Beanrooms.RenderBridge` exposes it over WebSocket. `Dockerfile`, `render.yaml`, and `start.sh` provide a Render deployment. Set `BEANROOMS_JOIN_KEY` as a secret environment variable on the service; do not put its value in this repository. The client online menu accepts the Render hostname and join key.

## Checks

```powershell
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```

See [client details](Beanrooms.Silk/README.md) for controls and additional local network checks.
