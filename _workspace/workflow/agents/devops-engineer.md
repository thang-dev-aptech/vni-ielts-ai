# DevOps Engineer Report - D2

Run: `demo-rlw-2026-09-07`
Task: `D2` - Ops harden: stable Jwt SigningKey + verify Worker loads shared secrets + Mongo 27018
Status: `done`

## What changed

- Updated `backend/src/Vni.Ielts.Api/secrets.develop.json` so `Jwt.SigningKey` is set.
- SigningKey set (length=43).
- Did not change `Ai.OpenAi.ApiKey`, object storage settings, or Google SSO settings.
- `backend/src/Vni.Ielts.Api/secrets.develop.json` is ignored by git.

## Worker config verified

- API Development Mongo: `mongodb://localhost:27018/?directConnection=true`, database `vni_ielts_dev`.
- Worker Development Mongo: `mongodb://localhost:27018/?directConnection=true`, database `vni_ielts_dev`.
- Worker `Program.cs` calls `builder.AddVniSecretsFile();`, so it shares the API secrets file in Development.
- Shared AI config present for local Writing marking: OpenAI-compatible base URL host `apithat.dev`, model `gpt-5.5`.
- Worker Mongo already matched API Development Mongo, so no Worker config file change was needed.

## Start commands for tomorrow

Run these from repository root, in separate terminals where the process stays running:

```powershell
pnpm infra
```

```powershell
pnpm api
```

```powershell
dotnet run --project backend/src/Vni.Ielts.Worker
```

```powershell
pnpm dev
```

Expected local ports from current config:

- Web: `http://localhost:5173`
- API Google callback config: `http://localhost:5099/api/v1/auth/sso/google/callback`
- MongoDB: `localhost:27018`
- MinIO: `localhost:9000` / console `localhost:9001`

## Runtime state checked

`docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"` - exit code `0`

```text
NAMES       IMAGE                                   STATUS                 PORTS
vni-pbm     percona/percona-backup-mongodb:2.12.0   Up 8 hours
vni-mongo   mongo:7                                 Up 8 hours (healthy)   127.0.0.1:27018->27017/tcp, [::1]:27018->27017/tcp
vni-minio   minio/minio:latest                      Up 8 hours (healthy)   127.0.0.1:9000-9001->9000-9001/tcp, [::1]:9000-9001->9000-9001/tcp
```

No stack was torn down.

## Verification commands run

- Initial `python - <<'PY' ...` key update attempt - exit code `1`; failed because PowerShell does not support POSIX heredoc syntax. No file change completed.
- Initial PowerShell key update using `RandomNumberGenerator.Fill` - exit code `1`; failed because that static method is not available in this PowerShell/.NET runtime. No file change completed.
- PowerShell key update using `RandomNumberGenerator.Create().GetBytes(...)` - exit code `0`; reported `SigningKey set length=43`.
- Safe config verification script - exit code `0`; confirmed SigningKey length, Worker/API Mongo match, Worker `AddVniSecretsFile()`, OpenAI-compatible host `apithat.dev`, and model `gpt-5.5`.
- `git check-ignore -q backend/src/Vni.Ielts.Api/secrets.develop.json` - exit code `0`.
- `dotnet build backend/src/Vni.Ielts.Worker/Vni.Ielts.Worker.csproj --no-restore` - exit code `0`; build succeeded with `0 Warning(s), 0 Error(s)`.
- `docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"` - exit code `0`; Mongo and MinIO are already healthy.

## Blockers for user

- None for D2.
- For tomorrow's demo, keep the API and Worker running at the same time; Writing marking will not drain if Worker is not started.
