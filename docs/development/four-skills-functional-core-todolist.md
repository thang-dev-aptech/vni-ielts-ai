# Four Skills Functional Core — implementation todo

> **Ngày lập:** 28/08/2026  
> **Trạng thái:** kế hoạch có thể thực thi; chưa bắt đầu implementation.  
> **Mục tiêu đóng checklist:** hoàn thiện Practice Reading/Listening/Writing, toàn bộ exam runner,
> Mock state machine, AI explanation, Writing AI marking và Speaking capture lên R2. Speaking AI và
> overall band bốn kỹ năng được giữ ở trạng thái trung thực cho đến khi đủ dữ liệu/model voice.

## 1. Kết luận phạm vi

Repository và dữ liệu hiện có đủ để triển khai phần lớn chức năng chính mà không chờ voice provider:

| Khả năng | Có thể hoàn thiện trong queue này? | Kết quả khi đóng queue |
|---|---:|---|
| Reading practice theo part | Có | làm bài, autosave, submit, raw/accuracy, review và AI explanation |
| Reading full skill | Có | 3 passage/40 response slot; band chỉ hiện khi package có bảng hợp lệ |
| Listening practice theo part | Có | audio, renderer, autosave, raw/accuracy, review và AI explanation |
| Listening full skill | Có | 4 part/40 response slot; band chỉ hiện khi package có bảng hợp lệ |
| Writing Task 1/Task 2 lẻ | Có | AI-estimated criterion bands, evidence và feedback |
| Writing full skill | Có | hai task được đánh giá; application ghép band theo profile được duyệt |
| Speaking runner | Có | 3 part, prep/response timer, record/re-record, upload và recovery |
| Speaking lưu recording trên R2 | Có | private object, metadata, retention seam, retry/reconciliation |
| Speaking AI/Pronunciation | **Chưa** | trả `AwaitingVoiceProvider`, không đoán band |
| Full mock 4 kỹ năng | Có điều kiện | chạy đủ sequence; Speaking/overall pending nếu voice marking chưa bật |
| Overall IELTS band | **Chưa trọn vẹn** | chỉ tính khi cả bốn skill đều có band hợp lệ |

Tên chứng nhận cuối queue là **Functional Core Ready — Speaking AI deferred**, không phải “full AI
four-skills complete”.

## 2. Bằng chứng dữ liệu đã kiểm kê

### 2.1 `Đề IELTS/`

- 138 file, khoảng 1.39 GiB.
- 14 PDF:
  - Cambridge IELTS 16, 17, 18, 19, 20 và 21;
  - rubric/key criteria và band descriptors cho Writing/Speaking;
  - official-style Writing sample tasks và examiner comments;
  - Writing topics 2025, Speaking prediction 05–08/2026 và band calculator.
- 32 DOCX của VOL 9:
  - 8 Reading test, mỗi test có 3 passage và key tương ứng;
  - 8 Listening test, key/transcript document và audio tương ứng.
- 92 audio/video cho nguồn đề: 68 MP3, 16 M4A và 8 MP4.
- Cộng với 4 MP3 trong `exam/Exam1`, toàn workspace có 96 media file, khoảng 15.83 giờ; `ffprobe`
  đọc được 96/96.

**Nguồn ưu tiên để pilot:** VOL 9 Test 1 vì câu hỏi, key và audio nằm thành các file riêng, dễ tạo một
package có evidence hơn việc parse PDF Cambridge trước.

### 2.2 `exam/Exam1/`

- 41 file, 20.7 MiB; đã có package bốn kỹ năng dạng JSON/Markdown/HTML.
- Reading: 3 passage, 40 câu.
- Listening: 4 part, 36 question object nhưng 40 mark; đây là bằng chứng thực tế cho nhu cầu
  `ResponseSlot` vì các câu `Choose TWO/THREE` mang nhiều số đáp án.
- Writing: 2 task.
- Speaking: 3 prompt do lần chuẩn hóa trước tự soạn.
- Reading/Listening đã có answer key và nội dung đã từng được mô phỏng 40/40.

`exam/Exam1/README.md` hiện ghi provenance chưa xác lập và chỉ cho phép dùng làm fixture. Chỉ đạo mới
nói **một số** đề đã có quyền phân phối, chưa chỉ rõ file nào. Vì vậy queue bắt buộc tạo rights registry
theo từng source; không được suy ra rằng mọi file trong hai thư mục đều được publish.

### 2.3 Dữ liệu còn thiếu

- Không có corpus Speaking của học viên với:
  - original audio;
  - human transcript;
  - ít nhất hai human ratings cho FC/LR/GRA/Pronunciation;
  - consent cho provider bake-off.
- Chưa có bảng raw→band đã equate theo từng test version. File band calculator chỉ đủ làm nguồn tham
  khảo/generic profile, không tự biến thành bảng chính thức cho từng đề.
- Writing có rubric và một bộ sample task/examiner comments để bắt đầu, nhưng chưa có 30–50 bài VNI
  trải nhiều band. Chủ sản phẩm chấp nhận AI chấm, nên functional path được làm ngay; kết quả phải ghi
  rõ `AI-estimated` và lưu model/prompt/rubric version cho đến khi có calibration rộng hơn.

## 3. Quyết định đã khóa cho queue

1. GPT và Gemini là hai LLM provider được phép; Claude API không dùng.
2. Writing được phép trả đánh giá và điểm AI cho learner sau khi output qua schema/range/evidence
   validation phía server.
3. Reading/Listening vẫn chấm deterministic; AI chỉ giải thích.
4. Cloudflare R2 là production candidate cho Speaking recording; code vẫn đi qua S3-compatible port và
   test bằng MinIO, không đưa type R2 vào Domain/Application.
5. Secret chỉ nạp qua environment/secret manager hoặc user-secrets; không paste vào tài liệu/chat và
   không commit.
6. Voice AI làm sau. Queue hiện tại phải hoàn thiện seam, capture, upload, status và retry nhưng không
   tự tạo transcript/Pronunciation band.
7. Practice part lẻ hiển thị raw + accuracy. Chỉ hiển thị `estimated band` khi unit có calibration
   profile riêng.
8. Unknown business policy là configuration có validation; không hard-code một đáp án ngầm.

## 4. Điều kiện bắt đầu

- [ ] Infrastructure queue `F0…F5` trong
  [`infrastructure-foundation-todolist.md`](infrastructure-foundation-todolist.md) đã đóng và
  `Foundation Ready` đã đạt.
- [ ] `git status --short` được ghi vào report; mọi thay đổi có sẵn được bảo toàn.
- [ ] Chủ sở hữu tạo rights registry chỉ rõ từng source được phép dùng ở `fixture`, `internal-review`
  hay `learner-production`.

Không bắt đầu feature implementation khi Foundation chưa đạt. Dùng `/complete-infrastructure` trước;
sau đó mới dùng `/complete-four-skills-core`.

## 5. Quy tắc thực thi bắt buộc

1. Chạy theo thứ tự `FS0 → FS9`; trong một phase chỉ song song các item không sửa cùng boundary.
2. Giữ đúng một item ở dòng `Đang thực hiện`.
3. Không tích `[x]` trước khi code, targeted test, negative proof và report đều hoàn thành.
4. Negative proof phải chứng minh gate bắt được lỗi cũ/sai invariant; không phá worktree để diễn test đỏ.
5. Không giảm assertion, biến failure thành skip, nới timeout tùy tiện hoặc test mock đúng chính
   implementation đang test.
6. Mỗi phase ghi vào
   [`four-skills-functional-core-report.md`](four-skills-functional-core-report.md): thay đổi, command,
   exit code, test count, negative proof, artifact, risk và git state.
7. Sau phase gate xanh mới tích phase trong master checklist rồi tiếp tục phase kế.
8. Nếu thiếu external key, hoàn thiện adapter + configuration validation + fake/recorded contract test;
   ghi live smoke là pending. Không được đánh đồng “không có key” với “code chưa hoàn thiện”.
9. Không publish source chưa có `learner-production` right.
10. Không log essay, answer key, audio, transcript, token hay PII.

## 6. Trạng thái điều phối

- **Đang thực hiện:** `FS1.1` — Exam Package schema v2 và `ResponseSlot`
- **Phase hiện tại:** FS1
- **Functional Core Ready:** chưa đạt
- **Speaking AI:** deferred có chủ đích
- **Báo cáo:**
  [`four-skills-functional-core-report.md`](four-skills-functional-core-report.md)
- **Prompt Claude Code:**
  [`.claude/commands/complete-four-skills-core.md`](../../.claude/commands/complete-four-skills-core.md)

### Master checklist

- [x] **FS0 — Content provenance, baseline và contract freeze** — đóng 29/08/2026
- [ ] **FS1 — Exam Package v2 và ResponseSlot**
- [ ] **FS2 — Import/conversion pipeline và pilot content**
- [ ] **FS3 — PracticeUnit catalogue và session scope**
- [ ] **FS4 — Reading/Listening practice runner**
- [ ] **FS5 — Deterministic results và AI explanation**
- [ ] **FS6 — Writing runner và GPT/Gemini marking**
- [ ] **FS7 — Mock state machine bốn kỹ năng**
- [ ] **FS8 — Speaking capture và Cloudflare R2**
- [ ] **FS9 — Hardening, E2E và Functional Core certification**

---

## 7. FS0 — Content provenance, baseline và contract freeze

### Checklist

- [x] **FS0.1 · Content rights registry**
  - Tạo record có `sourceId`, path/hash, owner, license/proof reference, allowed environments, expiry và
    reviewer.
  - Gắn riêng từng Cambridge book, VOL 9 test, Writing/Speaking resource và `Exam1`.
  - Import được source không có quyền publish nhưng publish endpoint phải từ chối.
- [x] **FS0.2 · Machine-readable content inventory**
  - Script read-only kiểm kê file, checksum, media duration/codec và cặp test↔key↔audio.
  - Báo file thiếu/cặp mơ hồ; không dựa vào filename thủ công trong application.
- [x] **FS0.3 · Product/config decisions**
  - `sequenceProfile`, part-score policy, partial-credit profile, explanation policy, practice-history
    policy và Writing task weights đều là dữ liệu versioned.
  - Default hiện tại không được đổi ngầm; mỗi package phải khai báo hoặc bị validation từ chối ở nơi
    giá trị là bắt buộc.
- [x] **FS0.4 · AI/R2 secret contract**
  - Chốt tên biến cấu hình, startup validation, synthetic-data guard và redaction.
  - Ghi setup R2 CORS/lifecycle/bucket nhưng không ghi credential.
- [x] **FS0.5 · Baseline executable**
  - Chạy docs, format, typecheck, web/API tests và E2E hiện có theo Foundation gate.
  - Ghi test counts thực tế và các test đang skip; không copy số cũ.
- [x] **FS0.6 · Gate integrity** — *hạng mục do orchestrator chèn thêm 29/08/2026, không có trong kế
      hoạch gốc*
  - `FS0.5` phát hiện hai gate báo thành công mà không kiểm tra gì: `check-test-skips.mjs` nuốt im
    lặng một report không parse được (`catch { return null; }`), và `pnpm check` gọi `test:api` mà
    không đặt `VNI_REQUIRE_MONGO`/`VNI_REQUIRE_MINIO`, nên một suite bỏ qua toàn bộ 164 điểm điều
    kiện vẫn báo pass.
  - Lý do chèn: quy tắc thực thi 3–5 của chính kế hoạch này đòi negative proof và cấm biến failure
    thành skip. Không thể lấy bằng chứng phase gate đáng tin từ những gate fail-open.
  - Phát hiện thứ ba trong lúc chứng minh: `_artifacts/verify/test-results` chưa bao giờ được dọn,
    nên số đếm test lịch sử đọc từ đó đã bị thổi phồng.

### Phase gate FS0

- [x] Một source không có production right bị publish từ chối bằng integration test.
      3 integration test đỏ khi gỡ guard, trong khi test "nguồn **có** quyền vẫn publish được" giữ
      xanh — chứng minh là cổng, không phải từ chối vô điều kiện.
- [x] Inventory ghép đúng ít nhất VOL 9 Test 1 Reading/Listening với key và audio; file bị thay đổi
      hash được phát hiện.
      **Có điều kiện:** ghép cặp thật đã chạy trên máy có nội dung (171 file, 1.5 GB, 0 lỗi, 2 điểm
      mơ hồ đúng như dự đoán). `/exam/` và `/Đề IELTS/` bị gitignore nên CI **không tái lập được**
      khẳng định này; thứ commit được là 32 test trên fixture tổng hợp.
- [x] Startup log/config dump đã được chứng minh không lộ secret.
      Đỏ hai lần trước khi xanh — lần thứ hai hoàn nguyên `ObjectStorage:ServiceUrl` về đúng dòng có
      ở baseline và bắt được rằng dòng đó nội suy nguyên URL, mà service URL kiểu S3 có thể mang
      `https://key:secret@host`.
- [x] Báo cáo FS0 hoàn chỉnh rồi mới tích phase.
      → [`four-skills-functional-core-report.md`](four-skills-functional-core-report.md)
- Gate chạy: `node scripts/verify.mjs` → exit 1 · **27 passed · 1 failed · 1 not run**. Lần fail duy
  nhất là `restore-drill`, khoảng trống môi trường có sẵn từ baseline (thiếu `mongosh`/`mongodump`),
  không phải hồi quy. Mọi stage xanh ở baseline vẫn xanh.

---

## 8. FS1 — Exam Package v2 và ResponseSlot

### Checklist

- [ ] **FS1.1 · Schema v2 additive/migratable**
  - Bổ sung `formatProfile`, `sequenceProfile`, `scoringProfileRef`, `QuestionGroup`, `ResponseSlot[]`,
    explanation evidence reference và timing per part.
  - Một question có thể có nhiều slot/mark nhưng mỗi slot có số answer-sheet riêng.
- [ ] **FS1.2 · Domain mapping**
  - `ExamVersion → Section → SectionPart → QuestionGroup → Question → ResponseSlot` giữ immutable sau
    publish.
  - Không để persistence/provider type rò vào Domain/Application.
- [ ] **FS1.3 · v1 compatibility/migration**
  - Câu 1 mark v1 sinh một slot ổn định.
  - `multiple-select` nhiều mark được migration thành số slot đúng mà không đổi accepted answer
    semantics.
  - Session/result lịch sử vẫn đọc version cũ.
- [ ] **FS1.4 · Validation**
  - Slot number duy nhất/liên tục theo profile; answer key coverage; asset path/checksum; group options;
    band-table coverage; content rights và sequence invariants.
- [ ] **FS1.5 · API/OpenAPI/client**
  - Session view trả slot, part, group và public content; tuyệt đối không trả key/explanation trước
    submit.
  - Generate client và giữ drift gate xanh.

### Phase gate FS1

- Contract tests gồm: một question/two slots, gap inline nhiều slot, matching bank, v1 migration và
  unknown v2 major.
- Negative tests bắt duplicate slot, missing key, leaked answer key và scoring table sai range.
- `Exam1` sau migration vẫn có Reading 40 slot và Listening 40 slot dù Listening chỉ có 36 question
  object.

---

## 9. FS2 — Import/conversion pipeline và pilot content

### Checklist

- [ ] **FS2.1 · Hai đường nhập tách biệt**
  - Structured package đi thẳng vào deterministic validator.
  - PDF/DOCX/raw source đi qua extraction → GPT/Gemini structured parse → review draft → cùng validator.
  - AI output không được publish trực tiếp.
- [ ] **FS2.2 · Safe source extraction**
  - DOCX/PDF extraction có size/page/embedded-media caps, timeout, sandbox path và content hash.
  - Media được probe; asset được upload private qua object-storage port.
- [ ] **FS2.3 · Provider-neutral parser**
  - JSON Schema structured output, prompt/version/model/request metadata, retry giới hạn và cost metric.
  - GPT/Gemini selectable; fake/recorded response dùng trong test; reseller chỉ synthetic data.
- [ ] **FS2.4 · Review workflow**
  - CMS diff source↔parsed content, unresolved warnings, manual sửa, approve và publish permission.
  - Reviewer kiểm câu, option, word limit, accepted variants, transcript/evidence và asset mapping.
- [ ] **FS2.5 · Pilot VOL 9 Test 1**
  - Tạo package Reading + Listening đã kiểm 40 slot/skill.
  - Thêm Writing task có quyền sử dụng nếu rights registry cho phép.
  - Không bịa Speaking để biến package thành full mock.
- [ ] **FS2.6 · Cambridge batch readiness**
  - Xây batch job resumable/idempotent; chưa cần import hết sáu sách trong phase pilot.
  - Một source lỗi không làm mất các draft đã parse xong; publish vẫn từng exam version.

### Phase gate FS2

- Parser sai một answer/slot/asset bị validator hoặc review gate chặn.
- Pilot hoàn thành deterministic perfect-candidate round trip 40/40 cho R và L.
- Object checksum/media probe khớp sau upload/download.
- Không source nào được publish nếu rights registry chưa cho phép.

---

## 10. FS3 — PracticeUnit catalogue và session scope

### Checklist

- [ ] **FS3.1 · PracticeUnit projection**
  - Sinh unit theo `runKind: practice|mock`, `scope: part|skill|full-test`, module và part IDs.
  - Projection chỉ reference immutable `ExamVersion`; không copy passage/audio/key.
- [ ] **FS3.2 · Catalogue API**
  - `GET /api/v1/practice-units` filter skill/scope/variant; trả slot count, duration, availability và
    score capability (`raw`, `estimated-band`, `band`).
- [ ] **FS3.3 · Start-session contract**
  - Nhận `practiceUnitId`; server tự resolve mode/module/parts/timing, không tin client tự ghép scope.
  - Giữ compatibility có thời hạn cho request v1 và có deprecation test.
- [ ] **FS3.4 · Session part state**
  - Current part, per-part answer sheet, navigation, timer và submit scope được persist/reload.
  - Practice part/skill dùng open stopwatch; mock dùng deadline và không pause.
- [ ] **FS3.5 · History separation**
  - Practice part raw/estimated result không trộn vào full-mock IELTS trend.

### Phase gate FS3

- Một `ExamVersion` sinh đúng 3 Reading part unit + 1 Reading full-skill unit, 4 Listening part unit +
  1 Listening full-skill unit và một full mock khi đủ bốn skill.
- Client không thể start một unit với part/module khác projection.
- Publish version mới không đổi unit/session của version cũ.

---

## 11. FS4 — Reading/Listening practice runner

### Checklist

- [ ] **FS4.1 · Runner shell**
  - Route riêng; header/main/footer ổn định; exit confirm; save/connection states; mobile responsive.
- [ ] **FS4.2 · Header/timer**
  - Logo, skill icon, title, part; practice pause/resume server-side; target preset/custom; elapsed time.
  - Mock branch không có pause/target và chỉ hiển thị server deadline.
- [ ] **FS4.3 · Footer theo ResponseSlot**
  - Active part mở các ô số; saved answered có tick xanh + shape/text; part khác thu thành answered/total.
  - Previous/Next và focus đúng slot; không dùng màu như tín hiệu duy nhất.
- [ ] **FS4.4 · Reading layout**
  - Passage trái/questions phải ở desktop; mobile tab/stack; giữ scroll position từng part.
- [ ] **FS4.5 · Listening layout**
  - Audio/part state, preloading hợp lý, range request và retry; mock one-pass/seek policy lấy từ profile.
- [ ] **FS4.6 · Question renderers**
  - Inline completion có số slot, radio, checkbox, T/F/NG, Y/N/NG, matching/labelling bank.
  - Drag/drop có tap/click và keyboard fallback; không giữ `<select>` là trải nghiệm duy nhất.
- [ ] **FS4.7 · Autosave/offline**
  - Patch theo `responseSlotId`, revision + per-slot sequence, offline journal và final-save barrier trước
    navigation/submit.

### Phase gate FS4

- Component/a11y tests cho mọi renderer và keyboard-only drag/drop.
- Browser E2E desktop + mobile hoàn thành một Reading part và một Listening part.
- Fault tests: save out-of-order, offline/reconnect, audio 404/range failure, reload giữa part và submit
  double-click.
- Answer key/transcript/explanation không có trong pre-submit network response.

---

## 12. FS5 — Deterministic results và AI explanation

### Checklist

- [ ] **FS5.1 · Slot-based scorer**
  - Normalize case/space/punctuation theo profile; accepted variants; word-limit violation; multi-slot
    partial credit theo package.
- [ ] **FS5.2 · Result contract**
  - Raw/max/accuracy, per-slot correct/incorrect/unanswered, correct answer sau submit và score label rõ.
  - Part không calibration không trả IELTS band.
- [ ] **FS5.3 · Canonical explanation**
  - GPT/Gemini tạo khi import/publish; schema gồm answer, reason, evidence, common mistake,
    provider/model/prompt version; CMS review trước learner.
- [ ] **FS5.4 · Personalized explanation**
  - On-demand, sau submit, quota/rate limit/idempotency/cache; không thể thay mark.
- [ ] **FS5.5 · Evidence safety**
  - Reading evidence phải là span trong passage; Listening evidence là transcript span/timestamp nếu
    source có; invalid evidence bị refuse.
- [ ] **FS5.6 · Failure semantics**
  - Deterministic result hiện ngay; explanation pending/failed độc lập và retry được.

### Phase gate FS5

- Perfect/wrong/blank/variant/word-limit/multi-slot matrices cho R/L.
- Một model response cố thay correct answer/band bị schema/application từ chối.
- Cache chứng minh cùng canonical question không gọi provider lại cho mỗi learner.
- Provider timeout không trì hoãn hoặc làm mất deterministic score.

---

## 13. FS6 — Writing runner và GPT/Gemini marking

### Checklist

- [ ] **FS6.1 · Versioned rubric data**
  - Trích rubric được phép dùng thành artifact có `rubricVersion`, `descriptorSource`, hash và effective
    date; không hard-code descriptor rải rác trong adapter.
- [ ] **FS6.2 · Writing editor**
  - Task prompt/image, editor, word count, autosave, no spellcheck/autocorrect trong mock, submit confirm
    và recovery.
- [ ] **FS6.3 · OpenAI adapter**
  - Official Responses API/structured output, timeout/retry/cancellation, request metadata, safe logging
    và configuration validation.
- [ ] **FS6.4 · Gemini adapter**
  - Cùng application contract; JSON Schema output; provider/model selectable bằng policy.
- [ ] **FS6.5 · Server validation**
  - Đủ TA/TR, CC, LR, GRA; band đúng half-step/range; evidence quote thật sự tồn tại trong essay;
    application recompute band và từ chối malformed claim.
- [ ] **FS6.6 · Task/full result**
  - Task lẻ trả `AI-estimated task evaluation`, không gọi full Writing band.
  - Full skill ghép Task 1/2 bằng versioned scoring profile; model không tự tính module band.
- [ ] **FS6.7 · Initial evaluation set**
  - Dùng official sample tasks/examiner comments được phép làm golden seed.
  - Lưu chênh lệch model↔reference, prompt/model version và failure analysis.
- [ ] **FS6.8 · Production controls**
  - Feature flag, primary/fallback có kiểm soát, budget/rate limit, outbox retry/dead-letter và learner
    status `pending|completed|failed`.

### Phase gate FS6

- Recorded-response tests của cả GPT/Gemini; prompt injection trong essay không phá output contract.
- Evidence giả, thiếu criterion, band 6.3, model-reported average sai đều bị refuse.
- Worker restart/duplicate delivery chỉ lưu một marking; result pending chuyển completed mà không reload
  mất dữ liệu.
- Live synthetic smoke là conditional gate khi key được nạp; không dùng learner data qua reseller.

---

## 14. FS7 — Mock state machine bốn kỹ năng

### Checklist

- [ ] **FS7.1 · Package-driven sequence**
  - Không hard-code Reading→Listening hiện tại; sequence lấy từ versioned profile.
  - Hỗ trợ high-fidelity `Listening → Reading → Writing` và Speaking block riêng, hoặc product
    simulation có label rõ.
- [ ] **FS7.2 · Transition rules**
  - `Next` đóng/freeze skill, final-save, enqueue marking và mở skill kế idempotently.
  - Skill đã đóng không sửa lại; trong skill hiện tại được navigate part theo profile.
- [ ] **FS7.3 · Deadlines**
  - Mỗi skill server-authoritative; no pause; expiry race với submit/advance an toàn.
- [ ] **FS7.4 · Result aggregation**
  - R/L deterministic hiện ngay; Writing pending; Speaking `AwaitingVoiceProvider` nếu voice chưa có.
  - Overall chỉ tính khi bốn valid bands tồn tại; không lấy trung bình ba skill.
- [ ] **FS7.5 · Resume/recovery**
  - Reload/device reconnect mở đúng skill/part; closed session vào results; retry không advance hai lần.

### Phase gate FS7

- E2E chạy một mock rút gọn qua bốn skill với fake evaluator/transcript adapter.
- Production-default no-voice run kết thúc trung thực với Speaking/overall pending, không crash và không
  band giả.
- Race tests submit/advance/expiry/double-click và worker duplicate đều giữ đúng một transition/marking.

---

## 15. FS8 — Speaking capture và Cloudflare R2

### Checklist

- [ ] **FS8.1 · Recording contract v2**
  - Init/complete flow; server tạo object key từ session/question/attempt, không nhận key/filename tùy ý.
  - Metadata Mongo giữ owner, question, content type, size, checksum, status, created/retention time.
- [ ] **FS8.2 · R2-compatible private store**
  - Thêm recording bucket riêng vào S3-compatible options và adapter; MinIO là contract-test target.
  - Không public object; presigned upload/download TTL ngắn; server `HEAD` verify trước khi file vào sheet.
- [ ] **FS8.3 · Resumable/retry**
  - Multipart khi vượt threshold; abort stale uploads; retry/re-record thay revision cũ, không tạo orphan.
- [ ] **FS8.4 · Web recorder**
  - Permission, input-level/waveform, prep/response timer, record/stop/re-record, local durability trước
    upload, progress/retry và lost-network recovery.
- [ ] **FS8.5 · Native seam**
  - Web implementation giữ interface phù hợp Capacitor native plugin; native plugin không phải gate cho
    web Functional Core nếu chưa có mobile build target.
- [ ] **FS8.6 · Retention/deletion/reconciliation**
  - Lifecycle là config không default phá hủy; delete account/attempt đi tới object store; orphan sweep
    có age bound; audit không chứa audio URL dài hạn.
- [ ] **FS8.7 · No-voice result state**
  - Recording hoàn tất nhưng chưa có ASR trả `AwaitingVoiceProvider`; có hướng dẫn rõ cho learner/admin.

### Phase gate FS8

- MinIO integration: init→upload→complete→HEAD→sheet link; checksum/size/type sai bị từ chối.
- Negative tests: traversal key, upload của user khác, expired URL, frozen section, orphan/re-record và
  R2/MinIO unavailable.
- Real-server browser test upload file >1 MiB; TestServer-only test không được dùng làm bằng chứng cho
  Kestrel body limit.
- R2 live smoke conditional khi owner nạp key; report chỉ ghi pass/fail, không ghi endpoint/key nhạy cảm.

---

## 16. FS9 — Hardening, E2E và Functional Core certification

### Checklist

- [ ] **FS9.1 · Security/privacy**
  - Authorization/IDOR, answer-key leak, upload abuse, model prompt injection, secret scan, retention và
    audit redaction.
- [ ] **FS9.2 · Accessibility/responsive**
  - Keyboard/screen reader, reduced motion, colour-independent states, phone/tablet/desktop và long
    content/zoom.
- [ ] **FS9.3 · Performance/reliability**
  - Catalogue/session/autosave latency, audio range/cache, concurrent uploads, AI queue backpressure,
    provider timeout/circuit breaker và object-store outage.
- [ ] **FS9.4 · Full regression**
  - Docs, format, typecheck, unit, API, integration với required Mongo/MinIO, browser E2E và build đều
    xanh; không skip dependency tests trong certification run.
- [ ] **FS9.5 · Operational docs**
  - Runbook provider/R2 configuration, key rotation, replay/dead-letter, content publish rollback,
    recording deletion và AI disable switch.
- [ ] **FS9.6 · Final report**
  - Tổng hợp capability đã chạy, content đã import, test counts, live-smoke status, cost/latency quan
    sát được, known limitations và deferred voice backlog.

### Final gate FS9

- Một learner có thể hoàn thành part/full Reading, part/full Listening, Writing task/full, Speaking
  recording và một mock bốn skill trên browser.
- R/L score không cần AI; explanation failure không làm mất score; Writing AI output được validate;
  recording không nằm trong Mongo GridFS ở cấu hình R2.
- No-voice deployment không overclaim: Speaking và overall không có band.
- Tất cả phase report có command + exit code + negative proof; master checklist mới được đóng.

## 17. Deferred voice backlog — không được tích hoàn thành trong queue hiện tại

| ID | Hạng mục | Vì sao chưa làm được | Chủ sản phẩm cần bổ sung |
|---|---|---|---|
| V1 | ASR + word timestamps | Chưa chọn/enable voice provider và chưa đo WER trên giọng Việt | Credential cho ít nhất hai candidate; ưu tiên Gemini Transcribe + Deepgram/Azure |
| V2 | Pronunciation/prosody | GPT/Gemini transcript không tự cung cấp phoneme/prosody score đáng tin | Azure Pronunciation hoặc Speechace account; region/retention/DPA |
| V3 | Speaking calibration | Không có audio có human transcript và criterion bands | 30–50 full Speaking samples, consent, ít nhất hai raters, band 4–8 và nhiều thiết bị/vùng giọng |
| V4 | Acceptance threshold | Không thể định nghĩa accuracy từ vendor marketing | Ngưỡng agreement ±0.5, bias ceiling, latency/cost ceiling sau bake-off |
| V5 | Production voice scoring | Phụ thuộc V1–V4 và data-transfer approval | Chốt provider, data region, retention/deletion và budget |

Khi V1–V5 đủ, tạo queue riêng **Speaking AI Completion**. Queue đó mới được phép đổi chứng nhận từ
`Speaking AI deferred` thành `Full Four-Skills AI Ready`.

## 18. External input còn cần nhưng không chặn viết code

| Input | Code có thể làm trước? | Khi nào trở thành blocker |
|---|---:|---|
| OpenAI/Gemini key | Có, dùng fake/recorded contracts | live provider smoke và production enable |
| R2 endpoint/access/secret | Có, dùng MinIO | R2 live smoke/deployment |
| R2 CORS/lifecycle values | Có seam + validation | browser direct upload production |
| Danh sách source có quyền cụ thể | Có importer/fixture | bất kỳ publish cho learner |
| Per-test raw→band table | Có scorer/profile schema | hiển thị band thay vì raw/accuracy |
| Writing calibration set lớn | Có functional AI marking | tuyên bố calibrated accuracy thay vì AI-estimated |

