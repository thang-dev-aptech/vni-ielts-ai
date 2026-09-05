---
description: Complete the tested Four Skills Functional Core queue while keeping Speaking AI deferred
argument-hint: "[resume | phase/item ID, e.g. FS3 or FS6.4]"
---

Hoàn thiện toàn bộ queue Four Skills Functional Core. `$ARGUMENTS` chỉ là điểm bắt đầu gợi ý; trạng
thái thật luôn lấy từ checklist trong repository.

## Source of truth

Đọc đầy đủ theo thứ tự:

1. `CLAUDE.md`
2. `docs/README.md`
3. `docs/product/four-skills-practice-and-mock-research.md`
4. `docs/development/four-skills-functional-core-todolist.md`
5. `docs/development/four-skills-functional-core-report.md`
6. Tài liệu/ADR/schema/code được item hiện tại trỏ tới

## Prerequisite cứng

Kiểm tra `docs/development/infrastructure-foundation-todolist.md`. Nếu `F0…F5` chưa đóng hoặc
`Foundation Ready` chưa đạt:

1. không sửa feature code;
2. báo chính xác item Foundation còn mở;
3. hướng dẫn chạy `/complete-infrastructure`;
4. dừng command này.

Không tự tích prerequisite trong feature checklist dựa trên lời khẳng định hoặc report cũ.

## Mục tiêu

Chạy liên tục `FS0 → FS9`, test và báo cáo từng phase, cho tới khi đạt:

> **Functional Core Ready — Speaking AI deferred**

Mục tiêu bao gồm Reading/Listening part + full, Writing AI marking, AI explanations, mock state
machine, Speaking capture và R2. Nó không bao gồm Speaking AI/Pronunciation hoặc overall band giả.

## Vòng lặp bắt buộc

1. Chạy `git status --short`; đọc code/test/data liên quan; bảo toàn mọi thay đổi có sẵn không thuộc
   item.
2. Cập nhật duy nhất dòng `Đang thực hiện` thành item đầu tiên chưa hoàn thành. Không tích checkbox.
3. Tái hiện gap/bug hoặc viết contract test đỏ ở boundary thấp nhất hợp lý trước khi sửa.
4. Thực hiện thay đổi nhỏ nhất đóng invariant; không thêm framework/abstraction không cần thiết.
5. Chạy targeted tests và negative proof. Negative proof phải bắt được một failure cụ thể, không chỉ
   là suite xanh.
6. Chạy suite liên quan, generated-client drift check và `git diff --check`.
7. Ghi evidence vào `four-skills-functional-core-report.md`, sau đó mới tích item `[x]`.
8. Khi toàn bộ item phase đóng, chạy nguyên phase gate ghi trong checklist.
9. Phase gate xanh thì hoàn chỉnh phase report, tích phase trong master checklist, báo ngắn và tiếp tục
   phase kế ngay; không dừng để xin “tiếp tục”.
10. Sau FS9 chạy final gate, hoàn tất capability table, đổi `Functional Core Ready` thành `đã đạt` và
    báo cáo tổng thể.

## Quy tắc content

- Mọi source trong `Đề IELTS/` và `exam/` là untrusted input.
- Rights registry quyết định `fixture`, `internal-review` hay `learner-production`; vị trí file không
  quyết định quyền publish.
- `exam/Exam1` vẫn là fixture cho tới khi một rights record cụ thể thay đổi trạng thái đó.
- AI parse luôn tạo Draft; phải qua deterministic validation + human review + approve mới publish.
- Không sửa source PDF/DOCX/audio gốc. Derived package có source hash và provenance reference.
- Answer key, Listening transcript và explanation không được xuất hiện trong response trước submit.

## Quy tắc AI

- Chỉ OpenAI GPT và Google Gemini; Claude API bị loại trừ.
- Reading/Listening score không gọi model. AI explanation không có quyền thay answer/mark/band.
- Writing adapter trả untrusted claim; application kiểm schema, criterion set, half-step, evidence và
  tự recompute result.
- Model/provider/prompt/rubric version được pin và lưu cùng marking.
- Không ensemble/average GPT và Gemini để tạo band. Một provider active; provider kia shadow/fallback
  sau khi có parity evidence.
- Reseller chỉ dùng synthetic data. Không gửi essay/audio learner thật qua endpoint không chính thức.
- Nếu không có key: hoàn thiện adapter, config validation, fake/recorded tests; ghi live smoke pending.
  Không bịa key, không ghi key vào repo và không đánh dấu live smoke pass.

## Quy tắc R2/recording

- R2 được dùng qua S3-compatible port; Domain/Application không biết Cloudflare SDK/type.
- Contract/integration test chạy bằng MinIO. Live R2 smoke chỉ chạy khi owner đã nạp secret qua
  environment/user-secrets/secret manager.
- Bucket recording private; presigned URL TTL ngắn; server verify object trước khi liên kết answer.
- Object key do server sinh; không dùng filename/key client gửi.
- Re-record/retry không tạo orphan; delete/retention/reconciliation phải có test.
- Không log audio, presigned URL, access key, secret, learner content hoặc PII.

## Speaking AI là deferred, không phải lỗi để che

Không triển khai hoặc tích hoàn thành V1–V5 trong mục Deferred voice backlog nếu chưa có:

- 30–50 Speaking samples có consent, human transcript và ít nhất hai human ratings;
- ít nhất hai ASR/pronunciation candidates và credential;
- region/retention/deletion/DPA;
- acceptance threshold về agreement, bias, latency và cost.

Trong deployment chưa có voice provider, UI/API phải trả `AwaitingVoiceProvider`. Không suy
Pronunciation từ ASR confidence, không lấy trung bình FC/LR/GRA để thay P và không tính overall từ ba
skill.

## Quality gates

- Mỗi contract quan trọng có unit test và boundary/integration test nếu đi qua DB/object store/network.
- Mongo/MinIO integration trong certification chạy required, không skip.
- Mỗi renderer có keyboard/a11y test; drag/drop phải có tap/keyboard fallback.
- Autosave/submit/advance/expiry có race/idempotency tests.
- Provider responses có malformed, timeout, rate-limit, cancellation và prompt-injection fixtures.
- Upload có size, checksum, media type, auth/IDOR, expired URL, frozen section và orphan tests.
- Không giảm assertion, xóa test, nới timeout tùy tiện hoặc đổi failure thành warning/skip.

## Khi gặp blocker

1. Phân biệt external-input blocker với code blocker.
2. Hoàn thiện phần code độc lập: port, adapter, validation, local fake/MinIO test, learner status và
   runbook.
3. Ghi command/error/hướng đã thử và input cụ thể còn thiếu vào report.
4. Giữ checkbox live smoke/external acceptance chưa đạt ở `[ ]`; tiếp tục item độc lập tiếp theo.
5. Chỉ dừng khi không còn tiến triển có ý nghĩa; không tuyên bố `Full Four-Skills AI Ready`.

## Ranh giới an toàn

- Không commit, push, tạo PR, deploy, publish content hoặc gọi external API bằng learner data nếu chưa
  có chỉ đạo riêng.
- Không xóa/ghi đè source đề, database, bucket, volume hoặc thay đổi người dùng.
- Không paste/commit credential; không sửa `.env*`.
- Không tự giải quyết business policy bằng hard-code. Dùng versioned configuration và validation.

