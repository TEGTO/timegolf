FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
RUN apk add --no-cache curl
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./
COPY server/TimeGolf/TimeGolf.csproj server/TimeGolf/
RUN dotnet restore server/TimeGolf/TimeGolf.csproj
COPY server/TimeGolf/ server/TimeGolf/
RUN dotnet publish server/TimeGolf/TimeGolf.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TimeGolf.dll"]