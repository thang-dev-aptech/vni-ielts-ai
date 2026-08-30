# Cấu hình secrets (API + Worker)

Ba lớp file — **không dùng `dotnet user-secrets` nữa**.

| File | Git? | Khi nào dùng |
|---|---|---|
| `secrets.example.json` | **Có** | Mẫu + mô tả từng khóa (`$…` là ghi chú, .NET bỏ qua) |
| `secrets.develop.json` | **Không** | Máy dev — API/Worker tự nạp khi `ASPNETCORE_ENVIRONMENT=Development` |
| `secrets.production.example.json` | **Có** | Mẫu production + map biến môi trường |
| `secrets.production.json` | **Không** | Mount trên server (tùy chọn); env vars vẫn thắng |

## Dev — bắt đầu nhanh

```powershell
cd backend\src\Vni.Ielts.Api
Copy-Item secrets.example.json secrets.develop.json
# Mở secrets.develop.json → điền ApiKey, SSO, storage…
dotnet run --launch-profile http
```

Worker dùng chung file trong thư mục Api (không cần copy thêm).

### Migrate từ `secrets.team.json` / user-secrets

```powershell
Copy-Item secrets.team.json secrets.develop.json -ErrorAction SilentlyContinue
# Hoặc dán nội dung user-secrets cũ vào secrets.develop.json
```

## Production

**Ưu tiên biến môi trường** (12-factor):

```bash
export Jwt__SigningKey="<min-32-chars>"
export Ai__OpenAi__ApiKey="sk-..."
export Sso__Google__ClientId="..."
export Sso__Google__ClientSecret="..."
export ObjectStorage__ServiceUrl="https://..."
```

Hoặc mount `secrets.production.json` (copy từ `secrets.production.example.json`, điền trên server). Biến môi trường **ghi đè** file.

Docker Compose:

```yaml
services:
  api:
    env_file:
      - ./secrets.production.json   # optional
    environment:
      Jwt__SigningKey: ${JWT_SIGNING_KEY}   # wins over file
```

## Thứ tự nạp cấu hình

| Môi trường | Thứ tự (thấp → cao) |
|---|---|
| Development | `appsettings*.json` → … → **`secrets.develop.json`** |
| Production | **`secrets.production.json`** (nếu có) → `appsettings*.json` → **env vars** |

## An toàn

- Không commit `secrets.develop.json` / `secrets.production.json` (đã `.gitignore`)
- Hook chặn ghi `secrets.json` / `.env*` vào repo
- Key lộ qua chat → xoay lại trên nhà cung cấp

## vietapi + synthetic (dev smoke)

`secrets.develop.json` có thể trỏ `https://api.vietapi.tech/v1` với `SyntheticDataOnly: true` — chỉ smoke/connectivity.

Writing band + giải thích cá nhân hóa **không** đi qua reseller; cần OpenAI chính thức + `AllowCrossBorderTransfer` (PDPL/`B-2`).
