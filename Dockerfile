# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG Channel=local
ARG BuildNumber=0
ARG BuildDate=
WORKDIR /src

# Restore first (better layer caching).
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/KommasKorrektus.Core/KommasKorrektus.Core.csproj", "src/KommasKorrektus.Core/"]
COPY ["src/KommasKorrektus/KommasKorrektus.csproj", "src/KommasKorrektus/"]
RUN dotnet restore "src/KommasKorrektus/KommasKorrektus.csproj"

COPY src src
# Fall back to today's date when BuildDate is not supplied, so the version never ends in "-".
RUN BUILD_DATE="${BuildDate:-$(date -u +%Y%m%d)}" && \
    dotnet publish "src/KommasKorrektus/KommasKorrektus.csproj" \
    -c Release -o /app/publish --no-restore \
    -p:UseAppHost=false \
    -p:Channel=$Channel -p:BuildNumber=$BuildNumber -p:BuildDate=$BUILD_DATE

# ---------------------------------------------------------------------------
# Runtime stage (ASP.NET Core base image)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The PDF engine draws text with the system font library: without these packages every PDF would come out without letters.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-liberation fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS="" \
    KOMMASKORREKTUS_DATA=/data \
    DOTNET_gcServer=0 \
    DOTNET_TieredPGO=1

# Config, keys, documents: one volume.
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME ["/data"]

EXPOSE 2930

COPY --from=build /app/publish .
USER $APP_UID

# The app checks itself, so the image needs no extra tools.
HEALTHCHECK --interval=30s --timeout=8s --start-period=40s --retries=3 \
    CMD ["dotnet", "KommasKorrektus.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "KommasKorrektus.dll"]
