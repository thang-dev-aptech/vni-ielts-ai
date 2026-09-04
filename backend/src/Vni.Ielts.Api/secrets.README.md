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

Đặt `VNI_SECRETS_FILE=off` để **không** nạp file nào (host test của `Vni.Ielts.Integration.Tests` tự đặt biến này, vì host test chạy Development và sẽ kế thừa file develop của máy dev).

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
| Production | `appsettings*.json` → **`secrets.production.json`** (nếu có) → **env vars** |

File production nằm **trên** `appsettings*.json` (một secret mount vào mà thua giá trị mặc định đã commit thì điền xong không có tác dụng) và **dưới** biến môi trường (xoay key phải ăn ngay). → `SecretsFileConfigurationTests`

## An toàn

- Không commit `secrets.develop.json` / `secrets.production.json` (đã `.gitignore`)
- Hook chặn ghi `secrets.json` / `.env*` vào repo
- Key lộ qua chat → xoay lại trên nhà cung cấp

## vietapi + synthetic (dev smoke)

`secrets.develop.json` có thể trỏ `https://api.vietapi.tech/v1` với `SyntheticDataOnly: true` — chỉ smoke/connectivity.

Writing band + giải thích cá nhân hóa **không** đi qua reseller; cần OpenAI chính thức + `AllowCrossBorderTransfer` (PDPL/`B-2`).


## ObjectStorage — hai cách bố trí bucket

| Cách | Cấu hình | Khi nào |
|---|---|---|
| Mỗi loại nội dung một bucket (mặc định, compose local tạo sẵn) | `ExamAssetsBucket`, `DictationBucket`, `SpeakingRecordingsBucket` khác nhau; ba khóa `*Prefix` để trống | MinIO local, hoặc nhà cung cấp cho phép đặt rule retention/versioning theo bucket |
| **Một bucket, mỗi loại một folder** — `[QUYẾT ĐỊNH]` chủ sản phẩm 04/09/2026 | Ba khóa `*Bucket` cùng một tên; `ExamAssetsPrefix: "examassets/"`, `DictationPrefix: "dictation/"`, `SpeakingRecordingsPrefix: "speakingrecord/"` | Cloudflare R2 `vni-ielts-ai-dev` hiện tại |

Gate khởi động từ chối hai loại dùng chung bucket mà prefix trống hoặc lồng nhau. Bucket dùng chung **không được bật versioning** (readiness kiểm tra thật trên bucket — giọng nói học viên không được có lịch sử phiên bản sống lâu hơn lệnh xoá, PDPL). Rule retention khi đó đặt **theo prefix** trong console của nhà cung cấp. → [ADR-0016](../../../docs/decisions/0016-object-storage-one-bucket-prefix-per-class.md)

Audio đề thi không còn nằm trong git từ 04/09/2026. Máy mới clone về cần kéo audio về đúng folder API đọc:

```bash
dotnet run --project backend/tools/Vni.Ielts.AssetSync -- pull      # R2 → fixtures/exams/assets
dotnet run --project backend/tools/Vni.Ielts.AssetSync -- push      # fixtures/exams/assets → R2 (thêm đề mới)
```
