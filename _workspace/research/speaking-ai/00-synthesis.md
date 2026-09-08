# Speaking AI — tổng hợp nghiên cứu 5 trục

> **Ngày:** 2026-09-03 · **Trạng thái:** nghiên cứu, chưa có quyết định nào được thực thi
> **Nguồn:** năm báo cáo độc lập trong cùng thư mục — `01` pipeline/provider · `02` domain/scoring ·
> `03` calibration/testing · `04` privacy/security · `05` architecture/seams
> **Không file sản phẩm nào bị sửa. Không API nhà cung cấp nào được gọi.**

Tài liệu này chỉ giữ những kết luận **có ít nhất hai trục độc lập cùng chỉ tới**, cộng với những
phát hiện đơn lẻ đủ nặng để phải hành động. Chi tiết, trích dẫn nguồn và lập luận đầy đủ nằm trong
năm báo cáo gốc.

---

## 1 · Câu trả lời cho câu hỏi được hỏi

**Kiến trúc: hai tầng — ASR → trích đặc trưng xác định trong code → LLM. Không dùng audio-native.**

Ba trục độc lập cùng đi tới kết luận này, mỗi trục vì một lý do khác nhau:

| Trục | Lý do |
|---|---|
| `01` pipeline | Audio-native phá check 9: không có văn bản người học nộp, "trích dẫn" của model chính là bản phiên âm của nó — phép kiểm so đầu ra của model với đầu ra của model. Nó vẫn xanh và thôi có nghĩa |
| `04` security | `gpt-4o-transcribe` và Chirp 3 **không trả word timings** — audio-native không đáp ứng nổi `V-10` ngay từ đầu. Và nó đóng luôn cửa lai ghép ASR-tự-vận-hành + LLM-thuê, vốn là thế đứng PDPL tốt nhất |
| `05` architecture | Không có transcript thì không có gì để tính lại; mọi lần chấm trong quá khứ thành không kiểm toán được |

**Cái giá nếu sai lệch nhau rõ rệt:** hai tầng sai thì chỉ sai ở Pronunciation, và sửa được bằng cách
*cộng thêm*. Audio-native sai thì sai về kiến trúc — không có transcript để thêm vào sau.

**Độ sâu: Level B làm nền, nhưng bỏ trống Pronunciation. Chạy Level C ở chế độ bóng.**

---

## 2 · Điều nặng nhất chưa ai nói thẳng: Level B không thể sinh band Pronunciation

Hai trục tìm ra độc lập, từ hai hướng.

`01` lập luận từ bản chất dữ liệu: transcript không chứa *ít* thông tin phát âm — nó chứa **không có
gì**. Con số model viết ra là suy diễn từ prior về lỗi phát âm của người nói tiếng Việt, tức một
**prior nhân khẩu học**, không phải phép đo.

`02` kiểm chứng bằng code: `CriterionAssessment.Create` bắt buộc ≥1 đoạn bằng chứng;
`IsGroundedIn` so khớp chuỗi con với bài làm. **Mọi descriptor Pronunciation ở mọi band đều mô tả
thứ thuộc về âm thanh** — âm vị, ngắt nhóm, trọng âm, ngữ điệu, độ dễ hiểu. Không có gì trong
transcript để trích. Model chỉ còn hai đường: trích một đoạn không chứng minh được phán đoán (qua
được mọi phép kiểm, vô nghĩa, và **tệ nhất vì trông như đã kiểm chứng**), hoặc dính
`EvidenceNotGrounded` ở mọi lần chấm.

**Mọi lớp bảo vệ đều xanh trên một con số không đo gì cả.** Dùng ASR word-confidence làm proxy còn
tệ hơn: nó tương quan với độ nặng của giọng, tức phạt giọng chứ không phạt độ dễ hiểu.

Thêm một tầng mà không gì trong kiểu dữ liệu nói ra: với Speaking, phép kiểm bằng chứng đối chiếu
lời model với **transcript của máy**, tức kiểm theo cái máy *nghe được*, không phải cái học viên
*nói ra*. Bảo đảm này yếu hơn Writing về bản chất.

**Hai phương án trung thực — `[BUSINESS DECISION]`, không tự chốt:**

| | Chi phí/lượt | Đánh đổi |
|---|---|---|
| **B + bỏ trống** | ~$0.066 | `pronunciation.band: null`. Trung thực, nhưng **không đúng hình dạng IELTS** — người học thấy 3 điểm thay vì 4 |
| **C** (dịch vụ chấm phát âm riêng) | ~$0.093 (+40%) | Có đủ 4 band. Nhưng ba cái bẫy ở §6 báo cáo `01` |

Ba bẫy của C, đáng đọc trước khi chọn: cách khắc phục do chính Microsoft khuyến nghị tạo ra một
reference text vòng tròn **ưu ái người nói có giọng**; `AccuracyScore` đo *độ giống người bản ngữ*,
là sai cấu trúc khái niệm với IELTS (IELTS chấm **độ dễ hiểu**, không chấm giống bản ngữ); và
prosody chỉ có cho `en-US`.

**Đề xuất trình tự:** ship B với ô bỏ trống, chạy C ở chế độ bóng ghi vào `featureSnapshot` cho tới
khi hiệu chỉnh được ánh xạ của nó với điểm người chấm. `pronunciation.band` phải **nullable trong
chính schema**, để "bỏ trống" là một trạng thái diễn đạt được bằng hợp đồng chứ không phải bằng quy ước.

---

## 3 · Phát hiện sắc nhất: lời giải tối ưu về quyền riêng tư và về độ chính xác là cùng một lời giải — và chưa ai viết nó ra

Giả thuyết ban đầu — "tự vận hành Whisper trong nước là lối thoát" — **sai ở nửa sau**, và trục `04`
không giấu điều đó để giữ lập luận:

> McGuire 2025 (arXiv 2503.06924): **Whisper large-v3 trên tiếng Anh do người Việt nói đạt MER 0.124
> so với 0.007 của người bản ngữ — nhóm tệ nhất trong mọi nhóm được đo. Trên giọng nói tự phát,
> Whisper là hệ tệ nhất trong 5 hệ.**

Tức là đúng nhóm học viên của VNI, và đúng kiểu lời nói của bài thi Speaking.

Khảo sát hạ tầng trong nước (agent con của `04`) khẳng định thêm:

- **Không một nhà cung cấp ASR Việt Nam nào công bố word-level timestamps.** Với FPT `hmi/asr` đây là
  **phủ định đã kiểm chứng** — schema chỉ có `status`, `hypotheses[].utterance`, `id`. Các vendor
  còn lại không đọc được schema → *chưa xác nhận*, không phải "chắc là có".
- **Sản phẩm ASR Việt Nam là sản phẩm tiếng Việt.** Không ai nêu tiếng Anh trong danh sách ngôn ngữ,
  **không ai công bố WER tiếng Anh** — trong khi giọng thí sinh IELTS là ca khó nhất.
- **VinAI đã bị Qualcomm mua từ 01/04/2025** — không còn là vendor Việt Nam để mua dịch vụ.
- **Đội H100 của GreenNode được mô tả đặt tại Thái Lan** — vẫn là chuyển giao xuyên biên giới, làm
  hỏng chính lý do chọn nó.

### Phương án D không tồn tại. Phương án E thì có, và không tài liệu canonical nào có nó.

`04` bác bỏ phương án được kỳ vọng nhất: **không nhà cung cấp ASR nào có vùng Việt Nam.** Mọi cam kết
"residency" là Singapore/Sydney — **vẫn là chuyển qua biên giới theo Điều 20(1)(b)**.

**Phương án E — container của nhà cung cấp nước ngoài chạy trong hạ tầng của VNI:**

| | Ghi chú |
|---|---|
| Azure Speech disconnected container | STT 10.000 giờ/năm — $74.100 |
| Deepgram self-hosted | |
| Speechmatics appliance | |

**Không có chuyển giao nào xảy ra, mà cả ba đều tốt hơn Whisper trên nhóm người nói tiếng Việt.**
Word timing là thuộc tính của runtime, không phải của vendor — nên nó cũng giải quyết `V-10`.

Hạ tầng có thật: **FPT AI Factory** là nơi duy nhất có bảng giá GPU công khai tự phục vụ — H100 SXM5
**$2.54/GPU-giờ**, hoặc dòng GPU cũ **11,8–27 triệu VND/tháng**. Hai cảnh báo: giá USD ghi rõ *"áp
dụng cho khách không cư trú tại Việt Nam"*, và nhãn model GPU trên bảng VND mâu thuẫn với thông số
NVIDIA — **giá tin được, nhãn model chưa**.

**Một phép thử gỡ được phần lớn bất định:** gửi một file audio tiếng Anh tới
`https://mkp-api.fptcloud.com/v1/audio/transcriptions` kèm `timestamp_granularities: ["word"]`.
Endpoint theo khuôn OpenAI nhưng tài liệu FPT **không nhắc tới tham số đó**. Nếu nó tôn trọng: FPT
vừa giải quyết word timings, vừa giải quyết cư trú dữ liệu, vừa có giá công khai — trong một endpoint.
**Chưa chạy. Cần credential FPT và sự cho phép của chủ sản phẩm. Phải dùng audio tổng hợp.**

---

## 4 · Cảnh báo pháp lý — lớn hơn câu hỏi được giao

`04` phát hiện khung pháp lý trong repo **đã lỗi thời**, và có một hồ sơ thứ hai **nhiều khả năng
đang quá hạn**.

### 4.1 · Nghị định 356/2025/NĐ-CP (31/12/2025) đã thay Nghị định 13/2023

`docs/security/privacy-vietnam-pdpl.md` vẫn viết theo văn bản cũ.

### 4.2 · Có một đồng hồ thứ hai, và nó không liên quan gì tới biên giới

Toàn bộ `B-2` và `AiOptions` chỉ nói về CTIA (Điều 20). Nhưng:

> **Điều 21 buộc nộp hồ sơ ĐGTĐ *xử lý* dữ liệu cá nhân trong 60 ngày kể từ lần xử lý đầu tiên.**

Đồng hồ đó **bắt đầu từ học viên đăng ký đầu tiên**, không phải từ 02/09/2026. Nó không chờ AI, không
chờ biên giới, không chờ Speaking.

### 4.3 · Bật Speaking là hành động làm mất miễn trừ 5 năm

> **Điều 38(2)** cho doanh nghiệp nhỏ **5 năm miễn hồ sơ ĐGTĐ — trừ đơn vị "trực tiếp xử lý dữ liệu
> cá nhân nhạy cảm".**

Điều 4 liệt kê **"dữ liệu sinh trắc học, đặc điểm di truyền"** là nhạy cảm. Danh mục không viết chữ
"giọng nói", nhưng Luật Căn cước 26/2023 thì có. **Bật Speaking rất có thể là hành động làm mất miễn
trừ; bật Writing thì chưa chắc.** Đây là khác biệt pháp lý thật giữa hai kỹ năng, và nó chưa từng
xuất hiện trong bất kỳ tài liệu nào của dự án.

### 4.4 · Nghĩa vụ không có trong repo

**Điều 30(4): xử lý dữ liệu cá nhân bằng AI phải được phân loại theo mức rủi ro.** Không tài liệu nào
nhắc tới.

### 4.5 · Thêm ASR

Không đẻ hồ sơ CTIA mới (Điều 20(3) — làm một lần cho cả vòng đời) **nhưng buộc cập nhật ngay** theo
Điều 22(2)(c), và cần **đồng ý riêng** theo Điều 9(4)(a) + Điều 31.

### 4.6 · Câu hỏi cần hỏi luật sư, hỏi đúng cách

> *"Bản ghi giọng nói thu để chấm phát âm, không để định danh, có thuộc 'dữ liệu sinh trắc học' tại
> Điều 4 Nghị định 356/2025 không?"*

Điều 31(2) định nghĩa sinh trắc học **gắn với mục đích định danh** — đó là khoảng hở duy nhất đáng
hỏi. Không agent nào ở đây là luật sư.

### 4.7 · Khuyến nghị mạnh nhất của trục bảo mật

**ASR không được đi qua reseller trong bất kỳ hoàn cảnh nào.** Mọi cam kết ZDR/residency là hợp đồng
với nhà cung cấp, và **không cam kết nào sống sót qua một `baseURL` trung gian**.

---

## 5 · Lỗi trong code hiện tại — thật, hôm nay, phần lớn không liên quan tới Speaking

| # | Vị trí | Lỗi | Ảnh hưởng |
|---|---|---|---|
| 1 | `ExamHandlers.cs:1158-1160` | **Chấm ngay trong request HTTP** rồi mới nhờ worker | Writing đã 120s; ASR cả bài nói thì proxy cắt |
| 2 | `SectionMarkingRunner.cs:222-229` vs `MarkingWorker.cs:314-325` | Comment ghi "không retry, hỏi lại là tính tiền hai lần" — **worker retry 5 lần** | **Lỗi thật hôm nay với Writing.** Hai comment ngược nhau, worker đang thắng |
| 3 | `WritingSectionEvaluator.ComputeIdempotencyKey` | Băm `{rubricVersion}:{prompt}:{submission}` | Với Speaking, `submission` là **output của máy không tất định** → re-ASR lệch một từ = key khác = chấm lại = **trả tiền ASR 5 lần và có band khác**. Phải băm checksum audio |
| 4 | `SectionMarkingRunner.MarkUnitAsync` | `NothingSubmitted` chỉ bắt **không có** bản ghi; không gì bắt **có một phần** | Thu mỗi Part 1 rồi nộp → chấm trên ⅓ bằng chứng, **không gì nói ra**. Luật đúng đã có sẵn cho Reading trong `band-scoring.md` |
| 5 | `RetentionExpiresAt` | Ghi vào Mongo, **không đường code nào đọc lại**. `RecordingReconciliation` chỉ xoá bản ghi *mồ côi* | Tuân thủ giới hạn lưu trữ phụ thuộc hoàn toàn vào một bucket lifecycle rule **mà không gì kiểm tra nó tồn tại**, và nó không bao giờ chạm Mongo |
| 6 | Không có `AiJob` | `MarkingJob`/`SectionMarking` **không ghi provider, model, hay mốc gửi** | **Hồ sơ CTIA không có bằng chứng để dựa vào.** Với các bài đã chấm thật từ 02/09, hệ thống không trả lời được câu hỏi mà chính hồ sơ sẽ hỏi |
| 7 | `S3SpeakingRecordingBlobStore.CreatePresignedPutUrl` | **Hạ HTTPS xuống HTTP** khi `ServiceUrl` bắt đầu bằng `http://` | Đường MinIO local, nhưng **không gì chặn cấu hình production làm vậy** |
| 8 | `PersistenceBoundaryTests` `ForbiddenDependencies` | Phủ OpenAI/Google/Azure/Amazon nhưng **không phủ** Deepgram, AssemblyAI, Speechmatics, Whisper tự host | Luật ADR-0005 không được kiểm cho đúng vendor sắp chọn — **một luật không được kiểm sẽ mục âm thầm** |
| 9 | `NoTranscriptSource` path | Bản ghi 4 phút **toàn im lặng** → transcript rỗng → báo `AwaitingVoiceProvider` | Nền tảng **đổ lỗi cho ASR của chính nó** thay vì cho học viên không nói gì |
| 10 | `OpenAiWritingEvaluationClient` | Prompt prefix ~650 token, **dưới ngưỡng cache 1.024 token** → **0 cache hit**; `ExtractResponsesUsage` không đọc `cached_tokens` | Đang trả giá đầy đủ mỗi lần, **và không ai đo được** |
| 11 | `ExamSession.cs:212-214` `AdvanceToNextPart` | Chia đều thời lượng cho các part | Sai với Speaking — **chỉ Part 2 có phút chuẩn bị**. Lỗi nằm chờ đúng ngày `H-1` được trả lời |

Thêm hai lỗi im lặng ở tầng nhà cung cấp, cần bẫy **trước** khi chọn:

- **Deepgram và AssemblyAI xoá `uh`/`um` theo mặc định.** Filler density — một feature Fluency có tên
  — sẽ bằng **0 cho mọi học viên**, không báo lỗi, và **thổi Fluency lên toàn hệ thống**. Cần cờ tường
  minh, test đã kiểm chứng đỏ, và một guard lúc chạy: filler density đúng bằng 0.0 ở band <7 là **cấu
  hình sai**, không phải học viên trôi chảy.
- **Google cảnh báo timing của `gemini-3.5-transcribe` *làm giảm* độ chính xác** — nên phải benchmark
  **với timing đang bật**, không phải benchmark rồi bật sau.

---

## 6 · Đo lường — không có phần này thì mọi khuyến nghị trên chỉ là ý kiến

### 6.1 · Ngưỡng hiện tại chưa mạch lạc

`vision-and-scope.md` ghi **"±0.5 ≥80%"**. Con số này được viết **trước khi có bất kỳ phép đo
người–người nào tại VNI**. Nếu hai giám khảo VNI chỉ đồng ý ở mức 78%, mục tiêu đó đang **yêu cầu AI
vượt người**.

**Phát biểu lại:** `≥80%` **hoặc** `≥0.90 × trần người–người đo được`, lấy giá trị thấp hơn.

### 6.2 · Bộ 30–50 của `H-8c` là con số khả thi, không phải con số thống kê — và điều này đúng cho cả Writing đang chạy thật

Với n=50, khoảng tin cậy 95% của tỷ lệ 0.80 là **±11,1 điểm phần trăm**: **không phân biệt nổi 80%
với 69%**.

**Đề xuất:** 60 ngay, 120 trong 6 tháng, và **luôn in khoảng tin cậy kèm con số**.

### 6.3 · Điểm kỹ thuật giá trị nhất: gate trên MAE liên tục theo cặp, không trên tỷ lệ nhị phân

Ở n=60:

| Phép kiểm | Phát hiện được mức xấu đi |
|---|---|
| McNemar trên agreement nhị phân | ~**15 điểm phần trăm** (gần như không phát hiện được gì) |
| Wilcoxon signed-rank trên `\|d\|` theo cặp | ~**0,145 band** (`δ = 2.8·SD/√n`) |

**Nhị phân hoá vứt đi khoảng một phần ba cỡ mẫu.** Đây là thứ khiến n=60 dùng được làm cổng chặn
merge dù không đủ cho một tuyên bố tuyệt đối.

### 6.4 · Sai của ASR lệch một chiều, và đúng chiều nguy hiểm

Model ASR âm thầm sửa disfluency, bỏ từ lặp, chuẩn hoá ngữ pháp → transcript **trôi chảy hơn lời nói
thật** → Fluency/Grammar/Lexis **bị thổi lên mạnh nhất ở nhóm học viên yếu**, tức nhóm cần điểm chính
xác nhất. **WER tổng không nhìn thấy hiện tượng này.**

| Đo cái gì | Ngưỡng đề xuất |
|---|---|
| Disfluency retention rate | **≥80%** |
| Negation error rate | **≤2%** |
| **Pause-detection F1** ở T=250ms/500ms | **≥0,85** |

Cổng cho timing phải là **F1 phát hiện pause**, không phải sai số mili-giây — vì **không feature nào
đọc timestamp thô**, chúng đọc pause được *suy ra* từ timestamp.

### 6.5 · Metric không ai nghĩ tới, và sáu metric kia đều cho qua

**Tỷ lệ bài mà cả 4 tiêu chí bằng nhau.** Nếu **>60%**, AI không chấm bốn tiêu chí — **nó chấm một
rồi copy sang ba ô còn lại**. Mọi check trong `output-contracts.md` vẫn xanh, `sectionBand` vẫn khớp,
và toàn bộ giá trị thật với người học là hư cấu.

### 6.6 · `R5` chưa được đo lần nào

**Nhất quán khi chấm lại** — rủi ro High, mục tiêu 95% đã ghi trong tài liệu — **không có test nào
chấm lại cùng một bài**, kể cả cho Writing đang chạy thật.

### 6.7 · Cảnh báo từ chính nghiên cứu của IELTS

Seedhouse et al. 2014 (IELTS Partners xuất bản, 60 bài thi thật) đo **đúng** các đặc trưng mà
`speaking-pipeline.md` đề xuất:

- **Độ dài pause thô không phân biệt được band 5–8** (p<0,38). Chuẩn hoá theo 100 từ thì có — nhưng
  hậu nghiệm, và chỉ band 5 với band 8.
- **Độ phức tạp và ngữ pháp không đơn điệu**: band 7 **cao hơn** band 8 ở cả hai.
- **Tổng số từ đơn điệu và mạnh** (p<0,001) — **và không ai đang dùng**.
- Định tính: *"không xác định được đặc trưng nói đơn lẻ nào phân biệt được các band điểm."*

**Level B vẫn là nền đúng, nhưng vì lý do khác tài liệu đang ghi:** đặc trưng là **đầu vào cho một
phán đoán, không bao giờ là ánh xạ ra band**. Một luật kiểu `if (pauseRatio > x) fluency = 5` sẽ sai
theo đúng kiểu `H-4` — **sai một cách tự tin và vô hình**.

---

## 7 · Hình dạng code

### 7.1 · Ràng buộc quyết định toàn bộ thiết kế, và nó vô hình nếu không đọc kỹ

`Domain/Assessment/CriterionMarking.cs:262-269` đòi mỗi trích dẫn bằng chứng là **substring nguyên văn**
của `LearnerSubmission` — **cố ý** không stem, không so khớp theo từ.

> Nhồi timestamp hay nhãn `[Part 2]` vào chuỗi transcript → **mọi** bản chấm Speaking bị gắn cờ
> `EvidenceNotGrounded`. Không exception, không lỗi build. Hỏng toàn bộ, âm thầm.

**Đặc trưng phải đi *bên cạnh*, không đi *bên trong*.**

### 7.2 · Mở rộng `EvaluationRequest`, không thêm port thứ hai

```csharp
public sealed record EvaluationRequest(
    Rubric Rubric, string LearnerSubmission, string Prompt,
    IReadOnlyList<DeliveryFeature>? Features = null);

public sealed record DeliveryFeature(string Key, decimal Value, string? Unit);
```

Call site duy nhất (`SectionMarkingRunner.cs:214`) dựng theo vị trí 3 tham số → tham số optional giữ
nguyên khả năng biên dịch. **Tiêu chí nghiệm thu: test Writing xanh mà không sửa một dòng nào.**

Port thứ hai bị loại vì `SectionMarkingRunner:111` lọc `IEnumerable<ISectionEvaluator>` theo module và
đầu ra vẫn là `ClaimedEvaluation` y hệt. **Level C là một adapter thứ hai trong Infrastructure**, nên
`H-3` **không chặn việc xây gì cả**.

### 7.3 · `ITranscriptSource` sai theo ba cách

Không chỉ thiếu timings. `null` đang gánh **ba nghĩa** — ngày có provider thật, một lỗi 503 sẽ báo cho
học viên *"chưa chọn nhà cung cấp"*. Và nó thiếu `IsConfigured` mà `ISectionEvaluator` có.

```csharp
bool IsConfigured { get; }
Task<Transcript> TranscribeAsync(...);

record Transcript(string Text, IReadOnlyList<TranscribedWord> Words,
                  string ProviderId, string ModelId, decimal AudioSeconds);
record TranscribedWord(string Text, int StartMs, int EndMs,
                       decimal Confidence, int PartNumber);
```

`int` mili-giây và `decimal` — vì `PersistenceRepresentationTests:135-161` **fail build trên `double`**.
Bán kính nổ: **11 call site**, phần lớn là test. Không đụng Domain, Api, `contracts/`, `apps/`.

Cắt bớt so với `SpeechAnalysis` trong `four-skills-practice-and-mock-research.md:252-260`: bản đó gộp
bốn mối quan tâm, chốt trước Level C, và lưu `fluencyFeatures` — **dẫn xuất của `Words`, hai bản sao
sẽ lệch**.

### 7.4 · Rủi ro lớn nhất không phải port — là trả tiền ASR nhiều lần

`MaxAttempts = 5` và **không gì lưu transcript** → **một lỗi LLM kéo theo 5 lần chạy ASR cho cùng một
bài nói**.

**Sửa:** `ITranscriptStore` insert-if-absent khoá theo `sessionId` (khoá này sống được với **cả hai**
câu trả lời `H-1`). **Lưu transcript** (đắt để tái tạo), **tính lại features** (<100ms, miễn phí) —
không dựng `featureSnapshot` như một bản sao thứ hai.

Thêm: `/v1/audio/transcriptions` **không phải** endpoint của Batch API, nên phiên âm lại hàng loạt
**không được giảm 50%** — thêm một lý do nữa để tính lại từ transcript đã lưu.

### 7.5 · Schema — cơ chế đáng giữ nhất

**`featureRefs` ràng buộc bằng `enum` các khoá feature thực sự được cung cấp.** Model chỉ có thể viện
dẫn một feature *có tồn tại*, nên một `hesitation_index` bịa ra là **bất khả về mặt cấu trúc** — đúng
lối suy nghĩ đã làm cho band là enum, áp dụng cho phần lý giải thay vì phần điểm.

Hai điểm khác với schema Writing:
- `evidence` thành object mang theo `part`, để câu trích được đối chiếu với transcript **đúng part**.
- `pronunciation.band` **nullable**, và **`null` tuyệt đối không được ép về số** — đó chính là lỗi
  clamping đội lốt khác.

Siêu dữ liệu tái lập cần mở rộng: `asrProvider`, `asrModelVersion`, `featureExtractorVersion`,
`pauseThresholdMs`. **Hai lần chấm cùng `modelVersion` nhưng khác phiên bản ASR là không so sánh được,
và hiện không gì ghi lại sự khác biệt đó.**

### 7.6 · Injection qua giọng nói **dễ** phòng hơn qua chữ viết

Bảng chữ cái của transcript hẹp và biết trước → dùng được **allowlist** thay vì blacklist. **Mạnh hơn
`SanitizeLearnerText` hiện tại, và chỉ Speaking làm được.**

Lớp phát hiện mạnh nhất chỉ là **thêm một thành viên vào enum `MarkingFlag` đã có**:
`ImplausibleGivenFeatures`.

### 7.7 · Ranh giới dữ liệu thử nghiệm — một câu cho cả đội

> **Nếu có người nào từng mở miệng để tạo ra file này, nó là `LearnerPersonal`.**

Giọng nhân viên tình nguyện **không** phải `Synthetic`. Đồng ý bằng văn bản giải quyết *điều kiện đồng
ý*, **không biến dữ liệu thành dữ liệu bịa**. TTS là ngoại lệ duy nhất.

---

## 8 · `H-1` không còn chặn gì, và tài liệu đang nói sai về chính code của nó

`confirmed.md:222-226` viết `H-1` *"quyết định hình dạng `SectionAttempt`"*. Nhưng
`SectionAttempt.PartId` (`ExamSession.cs:313`) và `AdvanceToNextPart` (`:194-221`) **đã chạy** — mỗi
part một attempt, một deadline. `speakingTiming.parts` chịu được cả hai cách đọc: **kiểm chứng đúng**.

**Mấu chốt: IELTS báo một band Speaking duy nhất**, nên `H-1` là câu hỏi về **giao đề và nộp bài**,
không phải về **độ hạt của việc chấm**. Phía chấm (`Units()` sinh một `MarkableUnit`, khoá
`{session}:Speaking`, một `MarkingJob`) **đúng dưới cả hai câu trả lời, không đổi gì**.

Chi phí thật của phương án "ba part riêng": **~1 dòng ở server** (nới guard `PracticeUnitId is null`
tại `ExamSession.cs:198`) cộng việc vừa phải ở client.

Có phương án thứ ba **không cần đổi entity nào**: một lượt thi, có mốc nộp theo từng phần.

**Phần thực sự còn mở thì hẹp hơn nhiều, và chính là lỗi #4 ở §5:** *học viên có được phép nộp khi
mới thu một phần không, và khi đó kết quả là gì?*

---

## 9 · Chi phí — ASR chiếm phần lớn, đảo ngược trực giác từ Writing

| | Chi phí/lượt |
|---|---|
| Speaking | **$0,029 – $0,183** |
| Writing | ~$0,023 |

**Đòn bẩy, xếp lại theo độ lớn thật:**

1. **Thu lời học viên theo từng part, cắt bằng VAD, không thu cả phiên** — **40–50% chi phí ASR**, là
   cấu hình phía client, và plugin đã phân đoạn sẵn.
2. **Chọn nhà cung cấp ASR** — chênh **7 lần** ($0,023 AssemblyAI U-2 → $0,156 Speechmatics).
3. **Độ dài output** — **67%** chi phí LLM hàng đầu. Cắt 900→500 token output **tiết kiệm nhiều hơn
   caching**.

**Hai chỗ `docs/ai/cost-model.md` và `provider-comparison.md` sai:** giá Deepgram **cao gấp 5 lần**
thực tế ($0,0218 so với $0,0043/phút), và con số Azure là **giá real-time** trong khi batch là
$0,18/giờ. Và **prompt caching không phải đòn bẩy lớn nhất** với rubric cỡ này — **độ dài output mới
là**, còn caching hiện đáng giá **$0**.

Ngưỡng cache **không đơn điệu**: Gemini 3.5–3.8 Flash cần **4.096** token trong khi 2.5 Pro cũ cần
2.048. Một `[GOTCHA]` đã ghi, nay xác nhận theo cả chiều ngược lại.

---

## 10 · Tài liệu cần sửa

| File | Sửa gì |
|---|---|
| `docs/domain/band-scoring.md` | **Speaking KHÔNG thuộc diện `H-8b`.** ielts.org công bố rõ: bốn tiêu chí **trọng số bằng nhau, lấy trung bình**. Đang gắn `[ASSUMPTION]` cho một dữ kiện **đã có nguồn**. Từ chối tỉ lệ Task 1:Task 2 của Writing vẫn đúng; từ chối trung bình của Speaking là **bỏ qua nguồn**, không phải tuân thủ `G-11` |
| `docs/domain/band-scoring.md` | Thêm ghi chú (i) và (ii) của descriptors chính thức — *"chấm theo thành tích trung bình trên tất cả các phần"* và *"phải khớp đầy đủ các đặc điểm tích cực của descriptor"*. **Không xuất hiện ở bất kỳ đâu trong `docs/`** |
| `docs/security/privacy-vietnam-pdpl.md` | Nghị định **356/2025** thay 13/2023 · **dưới 16 tuổi** (Luật Trẻ em 2016 Điều 1) thay "dưới 18" · thêm **Điều 21, Điều 30(4), Điều 31** · **bỏ phương án D, thêm phương án E** |
| `assumptions-and-open-questions.md` `H-8a` | **Hẹp hơn và xấu hơn bản ghi.** Descriptors **có** nêu điều khoản, và rất chặt: chỉ dùng cá nhân phi thương mại, cấm dùng thương mại, cấm đăng lại, **cấm sửa đổi dưới bất kỳ hình thức nào**, và *"nộp đơn xin bản quyền không đồng nghĩa với việc đã có giấy phép hợp lệ"*. Nên "dùng bản public trong lúc chờ pháp lý" **không phải thế đứng trung lập**. Và "cấm sửa đổi" khiến bản VNI viết bằng cách diễn đạt lại bản gốc là **tác phẩm phái sinh** |
| `assumptions-and-open-questions.md` `V-10` | Thay bằng phép đo được: **sai số biên trung vị ≤100ms, p95 ≤250ms** — và gate thật là **pause-detection F1 ≥0,85**. *Có* timestamp không phải tiêu chí; **độ chính xác của nó mới là** |
| `docs/requirements/confirmed.md:222-226` | `H-1` **không còn quyết định hình dạng `SectionAttempt`** — đã hiện thực rồi |
| `docs/ai/cost-model.md` · `provider-comparison.md` | Giá Deepgram sai 5 lần · Azure là giá real-time không phải batch · caching **không** phải đòn bẩy lớn nhất |
| `docs/product/vision-and-scope.md` | Ngưỡng ±0,5 ≥80% phát biểu lại theo trần người–người đo được |
| ADR-0005 | Chỉ cần **ghi chú cập nhật**: tên port nó đề xuất (`ISpeechRecognizer`, `ISpeakingEvaluator`) **chưa bao giờ được hiện thực**; code chọn `ITranscriptSource` + `ISectionEvaluator`. Nguyên tắc không đổi → **không supersede** |

**Một điều đang đúng và không được "sửa":** `Assessment:Speaking:DescriptorSource` là `""` trong
`secrets.develop.json` với comment *"CHƯA ĐIỀN — H-3/H-8a chưa chốt, không tự bịa descriptor (G-11)"*,
nên Speaking báo `AwaitingRubric`. **Đó là `G-11` đang hoạt động đúng như thiết kế.** Còn giá trị
`synthetic-ielts-style-descriptors-for-functional-core-fixtures` của Writing được dán nhãn trung thực
— và **không được âm thầm trở thành giá trị production**.

---

## 11 · Việc làm được ngay, không chờ ai

Thứ tự đề xuất. Mọi mục dưới đây **không cần chọn ASR, không cần phán quyết PDPL, không cần API key**.

| # | Việc | Ghi chú |
|---|---|---|
| 1 | **Trích xuất feature trong Application từ `Transcript` dựng tay** | Giá trị cao nhất và **hoàn toàn không bị chặn**. Mọi con số bảng Stage 4 có unit test với đáp án biết trước. Không audio, không vendor, không key |
| 2 | Mở rộng `EvaluationRequest` với `Features` optional | Nghiệm thu: **test Writing xanh, không sửa dòng nào** |
| 3 | Thay `ITranscriptSource` → `Transcript`/`TranscribedWord` + `IsConfigured` | 11 call site, phần lớn là test |
| 4 | `ITranscriptStore` insert-if-absent khoá `sessionId` | Chặn việc trả tiền ASR 5 lần |
| 5 | `SpeakingSectionEvaluator` với recorded client | Khuôn `Ai/Writing/RecordedWritingEvaluationClient.cs` đã chạy, dưới `AiDataClassification.Synthetic` — **hợp lệ hôm nay, không vướng PDPL** |
| 6 | **Sửa lỗi #1, #2, #3, #7, #8, #9, #10 ở §5** | Phần lớn là lỗi của đường **Writing đang chạy thật**, không phải của Speaking |
| 7 | Comparator + manifest schema + adversarial fixture | **Hàm thuần trên JSON.** Cái bị chặn là *dữ liệu*, không phải *cổng* — chạy miễn phí trong CI |
| 8 | Trạng thái `PartiallySubmitted` cho Speaking | Lỗi #4; luật đúng đã có sẵn cho Reading |
| 9 | Đọc `RetentionExpiresAt` và thực sự xoá | Lỗi #5 |

**Bằng chứng cho cổng đo lường** (theo chuẩn repo — đỏ khi gỡ bản sửa): commit một **manifest
candidate đã bị làm hỏng** với 8 item dịch một band, kèm test assert comparator **fail** trên nó.
Chạy miễn phí, và **đỏ ngay khi ai đó nới một ngưỡng**.

Nhờ các port ở §7, **adapter ASR thật về sau chỉ là một file mới trong Infrastructure và một dòng DI.**

---

## 12 · Việc cần chủ sản phẩm quyết

| # | Quyết định | Vì sao không phải quyết định kỹ thuật |
|---|---|---|
| 1 | **Độ sâu `H-3`: B + bỏ trống, hay C** | Quyết định người học **thấy 3 band hay 4 band**, và +40% chi phí |
| 2 | **Chọn ASR (`V-10`)** | Mang theo **hoá đơn tính theo phút, một DPA, và nhiều khả năng nghĩa vụ hồ sơ mới** — audio ≠ văn bản |
| 3 | **Phương án E — container vendor chạy trong hạ tầng VNI** | Chi phí vốn (Azure disconnected: $74.100 cho 10.000 giờ/năm) đổi lấy **không chuyển giao xuyên biên giới** |
| 4 | **Nguồn descriptors `H-8a`** | Câu hỏi pháp lý, không phải kỹ thuật. Và **hẹp hơn bản ghi hiện tại** |
| 5 | **12 ngưỡng đo lường** | Đăng ký dưới `D12`/`M-51`, sống trong configuration theo `G-11` |
| 6 | **Có chạy phép thử word-timestamp của FPT không** | Cần credential FPT; là lời gọi ra dịch vụ bên ngoài. **Phải dùng audio tổng hợp** |
| 7 | **Hồ sơ ĐGTĐ Điều 21** | **Nhiều khả năng đã quá hạn.** Không phải quyết định kỹ thuật và không chờ Speaking |
| 8 | **Ý kiến luật sư về Điều 4 Nghị định 356/2025** | Câu hỏi đúng ở §4.6. Không agent nào ở đây là luật sư |

---

## 13 · ADR cần viết

| ADR | Nội dung | Loại |
|---|---|---|
| ADR-0016 | Hình dạng port (`EvaluationRequest.Features`, `Transcript`) + **chấm chỉ trong worker** | **`[QUYẾT ĐỊNH kỹ thuật]`** — viết được hôm nay |
| ADR-0017 | Cache transcript | **Kỹ thuật**, nhưng **retention/xoá là PDPL** — cần đầu vào pháp lý |
| ADR-0018 | Mô hình phiên `H-1` + nguồn deadline của part | **Kinh doanh, đã hạ cấp** — chi phí kỹ thuật gần bằng 0 |

Nếu chọn TTS cho `M-5`, một `[QUYẾT ĐỊNH kỹ thuật]` đáng ghi riêng: **render tại thời điểm publish**
vào version asset. Render lúc thi nghĩa là **hai học viên nhận audio khác nhau từ cùng một
`ExamVersion`** — mất chính tính tái lập mà tính bất biến sinh ra để bảo đảm.

---

## 14 · Một điều không agent nào được giao nhưng cả hai trục domain đều nêu

**Không có kỳ thi IELTS Speaking chính thức nào là bất đồng bộ hay chấm bằng máy** — kể cả IELTS
Online cũng là cuộc gọi video trực tiếp với giám khảo người, đặt lịch trước ngày thi viết.

Thứ VNI xây là **mô phỏng luyện tập**. Đó là **nghĩa vụ dán nhãn**, không phải một lời chê.
