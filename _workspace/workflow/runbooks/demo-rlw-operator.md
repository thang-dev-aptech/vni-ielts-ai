# Operator Runbook — Demo Reading / Listening / Writing

**Audience:** bạn vận hành máy demo trước khi sếp ngồi vào.  
**Framing bắt buộc:** Development / nội bộ — đề borrowed (Exam1, Cambridge, VOL9) **không** phải LearnerProduction (`P-21`).

## AI (đã cấu hình)

| Key | Value |
|---|---|
| BaseUrl | `https://apithat.dev/v1` (reseller — **không** để trống) |
| Model | `gpt-5.5` |
| SyntheticDataOnly | `false` |
| AllowCrossBorderTransfer | `true` (rủi ro PDPL đã chấp nhận tạm — nói rõ khi demo Writing) |

Không dán API key vào chat / slide.

## Thứ tự bật (mỗi buổi demo)

```powershell
# 1) Infra (Mongo rs0 :27018 + MinIO — MinIO optional vì exam assets đang R2)
pnpm infra:up

# 2) API
dotnet run --project backend/src/Vni.Ielts.Api

# 3) Worker — BẮT BUỘC cho Writing AI (cùng secrets.develop.json + Mongo 27018)
#    Quan trọng: đặt URL riêng — nếu không, Worker tranh cổng 5000 với API.
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://localhost:5010'
dotnet run --project backend/src/Vni.Ielts.Worker --no-launch-profile

# 4) Learner web
pnpm --filter @vni/web dev
# → http://localhost:5173
```

Admin CMS (`@vni/admin`) **không** bắt buộc cho demo học viên.

## JWT

`Jwt:SigningKey` đã set ổn định trên máy này (length ≥ 32). **Không restart API giữa demo** nếu chưa chắc key còn trong secrets — restart với key ổn định thì session giữ được; key trống thì mọi token chết.

## Đề nên dùng

| Paper | R | L audio trên R2 | W | Band số R/L |
|---|---|---|---|---|
| Exam 1 | ✓ | ✓ (đã xác nhận remote) | ✓ | **Ẩn** — bảng chưa equated (`P-11`) → hiện điểm thô + lý do |
| VOL9 Test 1 | ✓ | ✓ | ✓ | Ẩn như trên |
| Cambridge 16–19 / VOL9 2·4·6 | ✓ | ✓ (nhiều file trên R2) | ✓ | Ẩn trừ khi provenance equated |

Trước khi sếp vào: mở Listening của đúng đề, xác nhận audio phát được (HTTP 200/206).

## Script demo ~45 phút (theo BA)

1. Đăng nhập learner → `/practice` — Practice vs Mock tách (`P-04`).
2. **Practice Reading** → nộp → kết quả: điểm thô / đúng-sai / cột đề; band chỉ khi verified.
3. **Practice Listening** → phát audio → nộp → review đáp án.
4. **Practice Writing** → Task 1 + Task 2 → chờ Worker → 4 tiêu chí + band tổng 1:2 + nhãn **AI · tham khảo** (`P-12`, `P-13`).
5. **Mock** `/practice?mode=full` → Reading → Next → Listening → Next → Writing → Submit. **Không** nói overall band 3 kỹ năng nếu API không trả (`G-11`).

## Câu phải nói khi demo

- Writing: AI tham khảo, không phải IELTS chính thức (`P-13`).
- Band R/L: chỉ hiện khi bảng quy đổi đã xác minh (`P-11`); fixture hiện tại thường chưa equated.
- Nội dung đề: fixture / nội bộ, chưa RightsProof → không publish production (`P-21`).
- Reseller apithat: chuyển dữ liệu qua biên giới — rủi ro PDPL đã nhận, chưa DPA/CTIA xong (`B-2`).

## Khi Writing treo “đang chấm”

1. Worker có chạy không? Log có claim job không?
2. `Ai:OpenAi` BaseUrl/Model còn `apithat.dev` / `gpt-5.5`?
3. Nút “kiểm tra lại” trên trang kết quả.
4. Timeout reseller — chờ / thử lại; không bịa band.

## Checklist sáng mai (bạn cung cấp / xác nhận)

- [ ] Mongo `:27018` healthy
- [ ] API + Worker + web cùng lên
- [ ] Tài khoản learner demo (email/password hoặc Google SSO đã cấu hình)
- [ ] Listening audio đề chọn phát được
- [ ] Một lần Writing smoke thành công trước khi sếp vào (5–10 phút)
- [ ] Không restart API giữa buổi
