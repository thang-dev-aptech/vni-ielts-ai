# Chấm Speaking — phương án tối ưu chi phí, và những thứ phải chuẩn bị

**Mở 21/09/2026.** Chắt lọc từ ba nguồn: [`../research/2026-09-21-speaking-cost-optimised-architecture.md`](../research/2026-09-21-speaking-cost-optimised-architecture.md)
(nghiên cứu ngoài, chủ sản phẩm đưa vào), `docs/ai/provider-comparison.md` + `docs/ai/speaking-pipeline.md` (đã có trong
repo), và kiểm chứng trực tiếp trên code ngày 21/09.

Đây là **một phương án**, không phải danh sách lựa chọn. Slice `W9` trong
`web-enduser-completion.md` trỏ về đây.

---

## Ý tưởng trung tâm

> **Azure là chi phí nghiên cứu, không phải chi phí trên đầu học viên — cho tới khi hiệu chỉnh
> chứng minh nó xứng đáng.**

Lý do là `G-11`, không phải tiết kiệm. Azure trả điểm 0–100; IELTS trả band 0–9; **không có ánh xạ
nào được công bố**. Chừng nào chưa có bộ mẫu người chấm thì điểm Azure là con số **không được phép
dùng để ra band**. Trả tiền $0,044 mỗi bài cho một con số không được dùng là trả tiền để thu dữ liệu
nghiên cứu trên lưng từng học viên.

Nên: bật Azure cho **một cohort ~200 bài** (tốn một lần, ~$9), không bật cho mọi bài.

---

## Kiến trúc

```
                        BẢN GHI CỦA HỌC VIÊN
                                 |
        +------------------------+------------------------+
        |                                                 |
   TOÀN BỘ AUDIO                                    MẪU ~120 giây
        |                                     trải Part 1 + 2 + 3
        v                                                 |
  whisper-1, response_format = verbose_json               |
        |                                                 |
  transcript + word timings                               |
        |                                                 |
   +----+--------------------+                            v
   |                         |                Azure Pronunciation
   v                         v                 (scripted, dùng
Số học thuần            LLM văn bản            transcript whisper
trên timings          (đường Writing            làm ReferenceText)
   |                    đã có sẵn)                        |
Fluency &                   |                             |
Coherence            Lexical Resource                Pronunciation
                  Grammatical Range               ── ĐẶC TRƯNG, ──
                                                    không phải band
```

**Bốn điều làm nên giá rẻ, không cái nào là mẹo:**

1. **`verbose_json` không đắt hơn `json`.** whisper tính tiền theo **phút audio**, không theo hình
   dạng response. Word timings **miễn phí** — hôm nay chỉ đang không xin.
   [OpenAiAudioTranscriber.cs:113](../../backend/src/Vni.Ielts.Infrastructure/Ai/Importing/OpenAiAudioTranscriber.cs)
   cố ý xin `json`, và comment ghi thẳng verbose là *"precisely what Speaking marking would need"*.
2. **Fluency là số học, không phải model.** Khoảng lặng, tốc độ nói, độ dài cụm, độ trễ phản hồi —
   tất cả suy ra từ word timings bằng phép trừ. **Không GPU, không API, $0.**
3. **Azure scripted PA nhận `ReferenceText` từ bất kỳ ASR nào** — không bắt buộc dùng Azure STT.
   Bỏ hẳn một lần gọi Azure.
4. **Mẫu 120s trải ba part, không lấy một khối liền.** Part 2 có 1 phút chuẩn bị nên là đoạn trôi
   chảy nhất; chấm phát âm chỉ ở đó thì **chấm cao hơn thực tế một cách có hệ thống**.

---

## Chi phí

Một bài ≈ 11 phút audio ghi được.

| Khoản | Đơn giá | Mỗi bài |
|---|---|---|
| whisper-1 toàn bài | $0.006/phút | **$0.066** |
| Trích đặc trưng fluency | số học cục bộ | **$0** |
| LLM chấm Lexical + Grammar | ~5k token vào | **~$0.015** |
| **Giai đoạn 1 — 3 tiêu chí** | | **≈ $0.08** |
| Azure PA, mẫu 120s | $1.32/giờ | +$0.044 |
| **Sau hiệu chỉnh — 4 tiêu chí** | | **≈ $0.125** |

**Cohort hiệu chỉnh, tốn một lần:** 200 bài × ($0.066 + $0.044 + $0.015) ≈ **$25 tổng.**

`[NEEDS VALIDATION]` Đơn giá là giá niêm yết đọc ngày 21/09/2026, chưa phải hợp đồng.

---

## Chống phá — và đây mới là đòn bẩy thật

Đổi kiến trúc $0.50 → $0.125 giảm thiệt hại **4 lần**. Một cái trần giảm **~1000 lần**. Hai thứ
khác bậc, và tối ưu đơn giá **không phải** câu trả lời cho phá hoại.

| Lớp | Cơ chế |
|---|---|
| 1 | **Băm nội dung bản ghi.** Cùng bytes → cùng kết quả, lấy từ kho, **không gọi provider**. Giết kiểu phá rẻ nhất: tải một file rồi bấm lại trăm lần |
| 2 | **Hết VNI thì không gửi đi chấm** — ngoại lệ của `Q-06d` cho riêng Speaking. Bài vẫn lưu, vẫn nghe lại được |
| 3 | **Trần N bài Speaking/ngày/tài khoản** |

> **Bảng giá `Q-06` đã tự chứa sẵn một cái trần hợp lý** — đăng nhập +3 VNI/ngày, một phiên −5 VNI,
> tức nuôi được **0,6 phiên/ngày**. `Q-06d` (*"hết VNI không chặn"*) là thứ ngắt con số đó khỏi
> thực tế. `Q-06d` được chốt khi thao tác đắt nhất là chấm Writing, tức **vài xu**; Speaking đắt hơn
> một bậc. Quyết định vẫn đúng với tiền đề của nó — **tiền đề vừa đổi.**

Xấu nhất với trần 3/ngày: **3 × $0.125 = $0.375/tài khoản/ngày.**

---

## Ba giai đoạn

| | Làm gì | Ra gì | Chi phí |
|---|---|---|---|
| **G1** | Sửa cổng `ITranscriptSource` → `SpeechTranscript` (text + word timings) · whisper `verbose_json` · fluency số học · LLM text · băm trùng + trần | **3 tiêu chí có band. Pronunciation nói thẳng "chưa chấm".** | $0.08/bài |
| **G2** | Bật Azure PA **chỉ cho cohort**. Lưu đặc trưng thô. Không ra band nào từ nó | Bộ dữ liệu hiệu chỉnh | ~$25 một lần |
| **G3** | Hồi quy ordinal trên cohort. Đo QWK / sai số. **Đạt thì bật, không đạt thì không bật** | Pronunciation có band, hoặc quyết định không có | +$0.044/bài |

**Không làm trong cả ba giai đoạn:** prosody/phoneme cục bộ (GPU), adaptive routing, audio LLM. Đó là
§8/§12/§17 của tài liệu nghiên cứu — đúng về khoa học, nhưng chúng **chuyển chi phí từ hoá đơn sang
bảng lương** và cần dữ liệu chưa tồn tại.

---

# ANH CẦN CHUẨN BỊ GÌ

Xếp theo thứ tự *chặn nặng nhất trước*, không theo thứ tự dễ làm.

## A. Đề Speaking — **rào lớn nhất, và không ai nhắc tới nó**

Đo ngày 21/09/2026 trên `fixtures/exams/`:

```
25 đề:  reading 25 · listening 17 · writing 1 · speaking 1
Đề duy nhất có Speaking: exam-1.json — đề fixture demo
```

**Toàn hệ thống có đúng một bài Speaking.** Không thể chạy sản phẩm chấm Speaking trên một đề: học
viên thi lại đúng bộ câu hỏi đó mãi, và **cohort 200 bản ghi trên một bộ câu hỏi sẽ hiệu chỉnh theo
chủ đề chứ không theo năng lực** — mô hình học được "nói về chủ đề này thì band 6", vô dụng ở đề khác.

| Cần | Tối thiểu |
|---|---|
| Số đề Speaking đủ 3 part | **≥ 10** để chạy thật; **≥ 5** cho cohort |
| Mỗi đề | Part 1 (4–5 câu), Part 2 (cue card + prep 60s), Part 3 (4–5 câu thảo luận) |
| Khai `speakingTiming` | `PrepSeconds` / `ResponseSeconds` từng part |
| Bản quyền | Cùng thế đứng `Q-03`, hoặc đề VNI tự soạn |

## B. Dữ liệu hiệu chỉnh — chặn `G3`, không chặn `G1`

| Cần | Chi tiết |
|---|---|
| Số bản ghi | **100–200** (20–30 chỉ đủ để bắt lỗi thô, không đủ để tin) |
| Người nói | Học viên Việt, **band 4–8**, trải đều |
| Nội dung | Nói tự phát, **đủ cả ba part** |
| Điểm người chấm | **Từng tiêu chí riêng** — Fluency · Lexical · Grammar · Pronunciation. **Điểm tổng thôi là vô dụng** |
| Double-rate | **~30 bài, hai giám khảo** — để biết người chấm lệch nhau bao nhiêu. Không có số này thì không biết AI sai hay người sai |
| Ghi kèm | Danh tính giám khảo, ngày chấm, phiên bản rubric |

## C. Quyết định của anh — miễn phí, nhưng chặn cứng

| # | Quyết định | Chặn |
|---|---|---|
| 1 | **Đảo `P-02`** — Speaking được chấm trong MVP | tất cả |
| 2 | **`B-1`: chốt `whisper-1` qua reseller đang dùng** | `G1` |
| 3 | **Ngoại lệ `Q-06d`: hết VNI thì Speaking không gửi đi chấm** | chống phá |
| 4 | **Trần N bài Speaking/ngày** (đề xuất 3) | chống phá |
| 5 | **Giá một phiên Speaking** — 5 VNI như mọi phiên, hay cao hơn | `G1` |
| 6 | **Xác nhận: v1 KHÔNG trả band Pronunciation** | `G1` |

✅ Đã có: `Q-07` (21/09) — chấp nhận rủi ro PDPL cho giọng học viên.

## D. Tài khoản và khoá

| Khi nào | Cần gì |
|---|---|
| `G1` | **Kiểm chứng reseller có hỗ trợ `audio/transcriptions` + `verbose_json` không.** Chưa ai thử. Tôi test được bằng một file tổng hợp — cần anh cho phép gọi thật một lần |
| `G2` | Tài khoản Azure + Speech resource. Chọn region. **Khoá vào biến môi trường, không vào repo** (`CLAUDE.md` rule 6) |
| `G2` | Thêm host Azure vào `AiProviderPolicy.ContractedProcessorHosts` |

## E. Pháp lý

| Việc | Trạng thái |
|---|---|
| **DPA với reseller** | ❌ **vẫn chưa tồn tại** — `CLAUDE.md` rule 6, `M-28`. Đây là nợ có từ trước, và giọng nói làm nó nặng hơn |
| **Hồ sơ CTIA cho giọng nói** | ⏳ hạn 60 ngày kể từ **bản ghi học viên đầu tiên gửi ra nước ngoài** — đồng hồ thứ hai, khác đồng hồ bài viết đã chạy từ 03/09 |
| **Văn bản đồng ý của học viên** | ❌ chưa có — phải nói rõ giọng nói được gửi ra nước ngoài để chấm |
| **DPA với Microsoft** | khi tới `G2` |

## F. Người

| Vai | Vì sao cần |
|---|---|
| Giám khảo IELTS có chứng chỉ | Chấm 100–200 bài, 30 bài double-rate. **Hàng tuần công** |
| Người soạn đề Speaking | Mục A |
| Chủ sở hữu mô hình hiệu chỉnh | `G3` tạo ra một mô hình thống kê phải được **version, theo dõi drift, và giải thích được cho học viên khiếu nại**. Đây là nghiệp vụ mới, không phải một lần setup |

---

## Bắt đầu được ngay, không chờ gì

**Sửa hình dạng cổng** `ITranscriptSource.ForAsync` từ `Task<string?>` sang một `SpeechTranscript`
mang text + word timings. Không phụ thuộc vendor, không tốn một xu AI nào, và **mọi phương án ở trên
đều cần nó**. Đo ngày 21/09: word timing hiện **không được lưu ở đâu trong repo**.
