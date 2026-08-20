# DESIGN.md — ngôn ngữ thiết kế web VNI IELTS AI

> Rút từ token đã duyệt (artboard `3x`, chốt Q1 ngày 19/08/2026) và `styles.css` của prototype
> tại `/Users/metacom/Documents/VNI/VNI IELTS AI Web design` — prototype **đã đóng băng 20/08/2026**.
> Prototype nằm **ngoài** repo này. File này là bản sao có thẩm quyền của hệ thiết kế,
> để lần sinh màn sau không trôi lệch như nhánh Inter + gradient tím.

## Trạng thái từng phần

File này **không đồng nhất một trạng thái**. Hướng thẩm mỹ đã được chủ sản phẩm chốt; các ràng buộc kỹ thuật có hiệu lực với mọi bản build nhưng giữ `PROPOSED` chờ xác nhận gộp tại requirement freeze — xem ghi chú dưới bảng.

| Phần | Status | Căn cứ |
|---|---|---|
| Bảng token màu + tỉ lệ tương phản đã đo | **PROPOSED** | Duyệt tại phiên làm việc 19/08/2026 (chốt Q1, artboard `3x`) — biên bản chưa ghi danh người duyệt, chưa đủ chuẩn nguồn cho `CONFIRMED` |
| Font Archivo + JetBrains Mono | **PROPOSED** | Ràng buộc kỹ thuật đã kiểm chứng — subset `vietnamese` trên Google Fonts CSS API. Không phải phát biểu của chủ sản phẩm |
| Thang cỡ chữ đóng + sàn 14px + `line-height` ≥ 1.5 | **PROPOSED** | Ràng buộc kỹ thuật cho tiếng Việt, đã tái phát ba lần khi để tự quyết. Không phải phát biểu của chủ sản phẩm |
| Bốn luật sản phẩm → quy tắc thiết kế | **PROPOSED** | Dẫn xuất từ [ADR-0007](../decisions/0007-server-authoritative-exam-timer.md) — Accepted — và các quyết định nghiệp vụ đã ghi. Quyết định gốc là `EXISTING`; phần diễn dịch thành quy tắc thiết kế là đặc tả |
| **Hướng thiết kế: C · Thẻ mềm** | **CONFIRMED** | Chủ sản phẩm chọn **20/08/2026**, sau khi khảo sát 4 design system. → [§ Hướng đã chốt](#hướng-đã-chốt--c--thẻ-mềm) |
| Thang spacing 4px | **PROPOSED** | Một phần của hướng C; nội dung đi kèm quyết định 20/08 nhưng chưa có phát biểu riêng của chủ sản phẩm cho thang này |

**T1 đã đóng 20/08/2026.** File này giờ khóa cả ràng buộc lẫn hướng.

> **Vì sao nhiều dòng là `PROPOSED` dù đã nghiệm thu qua T1:** quy tắc nguồn trong
> [`../README.md` § Sourcing rule](../README.md) yêu cầu `CONFIRMED` phải trích được phát biểu
> nguyên văn của chủ sản phẩm — các dòng trên không có. `PROPOSED` ở đây đo **độ truy vết của nguồn**,
> không đo độ bắt buộc: những ràng buộc này **vẫn có hiệu lực với mọi bản build**. Nâng lên `CONFIRMED`
> bằng một lần xác nhận gộp của chủ sản phẩm tại requirement freeze (dùng `/req`).

### File này áp cho đâu

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 20/08/2026: prototype đã đóng băng, không động vào nữa.** Mục đích của
nó chỉ là phác họa tính năng được trình bày như thế nào, và nó đã làm xong việc đó.

| | |
|---|---|
| **Áp cho** | Bản build thật — web, Android, iOS, CMS |
| **Không áp cho** | Prototype trong `VNI IELTS AI Web design/`. Không sửa `styles.css`, không gộp `font-size` inline, không bỏ shadow ở đó |

Chỗ nào prototype lệch file này thì đó là **yêu cầu cho bản build**, không phải bug cần vá. Các con số
đo được trên prototype (31 shadow · 17 giá trị spacing · 118 `font-size` inline) là **bằng chứng vì sao
cần thang đóng**, không phải danh sách việc.

Đây là **phần mềm thi cử**. Giao diện phải bình tĩnh. Lo lắng làm tụt điểm, nên giao diện gây
hoảng là lỗi, không phải lựa chọn phong cách.

Người dùng: học viên Việt Nam 16–25 tuổi, tự luyện. Vai: `learner` và `admin`. Vai giáo viên
ngoài phạm vi (M-11, 18/08/2026). UI tiếng Việt; nội dung đề tiếng Anh.

---

## Font

| Vai trò | Family | Subset `vietnamese` |
|---|---|---|
| UI | **Archivo** 400–800 | ✅ đã kiểm Google Fonts CSS API (latin + latin-ext + vietnamese) |
| Số (đồng hồ, band, đếm từ) | **JetBrains Mono** 400–700 + `tabular-nums` | ✅ đã kiểm |

Áp mọi chữ số qua class `.num`. Không có `tabular-nums` thì đồng hồ nhảy bề ngang từng giây.

**Đã loại:** `Outfit` (không có subset tiếng Việt). **Không dùng:** `Inter` — từng xuất hiện ở
prototype nhánh B, chưa được kiểm subset, đã bị Q1 bỏ.

Mọi font đề xuất sau này **phải kiểm subset `vietnamese` trước khi chọn**.

---

## Palette

Logo gốc: xanh `#2A6FB1` · cam `#F48634` · lá `#16AD54`.
Cam và lá **không đủ tương phản để làm chữ** (2.39 và 2.79 trên nền sáng; ngưỡng 4.5).
Chữ trên nền cam/lá phải **đen**. Xanh logo đạt sát nút (4.96) — chỉ mảng lớn, không chữ nhỏ.

Cách hợp lệ để dùng xanh VNI làm chữ: **giữ hue, hạ độ sáng**. `#2A6FB1` (hue 209.3°) →
`--acc #2867ac` (hue 211.4°), lệch 2.1°.

| Token | Hex | Vai trò | Cặp chữ/nền đã đo |
|---|---|---|---|
| `--ink` | `#17161a` | Tiêu đề, chữ chính | 16.7 trên `--page` · 18.2 chữ trắng trên ink |
| `--ink-2` | `#4a4950` | Thân bài | 8.6 trên `#fff` |
| `--muted` | `#6b6a71` | Meta, nhãn phụ | 5.36 trên `#fff` · 4.93 trên `--page` |
| `--page` | `#f6f5f3` | Nền trang | — |
| `--card` | `#ffffff` | Thẻ | — |
| `--sunk` | `#faf9f7` | Vùng lõm | — |
| `--line` | `#e6e4e0` | Viền | — |
| `--line-2` | `#efedea` | Viền nhẹ | — |
| `--acc` | `#2867ac` | Hành động chính, ô đã trả lời, focus | 5.5 trên `#fff` · 5.3 trên `--acc-soft` |
| `--acc-700` | `#1c4b7e` | Hover, chữ trên nền xanh nhạt | 8.1 trên `--acc-soft` |
| `--acc-soft` | `#eef4fb` | Nền nhấn nhẹ | — |
| `--acc-line` | `#cfe0f1` | Viền nhấn | — |
| `--warn` | `#9a4e07` | Cảnh báo **thông tin** (đồng hồ mức 2–3, chưa đủ từ) | 5.4 trên `--warn-soft` |
| `--warn-soft` | `#fdf1e3` | Nền cảnh báo thông tin | — |
| `--ok` | `#1e7a3c` | Thành công thật | 4.7 trên `--ok-soft` |
| `--ok-soft` | `#e4f4e9` | Nền thành công | — |
| `--bad` | `#b3261e` | **Chỉ** việc đã hỏng | 5.6 trên `--bad-soft` |
| `--bad-soft` | `#fce9e7` | Nền lỗi | — |

`--color-accent: var(--acc)` bắt buộc — thiếu thì focus ring của công cụ canvas ra đỏ `#ec3013`.

Mọi cặp chữ/nền **≥ 4.5**. Không đoán bằng mắt.

Bo góc đóng: `--r-lg 18px` · `--r-md 12px` · `--r-sm 8px` · `--r-pill 999px`.
Trần nội dung: cột đọc ≤ 720px căn giữa; **trần trang 1200px** (`--container`).
Chrome ngoài phiên thi: header 72px + logo VNI + footer.

> **Sửa 20/08/2026:** bản trước ghi *"trần trang 1400px"*. Không có căn cứ — `styles.css` không có
> `1400px` ở đâu, và cả bốn design system đã khảo sát đều dừng ở **1200px**. Đã bỏ.

**Khoảng cách:** thang 4px, xem [§ Thang spacing](#thang-spacing--proposed-phần-thiếu-quan-trọng-nhất).
**Độ sâu:** dùng lớp nền `--page` → `--card` → `--sunk`, **không dùng shadow** — xem cùng mục.

---

## Kiểu chữ tiếng Việt — ràng buộc cứng

Thang đóng, **cấm mọi giá trị khác**, đặc biệt cấm cỡ nửa pixel:

| px | Dùng cho |
|---|---|
| 13 | Chỉ chuỗi **ASCII** (mã, ID) |
| **14** | Sàn mọi chuỗi tiếng Việt |
| 16 | Thân bài, ô bảng, input |
| 18 · 20 · 24 | Tiêu đề nhỏ / vừa |
| 32 · 44 · 60 | Tiêu đề lớn; 60 chỉ banner |

- `line-height` **≥ 1.5** cho chữ < 32px; **1.2** cho chữ ≥ 32px (ngoại lệ duy nhất).
  Dấu chồng hai tầng (`ế ộ ằ`) dưới 1.5 thì đâm vào dòng trên.
- `letter-spacing` tiêu đề tối đa **−0.01em**. Siết hơn làm dấu đè chữ bên cạnh.
- **Cấm `text-transform: uppercase`** trên mọi phần tử chứa tiếng Việt. Nhãn nhỏ:
  `14px + weight 600 + letter-spacing .04em + màu muted`.

Ba lỗi này đã tái phát **ba lần** khi để công cụ tự quyết. Khóa trong CSS, đừng khóa trong prompt.

---

## Bốn luật sản phẩm → quy tắc thiết kế

### L1 · Đồng hồ không hoảng

Đồng hồ đếm ngược **không bao giờ** đỏ, nhấp nháy, rung, hay có animation.
Đỏ dành cho việc **đã hỏng**, không dành cho thời gian đang trôi.

Ba mức, phân biệt ≥ 3 kênh (cỡ + viền + nhãn chữ), sống sót phép thử ảnh xám:

| Mức | Khi nào | Hình |
|---|---|---|
| 1 | Bình thường | Cỡ 24, không viền, không nhãn |
| 2 | < 5 phút | Cỡ 32, viền 1px `--warn`, nhãn "còn dưới 5 phút" |
| 3 | < 1 phút | Cỡ 44, viền 2px `--warn`, nền `--warn-soft`, nhãn "còn dưới 1 phút" |

Câu **"Đồng hồ không dừng khi mất mạng"** công bố ở màn trước khi thi, không để thí sinh tự phát hiện.
Trong phiên thi, mất mạng phải nhắc **hai điều**: bài giữ trên máy **và** đồng hồ vẫn chạy.
Thẻ "bài đang làm dở" trên trang cá nhân **không** được ghi "đồng hồ tạm dừng".

Speaking Part 2: **hai đồng hồ cùng màn**, khác nhau ở vị trí (chrome vs trong thẻ), nền, và nhãn
("đồng hồ bài thi" vs "chuẩn bị 1 phút"). Không đặt cả hai lên chrome.

### L2 · "Đã lưu" không nói dối

Bốn chip, khác nhau ở **hình dạng biểu tượng và kiểu viền**, không chỉ màu:

| Chip | Khi nào | Tick? |
|---|---|---|
| Đã lưu | Máy chủ đã nhận (HTTP 200) | Có |
| Đang gửi | Request đang bay | Không |
| Chưa gửi được | Còn trong hàng đợi trên máy / mất mạng | Không, không chữ "Đã lưu" |
| Gửi thất bại | Hỏng hẳn, cần hành động | Không |

### L3 · Chưa có điểm thì `—`

Không `0.0`, không điểm ước lượng, **không skeleton ở ô điểm** (skeleton trông như số đang tới).
Chấm thất bại hiện là thất bại; kỹ năng chấm được vẫn hiện điểm.
Overall chỉ có khi đủ bốn kỹ năng — thiếu thì Overall = `—`.

Band chỉ nhận `0, 0.5, … 9`. Không có `6.25` trên UI; `6.25` chỉ được xuất hiện trong **dòng dẫn giải**
làm tròn ("Trung bình 6.25 → làm tròn lên 6.5").

### L4 · Điểm AI mang nhãn tham khảo

`Theo đáp án` = nền xám, viền đặc.
`AI · tham khảo` = viền **gạch đứt**, không trông như lỗi, không trông ngang hàng với đáp án.
Phân biệt được khi chuyển ảnh sang xám.

---

## Chrome trong / ngoài phiên thi

| | Ngoài phiên thi | Trong phiên thi |
|---|---|---|
| Header | 72px, logo, nav, tài khoản | Chỉ chip lưu · tên phần · đồng hồ |
| Footer | Có | Không |
| Link ra ngoài | Có | **Không** |

Listening: thanh audio **không nút tua, không thanh kéo** — chỉ tiến độ đọc. `<audio controls>` mặc
định là vi phạm nghiệp vụ.

Writing: đếm từ trực tiếp; dưới ngưỡng dùng `--warn` (thông tin), không `--bad`.

---

## Anti-pattern

1. Font chưa kiểm subset `vietnamese`.
2. Cỡ chữ ngoài thang đóng, đặc biệt 11px / nửa pixel.
3. `line-height` < 1.5 cho chữ tiếng Việt < 32px.
4. `text-transform: uppercase` trên chuỗi tiếng Việt.
5. Gradient tím, hoặc bất kỳ màu không có trong bảng token, làm màu chính.
6. Cam `#F48634` / lá `#16AD54` làm màu chữ.
7. Đồng hồ đỏ / nháy / rung.
8. Chip "Đã lưu" khi câu còn trên máy.
9. `0.0` hoặc skeleton ở ô điểm.
10. Điểm AI không nhãn "tham khảo".
11. Band không thuộc enum đóng (ví dụ `6.25` trên thẻ lịch sử).
12. Bịa số liệu: số đề, % chính xác AI, thời gian chấm, "miễn phí không giới hạn", testimonial, cam kết tăng band. Chưa chốt thì `—` + nhãn *[giả định]*.
13. Emoji hệ điều hành làm icon nav (🚩 ra đỏ trong phiên thi).
14. `display: none` bài đọc trên mobile — dùng tab "Bài đọc / Câu hỏi".
15. Cuộn ngang. Bảng hẹp lại thì xếp lại, không trượt ngang.
16. Thiết kế màn gói / nạp tiền trước khi B-3 và B-4 được chốt. *(Màn token thì được — `T-1` đã CONFIRMED — nhưng **không được hiện số token cụ thể** cho tới khi `B-5b` có câu trả lời.)*
17. Module ngoài tài liệu: Vocabulary, Grammar, vai giáo viên, thanh toán (MVP).
18. **Animation khi chuyển đáp án.** Hiện lại phần chữ đang lưu bằng hoạt ảnh làm mất tập trung giữa lúc đọc. Trạng thái lưu phải đổi tại chỗ, không diễn hoạt. *(Khớp L1; đề xuất từ bản nhận xét 20/08.)*
19. **Trích DESIGN.md như thể mọi phần đều là quyết định của chủ sản phẩm.** Chỉ hướng **C · Thẻ mềm** là `CONFIRMED`; các ràng buộc kỹ thuật là `PROPOSED` có hiệu lực — xem bảng trạng thái đầu file, đừng nâng cấp chúng khi trích dẫn.
20. **Khoảng cách ngoài thang 4px.** Bảy giá trị `6 10 11 14 18 22 28px` bị cấm — chúng chiếm 47 lần dùng trong `styles.css` và là nguồn gốc của việc mỗi màn tự chế một cỡ.
21. **Đổ bóng để tạo độ sâu.** Dùng lớp nền `--page` / `--card` / `--sunk`. Cả bốn design system đã khảo sát đều bỏ shadow; ta đang dùng 31 chỗ.
22. **Hai hệ thiết kế cho học viên và CMS.** Một ngôn ngữ, hai chế độ mật độ — `comfortable` và `compact`. `admin/` đã dùng chung `client/styles.css`, giữ nguyên như vậy.
23. **Linh vật, sticker, hoặc bất kỳ thứ gì mượn từ ed-tech kiểu Duolingo.** Đây là phần mềm thi cử; nó phải giống phòng thi thật, không giống trò chơi học từ vựng.

---

---

## Hướng thiết kế — nghiên cứu 20/08/2026

Chủ sản phẩm đã chọn hướng **C · Thẻ mềm** ngày 20/08/2026 — hướng đó là `CONFIRMED` và **T1 đã đóng**. Mục này giữ lại làm hồ sơ khảo sát: chọn reference theo tiêu chí nào, và vì sao loại từng ứng viên.

### Cách chọn reference: theo *bài toán*, không theo *ngành*

Sai lầm dễ mắc là tìm reference trong ngành giáo dục. Nhưng bài toán của sản phẩm này không phải "dạy
học cho vui" — mà là:

> Người dùng đang **lo lắng**, ngồi trước màn hình **60 phút liên tục**, phải **đọc chính xác**, và một
> thao tác nhầm làm hỏng bài thi có tính giờ.

Ngành gần nhất với bài toán đó là **ngân hàng** và **báo cáo dữ liệu**, không phải ed-tech.

### Bốn hệ đã đọc

Nguồn: [Refero](https://styles.refero.design/) — thư viện design system tìm kiếm được, **không có
category cố định**; duyệt qua Trending / Popular / Newest hoặc tìm theo tên (kiểm 20/08/2026).

| Hệ | Ngành | Nền | Đặc trưng | Hợp không |
|---|---|---|---|---|
| **Mercury** | Ngân hàng | Tối `#171721` | Một accent duy nhất `#5266eb`. Weight trung gian 480, **không bao giờ bold**. Không shadow — nổi khối bằng chênh lệch sáng | ✅ đúng *register*, sai *nền* (VNI là nền sáng) |
| **Ventriloc** | Báo cáo dữ liệu | Sáng ấm `#efefef` | **95% phi màu**, accent chỉ để gạch chân link và điểm nhấn biểu đồ. Ngăn cách bằng **lớp nền**, không shadow. Biểu đồ *chính là* hình ảnh | ✅ gần nhất |
| **Ditto** | Công cụ tuân thủ | Sáng `#f9fbf2` | Pill là chữ ký hình học. Không shadow — phân tầng bằng lớp nền ngả xanh | ✅ một phần |
| **Duolingo** | **Học ngôn ngữ** | Trắng | Feather 700 cỡ 48–64px, linh vật, component kiểu "sticker" viền 2px | ❌ xem dưới |

### Duolingo — reference "đúng ngành" nhưng sai sản phẩm

Đây là hệ mà ai cũng nghĩ tới đầu tiên. Đã đọc kỹ, và nó **vi phạm hai ràng buộc đã CONFIRMED** của chúng ta:

| Duolingo làm | Xung đột |
|---|---|
| Nhãn điều hướng **viết hoa** (15px/700/letter-spacing 0.053em) | `DESIGN.md` **cấm `text-transform: uppercase`** trên tiếng Việt — viết hoa làm mất dấu |
| Body 17px, **`line-height: 1.18`** | Ta yêu cầu **≥ 1.5** cho tiếng Việt. Dấu chồng hai tầng (`ế ộ ằ`) ở 1.18 sẽ đâm vào dòng trên |

Cộng thêm chuyện register: linh vật và "sticker" hợp với học vui 5 phút mỗi ngày, **không hợp một kỳ
thi mô phỏng có tính giờ**. Sản phẩm này càng giống phòng thi thật càng tốt.

> Giữ lại đúng **một** điều từ Duolingo: *"không bao giờ hai nút vàng trong một khung nhìn"* — kỷ luật
> một hành động chính mỗi màn. Áp cho `--acc` của ta.

### Bốn hệ đồng ý với nhau ở năm điểm

Đây là tín hiệu mạnh nhất của cả đợt nghiên cứu — bốn hệ khác ngành, khác nền, khác gu, mà trùng nhau:

| # | Điểm chung | VNI hiện tại |
|---|---|---|
| 1 | **Trần rộng 1200px** — cả bốn | ✅ `--container: 1200px` đã khớp |
| 2 | **Không dùng shadow.** Nổi khối bằng **chênh lệch nền**, không bằng đổ bóng | ❌ **31 chỗ dùng shadow**, 19 qua `var(--sh-*)` |
| 3 | **Đơn vị gốc 4px** (Mercury · Ventriloc · Duolingo), Ditto 8px | ❌ **không có thang nào** |
| 4 | **Một accent duy nhất**, dùng rất tiết kiệm | ✅ `--acc` đã đúng vai |
| 5 | **Kiềm chế weight** — Mercury cao nhất 480, Ventriloc chỉ 400 | ⚠️ chưa quy định |

### Ba phát hiện về hệ hiện tại

**1 · Không có thang spacing — đây là lỗ hổng lớn nhất.**
`styles.css` khai báo radius, shadow, container, nhưng **không có một token khoảng cách nào**. Hệ quả
đo được: **17 giá trị rời rạc** đang dùng — `1 2 4 6 8 10 11 12 14 16 18 20 22 24 28 32 40px`.

Đây cũng là **nguyên nhân gốc của 118 khai báo `font-size` inline** mà `web-demo-feature-map.md` ghi
nhận: khi không có thang, mỗi màn tự chế một giá trị.

**2 · 31 chỗ dùng shadow, trong khi cả bốn reference đều bỏ shadow.**
Và nó xung đột với chính luật L1 của ta — *giao diện phải bình tĩnh*. Đổ bóng là hiệu ứng độ sâu; bốn
hệ này đều thay bằng **lớp nền**. Ta đã có sẵn ba lớp `--page` / `--card` / `--sunk` mà chưa khai thác.

**3 · `DESIGN.md` ghi "trần trang 1400px" nhưng không có căn cứ.**
`styles.css` không có `1400px` ở đâu, và cả bốn reference đều dừng ở **1200px**. Con số 1400 nên bỏ.

---

## Thang spacing — đơn vị gốc 4px

Đơn vị gốc **4px**, theo Mercury · Ventriloc · Duolingo. Thang đóng, **cấm giá trị ngoài thang**:

```css
--s-1:   4px;   /* khe giữa icon và chữ */
--s-2:   8px;   /* trong một control */
--s-3:  12px;   /* giữa các phần tử liên quan */
--s-4:  16px;   /* padding mặc định */
--s-5:  24px;   /* giữa các nhóm */
--s-6:  32px;   /* padding thẻ */
--s-7:  48px;   /* giữa các khối */
--s-8:  72px;   /* nhịp giữa các section */
```

Bỏ hẳn `6 10 11 14 18 22 28px` — bảy giá trị này không thuộc thang nào và chiếm **47 lần dùng**.

**Nhịp dọc giữa section:** Mercury 72px · Ventriloc 80px · Ditto 48–80px · Duolingo 80–120px.
Đề xuất **72px** ngoài phiên thi, **48px** trong phiên thi (màn thi cần gọn hơn, không cần thoáng như
trang giới thiệu).

---

## Ba hướng để chọn

Cả ba **giữ nguyên** palette, font, thang cỡ chữ và bốn luật sản phẩm — những thứ đã CONFIRMED và đã đo
tương phản. Chúng khác nhau ở **độ sâu, hình học bo góc, và mật độ**.

### A · Giấy tĩnh — *nghiêng Ventriloc*

Phẳng hoàn toàn. Độ sâu **chỉ** đến từ ba lớp nền `--page` → `--card` → `--sunk`. Bo góc khiêm tốn
(8/12px), nút bo 8px chứ không pill. Nhịp rất thoáng. Cột đọc là trung tâm.

| Hợp nhất với | Rủi ro |
|---|---|
| Reading, Writing, trang kết quả, tài liệu, bài viết | Màn dày dữ liệu (CMS, bảng 40 câu) có thể trông hơi phẳng, khó phân tầng |

### B · Khí cụ — *nghiêng Mercury, chuyển sang nền sáng*

Phẳng, viền mảnh `--line` làm ranh giới chính. Số liệu dùng `JetBrains Mono` nổi bật hơn. Bo góc nhỏ
(4/8px) cho cảm giác chính xác. Nút pill cho hành động chính, tách bạch rõ khỏi mọi thứ khác. Mật độ
chặt hơn A.

| Hợp nhất với | Rủi ro |
|---|---|
| CMS, bảng điều hướng câu, dashboard, màn điểm số | Có thể hơi lạnh và kỹ thuật với học viên 16–25 tuổi |

### C · Thẻ mềm — ✅ **ĐÃ CHỌN 20/08/2026** · `CONFIRMED`

**Quyết định của chủ sản phẩm.** T1 đóng. Đặc tả đầy đủ ở [§ Hướng đã chốt](#hướng-đã-chốt--c--thẻ-mềm) bên dưới.

Giữ bo góc lớn (12/18px) như prototype đang có, **bỏ toàn bộ shadow**, thay bằng lớp nền + viền
`--line-2`. Mật độ trung bình. Thân thiện hơn A và B.

| Được | Đánh đổi |
|---|---|
| Giữ được công sức đã bỏ vào 35 màn; ít phải làm lại nhất | Gần "dashboard SaaS chung chung" nhất — bản sắc phải đến từ **kỷ luật**, không từ hình dáng lạ |

> **Đánh đổi đó phải được trả bằng kỷ luật, không bằng trang trí.** Vì hình học không tạo bản sắc, thứ
> phân biệt sản phẩm này với một dashboard bất kỳ là bốn luật sản phẩm và sự nhất quán tuyệt đối của
> thang spacing. Đừng bù bằng gradient, hoa văn, hay màu thứ tư.

### Kèm theo bất kỳ hướng nào: **hai chế độ mật độ, một ngôn ngữ**

Không làm hai hệ thiết kế. Làm **một hệ, hai mật độ**:

| Chế độ | Dùng ở | Padding thẻ | Nhịp section |
|---|---|---|---|
| `comfortable` | Học viên — trang chủ, kết quả, tài liệu | `--s-6` 32px | `--s-8` 72px |
| `compact` | CMS, bảng câu hỏi, dashboard | `--s-4` 16px | `--s-7` 48px |

Đây là lý do `admin/` dùng chung được `../client/styles.css` — và nên tiếp tục như vậy.

---

## Hướng đã chốt — C · Thẻ mềm

`CONFIRMED` 20/08/2026. Đây là phần đóng DoD của T1.

### Độ sâu — quy tắc thay đổi lớn nhất

**Bỏ toàn bộ 5 token shadow.** `--sh-sm` `--sh-md` `--sh-lg` `--sh-xl` `--sh-acc` không còn được dùng.
31 chỗ đang dùng trong `styles.css` phải chuyển sang ba lớp nền:

| Lớp | Token | Dùng cho | Ranh giới |
|---|---|---|---|
| Nền trang | `--page` `#f6f5f3` | Nền ngoài cùng | — |
| Bề mặt | `--card` `#ffffff` | Thẻ, panel, modal | viền 1px `--line-2` |
| Vùng lõm | `--sunk` `#faf9f7` | Ô nhập, vùng code, khối trích dẫn | viền 1px `--line` |

Nổi khối bằng **chênh lệch nền + viền mảnh**, không bằng đổ bóng. Cả bốn design system đã khảo sát đều
làm vậy, và nó khớp luật L1 — giao diện phải bình tĩnh.

**Ngoại lệ duy nhất:** lớp phủ modal được dùng `rgba(23,22,26,.32)` làm nền mờ. Đó là *lớp phủ*, không
phải *đổ bóng*.

### Hình học

| Token | Giá trị | Dùng cho |
|---|---|---|
| `--r-lg` | 18px | Thẻ, panel, modal |
| `--r-md` | 12px | Nút, ô nhập, select |
| `--r-sm` | 8px | Chip, ô câu hỏi trong bảng điều hướng, tag nhỏ |
| `--r-pill` | 999px | **Chỉ** chip trạng thái và nhãn. Không dùng cho nút |

### Khoảng cách

Thang 4px ở [§ Thang spacing](#thang-spacing--proposed-phần-thiếu-quan-trọng-nhất). Bảy giá trị ngoài
thang bị cấm.

### Hai chế độ mật độ, một ngôn ngữ

| | `comfortable` | `compact` |
|---|---|---|
| Dùng ở | Học viên: trang chủ, kết quả, tài liệu, bài viết | CMS, bảng điều hướng câu, dashboard |
| Padding thẻ | `--s-6` 32px | `--s-4` 16px |
| Nhịp section | `--s-8` 72px | `--s-7` 48px |
| Khe giữa phần tử | `--s-3` 12px | `--s-2` 8px |

Trong phiên thi dùng `comfortable` nhưng nhịp section rút về `--s-7` 48px — màn thi cần gọn hơn trang
giới thiệu, nhưng không được chật như CMS.

`admin/` tiếp tục dùng chung `client/styles.css`, chỉ đổi chế độ mật độ.

### Kiềm chế weight

Archivo có 400–800. Giới hạn thực dùng:

| Weight | Dùng cho |
|---|---|
| 400 | Thân bài |
| 500 | Nhãn, meta |
| 600 | Nhấn mạnh trong câu, tiêu đề nhỏ, nhãn nút |
| 700 | **Chỉ** tiêu đề ≥ 32px |
| 800 | Không dùng |

Mercury dừng ở 480 và Ventriloc chỉ dùng 400 — cả hai tạo thứ bậc bằng **cỡ chữ và khoảng trắng**,
không bằng độ đậm. Ta không đi xa được đến vậy vì tiếng Việt cần weight cao hơn để dấu rõ, nhưng 800
thì thừa.

### Một hành động chính mỗi khung nhìn

Mượn từ Duolingo, thứ duy nhất đáng mượn từ đó: **không bao giờ hai nút `--acc` tô đặc trong cùng một
khung nhìn.** Hành động phụ dùng nút viền hoặc link. Trong phiên thi, hành động chính luôn là nút nộp
bài — không được có nút tô đặc nào cạnh tranh với nó.

### Bản sắc đến từ đâu

Hướng C dùng hình học phổ thông, nên bản sắc **không** đến từ hình dáng. Nó đến từ bốn thứ, tất cả đã
CONFIRMED:

1. **Đồng hồ không bao giờ hoảng** — không đỏ, không nháy, không rung. Rất ít sản phẩm thi cử làm được điều này.
2. **Trạng thái lưu không nói dối** — bốn chip phân biệt bằng hình dạng, không chỉ bằng màu.
3. **Chưa có điểm thì `—`, không bao giờ `0.0`** — và không có skeleton ở ô điểm.
4. **Điểm AI mang nhãn tham khảo**, viền gạch đứt, phân biệt được cả khi chuyển ảnh sang xám.

Cộng với **sự nhất quán tuyệt đối của thang spacing**. Đó là thứ người dùng cảm nhận được mà không gọi
tên được — và là thứ 17 giá trị khoảng cách rời rạc đang phá hỏng.

---

## Ràng buộc với mọi nguồn tham khảo

**Không lấy palette từ bất kỳ đâu.** Palette hiện tại dẫn xuất từ logo VNI Education và **đã khóa
contrast ≥ 4.5 cho mọi cặp chữ/nền**. Mượn một palette đẹp về là mất cả nhận diện lẫn khả năng đọc.

Mọi màu hoặc font mượn từ bất kỳ nguồn nào phải qua **hai cửa**, không bỏ cửa nào:

1. **Đo tương phản** — mọi cặp chữ/nền ≥ 4.5. Không đoán bằng mắt.
2. **Kiểm subset `vietnamese`** — trên Google Fonts CSS API hoặc bảng glyph. `Outfit` từng trượt đúng
   bài kiểm này và làm rơi dấu giữa chừng một từ.

> **Claude không tự chọn hướng.** Ba hướng được trình dưới dạng `PROPOSED` tại thời điểm khảo sát;
> chủ sản phẩm chọn **C · Thẻ mềm** ngày 20/08/2026 — điều kiện đóng T1 đã hoàn thành. Giữ nguyên
> quy trình này cho mọi quyết định thẩm mỹ sau: Claude trình ứng viên có bằng chứng, chủ sản phẩm chọn.

---

## Xung đột với bản nhận xét UI/UX 20/08/2026

`Nhan_xet_va_de_xuat_UI_UX_luyen_thi.docx` là **bản nhận xét bên thứ ba**, nguồn bậc 6, chuyển tiếp
kèm chữ "kiểm tra thêm". Mọi mục trong đó `UNCONFIRMED` cho tới khi chủ sản phẩm phán quyết (`B-8`).
Bảng đầy đủ 22 mục: [`../product/web-demo-feature-map.md`](../product/web-demo-feature-map.md).

Ba mục chạm trực tiếp vào file này:

### 1 · Font Calibri 12 — xung đột với ràng buộc đã CONFIRMED

| | |
|---|---|
| Đề xuất | Calibri cỡ 12, tiêu đề 16 căn giữa, giãn dòng 1.5 |
| Xung đột | Archivo + thang cỡ chữ đóng + **sàn 14px cho tiếng Việt** đều đã CONFIRMED |
| Vấn đề thứ hai | **Calibri chưa được kiểm subset `vietnamese`** — đúng bài kiểm mà `Outfit` đã trượt |
| Vấn đề thứ ba | Cỡ 12 dưới sàn 14px. Sàn này sinh ra vì dấu chồng hai tầng (`ế ộ ằ`) không đọc được ở cỡ nhỏ trên điện thoại |

**Không tự chọn bên.** Nếu chủ sản phẩm muốn đổi font, phải kiểm subset trước, rồi dựng lại toàn bộ
thang cỡ chữ — không phải đổi một dòng CSS. → `B-8`

### 2 · Green / Yellow / White cho trạng thái câu

Đề xuất: `Green = Done` · `Yellow = Mark/Review` · `White = Not done` — thay cho trạng thái "đánh dấu
xem lại" hiện tại (chữ đỏ trên nền xanh, đúng là khó nhìn).

**Ý tưởng hợp lý, nhưng chưa dùng được ngay:** bảng token **không có màu vàng nào đã đo tương phản**.
Trước khi áp phải map sang token có sẵn và đo lại:

| Trạng thái | Token đề xuất | Việc phải làm |
|---|---|---|
| Done | `--ok` / `--ok-soft` | Đo cặp chữ/nền |
| Mark / Review | `--warn` / `--warn-soft` | Đo cặp chữ/nền — đây là *thông tin*, không phải *lỗi*, nên đúng là `--warn` chứ không phải `--bad` |
| Not done | `--card` + `--line` | Đo cặp chữ/nền |

Và vẫn phải qua **phép thử ảnh xám**: chuyển sang xám mà không phân biệt được ba trạng thái thì thiết
kế đang phụ thuộc màu quá mức — thêm khác biệt về viền hoặc hình dạng.

### 3 · "Giảm animation khi chuyển đáp án"

Khớp thẳng với luật L1 (*giao diện phải bình tĩnh*). Ghi thành anti-pattern #18 bên dưới. `PROPOSED`.

### Một mục không phải đề xuất mà là luật đã có

`.docx` đề xuất Listening "pause được nhưng không tua". **Phần "không tua" đã là luật trong file này và
đã CONFIRMED từ trước** — nên không cần phán quyết lại.

`exam-listening.html` dùng `<audio controls>`, tức lệch luật. Nhưng prototype **đã đóng băng
20/08/2026** nên không sửa. Điều cần nhớ cho bản build thật: **`<audio controls>` là vi phạm nghiệp
vụ**, vì mặc định của trình duyệt cho tua lại và phá mô phỏng điều kiện thi.

Phần "pause được, và timer vẫn chạy khi pause" thì `UNCONFIRMED` → `B-8`.

---

## Nguồn

- Token: artboard `3x` trong `VNI IELTS AI v3 - luong thi.dc.html`
- CSS sống: `styles.css` (Token Version: Q1 Approved 19/08/2026)
- Màu logo: [`assets/brand/README.md`](../../assets/brand/README.md)
- Đối chiếu prototype: [`../product/web-demo-feature-map.md`](../product/web-demo-feature-map.md)
