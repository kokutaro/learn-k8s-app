FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LearnK8s.Api/LearnK8s.Api.csproj", "LearnK8s.Api/"]
RUN dotnet restore "LearnK8s.Api/LearnK8s.Api.csproj"
COPY . .
WORKDIR "/src/LearnK8s.Api"
RUN dotnet build "./LearnK8s.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LearnK8s.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LearnK8s.Api.dll"]
