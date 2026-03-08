FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY src/OsoujiSystem.Domain/OsoujiSystem.Domain.csproj src/OsoujiSystem.Domain/
COPY src/OsoujiSystem.Application/OsoujiSystem.Application.csproj src/OsoujiSystem.Application/
COPY src/OsoujiSystem.Infrastructure/OsoujiSystem.Infrastructure.csproj src/OsoujiSystem.Infrastructure/
COPY src/OsoujiSystem.WebApi/OsoujiSystem.WebApi.csproj src/OsoujiSystem.WebApi/
RUN dotnet restore src/OsoujiSystem.WebApi/OsoujiSystem.WebApi.csproj

COPY . .
RUN dotnet publish src/OsoujiSystem.WebApi/OsoujiSystem.WebApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OsoujiSystem.WebApi.dll"]
