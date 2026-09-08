# syntax=docker/dockerfile:1

# ── The API image ─────────────────────────────────────────────────────────
#
# <b>Written 2026-08-28. There was no Dockerfile at all</b>, so nothing could be
# deployed and the production configuration path had never been executed — which
# is how `Sso:EnableStubProvider` and an empty `Cors:Origins` could have reached
# a real environment without anything noticing.
#
# <b>Pinned by digest — F4.5, and this comment used to overstate it.</b> It
# previously read "pinned by digest-bearing tag", which `10.0-noble` is not:
# that tag moves every time the base is rebuilt, so two deployments of the
# same commit really were two different systems, and the difference surfaces
# as "it worked yesterday".
#
# The `tag@sha256:` form keeps both — the tag says what this is meant to be,
# the digest says exactly what it is, and only the digest is what gets pulled.
# Dependabot updates the digest weekly (`.github/dependabot.yml`), which is
# what stops a pin from quietly becoming a stale, unpatched base.
#
# <b>Two stages.</b> The SDK is ~800 MB and contains a compiler, a package
# manager and the source it built from; none of that belongs in a running
# container. The runtime image carries the framework and the published output.

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build
WORKDIR /src

# <b>Built from the repo root, like every other image here</b> (apps/web,
# apps/admin) — not from backend/. `ExamPackageReader`'s schema lookup
# (`LocateExamSchemaPath`, Vni.Ielts.Infrastructure/DependencyInjection.cs)
# walks up from the running process looking for contracts/schemas/, which
# only exists above backend/ — a backend/-only build context can never
# satisfy it, no matter what gets COPYed. This was live-broken in the
# preview deployment (S6b) before the context/COPY paths were aligned here.
#
#   docker build -f backend/Dockerfile -t vni-ielts-api:<tag> .
#
# Restore before copying the rest, so a change to a .cs file does not
# re-download every package. The props file is copied first for the same reason:
# it decides the target framework every project restores against.
COPY backend/Directory.Build.props ./
COPY backend/src/Vni.Ielts.Domain/Vni.Ielts.Domain.csproj src/Vni.Ielts.Domain/
COPY backend/src/Vni.Ielts.Application/Vni.Ielts.Application.csproj src/Vni.Ielts.Application/
COPY backend/src/Vni.Ielts.Infrastructure/Vni.Ielts.Infrastructure.csproj src/Vni.Ielts.Infrastructure/
COPY backend/src/Vni.Ielts.Api/Vni.Ielts.Api.csproj src/Vni.Ielts.Api/
COPY backend/src/Vni.Ielts.Worker/Vni.Ielts.Worker.csproj src/Vni.Ielts.Worker/

RUN dotnet restore src/Vni.Ielts.Api/Vni.Ielts.Api.csproj

COPY backend/src/ src/

# `TreatWarningsAsErrors` is on in Directory.Build.props, so this gate is real.
RUN dotnet publish src/Vni.Ielts.Api/Vni.Ielts.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS runtime
WORKDIR /app

# <b>Non-root, and it has to be explicit.</b> The base image ships a `app` user
# for exactly this and does not select it — a container running as root turns a
# remote code execution into a host compromise rather than a process one.
USER $APP_UID

COPY --from=build /app .

# S6b — the exam-import schema `LocateExamSchemaPath` requires at startup.
# Placed directly at /app/contracts/schemas so the very first directory the
# walk-up checks (AppContext.BaseDirectory itself) already has it — the
# walk exists for the local `dotnet run` layout, not to be relied on here.
COPY contracts/schemas/ contracts/schemas/

# Writing marking resolves this configured path from /app at runtime. Copy only
# the versioned rubric, not the full fixtures tree, so the image contains no
# learner data or unrelated test material.
COPY fixtures/assessment/writing-rubric-v1.json fixtures/assessment/writing-rubric-v1.json

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
