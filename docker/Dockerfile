# syntax=docker/dockerfile:1
FROM debian:bookworm-slim AS ffmpeg-build
ARG FFMPEG_VERSION=7.1.1
ARG FFMPEG_SHA256=733984395e0dbbe5c046abda2dc49a5544e7e0e1e2366bba849222ae9e3a03b1
RUN apt-get -o Acquire::Retries=3 update && apt-get -o Acquire::Retries=3 install -y --no-install-recommends \
    build-essential ca-certificates curl xz-utils nasm pkg-config \
    libgnutls28-dev libmp3lame-dev libopus-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /build
RUN curl --fail --location --retry 3 "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" -o ffmpeg.tar.xz \
    && echo "${FFMPEG_SHA256}  ffmpeg.tar.xz" | sha256sum -c - \
    && tar -xJf ffmpeg.tar.xz --strip-components=1 \
    && ./configure --prefix=/opt/ffmpeg --enable-shared --disable-static \
        --disable-doc --disable-debug --disable-ffplay --enable-gpl \
        --enable-gnutls --enable-libmp3lame --enable-libopus \
    && make -j "$(nproc)" && make install \
    && mkdir -p /opt/ffmpeg/licenses \
    && cp COPYING* LICENSE.md /opt/ffmpeg/licenses/

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
COPY --from=ffmpeg-build /opt/ffmpeg/lib/ ./ffmpeg/
COPY --from=ffmpeg-build /opt/ffmpeg/bin/ /opt/ffmpeg/bin/
COPY --from=ffmpeg-build /opt/ffmpeg/licenses/ /opt/ffmpeg/licenses/
RUN mkdir -p Configs models musics data logs && chown -R app:app data logs
ENV DOTNET_ENVIRONMENT=Production TZ=Asia/Shanghai LD_LIBRARY_PATH=/app/ffmpeg
EXPOSE 4530
USER app
ENTRYPOINT ["dotnet", "XiaoZhi.Net.Sample.Server.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS ota-server
WORKDIR /app
COPY --from=ota-build /publish/ ./
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:4531 TZ=Asia/Shanghai
EXPOSE 4531
USER app
ENTRYPOINT ["dotnet", "XiaoZhi.Net.Sample.OTA.Server.dll"]
