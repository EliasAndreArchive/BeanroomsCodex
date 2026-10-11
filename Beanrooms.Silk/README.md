# Beanrooms Silk.NET client

This is the desktop Beanrooms client using Silk.NET/GLFW for the window and input,
the existing OpenGL 4.3 shaders for rendering, OpenAL Soft for audio, and the
existing Bepu physics and multiplayer implementation.

The game content and tuning are shared with `../Nullrooms`: physics, world layout,
monster logic, movement, flicker, protocol, shaders, and assets are linked from
that project. `DebugMenu.cs` is the same settings/menu logic; only its drawing
surface was replaced so it works without Windows GDI+.

Build and run from the repository root:

```powershell
dotnet run --project Beanrooms.Silk/Beanrooms.Silk.csproj -c Release
```

The game opens fullscreen with a 4:3 image and black side bars.
Use **Tab** for the menu, **F11** for windowed/full screen, **WASD** to move,
and **Escape** to release the mouse. The title menu has solo and online play.
The online connection uses the same WebSocket protocol and join key as the
previous Windows client.

Build standalone packages:

```powershell
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r win-x64 --self-contained true -o publish/windows
dotnet publish Beanrooms.Silk/Beanrooms.Silk.csproj -c Release -r linux-x64 --self-contained true -o publish/linux
```

The GPU and its driver must support OpenGL 4.3. The Linux build is native;
Proton is not needed. OpenAL Soft is included with both standalone builds.

The headless checks live in `../Beanrooms.PortChecks`. Run them with:

```powershell
dotnet run --project Beanrooms.PortChecks/Beanrooms.PortChecks.csproj -c Release
```

To test local multiplayer, start `../Nullrooms.Server` on port 27075 and pass
`--network` to the checks. To test the Render-style WebSocket route, start the
server on 27015 and `../Beanrooms.RenderBridge` on 10000 with a temporary
`BEANROOMS_JOIN_KEY=port-test-key`, then pass `--websocket` to the checks.
