FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Nullrooms.Server/Nullrooms.Server.csproj --configfile NuGet.Config \
    && dotnet publish Nullrooms.Server/Nullrooms.Server.csproj -c Release --no-restore -o /out/server \
    && dotnet restore Beanrooms.RenderBridge/Beanrooms.RenderBridge.csproj --configfile NuGet.Config \
    && dotnet publish Beanrooms.RenderBridge/Beanrooms.RenderBridge.csproj -c Release --no-restore -o /out/bridge

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out/server /app/server
COPY --from=build /out/bridge /app/bridge
COPY start.sh /app/start.sh
RUN chmod 755 /app/start.sh
ENV PORT=10000
CMD ["/app/start.sh"]
