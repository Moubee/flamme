# Version: 1
# Description: Dockerfile for building and running Elsa Server with Datadog and OpenTelemetry auto-instrumentation

ARG DOTNET_RUNTIME_VERSION=11.0

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0.100 AS build
ARG DOTNET_RUNTIME_VERSION
WORKDIR /source

# Copy sources.
COPY src/. ./src
COPY ./NuGet.Config ./
COPY *.props *.targets global.json ./

# Restore packages.
RUN dotnet restore "./src/apps/Elsa.Server.Web/Elsa.Server.Web.csproj"

# Build and publish (UseAppHost=false creates platform independent binaries).
WORKDIR /source/src/apps/Elsa.Server.Web
RUN dotnet publish "Elsa.Server.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore -f net${DOTNET_RUNTIME_VERSION}

# Move binaries into smaller base image.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS base
WORKDIR /app
COPY --from=build /app/publish ./

# Install runtime dependencies, including CA certificates.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        libpython3-dev \
        python3 \
        python3-dev \
        python3-pip \
        unzip \
        wget \
    && update-ca-certificates \
    && ln -s "$(find /usr/lib -name 'libpython3*.so' -print -quit)" /usr/local/lib/libpython3.so \
    && rm -rf /var/lib/apt/lists/*

COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Set PYTHONNET_PYDLL environment variable
ENV PYTHONNET_PYDLL=/usr/local/lib/libpython3.so

# Set environment variables for OpenTelemetry Auto-Instrumentation
ENV OTEL_DOTNET_AUTO_HOME=/otel \
    OTEL_LOG_LEVEL="debug"

# Install the official release archive directly. The shell installer requires an
# authenticated GitHub CLI; pinned archive digests keep image builds credential-free.
ARG OTEL_VERSION=1.19.1
ARG OTEL_LINUX_X64_SHA256=fe4ddaf4c54b960dbb150db7cc4531c06598f2c1a70e56b3a8c13c7118e4df58
ARG OTEL_LINUX_ARM64_SHA256=0bb2e2ac37c68058addfb96d7955a85bc639db202b1e70991ca73f70ae39d70e
RUN set -eu; \
    case "$(dpkg --print-architecture)" in \
        amd64) otel_arch=x64; otel_sha256="$OTEL_LINUX_X64_SHA256" ;; \
        arm64) otel_arch=arm64; otel_sha256="$OTEL_LINUX_ARM64_SHA256" ;; \
        *) echo "Unsupported OpenTelemetry architecture" >&2; exit 1 ;; \
    esac; \
    otel_download_dir="$(mktemp -d)"; \
    curl -fL -o "$otel_download_dir/otel.zip" "https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/releases/download/v${OTEL_VERSION}/opentelemetry-dotnet-instrumentation-linux-glibc-${otel_arch}.zip"; \
    echo "$otel_sha256  $otel_download_dir/otel.zip" | sha256sum --check -; \
    mkdir -p /otel; \
    unzip -q "$otel_download_dir/otel.zip" -d /otel; \
    rm -rf "$otel_download_dir"; \
    chmod +x /otel/instrument.sh

EXPOSE 8080/tcp
EXPOSE 443/tcp

ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "Elsa.Server.Web.dll"]
