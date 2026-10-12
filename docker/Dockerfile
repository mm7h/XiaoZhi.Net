# syntax=docker/dockerfile:1
FROM debian:bookworm-slim AS ffmpeg-download
ARG TARGETARCH
ARG FFMPEG_VERSION=7.1.1
ARG FFMPEG_SHA256=1c56ec684da4cfd5f40dd67c56b6b21fdb9ad124218bc225e68deb4eeefe3143
# The application publish and Sherpa native libraries below currently target linux-x64.
RUN test "$TARGETARCH" = amd64 || { echo "This Dockerfile only supports linux/amd64." >&2; exit 1; }
RUN apt-get -o Acquire::Retries=3 update && apt-get -o Acquire::Retries=3 install -y --no-install-recommends \
    ca-certificates curl unzip \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /build
RUN curl --fail --location --retry 3 \
        "https://github.com/mm7h/XiaoZhi.Net/releases/download/resources/ffmpeg-${FFMPEG_VERSION}-linux-x64.zip" -o ffmpeg.zip \
    && echo "${FFMPEG_SHA256}  ffmpeg.zip" | sha256sum -c - \
    && unzip -q ffmpeg.zip -d extracted \
    && mkdir -p /opt/ffmpeg/lib /opt/ffmpeg/bin /opt/ffmpeg/licenses \
    && cp -a extracted/*/lib/. /opt/ffmpeg/lib/ \
    && cp extracted/*/bin/ffmpeg extracted/*/bin/ffprobe /opt/ffmpeg/bin/ \
    && cp extracted/*/LICENSE* /opt/ffmpeg/licenses/ \
    && chmod 755 /opt/ffmpeg/bin/* \
    && rm -rf ffmpeg.zip extracted

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS source
WORKDIR /source
COPY src/server/ src/server/
COPY demo/XiaoZhi.Net.Sample.Server/ demo/XiaoZhi.Net.Sample.Server/
COPY demo/XiaoZhi.Net.Sample.OTA.Server/ demo/XiaoZhi.Net.Sample.OTA.Server/

FROM source AS server-build
RUN apt-get -o Acquire::Retries=3 update && apt-get -o Acquire::Retries=3 install -y --no-install-recommends patchelf \
    && rm -rf /var/lib/apt/lists/*
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish demo/XiaoZhi.Net.Sample.Server/XiaoZhi.Net.Sample.Server.csproj \
    -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o /publish \
    && sherpa_version="$(sed -n 's/.*"org.k2fsa.sherpa.onnx.runtime.linux-x64\/\([^"]*\)".*/\1/p' demo/XiaoZhi.Net.Sample.Server/obj/project.assets.json | head -n 1)" \
    && test -n "$sherpa_version" \
    && mkdir -p /publish/sherpa \
    && cp "/root/.nuget/packages/org.k2fsa.sherpa.onnx.runtime.linux-x64/${sherpa_version}/runtimes/linux-x64/native/libonnxruntime.so" /publish/sherpa/libsherpa-onnxruntime.so \
    && patchelf --set-soname libsherpa-onnxruntime.so /publish/sherpa/libsherpa-onnxruntime.so \
    && patchelf --replace-needed libonnxruntime.so libsherpa-onnxruntime.so /publish/libsherpa-onnx-c-api.so \
    && patchelf --set-rpath '$ORIGIN/sherpa' /publish/libsherpa-onnx-c-api.so

FROM source AS ota-build
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish demo/XiaoZhi.Net.Sample.OTA.Server/XiaoZhi.Net.Sample.OTA.Server.csproj \
    -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o /publish

# The referenced vision module requires Microsoft.AspNetCore.App even when disabled.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS sample-server
USER root
RUN apt-get -o Acquire::Retries=3 update && apt-get -o Acquire::Retries=3 install -y --no-install-recommends \
    libgomp1 libgnutls30 libmp3lame0 libopus0 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=server-build /publish/ ./
COPY --from=ffmpeg-download /opt/ffmpeg/lib/ ./ffmpeg/
COPY --from=ffmpeg-download /opt/ffmpeg/bin/ /opt/ffmpeg/bin/
COPY --from=ffmpeg-download /opt/ffmpeg/licenses/ /opt/ffmpeg/licenses/
RUN LD_LIBRARY_PATH=/app/ffmpeg /opt/ffmpeg/bin/ffmpeg -version \
    && mkdir -p Configs models musics data logs .net && chown -R app:app data logs .net
ENV DOTNET_ENVIRONMENT=Production TZ=Asia/Shanghai LD_LIBRARY_PATH=/app/ffmpeg DOTNET_BUNDLE_EXTRACT_BASE_DIR=/app/.net
EXPOSE 4530
USER app
ENTRYPOINT ["dotnet", "XiaoZhi.Net.Sample.Server.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS ota-server
WORKDIR /app
COPY --from=ota-build /publish/ ./
USER root
RUN mkdir -p .net && chown app:app .net
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:4531 TZ=Asia/Shanghai DOTNET_BUNDLE_EXTRACT_BASE_DIR=/app/.net
EXPOSE 4531
USER app
ENTRYPOINT ["dotnet", "XiaoZhi.Net.Sample.OTA.Server.dll"]
