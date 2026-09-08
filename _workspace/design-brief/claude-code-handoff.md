# Bàn giao cho Claude Code — VNI IELTS AI, giai đoạn code-first

Đọc hết file này trước khi làm bất cứ gì. Đây là nguồn nhiệm vụ duy nhất cho giai đoạn hiện tại.

## Bối cảnh

Chủ sản phẩm chốt 22 quyết định ngày 06/09/2026 (`P-01`…`P-22`). Chiến lược đã đổi: **hoàn thiện code và API trước, giao diện làm sau**. Chủ sản phẩm sẽ tự mô tả giao diện ở giai đoạn sau; lúc đó việc còn lại phải chỉ là nối dữ liệu vào layout.

**Không làm redesign giao diện trong giai đoạn này.** Chỉ chạm vào UI ở đúng lát cắt `S1` và `S8` dưới đây.

## Nguồn đọc

| File | Vai trò |
|---|---|
| `_workspace/design-brief/vni-mvp-blueprint.html` | **22 quyết định `P-01`…`P-22`** + IA đích + hợp đồng dữ liệu Result/Review + vòng đời đề. Mở bằng trình duyệt hoặc đọc thẳng HTML |
| `_workspace/design-brief/vni-claude-design-prompt.md` | Brief giao diện. Ở giai đoạn này **chỉ dùng để biết mỗi màn cần dữ liệu gì**, không dùng để dựng giao diện |
| `docs/VNI_IELTS_AI_COMPLETE_REDESIGN_PROMPT.md` | Bộ UX `D-1`…`D-12` chốt 04/09 |

**Quy tắc ưu tiên:** `P-*` (06/09) mới hơn `D-*` (04/09). Khi hai bên mâu thuẫn, `P-*` thắng. Các chỗ mâu thuẫn đã liệt kê trong mục B của `vni-design-brief.html`. `D-10` (hệ thống thị giác) không mâu thuẫn với `P-*` và vẫn có hiệu lực.

## Năm luật xuyên suốt

1. **Giao diện chỉ hiển thị, không quyết định.** Mọi quyết định nghiệp vụ phải nằm trong dữ liệu API trả về hoặc trong cấu hình. Một component không được tự suy ra band, thời lượng, nguồn chấm hay quyền hạn.
2. **Không bịa luật nghiệp vụ** (`G-11`). Chưa có luật thì làm thành seam cấu hình với hiện thực rỗng, không phải giá trị mặc định tự nghĩ ra.
3. **Bằng chứng, không phải lời khai.** Một mục chỉ được đóng khi có test đã được kiểm chứng là **đỏ khi gỡ fix ra**. Suite xanh không phải bằng chứng.
4. **Một lát cắt một lần.** Làm xong một lát, báo cáo, dừng chờ duyệt. Không chạy trước.
5. **Không đụng danh sách bảo toàn** (`D-11`): `AuthContext`, `RequireAuth`, `lib/session.ts`, `lib/api.ts`, `examApi.ts`, `useAnswerSheet.ts`, `usePracticeClock.ts`, `sessionProjection.ts`, `SpeakingRecorder.tsx` (máy trạng thái + upload), `recordingDraft.ts`, `AudioPlayer.tsx`. Sửa được bố cục và chữ, không sửa logic.

---

# Các lát cắt, theo thứ tự

## S0 — Ghi quyết định vào repo (không code)

Bắt buộc làm trước. `CLAUDE.md` hiện **đang sai** so với thực tế: nó vẫn ghi "chưa có token pricing", "Speaking marking đã build chờ bật", và một danh sách blocker cũ. Mọi phiên Claude sau đó đều đọc file này.

- Thêm `P-01`…`P-22` vào `docs/requirements/confirmed.md`, cột Source = `Owner decision 06/09/2026`
- Lưu nội dung blueprint thành `docs/product/mvp-blueprint.md`
- Cập nhật `CLAUDE.md`: phần *Current phase* và đoạn kiểm kê "Built and running / Not built"
- Ghi rõ trong `docs/README.md`: `P-*` là quyết định sản phẩm 06/09, `D-*` là quyết định UX 04/09, `P-*` thắng khi mâu thuẫn
- Đóng các câu hỏi đã có lời giải: `M-30`, `B-12`, `B-13` (đóng bởi `P-04`), `M-10` (bởi `P-19`), `H-4` (bởi `P-11`), `H-8b` (bởi `P-12`), `H-8a` (bởi `P-13`), `M-53` (bởi `P-21`)

**Xong khi:** `node scripts/check-docs.mjs` xanh; đọc `CLAUDE.md` không còn câu nào sai so với code.

## S1 — Đưa quyết định nghiệp vụ ra khỏi giao diện

Nền tảng cho mọi lát sau. Năm chỗ, mỗi chỗ một test.

| Chỗ | Hiện tại | Phải thành |
|---|---|---|
| `ExamResultsPage.bandCell` | Luôn trả `'—'` kể cả khi server gửi band | API trả thêm cờ bảng quy đổi đã xác minh (dùng `bandTableProvenance` đã có trong `exam.schema.json`). UI hiện band khi cờ bật, hiện `—` kèm lý do khi tắt |
| `PracticeRunnerPage.timingFor` | Mặc định `{prep:0, response:300}` | Bỏ mặc định. Đề thiếu `speakingTiming` là lỗi dữ liệu — hiện trạng thái "đề chưa cấu hình", không tự bịa 5 phút |
| `FullTestReadinessModal` | Cứng 60/40/60 phút | Lấy từ `TimingProfile` của đề qua catalogue |
| Nhãn "AI · tham khảo" | `module === 'writing'` viết thẳng trong JSX | API trả `provenance: 'answer-key' \| 'ai-advisory'`. Dùng `@vni/types.requiresAdvisoryLabel` (đang là code chết) |
| `apps/admin/lib/permissions.ts` | ~45 quyền sao chép ở client | Lấy từ `GET /api/v1/admin/roles`. Server có 24 quyền, client bịa thêm — trong đó có `exam.review` **không tồn tại ở server** |

**Xong khi:** grep không còn năm mẫu trên; mỗi chỗ có một test đã kiểm chứng đỏ-khi-gỡ.

## S2 — Ba lỗ API cho trang Result/Review

`P-06`…`P-09` yêu cầu layout hai cột: trái là đề đã làm, phải là review. `QuestionResultView` hiện chỉ có `questionId`, không có đề bài — **cột trái không có nguồn dữ liệu**.

- Trả nội dung đề sau khi nộp: đề bài, đoạn văn Reading, các lựa chọn, cue card. Chỉ khi `status != inprogress`
- `GET` nghe lại bản ghi Speaking (hiện chỉ có POST upload) — presigned, kiểm quyền sở hữu
- Transcript: **không làm**, chặn bởi ASR chưa chọn

**Xong khi:** test hợp đồng OpenAPI xanh; e2e `pre-submit payloads carry no keys/explanations/transcripts` vẫn xanh (không được nới lỏng test này).

## S3 — Sổ cái usage

`P-14`, `P-15`, `P-16`: chỉ ghi nhận, **không chặn**. Không có màn "hết lượt", không có nút mua.

- Collection append-only. Mỗi dòng: `userId`, `at`, `action`, `sessionId?`, `provider?`, `model?`, `tokensIn/Out?`, `costEstimate?`
- Ghi ở: mở phiên, chấm Writing, sinh giải thích, coaching. Điểm móc có sẵn: `IWritingEvaluationCostMetric` đang là no-op
- Cấp 10 lượt khi tạo tài khoản; cộng khi đăng nhập hằng ngày (`learner_activity_days` đã có) và khi người được giới thiệu **xác thực email thành công**
- `GET /api/v1/me/usage` trả số dư + lịch sử

**Không làm:** phép trừ nguyên tử, kiểm tra số dư trước hành động, thanh toán.

**Lý do dùng sổ cái chứ không phải bộ đếm:** bật chặn sau này chỉ là thêm một phép kiểm tra; đi từ bộ đếm lên thì phải viết lại schema.

**Xong khi:** một phiên Reading + một lần chấm Writing sinh đúng số dòng dự kiến; hết 10 lượt vẫn mở được phiên mới.

## S4 — Band Writing tổng

`P-12`: Task 1 : Task 2 = **1 : 2**. Hiện `RequireWritingTaskWeights()` cố tình ném lỗi vì chưa có số.

- Đưa trọng số vào `ScoringProfile` của đề (hoặc cấu hình `Assessment:Writing`), không hard-code trong code
- Band Writing = `(Task1 + Task2×2) / 3`, làm tròn bằng `BandScore.RoundToHalfBand` đã có — **không tự viết phép làm tròn**
- `P-13`: rubric là framework 4 tiêu chí, không tuyên bố là chấm IELTS chính thức. Mọi chỗ hiện band Writing phải mang nhãn "AI · tham khảo"

**Xong khi:** test dùng đúng bảng làm tròn có sẵn trong `BandScore`, không tự tính kỳ vọng bằng tay.

## S5 — Documents và Articles thành nội dung thật

`P-22`: hai thư viện độc lập. Hiện là mảng TypeScript cứng trong `features/library/documents.ts` và `features/articles/articles.ts`.

- Hai collection, CRUD API, hai màn CMS, learner đọc từ API
- Chừa sẵn trường `relatedExamIds` để `null` — nối với đề sau này không phải migration
- Articles định địa chỉ bằng slug, không bằng id

**Xong khi:** tạo một tài liệu trong CMS thì nó hiện ở `/documents`; xoá mảng cứng.

## S6 — Mặt tiền nhập đề

`P-18`, `P-19`. Engine đã mạnh và đã trả giá bằng sự cố thật — **dùng lại, không viết lại**: `ExamImportWorkflow`, `ImportReviewWorkflow`, `ImportBatchRunner`, `FabricatedAnswerKeyGuard`, `CambridgeAnswerKeyNormalizer`, `SafeSourceDocumentExtractor`, `ExamPackageReader`.

Cái thiếu là mặt tiền:

- Endpoint upload ZIP. Cấu trúc gói: bốn thư mục `reading/ listening/ writing/ speaking/`; thiếu thư mục nào thì không tạo kỹ năng đó. **Tên thư mục xác định kỹ năng**, không cần AI dò
- **Kiểm an toàn ZIP chưa hề tồn tại** — không có `ZipArchive` nào xử lý gói đề trong `backend/src`. Phải viết: nhận dạng byte đầu, giới hạn số entry / kích thước giải nén / tỉ lệ nén, chuẩn hoá đường dẫn chống thoát thư mục. Đặc tả có sẵn ở `docs/security/zip-ingestion-security.md` — đó là bản PROPOSED, chưa phải code
- Trạng thái tiến trình từng lô
- Findings tách hai nhóm: **lỗi chặn** và **cảnh báo**. Thiếu transcript = cảnh báo (`P-19`); admin bỏ qua được nhưng **bắt buộc ghi lý do và vào nhật ký**
- RAR, upload folder, file rời: **không làm**, để sau

**Xong khi:** gói hợp lệ tạo được draft; gói bom nén bị từ chối; bỏ qua cảnh báo có ghi audit kèm lý do.

## S7 — Vòng đời duyệt ba vai

`P-20`: soạn → người khác duyệt → admin xuất bản. Backend thiếu ba thứ:

- `ExamVersionStatus` thêm `InReview` và `Approved` (hiện chỉ có `Draft, Published, Unpublished`)
- Quyền `exam.review` ở server — hiện **không tồn tại**, client admin đang gác bằng một quyền tự bịa
- Bốn `AuditAction` mới: gửi duyệt, duyệt, trả về, bỏ qua cảnh báo
- Luật **người duyệt ≠ người soạn**, cưỡng chế ở server chứ không phải ẩn nút

Phần đã đúng, dùng lại: `ImportReviewActor(CanEdit, CanReview, CanPublish)`, vai `content-editor` cố tình không có `exam.publish`, `ContentPublishGuard`.

`P-21`: chỉ publish nội dung VNI sở hữu hoặc đã xác nhận quyền. Điều này **code đã cưỡng chế sẵn** — `ContentRightsPolicy` từ chối `LearnerProduction` khi thiếu `RightsProof`, và có test giữ luật. Không nới lỏng. Hai gói `exam/Exam1` và `exam/Vol9Test1` giữ ở mức nội bộ; nếu cần dùng trên site test thì cấp `InternalReview`, **không bao giờ** `LearnerProduction`.

**Xong khi:** người soạn tự duyệt đề của mình → 403; chuỗi trạng thái đầy đủ có test.

## S8 — Dọn điểm cụt giao diện

Chỉ gỡ, không thiết kế lại.

- Gỡ `EntryTestModal` — modal tự bật với nút bị vô hiệu, không có nơi để đi
- Gỡ các mục sidebar CMS không có route (Bài viết / Tài liệu / Nghe chép gắn chip *pending*)
- Gỡ mọi chuỗi "đang xây" / "sắp có" trên giao diện học viên
- `apps/web/src/routes/ErrorBoundary.tsx` dòng 57: `title="Trang gặp sự cố / This page hit a problem"` → thuần tiếng Việt

**Xong khi:** e2e xanh; không còn nút vô hiệu nào trên đường đi chính.

## S9 — Cập nhật tài liệu theo những gì đã làm

Làm **sau mỗi lát**, không dồn tới cuối.

- Cập nhật `docs/` phần liên quan tới lát vừa xong
- Cập nhật đoạn kiểm kê trong `CLAUDE.md` — nó là thứ đầu tiên mọi phiên đọc, sai một câu là dẫn nhầm cả phiên sau
- Viết ADR cho quyết định kỹ thuật lớn (`/adr`)
- Đánh dấu câu hỏi đã đóng trong `docs/requirements/assumptions-and-open-questions.md`
- `node scripts/check-docs.mjs` phải xanh trước khi commit

---

# Cách chạy mỗi lát

`/spec` → `/plan` → `/build` → `/review`. Xong một lát thì báo cáo và **dừng lại chờ duyệt**.

Báo cáo mỗi lát gồm: đã sửa file nào; test nào mới, và bằng chứng nó **đỏ khi gỡ fix**; điều gì phát hiện được mà brief này chưa nói tới; điều gì cố ý không làm và vì sao.

**Không được:** nới lỏng assertion, xoá test, tăng timeout tuỳ tiện, biến failure thành skip. Nếu một test cũ cản đường, giải thích vì sao quyết định đã đổi hành vi nó kiểm — rồi mới sửa.

# Còn chặn, không giải quyết bằng code

- **Speaking chấm AI** — chưa chọn nhà cung cấp ASR. `P-02`: chỉ ghi âm và lưu, đánh dấu chưa chấm. Giữ `NoTranscriptSource`
- **Nội dung được phép xuất bản** — VNI chưa có đề nào của mình. Đây là đường găng thật của ngày lên sóng, không phải việc của code
- **PDPL cross-border** — chủ sản phẩm quyết định không chặn MVP, ghi nhận là rủi ro tuân thủ. Hồ sơ CTIA đến hạn khoảng đầu 11/2026. Giữ `Ai:AllowCrossBorderTransfer` là seam cấu hình

# Một việc cấu hình đừng để quên

`P-02` khiến hệ thống bắt đầu lưu giọng nói thật của người thật mà chưa dùng vào việc gì. `ObjectStorage:SpeakingRecordingRetentionDays` đang **để trống, nghĩa là giữ vĩnh viễn**, và `Recordings:SweepEnabled` mặc định tắt. Đặt một con số (tài liệu giả định 90 ngày) và bật dọn dẹp.
