# Secrets handoff (team)

Hai file, hai việc khác nhau. **Không bao giờ** commit key thật.

| File | Git? | Việc |
|---|---|---|
| `secrets.json.example` | Có | Bản mẫu rỗng / placeholder |
| `secrets.team.json` | **Không** (`.gitignore`) | Bản điền key — gửi đồng nghiệp qua kênh riêng (chat/drive) |

## Quy trình

### 1. Người giữ key (bạn)

```powershell
cd backend\src\Vni.Ielts.Api
Copy-Item secrets.json.example secrets.team.json
# Mở secrets.team.json → điền ApiKey / SSO / storage
# Gửi secrets.team.json cho đồng nghiệp (Zalo/Drive/1Password…) — KHÔNG git add
```

### 2. Đồng nghiệp (máy dev)

```powershell
cd backend\src\Vni.Ielts.Api
dotnet user-secrets init

$dir = Join-Path $env:APPDATA "Microsoft\UserSecrets\7ef8200d-2eb5-4a26-974f-b9ab754ba109"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Copy-Item .\secrets.team.json (Join-Path $dir "secrets.json") -Force

dotnet user-secrets list   # chỉ hiện tên key, kiểm tra đã nạp
```

Hoặc mở file user-secrets rồi dán nội dung JSON (bỏ các trường `$…` cũng được — .NET bỏ qua key lạ).

## Test profile: vietapi + synthetic fixtures

`secrets.team.json` may point at `https://api.vietapi.tech/v1` for **connectivity** checks.

**Egress rules (after AI wiring hardening):**

| Workload | Classification | On vietapi (`SyntheticDataOnly` + third-party BaseUrl) |
|---|---|---|
| Canonical R/L explanation (no learner answer) | Synthetic | Allowed |
| Personalized R/L explanation (has learner answer) | LearnerPersonal | **Refused** (uncontracted processor / synthetic-only) |
| Writing AI marking | LearnerPersonal | **Not configured** / refused — same gates |

So: GPT chat probe can PASS, but **Writing band + “giải thích theo bài học viên” will not run through vietapi**. For those paths you need either:

1. Official OpenAI (`BaseUrl` empty or `https://api.openai.com/v1`), `SyntheticDataOnly=false`, and an explicit `AllowCrossBorderTransfer=true` decision (PDPL/`B-2`), or  
2. Keep vietapi for smoke only; Writing/personalized stay on recorded / Awaiting* until official keys exist.

Do not set `SyntheticDataOnly=true` and expect real learner essays or personalized explanations to be sent to a reseller — that path is deliberately closed.

### 3. Production

Không dùng file trong repo. Dùng **user-secrets** (dev) hoặc **biến môi trường / secret store** (`Ai__OpenAi__ApiKey`, …). Chi tiết: `docs/development/ai-provider-setup.md`.

## An toàn

- `secrets.team.json` đã nằm trong `.gitignore`
- Hook chặn ghi `secrets.json` / `.env*` vào repo
- Key đã lộ qua chat → xoay lại key trên nhà cung cấp
