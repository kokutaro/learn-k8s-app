# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

USER $APP_UID

WORKDIR /app

EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["LearnK8s.Api.slnx", "./"]
COPY ["LearnK8s.Api/LearnK8s.Api.csproj", "LearnK8s.Api/"]
COPY ["LearnK8s.ServiceDefaults/LearnK8s.ServiceDefaults.csproj", "LearnK8s.ServiceDefaults/"]

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
  dotnet restore "LearnK8s.Api/LearnK8s.Api.csproj"

COPY . .

WORKDIR "/src/LearnK8s.Api"

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
  dotnet build --no-restore "./LearnK8s.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish

ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
  dotnet publish --no-restore "./LearnK8s.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final

WORKDIR /app

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "LearnK8s.Api.dll"]
