# File: Dockerfile
# Purpose: Build a reproducible non-root container for the local reference synchronization API.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/FieldOps.Api/FieldOps.Api.csproj --locked-mode
RUN dotnet publish src/FieldOps.Api/FieldOps.Api.csproj -c Release --no-restore \
    --no-self-contained -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID
COPY --from=build --chown=$APP_UID:$APP_UID /app .
ENTRYPOINT ["dotnet", "FieldOps.Api.dll"]
