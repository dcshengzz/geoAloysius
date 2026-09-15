# syntax=docker/dockerfile:1

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless the csproj changes)
COPY GpsSync.Api.csproj ./
RUN dotnet restore GpsSync.Api.csproj

# Build + publish
COPY . ./
RUN dotnet publish GpsSync.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Bind to all interfaces on 5080 inside the container (Caddy reaches this).
ENV ASPNETCORE_URLS=http://+:5080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5080

ENTRYPOINT ["dotnet", "GpsSync.Api.dll"]
