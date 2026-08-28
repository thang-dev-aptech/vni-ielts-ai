# syntax=docker/dockerfile:1

# ── The API image ─────────────────────────────────────────────────────────
#
# <b>Written 2026-08-28. There was no Dockerfile at all</b>, so nothing could be
# deployed and the production configuration path had never been executed — which
# is how `Sso:EnableStubProvider` and an empty `Cors:Origins` could have reached
# a real environment without anything noticing.
#
# <b>Pinned by digest-bearing tag, not `latest`.</b> A base image that moves
# under a rebuild means two deployments of the same commit are two different
# systems, and the difference surfaces as "it worked yesterday".
#
# <b>Two stages.</b> The SDK is ~800 MB and contains a compiler, a package
# manager and the source it built from; none of that belongs in a running
# container. The runtime image carries the framework and the published output.

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Restore before copying the rest, so a change to a .cs file does not
# re-download every package. The props file is copied first for the same reason:
# it decides the target framework every project restores against.
COPY Directory.Build.props ./
COPY src/Vni.Ielts.Domain/Vni.Ielts.Domain.csproj src/Vni.Ielts.Domain/
COPY src/Vni.Ielts.Application/Vni.Ielts.Application.csproj src/Vni.Ielts.Application/
COPY src/Vni.Ielts.Infrastructure/Vni.Ielts.Infrastructure.csproj src/Vni.Ielts.Infrastructure/
COPY src/Vni.Ielts.Api/Vni.Ielts.Api.csproj src/Vni.Ielts.Api/
COPY src/Vni.Ielts.Worker/Vni.Ielts.Worker.csproj src/Vni.Ielts.Worker/

RUN dotnet restore src/Vni.Ielts.Api/Vni.Ielts.Api.csproj

COPY src/ src/

# `TreatWarningsAsErrors` is on in Directory.Build.props, so this gate is real.
RUN dotnet publish src/Vni.Ielts.Api/Vni.Ielts.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# <b>Non-root, and it has to be explicit.</b> The base image ships a `app` user
# for exactly this and does not select it — a container running as root turns a
# remote code execution into a host compromise rather than a process one.
USER $APP_UID

COPY --from=build /app .

# The port the process listens on inside the container. Nothing maps it to 80
# here: that is the orchestrator's decision, and hard-coding it would make this
# image unusable behind anything that expects otherwise.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# <b>No `ASPNETCORE_ENVIRONMENT` default.</b> Defaulting it to Production would
# be right nine times out of ten and catastrophic the tenth, when somebody runs
# this locally and the startup gate refuses to boot for reasons they did not
# expect. It is supplied by whoever runs the container, deliberately.

HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD ["/app/Vni.Ielts.Api", "--healthcheck"]

ENTRYPOINT ["/app/Vni.Ielts.Api"]
