FROM debian:13-slim

ENV LUMYTE_ENV_ROOT=/opt/lumyte \
    MISE_DATA_DIR=/opt/lumyte/mise/data \
    MISE_CACHE_DIR=/opt/lumyte/mise/cache \
    MISE_STATE_DIR=/opt/lumyte/mise/state \
    MISE_CONFIG_DIR=/opt/lumyte/mise/config \
    MISE_DISABLE_UPDATE_WARNING=1 \
    DOTNET_CLI_HOME=/opt/lumyte/dotnet-home \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    NUGET_PACKAGES=/opt/lumyte/nuget-packages \
    VCPKG_ROOT=/opt/lumyte/vcpkg \
    VCPKG_DOWNLOADS=/opt/lumyte/vcpkg-downloads \
    VCPKG_DEFAULT_BINARY_CACHE=/opt/lumyte/vcpkg-binary-cache \
    VCPKG_DISABLE_METRICS=1
ENV PATH="/opt/lumyte/mise-bin:/opt/lumyte/mise/data/shims:/opt/lumyte/vcpkg:${PATH}"

COPY global.json /opt/lumyte-bootstrap/global.json
COPY mise.toml mise.lock /opt/lumyte-bootstrap/
COPY tools/setup/ /opt/lumyte-bootstrap/tools/setup/
RUN --mount=type=secret,id=proxy-ca \
    apt-get update -o APT::Update::Error-Mode=any \
    && apt-get install -y --no-install-recommends sudo ca-certificates \
    && if [ -s /run/secrets/proxy-ca ]; then \
         cp /run/secrets/proxy-ca /usr/local/share/ca-certificates/lumyte-proxy.crt; \
         update-ca-certificates; \
       fi \
    && bash /opt/lumyte-bootstrap/tools/setup/setup.sh \
    && useradd --create-home --shell /bin/bash --uid 1000 lumyte \
    && printf 'lumyte ALL=(ALL) NOPASSWD:ALL\n' > /etc/sudoers.d/lumyte \
    && chmod 0440 /etc/sudoers.d/lumyte \
    && chown -R lumyte:lumyte /opt/lumyte \
    && rm -rf /opt/lumyte-bootstrap /var/lib/apt/lists/*

USER lumyte
WORKDIR /workspaces/Lumyte
