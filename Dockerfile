# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

USER $APP_UID

WORKDIR /app

EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

WORKDIR /workspace

COPY ["OsoujiSystem.slnx", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/OsoujiSystem.Application", "src/OsoujiSystem.Application"]
COPY ["src/OsoujiSystem.Domain", "src/OsoujiSystem.Domain"]
COPY ["src/OsoujiSystem.Infrastructure", "src/OsoujiSystem.Infrastructure"]
COPY ["src/OsoujiSystem.ServiceDefaults", "src/OsoujiSystem.ServiceDefaults"]
COPY ["src/OsoujiSystem.WebApi", "src/OsoujiSystem.WebApi"]

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
  dotnet restore "src/OsoujiSystem.WebApi/OsoujiSystem.WebApi.csproj"

COPY . .

WORKDIR "/workspace/src/OsoujiSystem.WebApi"

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
  dotnet build --no-restore "./OsoujiSystem.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish

ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
  dotnet publish --no-restore "./OsoujiSystem.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final

WORKDIR /app

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "OsoujiSystem.WebApi.dll"]
