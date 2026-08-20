# Đối chiếu với Edly (edly.vn)

Khảo sát 18/08/2026 bằng trình duyệt. Các màn mẫu dựng từ khảo sát này đã bị xoá cùng prototype;
phần phân tích dưới đây thì giữ lại vì nó là nghiên cứu sản phẩm, không phải thiết kế. Đây là đối thủ trực tiếp gần nhất: nền tảng luyện IELTS
Việt Nam, chấm Writing/Speaking bằng AI, có cả vai học viên lẫn giáo viên.

Ghi lại vì hai lý do: nó **lấp được một câu hỏi đang treo của mình (B-4)**, và nó **lộ ra một
khoảng trống lớn trong phạm vi sản phẩm của mình (vai giáo viên)**.

---

## Bản đồ module

| Module | Edly có | Mình đã thiết kế |
|---|---|---|
| Luyện 4 kỹ năng, full đề | ✅ | ✅ |
| **Luyện từng passage / từng task theo dạng câu hỏi** | ✅ | ❌ |
| Phòng thi mô phỏng máy tính, đồng hồ thật | ✅ | ✅ |
| Chấm Writing bằng AI theo 4 tiêu chí | ✅ | ✅ |
| Chấm Speaking bằng AI | ✅ | ✅ |
| **Giao bài cho lớp · quản lý lớp** | ✅ | ❌ **vai giáo viên hoàn toàn chưa có** |
| **Vào lớp bằng mã hoặc QR** | ✅ | ❌ |
| **Nghe chép chính tả (dictation)** | ✅ | ❌ |
| **Kiểm tra đầu vào → lộ trình cá nhân hoá theo band** | ✅ | ❌ |
| **Tài liệu tải về** (slide, lesson plan, đề) | ✅ | ❌ |
| SAT/ACT | ✅ | — *(ngoài phạm vi)* |
| Blog / bài viết | ✅ | ❌ |

---

## Vai giáo viên — đã quyết: ngoài phạm vi

Edly xây hẳn một nửa sản phẩm quanh vai giáo viên: chọn đề → giao cho lớp → theo dõi bài làm →
xem điểm theo từng học viên. Danh sách màn của mình khi đó không có màn nào cho vai này, nên
câu hỏi được nêu ra để xác nhận đó là chủ ý chứ không phải bỏ sót.

**Quyết định 18/08/2026: bản đầu không làm vai giáo viên.** Chỉ làm sản phẩm cho học viên tự luyện.
→ [M-11](../requirements/assumptions-and-open-questions.md)

Nghĩa là **không** so sánh với Edly ở mảng này. Các module giáo viên của họ nằm ngoài phạm vi
đối chiếu, không phải khoảng trống cần lấp.

---

## B-4 · Mô hình lượt thi — Edly làm thế nào

Trích nguyên văn từ trang chấm Writing của họ. Đơn vị của họ gọi là **"hạt sồi"**.

| Việc | Sồi |
|---|---|
| Làm một đề Writing | **−5** khi nộp bài |
| Hoàn thành bài đó | **+1** |
| Mở chấm chuyên sâu | **−5** mỗi lần |
| Đăng nhập mỗi ngày | **+3** |
| Hoàn thành 1 bài | **+1** |
| Chia sẻ cho bạn vào thi | **+3** |

**Gói trả phí (IELTS Pro):** 150.000đ / 14 ngày · 250.000đ / 30 ngày · 700.000đ / 90 ngày.
Gói Pro miễn cả hai khoản trừ ở trên.

### Điều đáng học nhất

> *"nếu không đủ sồi bạn vẫn được chấm ở **mức tiêu chuẩn**, chỉ là không có phần phân tích sâu
> theo từng tiêu chí"*

Hết lượt **không chặn cứng** — chỉ **hạ cấp trải nghiệm**. Đây chính là một trong ba phương án
mình nêu ở B-4 ("hard block, wait, hay degraded"), và đối thủ đã chọn *degraded*.

Với sản phẩm thi cử thì lựa chọn này hợp lý hơn hẳn chặn cứng: học viên vẫn nộp được bài và vẫn
có điểm, chỉ mất phần phân tích sâu. Không ai bị kẹt giữa chừng.

### Điều cần cẩn trọng

Edly thưởng **+3 sồi cho việc "chia sẻ cho bạn bè vào thi"**. Nếu họ cộng ngay khi người dùng bấm
nút chia sẻ thì phần thưởng đó **không kiểm chứng được** — đúng vấn đề [ADR-0009](../decisions/0009-share-gating-not-verifiable.md)
đã phân tích. Không nên sao chép cơ chế này mà chưa đọc kỹ họ chốt điều kiện ở đâu.

`[NEEDS VALIDATION]` Chưa xác minh được Edly cộng sồi ở thời điểm nào — lúc bấm chia sẻ, hay lúc
người được giới thiệu thật sự đăng ký.

---

## Ba module đáng cân nhắc thêm vào phạm vi

Xếp theo giá trị so với công sức:

1. **Luyện từng passage / từng task** — cùng nội dung đề, chỉ khác cách vào bài. Học viên muốn
   sửa đúng một dạng câu hỏi thì không phải ngồi hết 60 phút. Rẻ để làm, dùng nhiều.
2. **Kiểm tra đầu vào → lộ trình theo band** — biến sản phẩm từ "kho đề" thành "lộ trình".
   Là lý do người dùng quay lại.
3. **Nghe chép chính tả** — module luyện nghe độc lập, dùng lại được kho audio sẵn có.

Cả ba đều **không** cần AI provider nên **không bị chặn bởi B-1**, và cả ba đều thuộc vai học
viên nên **không vướng quyết định M-11**.

**Trạng thái sau prototype web (19/08/2026).** Demo đã dựng (1) luyện dạng câu và (3) dictation.
(2) lộ trình theo band **chưa có** — dashboard chỉ có chỗ trống "cần 3 bài full test". Việc giữ
(1) và (3) trong MVP là M-14, chưa chốt. → [`web-demo-feature-map.md`](web-demo-feature-map.md)
