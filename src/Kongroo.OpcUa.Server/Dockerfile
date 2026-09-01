FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY --parents Directory.*.props nuget.config global.json **/*.csproj ./
RUN dotnet restore src/Kongroo.OpcUa.Server/Kongroo.OpcUa.Server.csproj

COPY . .
RUN dotnet publish src/Kongroo.OpcUa.Server/Kongroo.OpcUa.Server.csproj \
      -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app .
EXPOSE 62552
USER $APP_UID
ENTRYPOINT ["dotnet", "Kongroo.OpcUa.Server.dll"]
