#!/bin/sh
set -eu
dotnet /app/server/Beanrooms.Server.dll --listen 127.0.0.1 --port 27015 --health-port 27016 &
exec dotnet /app/bridge/Beanrooms.RenderBridge.dll
