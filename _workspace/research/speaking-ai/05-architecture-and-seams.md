# Speaking AI — thiết kế đáp xuống code ở đâu, và cần ADR nào

- **Trục:** kiến trúc và seam. Bốn agent kia lo provider, luật thi, đo lường, quyền riêng tư.
- **Ngày:** 2026-09-03 · **Người viết:** Solution Architect
- **Trạng thái:** `PROPOSED` — nghiên cứu, chưa sửa code sản phẩm
- **Ràng buộc áp dụng:** ADR-0004 (một biên giới nghiêm ngặt) · ADR-0005 (port AI) · `G-11` (seam có cấu hình, không bịa mặc định) · `D-5` (không dựng Clean Architecture sớm)

---

## 0 · Kết luận điều hành

Bốn điều đáng nói nhất, đặt trước vì phần còn lại là chứng minh.

1. **Seam đã dựng đúng chỗ, nhưng `ITranscriptSource` đã âm thầm thu hẹp hợp đồng mà ADR-0005 đã chấp nhận.** ADR-0005 (mục Notes) và `docs/architecture/backend-architecture.md:131` đều ghi *port ASR bắt buộc trả word-level timestamps*. Code hiện trả `Task<string?>` (`backend/src/Vni.Ielts.Application/Assessment/SectionMarkingRunner.cs:312`). Vậy nên **mở rộng nó là khôi phục một ADR đã được chấp nhận, không phải ra quyết định mới** — đây là điểm rẻ nhất và ít tranh cãi nhất để bắt đầu.

2. **`H-1` không còn chặn mô hình phiên thi nữa, và tài liệu đang nói sai về code.** `docs/requirements/confirmed.md:222-226` viết `H-1` "quyết định hình dạng của `SectionAttempt`". Nhưng `SectionAttempt.PartId` (`ExamSession.cs:313`) và `ExamSession.AdvanceToNextPart` (`ExamSession.cs:194-221`) **đã tồn tại và đang chạy** — mỗi part một attempt, một deadline riêng. Cả hai cách đọc `H-1` đều đã có chỗ đứng trong domain. Chi phí đo được ở dưới: **gần như bằng không ở server, vừa phải ở client.** Một blocker đã hết hạn vẫn đang chặn việc không bị chặn.

3. **Chỗ nguy hiểm nhất không phải là port, mà là *trả tiền ASR nhiều lần*.** `MarkingWorker.MaxAttempts = 5` (`MarkingWorker.cs:78`), và không có gì lưu lại transcript. Một lỗi LLM ở bước cuối kéo theo **5 lần chạy ASR cho cùng một bài nói**. Chưa kể `SectionMarkingRunner.cs:222-229` ghi rõ "một phản hồi bị từ chối thì *không* retry ở đây… hỏi lại là giấu tín hiệu và tính tiền hai lần" — nhưng worker vẫn retry nó 5 lần (`MarkingWorker.cs:314-325`). **Hai đoạn comment mâu thuẫn nhau và worker đang thắng.** Đây là lỗi có thật *ngay hôm nay* với Writing; thêm ASR vào trước nó thì đắt gấp bội.

4. **Thiết kế đúng không đụng tới hợp đồng wire.** `SectionMarkingView` (`contracts/openapi/v1.json:2657-2696`) và `MarkingStatusView` (`:2128-2159`) không cần thêm trường nào: word timings và features là **đầu vào cho model**, không phải đầu ra cho học viên. Nghĩa là không phải regen `packages/api-client`, không đụng `apps/web`, không qua cổng chống-drift OpenAPI. Đó là bằng chứng mạnh nhất rằng seam đã được đặt đúng chỗ.

---

## 1 · Hình dạng port: mở rộng `EvaluationRequest`, **không** thêm port thứ hai

### 1.1 Ràng buộc quyết định mọi thứ, và nó vô hình nếu không đọc kỹ

`CriterionMarking.Mark(rubric, claim.Criteria, claim.ReportedBand, submission, taskNumber)` (`SectionMarkingRunner.cs:216-217`) dùng chính chuỗi `submission` làm **văn bản đối chiếu bằng chứng**. `CriterionMarking.IsGroundedIn` (`backend/src/Vni.Ielts.Domain/Assessment/CriterionMarking.cs:262-269`) yêu cầu mỗi trích dẫn phải là **substring nguyên văn** của chuỗi đó — chỉ chuẩn hoá khoảng trắng, dấu nháy cong và gạch ngang, cố ý không stem, không so khớp theo từ:

> *"Stemming, or matching on word overlap, would let a paraphrase pass as a quotation, and the entire value of this check is that it distinguishes those two things."*

**Hệ quả cứng:** nếu nhồi timestamp, nhãn `[Part 2]`, hay khối feature vào `LearnerSubmission`, thì **mọi** trích dẫn bằng chứng sẽ bị gắn cờ `EvidenceNotGrounded` (`CriterionMarking.cs:196`) và mọi `SectionMarking` của Speaking đều `IsFlagged`. Sai lầm này không làm hỏng build, không ném exception, chỉ làm mọi bản chấm Speaking trông như đáng ngờ.

> **Luật rút ra:** *transcript đi vào `LearnerSubmission` phải là văn bản sạch, đúng bằng những gì model được cho xem là lời của học viên. Features đi bên cạnh, không đi bên trong.*

### 1.2 Chữ ký đề xuất

```csharp
// backend/src/Vni.Ielts.Application/Assessment/Ports.cs
public sealed record EvaluationRequest(
    Rubric Rubric,
    string LearnerSubmission,
    string Prompt,
    IReadOnlyList<DeliveryFeature>? Features = null);

/// <summary>
/// Một phép đo tất định về CÁCH nói, không phải về nội dung nói.
/// Số học thuần trên word timings — không model, không vendor, tái lập được.
/// </summary>
public sealed record DeliveryFeature(string Key, decimal Value, string? Unit);
```

`decimal`, không phải `double`: `PersistenceRepresentationTests.No_persistence_document_stores_a_binary_floating_point_number` (`backend/tests/Vni.Ielts.Architecture.Tests/PersistenceRepresentationTests.cs:135-161`) sẽ fail build nếu feature này về sau được lưu dưới dạng `double`. Chọn `decimal` ngay từ đầu để việc lưu về sau không phải viết lại.

### 1.3 Vì sao đây là chi phí bằng không cho Writing

Call site duy nhất đang dựng `EvaluationRequest` là `SectionMarkingRunner.cs:214`, dựng theo vị trí với đúng 3 tham số. Thêm một tham số **optional có mặc định** giữ nguyên khả năng biên dịch của nó. Test Writing hiện có phải xanh **không sửa một dòng** — đó chính là tiêu chí nghiệm thu của hạng mục này.

### 1.4 Bốn phương án đã cân nhắc thật

| Phương án | Ưu | Nhược | Phán quyết |
|---|---|---|---|
| **Mở rộng `EvaluationRequest` bằng một tham số optional** | Writing không đổi một dòng; một type mới; adapter tự do bố cục prompt | `Features` là `null` với Writing — một trường không dùng | **Chọn** |
| Thêm port riêng `ISpeakingEvaluator` (đúng tên ADR-0005 đề xuất) | "Đúng sách" hơn; request không có trường thừa | `SectionMarkingRunner` phân giải `IEnumerable<ISectionEvaluator>` rồi lọc theo `e.Module` (`:111`). Port thứ hai kéo theo đường phân giải thứ hai, giao thức `IsConfigured` thứ hai, nhánh `MarkOneAsync` thứ hai — và **đầu ra vẫn là `ClaimedEvaluation` y hệt**. Đây đúng là "one interface per use case" mà ADR-0004 loại bỏ có tên | Loại |
| Nhồi features vào `LearnerSubmission` | Không đổi type nào | Phá vỡ đối chiếu bằng chứng (§1.1). Hỏng âm thầm | Loại |
| `SpeakingEvaluationRequest : EvaluationRequest` | Type-safe theo module | Kế thừa record + `with` là bẫy đã biết; runner phải downcast — tức là Application đi kiểm tra *adapter nào* đứng sau port, đúng thứ ADR-0005 cấm về tinh thần | Loại |
| Không làm gì (giữ nguyên 3 trường) | Rẻ nhất hôm nay | Level B (`H-3`) là baseline được khuyến nghị và nó *cần* features đến được model. Không làm gì tức là chốt Level A bằng cách im lặng — vi phạm `G-11` | Loại |

### 1.5 Chấm phát âm (Level C) là adapter thứ hai, không phải port thứ hai

Nếu sau này có dịch vụ pronunciation riêng, `SpeakingSectionEvaluator` **trong Infrastructure** gọi ASR client → feature extractor → pronunciation client → LLM client, rồi trả về **một** `ClaimedEvaluation`. Application không bao giờ biết đã có ba lần gọi mạng. Đây đúng khuôn `WritingSectionEvaluator` đang chạy thật: nó giấu cả `WritingEvaluationRouter` với primary/fallback/retry sau một lời gọi `EvaluateAsync` (`backend/src/Vni.Ielts.Infrastructure/Assessment/WritingSectionEvaluator.cs:62`).

**Hệ quả cho lộ trình:** `H-3` (độ sâu A/B/C/D) **không chặn kiến trúc**. A→B miễn phí một khi port mang timings; B→C là một adapter mới trong Infrastructure, không đổi gì ở Application. Nên nói thẳng điều này với chủ sản phẩm: câu hỏi `H-3` không cần trả lời trước khi xây.

---

## 2 · `ITranscriptSource` không đủ — và nó sai theo ba cách, không phải một

Hiện tại (`SectionMarkingRunner.cs:298-313`):

```csharp
Task<string?> ForAsync(ExamSessionId, IReadOnlyList<SpeakingRecording>, CancellationToken);
```

| # | Khiếm khuyết | Hậu quả cụ thể |
|---|---|---|
| 1 | Trả `string?` — không có timings | Toàn bộ Stage 4 của `docs/ai/speaking-pipeline.md:70-100` (11 feature tất định) là bất khả thi. Vi phạm ADR-0005 mục Notes và `backend-architecture.md:131`, cả hai đều ghi word-level timestamps là tiêu chí **loại trừ** |
| 2 | `null` gánh ba nghĩa | Hôm nay `null` chỉ có nghĩa "chưa chọn ASR" (`UnconfiguredEvaluators.cs:52-55`), nên báo `AwaitingVoiceProvider` là đúng. Ngày có provider thật, một lỗi 503 tạm thời trả `null` sẽ báo cho học viên *"chưa có nhà cung cấp speech-to-text nào được chọn"* — một lời nói dối — và worker đốt 5 lần thử vào một trạng thái đọc lên như một lỗ hổng sản phẩm vĩnh viễn |
| 3 | Không có `IsConfigured`, khác `ISectionEvaluator` | Runner không hỏi được trước khi gọi. `ISectionEvaluator` có (`Ports.cs:33`) và tài liệu của nó giải thích rất rõ vì sao cần; `ITranscriptSource` bị bỏ sót |

### 2.1 Hình dạng đề xuất

```csharp
// backend/src/Vni.Ielts.Application/Assessment/Ports.cs
public interface ITranscriptSource
{
    /// False khi chưa có ASR nào được nối. → MarkingAvailability.AwaitingVoiceProvider
    bool IsConfigured { get; }

    /// Ném exception khi provider lỗi. KHÔNG BAO GIỜ trả null để nói "nó hỏng".
    Task<Transcript> TranscribeAsync(
        ExamSessionId sessionId,
        IReadOnlyList<SpeakingRecording> recordings,
        CancellationToken ct);
}

public sealed record Transcript(
    /// <summary>
    /// CHÍNH XÁC chuỗi được trao cho CriterionMarking.Mark để đối chiếu bằng chứng.
    /// Ghép các từ bằng khoảng trắng thường. Không timestamp, không nhãn part. → §1.1
    /// </summary>
    string Text,
    IReadOnlyList<TranscribedWord> Words,
    string ProviderId,
    string ModelId,
    decimal AudioSeconds);

/// <param name="StartMs">
/// Mốc tính từ đầu MÀN TRÌNH DIỄN đã ghép, không phải từ đầu bản ghi của chính nó.
/// Một band Speaking là một phán quyết trên toàn bộ màn trình diễn, và khoảng lặng
/// giữa Part 2 và Part 3 không phải là một lần ngập ngừng.
/// </param>
public sealed record TranscribedWord(
    string Text, int StartMs, int EndMs, decimal? Confidence, int PartNumber);
```

**Vì sao từng lựa chọn:**

- **`int` mili-giây**, không phải `double` giây, không phải `TimeSpan`. `PersistenceRepresentationTests` cấm `double`/`float` trong document (`:135-161`); `TimeSpan` không có biểu diễn sạch trong BSON lẫn PostgreSQL. `int` ms là chính xác, cộng được không sai số, và di trú được. `Confidence` là `decimal?` cùng lý do.
- **`PartNumber` gắn trên *từ*,** không phải một mảng part riêng. Nhờ vậy cách đọc "ba part" của `H-1` tính được từ đúng một hình dạng, không cần type thứ hai (§3).
- **Không có `AudioReference`** (thứ `backend-architecture.md:120` từng đề xuất). `SpeakingRecording(QuestionId, RecordingId)` (`SectionMarkingRunner.cs:319`) *đã là* tham chiếu đó, và `IRecordingStore` là cách adapter lấy bytes. Thêm `AudioReference` là đặt tên thứ hai cho một thứ đã có.

### 2.2 Đối chiếu với hình dạng `SpeechAnalysis` đã có trong tài liệu

`docs/product/four-skills-practice-and-mock-research.md:252-260` đề xuất một record giàu hơn nhiều: `audioQuality{duration, clipping, silence, SNR}`, `pronunciation[]{phoneme accuracy, stress, prosody}`, `fluencyFeatures{…}`, `provider/modelVersion/requestId/processingTime/cost`.

**Khuyến nghị: cắt bớt.** Record đó gộp bốn mối quan tâm vào một type và **chốt trước Level C** trong khi `H-3` còn mở — chính xác là thứ `G-11` cấm. Cụ thể:

- `fluencyFeatures` **là dẫn xuất** của `Words`. Đặt nó trong transcript là lưu hai lần cùng một sự thật, và hai bản sao sẽ lệch nhau.
- `pronunciation[]` chỉ tồn tại khi có adapter Level C. Khai báo trước tạo ra một trường mãi `null` mà mọi người đọc code sẽ tưởng là chưa làm xong.
- `cost` / `processingTime` là **telemetry**, đã có chỗ: `IWritingEvaluationCostMetric` (`Ai/Writing/WritingEvaluationContracts.cs:33`) và `Telemetry.QueueJobDuration` (`MarkingWorker.cs:263`). Nhét vào DTO của port là để Application cầm dữ liệu hoá đơn của vendor.
- `audioQuality` là tín hiệu thật và đáng có — **khi nào có provider trả nó**. Chưa chọn provider thì chưa biết trường nào có sẵn.

Giữ `Transcript` ở mức nhỏ nhất mà Level B cần. Mở rộng khi có bằng chứng, không mở rộng theo dự đoán.

### 2.3 Mọi call site phải đổi — danh sách đầy đủ

| File:dòng | Thay đổi |
|---|---|
| `Application/Assessment/SectionMarkingRunner.cs:298-313` | Bản thân interface + hai record mới |
| `Application/Assessment/SectionMarkingRunner.cs:101` | Tham số ctor — **không đổi** (cùng tên interface) |
| `Application/Assessment/SectionMarkingRunner.cs:183-195` | Kiểm `transcripts.IsConfigured` **trước**, rồi `TranscribeAsync`; nhánh `AwaitingVoiceProvider` khoá theo `IsConfigured` thay vì theo `null` trả về |
| `Application/Assessment/SectionMarkingRunner.cs:214` | `new EvaluationRequest(rubric, submission, unit.Prompt)` → thêm `features` |
| `Infrastructure/Assessment/UnconfiguredEvaluators.cs:50-56` | `NoTranscriptSource` thêm `IsConfigured => false` và **ném** từ `TranscribeAsync`, soi gương `UnconfiguredEvaluator` ngay bên trên nó (`:29-33`) |
| `Infrastructure/DependencyInjection.cs:136` | Đăng ký **không đổi** cho tới khi có adapter ASR thật |
| `tests/Vni.Ielts.Application.Tests/Assessment/Fakes.cs:156` | `FakeTranscriptSource(string?)` → nhận `Transcript?` |
| `tests/Vni.Ielts.Worker.Tests/GracefulShutdownTests.cs:68, :225` | `UnusedTranscriptSource` |
| `tests/…/Assessment/SectionMarkingRunnerTests.cs:197, :221, :242` | Ba test dùng transcript null/non-null |
| `tests/…/Exams/SpeakingRecordingRemainderTests.cs:175, :190, :215, :223` | Khẳng định `AwaitingVoiceProvider` đầu-cuối |
| `tests/…/Exams/ExamLifecycleTests.cs:634-656` | Rubric Speaking + `AwaitingVoiceProvider` |

**Bán kính nổ: một interface, một nhánh trong runner, một null-object, và các test.** Không đụng `Domain`, không đụng `Api`, không đụng `contracts/`, không đụng `apps/`. Nhỏ như vậy **là vì** seam đã được dựng — đây là bằng chứng seam có giá trị, không phải bằng chứng nó thừa.

---

## 3 · Mô hình phiên và `H-1` — blocker đã hết hạn

### 3.1 Điều tài liệu nói

`docs/requirements/confirmed.md:222-226` và `docs/requirements/assumptions-and-open-questions.md:492-497`:

> *"Nó quyết định hình dạng của chính `SectionAttempt`: một attempt mang timing nội bộ theo part, hay ba attempt với ba deadline do server suy ra và ba vòng đời upload. Đó là một thực thể lõi của exam engine… phải trả lời **trước khi** dựng mô hình phiên."*

Viết ngày 2026-08-20.

### 3.2 Điều code thực sự nói, hôm nay

**Đã cam kết một chiều — phía chấm:**

| Bằng chứng | Ý nghĩa |
|---|---|
| `SectionMarkingRunner.cs:242-266` | `Units()` sinh **đúng một** `MarkableUnit` cho Speaking, `TaskNumber = null`, prompt là thân của mọi part ghép lại, `QuestionIds` là mọi câu theo thứ tự |
| `Repositories.cs:745-748` | Khoá lưu marking là `{session}:Speaking` — một bản chấm |
| `MarkingOutbox.cs:117-118` | `MarkingJob.IdFor(session, module, rubricVersion)` — một job mỗi module |
| `ExamHandlers.cs:1024-1027` | `SubmitSpeakingRecording` đòi `session.Current.Module == Speaking` |
| `ExamHandlers.cs:1071-1072` | Mọi recording id rơi vào **cùng một** answer sheet của Speaking, theo question id |

**Đã hỗ trợ chiều kia — phía phiên:**

| Bằng chứng | Ý nghĩa |
|---|---|
| `ExamSession.cs:313` | `SectionAttempt.PartId` **đã tồn tại** |
| `ExamSession.cs:194-221` | `AdvanceToNextPart` **đã tồn tại**: mở attempt mới cho part kế, deadline riêng lấy từ `part.Timing?.DurationSeconds` |
| `ExamSession.cs:198` | …nhưng bị chặn bởi `PracticeUnitId is null → NotPartScoped`, nên hôm nay chỉ phục vụ practice unit |
| `contracts/schemas/exam.schema.json:122-146` | `speakingTiming.parts[]` mang `prepSeconds`/`responseSeconds` theo từng part. **Kiểm chứng: đúng.** Comment trong chính schema nói nó chịu được cả hai cách đọc, và điều đó là thật |
| `apps/web/src/features/exam/ExamRunnerPage.tsx:470-481` | Client phân trang part bằng chỉ số `activePart` cục bộ, timer đọc từ `speakingTiming` |

### 3.3 Diễn giải lại — và đây là phần có giá trị nhất

**IELTS báo một band Speaking, không phải ba.** Nên `H-1` là câu hỏi về **giao đề và nộp bài**, không phải về độ hạt của việc chấm. Phía chấm — một `MarkableUnit`, một `SectionMarking`, một `MarkingJob` — **đúng dưới cả hai cách trả lời**, và không cần đổi gì cả.

Bề mặt bị chặn thu từ *"mô hình phiên thi"* xuống *"Speaking có mấy hàng `SectionAttempt` và mấy deadline"*.

### 3.4 Chi phí đo được nếu `H-1` → "ba part nộp riêng"

| # | Việc phải làm | Quy mô |
|---|---|---|
| 1 | Nới điều kiện `PracticeUnitId is null → NotPartScoped` (`ExamSession.cs:198`) để một sitting Full/Single cũng part-scoped được | ~1 dòng + test |
| 2 | `SubmitSpeakingRecording` đòi `Current.Module == Speaking` — vẫn đúng với từng part attempt | **Không đổi** |
| 3 | Answer sheet chỉ đóng khi `ScopeComplete` (`ExamHandlers.cs:604-605`) — nghĩa là sheet đóng một lần, sau part 3 | **Không đổi**, và chính điều này giữ cho chỉ có một bản chấm |
| 4 | `MarkSection.RunAsync` chỉ chạy khi `ScopeComplete` (`ExamHandlers.cs:618-621`) | **Không đổi** — một job, enqueue sau part cuối |
| 5 | `MarkingJob.IdFor` | **Không đổi** |
| 6 | Client chuyển từ `activePart` cục bộ sang một vòng gọi server mỗi part | Việc thật ở `apps/web`, cỡ bằng phần đã viết cho practice runner |

**Kết luận: gần bằng không ở server, vừa phải ở client.** Khuyến nghị **hạ cấp `H-1`** từ "chặn mô hình phiên" xuống "một quyết định sản phẩm đổi cách phân trang ở client và một mệnh đề bảo vệ", và **ghi lại việc hạ cấp đó** — vì một blocker hết hạn vẫn chặn người đọc kế tiếp.

### 3.5 Hai thứ cần nói rõ kèm theo

- **`[TECHNICAL RISK]` — quy ước part id là một schema giấu trong một chuỗi.** `"{module}-part-{order}"` được sinh ở `PracticeScorePolicy.cs:56` và **parse ngược bằng `int.Parse`** ở `ExamSession.cs:213-215`. Mở rộng sang Speaking full sitting là thừa kế nợ này. Đây là **nợ cũ, không phải nợ mới**, và sửa nó *không* nằm trong phạm vi Speaking. Ghi nhận, đừng sửa kèm — sửa kèm chính là over-engineering.

- **`[TECHNICAL RISK]` — deadline part của Speaking sẽ sai vào đúng ngày `H-1` được trả lời.** `AdvanceToNextPart` suy deadline bằng `part.Timing?.DurationSeconds ?? (thời lượng module / số part)` (`ExamSession.cs:212-214`). Chia đều cho Speaking là sai: Part 2 ngắn hơn, và **chỉ Part 2 có thời gian chuẩn bị**. Nguồn đúng là `SpeakingPartTiming(Part, PrepSeconds, ResponseSeconds)` (`ExamContent.cs:319`), một type *khác* với `PartTiming` mà `AdvanceToNextPart` đang đọc. Đây là một lỗi có thật nằm chờ, không phải một mối lo giả định.

---

## 4 · Độ trễ, hàng đợi, và thất bại một phần

### 4.1 Hàng đợi đang là *lưới an toàn*, không phải đường chính — và với Speaking điều đó phải đảo lại

`MarkSection.RunAsync` **enqueue rồi chạy chấm ngay tại chỗ** (`ExamHandlers.cs:1158-1160`), và người gọi nó là các handler HTTP: `AdvanceSection.HandleAsync` và `SubmitExamSession.HandleAsync` (`ExamHandlers.cs:619, :695, :775`).

Hôm nay điều đó nghĩa là request nộp bài của học viên **chặn trên một lời gọi LLM Writing** — `WritingMarkingOptions.TimeoutSeconds` mặc định 120, kẹp ≤300 (`WritingSectionEvaluator.cs:48, :81`). Với Speaking, request đó còn phải chặn thêm trên ASR của **toàn bộ** màn trình diễn (11–14 phút audio). `speaking-pipeline.md:166-179` dự toán ASR 5–20 s, nhưng đó là cho một clip 2 phút.

Một request giữ hơn một phút sẽ bị proxy cắt, client retry — outbox làm cho việc đó *an toàn*, nhưng vẫn đốt request và vẫn để học viên nhìn màn hình treo.

**Khuyến nghị: bỏ nhánh chấm tại chỗ cho Speaking** (`ExamHandlers.cs:1160`). Enqueue đã xảy ra ở dòng ngay trên; worker đã chạy đúng runner đó (`MarkingWorker.cs:283-299`); và `docs/architecture/system-architecture.md:143` **đã tuyên bố** Writing và Speaking là bất đồng bộ. Đây chỉ là làm cho code khớp với kiến trúc đã ghi.

> Writing cũng nên như vậy, nhưng Writing đang chạy được và đổi nó **không thuộc phạm vi** nghiên cứu này. Ghi lại thành việc tiếp theo, đừng gộp vào.

### 4.2 Idempotency: enqueue được bảo vệ, lời gọi LLM được bảo vệ, **ASR thì không**

Những gì đã được bảo vệ:

| Tầng | Cơ chế |
|---|---|
| Enqueue | `_id` duy nhất `{session}:{module}:{rubricVersion}` (`MarkingOutbox.cs:117`, `[BsonId]` ở `Persistence/Exams/MarkingOutbox.cs:265-266`) |
| Claim | Một filtered update nguyên tử (`MongoMarkingOutbox.ClaimAsync`, `:61`) |
| Lưu marking | Insert-if-absent, nuốt duplicate-key (`Repositories.cs:775-782`) |
| Runner | Đọc `store.ListAsync` **trước khi** chấm (`SectionMarkingRunner.cs:123-134`) |
| Provider Writing | `IdempotencyKey` = SHA-256 của `{rubricVersion}:{prompt}:{submission}` (`WritingSectionEvaluator.cs:45, :123-129`) |

**Lỗ hổng: không có gì tương đương cho ASR** — đúng cái đắt nhất, chậm nhất, tính tiền theo phút. Ba đường trả tiền hai lần, cụ thể:

- **(a)** LLM lỗi → cả job retry → runner gọi lại `TranscribeAsync` vì không có gì lưu transcript. `MaxAttempts = 5` (`MarkingWorker.cs:78`) → **tối đa 5 lần ASR cho một bài nói.**
- **(b)** `MarkingAvailability.Rejected` (phản hồi model sai schema) trả về như một outcome chưa giải quyết, worker ném (`MarkingWorker.cs:314-325`) → cũng 5 lần ASR.
- **(c)** Mất lease giữa chừng (`MarkingWorker.cs:399-403` log đúng chữ *"The evaluation is being performed twice"*) → hai lần ASR chạy song song.

### 4.3 Cách sửa nhỏ nhất mà đủ: lưu transcript một lần

**Không** thêm trường vào `MarkingJob`. Làm vậy sẽ buộc artifact vào vòng đời của job và vào `rubricVersion` — thứ chẳng liên quan gì đến ASR (đổi rubric phải chấm lại, nhưng **không** phải phiên âm lại). Dùng một store riêng, cùng kỷ luật insert-if-absent như `MongoSectionMarkingStore`:

```csharp
// backend/src/Vni.Ielts.Application/Assessment/Ports.cs
public interface ITranscriptStore
{
    Task<Transcript?> FindAsync(ExamSessionId sessionId, CancellationToken ct);

    /// Insert-if-absent. Trả về bản đang được lưu — bản của chính người gọi ở
    /// lần ghi đầu, bản đã có nếu một worker khác tới trước.
    Task<Transcript> SaveIfAbsentAsync(
        ExamSessionId sessionId, Transcript transcript, CancellationToken ct);
}
```

Runner trở thành: *find → có thì dùng → không thì transcribe → save-if-absent → dùng thứ trả về*. (a) và (b) còn **đúng một** lần ASR; (c) tối đa hai (cửa sổ đua), không bao giờ năm.

Khoá theo `sessionId` đơn thuần: một sitting một màn trình diễn một transcript. **Nếu `H-1` ra "ba part", khoá vẫn nguyên** — ba lần upload, một transcript ghép, một band. Đó là hình dạng sống được với cả hai câu trả lời.

**Còn features thì có lưu không?** `speaking-pipeline.md:102` đề xuất `AiJob.featureSnapshot` (một type **chưa hề tồn tại** trong C# — chỉ có trong tài liệu). **Khuyến nghị: không, chưa.** Features là số học thuần trên transcript đã lưu; tính lại tốn <100 ms (`speaking-pipeline.md:166-179`) và không tốn tiền. Lưu chúng thêm một schema, thêm một đường di trú, và thêm một thứ có thể lệch khỏi bộ trích xuất đã sinh ra nó. **Lưu thứ đắt để tái tạo (transcript); tính lại thứ miễn phí (features).** Đó chính là phép thử ADR-0004 áp dụng nguyên văn. Khi nào có yêu cầu thật về *tái lập một phiên bản feature cụ thể cho khiếu nại*, lúc đó mới lưu — và lúc đó mới biết cần hình dạng gì.

### 4.4 Bảo lưu bắt buộc — transcript là dữ liệu cá nhân

Transcript là lời nói của học viên dưới dạng chữ. `system-architecture.md:345` đặt 2 năm cho "Transcripts and AI artifacts", nhưng đó là `[ASSUMPTION]`, không phải quyết định của chủ sản phẩm.

Lưu transcript nghĩa là `PurgeSpeakingRecordings` (`SpeakingRecordingUpload.cs:446`) và `IRecordingStore.DeleteForSessionAsync` **không còn đủ** để xoá tài khoản: một tài khoản đã xoá sẽ để lại một bản chép đọc được của giọng nói học viên.

**Đây là mối lo bảo mật, và theo luật làm việc của tôi thì bảo mật thắng mặc định.** Nó là yêu cầu cứng của thiết kế, không phải một điểm cộng.

Đã có sẵn khuôn mẫu để bắt chước, không cần phát minh: `StartupConfiguration.cs:370-383` **từ chối khởi động** nếu `SpeakingRecordingsBucket` được đặt mà retention thì không, và từ chối giá trị `<= 0`. `ITranscriptStore` phải theo đúng tiền lệ đó. Giá trị retention vẫn là **seam cấu hình không mặc định** (`G-11`) — hiện `SpeakingRecordingRetentionDays` **không có trong bất kỳ file settings nào**, một cách cố ý.

### 4.5 Mâu thuẫn có thật, hôm nay, trong phân loại retry

`SectionMarkingRunner.cs:222-229` nói:

> *"Refused, not repaired, and **not retried here**. …Silently asking again would hide the signal and bill for it twice."*

Nhưng `MarkOneAsync` trả `Pending(Rejected, …)`, `MarkingWorker.RunAsync` coi mọi outcome không phải `Marked`/`NothingSubmitted` là chưa giải quyết và **ném** (`MarkingWorker.cs:314-325`), rồi `GiveUpOrRetryAsync` (`:328-354`) xử mọi exception như nhau và retry 5 lần.

**Hai comment nói ngược nhau; worker đang thắng.** Đây là lỗi thật với Writing *ngay hôm nay*. Với Speaking, nó nhân lên vì mỗi lần thử kéo theo một lần ASR. Cần phân loại: một phản hồi sai schema là bằng chứng về prompt hoặc provider, không phải một sự cố tạm thời — nó nên có ngân sách thử riêng, hoặc dead-letter sớm hơn. **Việc này thuộc người sở hữu đường chấm, không thuộc file của tôi**, nhưng nó phải nằm trong báo cáo.

### 4.6 Ngân sách timeout

`WritingSectionEvaluator` kẹp timeout ≤300 s (`:81`); lease của worker là 2 phút, gia hạn mỗi 40 s (`MarkingWorker.cs:66, :68`) — gia hạn làm con số đó an toàn. Với Speaking, ASR + LLM trong **một** lời gọi `EvaluateAsync` có thể vượt 300 s trên một màn trình diễn dài.

**Khuyến nghị: `SpeakingMarkingOptions` riêng**, soi gương `WritingMarkingOptions` (`Infrastructure/Assessment/WritingMarkingOptions.cs`), **không dùng chung**. Hai giai đoạn có phân phối thời gian khác nhau; một cái kẹp cho cả hai thì hoặc bóp nghẹt Speaking hoặc nới lỏng Writing. Ghi nhận: khối `SpeakingMarking` hiện **không tồn tại ở bất kỳ đâu** trong config — chỉ có `Assessment:Speaking:{Version,DescriptorSource}` (`secrets.example.json:44-47`, `secrets.develop.json:40-43`).

---

## 5 · Cần ADR nào — và phân loại là phần quan trọng nhất

### 5.1 Phân loại từng quyết định

| # | Quyết định | Loại | Lý do phân loại như vậy |
|---|---|---|---|
| **A** | Hình dạng port Speaking: mở rộng `EvaluationRequest`; `ITranscriptSource` trả timings; trích features trong Application; **một** `ISectionEvaluator` chứ không hai | **`[QUYẾT ĐỊNH kỹ thuật]`** | Hoàn toàn nội bộ. Không hành vi nào lộ ra người dùng, không chi phí, không bề mặt pháp lý. **Giá nếu sai:** refactor một interface và một nhánh runner — đã đo ở §2.3, khoảng 11 call site, phần lớn là test. Đảo ngược rẻ |
| **B** | Speaking chấm **chỉ** trong worker, không chấm tại chỗ trong request nộp bài | **`[QUYẾT ĐỊNH kỹ thuật]`** | Đã là kiến trúc tuyên bố ở `system-architecture.md:143`; đây là làm code khớp tài liệu. **Giá nếu sai:** gần bằng không — màn hình kết quả đã render trạng thái "đang chấm" một cách trung thực |
| **C** | Lưu transcript, tính lại features | **`[QUYẾT ĐỊNH kỹ thuật]`** — nhưng **hệ quả riêng tư thì không** | Việc *cache* là đánh đổi chi phí/độ trễ có câu trả lời đo được. Nhưng **lưu gì, bao lâu, cái gì xoá nó** là chuyện PDPL, và "2 năm" ở `system-architecture.md:345` là `[ASSUMPTION]`. **Tách đôi:** ADR chốt cơ chế; thời hạn giữ nguyên là seam cấu hình không mặc định (`G-11`); quét-khi-xoá-tài-khoản là yêu cầu cứng trong mục Consequences |
| **D** | Deadline part của Speaking lấy từ `SpeakingPartTiming`, không lấy từ phép chia đều | **`[QUYẾT ĐỊNH kỹ thuật]`** | Chỉ có ý nghĩa nếu `H-1` → ba part. Gộp vào ADR của `H-1`, **không** tách ADR riêng |
| **E** | **Chọn nhà cung cấp ASR (`V-10`)** | **KINH DOANH** — nhưng chỉ một nửa | Tiêu chí kỹ thuật cứng (word-level timings) **đã được chốt** ở ADR-0005 mục Notes và không mở lại. Thứ thuộc chủ sản phẩm là: vendor nào, giá bao nhiêu, theo thoả thuận xử lý dữ liệu nào, và audio có được ra khỏi biên giới không (`B-2`). **Đừng để một kỹ sư quyết cái này** — nó mang một hoá đơn theo phút, một DPA, và một hồ sơ CTIA. Đồng hồ CTIA đã chạy từ bài luận Writing đầu tiên (CLAUDE.md, 02/09/2026); **audio là một loại chuyển giao mới** và nhiều khả năng là một hồ sơ mới |
| **F** | **Độ sâu chấm Speaking (`H-3`: A/B/C/D)** | **KINH DOANH** | Là đánh đổi chi phí/chất lượng thuộc chủ sản phẩm. `[ASSUMPTION]` Level B vẫn đứng (`assumptions-and-open-questions.md:558`). **Nhưng kiến trúc ở §1.5 làm A→B miễn phí và B→C là một adapter Infrastructure.** Nên nói thẳng: **`H-3` không chặn việc xây gì cả** |
| **G** | **`H-1` mô hình phiên Speaking** | **KINH DOANH** — nhưng đã hạ cấp | Là quyết định sản phẩm về cảm giác bài thi. Khẳng định "nó chặn mô hình phiên" **không còn đúng** (§3). ADR nên ghi hình dạng hiện tại làm mặc định + chi phí đo được của phương án kia, để chủ sản phẩm trả lời lúc nào cũng được mà không giữ chân thi công |

### 5.2 Kết quả: **ba ADR mới**, không phải bảy

| ADR | Nội dung | Viết được khi nào |
|---|---|---|
| **ADR-0016 — Speaking evaluation ports: transcript có timings, features trong Application, một section-evaluator port** | A + B | **Ngay hôm nay.** Không cần đầu vào bên ngoài, không cần provider, không cần phán quyết pháp lý |
| **ADR-0017 — Transcript caching and retention for Speaking** | C | Cần đầu vào của agent bảo mật về thời hạn giữ và cơ chế quét xoá. Giá trị retention vẫn là seam |
| **ADR-0018 — Speaking session model under `H-1`** | G + D | Ngay hôm nay, dưới dạng ghi nhận cả hai cách đọc + chi phí đo được + mặc định hiện tại. Supersede ngôn ngữ "blocking" ở `confirmed.md:222-226` và `assumptions-and-open-questions.md:492-497` |

**ADR-0005 nhận một ghi chú cập nhật, không bị supersede.** Tên port mà nó đề xuất — `ISpeechRecognizer`, `ISpeakingEvaluator`, `IFeedbackGenerator` — **chưa bao giờ được hiện thực**; code chọn `ITranscriptSource` + `ISectionEvaluator`. *Nguyên tắc* (port ở Application, type vendor chỉ ở Infrastructure, word-level timings bắt buộc) không đổi và vẫn ràng buộc. Ghi lại việc đổi tên để người đọc kế tiếp không đi grep một type không tồn tại. Đó là một note nối vào ADR-0005, **không phải một ADR mới** — quyết định không hề thay đổi.

**Chọn ASR (E) trở thành ADR *khi chủ sản phẩm quyết*, không phải trước.** CLAUDE.md nói rõ: *"An ADR records a decision, not an open question."*

---

## 6 · Thứ tự thi công

### 6.1 Dựng và kiểm chứng được NGAY BÂY GIỜ — chưa chọn ASR, chưa có phán quyết PDPL

| # | Hạng mục | Cách chứng minh nó chạy (đỏ trước, xanh sau) |
|---|---|---|
| **1** | `Transcript` / `TranscribedWord` + `ITranscriptSource` mở rộng + `NoTranscriptSource` có `IsConfigured` | Các test Speaking hiện có **vẫn** báo `AwaitingVoiceProvider`; thêm một test mới chứng minh trạng thái đó đến **từ `IsConfigured == false`**, không phải từ một giá trị null trả về. Gỡ `IsConfigured` ra phải làm test đỏ |
| **2** | **Trích xuất feature trong Application, từ một `Transcript` dựng tay** | **Hạng mục giá trị cao nhất mà hoàn toàn không bị chặn.** Không audio, không vendor, không key. Mọi con số trong bảng Stage 4 (`speaking-pipeline.md:70-88`) có một unit test với đáp án biết trước. Đây chính là thứ phân biệt Level B với Level A |
| **3** | `EvaluationRequest` thêm `Features` | Toàn bộ test Writing xanh **không sửa một dòng** — đó là tiêu chí nghiệm thu |
| **4** | `ITranscriptStore` + hiện thực Mongo insert-if-absent + runner dùng nó | Một fake transcript source **đếm số lần gọi**: chạy job hai lần → đúng **một** lần phiên âm. Test này là toàn bộ lý do hạng mục tồn tại, và nó không cần provider nào |
| **5** | Bỏ chấm-tại-chỗ cho Speaking (`ExamHandlers.cs:1160`) | Nộp một sitting Speaking; khẳng định request **không** gọi evaluator và outbox giữ một job `Pending` |
| **6** | `SpeakingSectionEvaluator` trong Infrastructure với **recorded client** | Đã có khuôn mẫu chạy được: `Ai/Writing/RecordedWritingEvaluationClient.cs`. Chứng minh toàn tuyến — dựng prompt → validate schema → `CriterionMarking.Mark` → **đối chiếu bằng chứng với transcript** — không mạng, không key. Dưới `AiDataClassification.Synthetic` việc này **hợp lệ hôm nay**, không vướng câu hỏi PDPL nào |
| **7** | Schema + validator đầu ra cho Speaking | Soi gương `Ai/Writing/WritingEvaluationSchema.cs` + `WritingEvaluationValidator.cs`. `CriterionKeys.Speaking` đã có sẵn (`Rubric.cs:109-112`) |
| **8** | Cấu hình rubric `Assessment:Speaking` | `RubricOptions` đã bind nó (`RubricOptions.cs:31, :83`); chỉ thiếu artifact + version. Ghi rõ: `H-8a` khiến `DescriptorSource` vẫn là bản thay thế tổng hợp, **y hệt tình trạng Writing hôm nay** |
| **9** | **Nghĩa vụ ADR-0004: mở rộng architecture test** | `ForbiddenDependencies` (`PersistenceBoundaryTests.cs:40-59`) đã chặn `OpenAI`, `Google.Cloud`, `Google.Apis`, `Azure.AI`, `Amazon` — tình cờ phủ phần lớn vendor ASR khả dĩ. **Không phủ** Deepgram, AssemblyAI, Speechmatics, hay binding Whisper tự host. **Thêm cái được chọn vào đúng ngày nó được chọn.** Một luật không được kiểm sẽ mục âm thầm, và đây là luật mà cuộc di trú phụ thuộc vào |

**Thứ tự khuyến nghị: 1 → 2 → 3 → 6 → 7 → 4 → 5 → 8 → 9.** Mục 2 đứng sớm vì nó là phần có giá trị nhất và ít bị chặn nhất; mục 6 đứng trước 4 vì một pipeline chạy được với recorded client làm cho việc kiểm chứng mục 4 trở nên tầm thường.

### 6.2 Phải chờ

| Hạng mục | Chờ ai | Ghi chú |
|---|---|---|
| Adapter ASR thật | `V-10` (chủ sản phẩm) + `B-2`/audio (pháp lý) | Nhờ các port ở trên, việc này là **một file mới trong Infrastructure và một dòng DI**. Không gì khác |
| Bật chấm Speaking cho học viên thật | Như trên, cộng DPA, cộng câu hỏi CTIA riêng cho audio | Audio là một loại chuyển giao khác với văn bản |
| Client nộp theo từng part | `H-1` | Server đã sẵn sàng (§3.4) |
| Dịch vụ pronunciation (Level C) | `H-3` | Là adapter thêm vào, không đổi Application (§1.5) |

### 6.3 Cố ý **không** xây

`IEvaluator<TRequest,TResponse>` tổng quát · specification pattern trên transcript · một port `ISpeakingEvaluator` riêng · mediator · lịch sử chấm theo event sourcing · `AiJob.featureSnapshot` như một document riêng.

Mỗi thứ trên đều trượt phép thử của ADR-0004: **nó có giảm chi phí di trú nhiều hơn cái nó cộng vào mọi tính năng xây từ nay đến lúc đó không?** Không cái nào đạt.

---

## 7 · Tóm tắt trạng thái các khẳng định trong báo cáo này

| Khẳng định | Trạng thái | Tag |
|---|---|---|
| `ITranscriptSource` trả `string?`, mâu thuẫn ADR-0005 | `EXISTING` — kiểm chứng tại `SectionMarkingRunner.cs:312` | — |
| `speakingTiming.parts` chịu được cả hai cách đọc `H-1` | `EXISTING` — kiểm chứng tại `contracts/schemas/exam.schema.json:122-146` | — |
| `SectionAttempt` đã hỗ trợ cả hai mô hình phiên | `EXISTING` — `ExamSession.cs:313, :194-221` | — |
| `H-1` không còn chặn mô hình phiên thi | `PROPOSED` | Cần ADR-0018 để chính thức hạ cấp |
| Chấm tại chỗ trong request nộp bài sẽ không chịu nổi ASR | `PROPOSED` | `[TECHNICAL RISK]` |
| ASR bị trả tiền tối đa 5 lần cho một bài nói | `EXISTING` — suy ra từ `MarkingWorker.cs:78` + không có transcript store | `[TECHNICAL RISK]` |
| Phân loại retry mâu thuẫn giữa runner và worker | `EXISTING` — `SectionMarkingRunner.cs:222-229` vs `MarkingWorker.cs:314-325` | `[TECHNICAL RISK]` — lỗi thật hôm nay, ảnh hưởng Writing |
| Deadline part Speaking chia đều là sai | `EXISTING` — `ExamSession.cs:212-214` vs `ExamContent.cs:319` | `[TECHNICAL RISK]` — chỉ kích hoạt nếu `H-1` → ba part |
| Quy ước part id `"{module}-part-{order}"` parse bằng `int.Parse` | `EXISTING` — `ExamSession.cs:213-215` | `[TECHNICAL RISK]` — nợ cũ, **không** sửa kèm |
| Transcript đã lưu là dữ liệu cá nhân cần quét khi xoá tài khoản | `PROPOSED` | Bảo mật thắng mặc định — yêu cầu cứng của ADR-0017 |
| Thời hạn giữ transcript "2 năm" | `system-architecture.md:345` | `[ASSUMPTION]` — chưa phải quyết định của chủ sản phẩm; giữ làm seam `G-11` |
| Level B là baseline | `assumptions-and-open-questions.md:558` | `[ASSUMPTION]` · `H-3` `[BUSINESS DECISION]` |
| Chọn ASR | `UNCONFIRMED` | `V-10` `[BUSINESS DECISION]` + `[NEEDS VALIDATION]` (WER trên giọng Việt) |
