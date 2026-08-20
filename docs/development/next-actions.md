# Next Actions — hàng đợi công việc

> Tài liệu này viết bằng tiếng Việt vì đây là **checklist vận hành cho người thực thi**, không phải tài liệu kiến trúc. Toàn bộ `docs/` còn lại là tiếng Anh.

**Cách dùng:** làm **đúng một task** mỗi lần. Xong → đối chiếu Definition of Done → báo cáo → **dừng lại**. Không tự động nhảy sang task kế tiếp.

Session mới chỉ cần đọc file này. Mọi thứ cần biết đều nằm ở đây hoặc trong link.

---

## Trạng thái

| | |
|---|---|
| Phase hiện tại | **Phase 1 — UI/UX** |
| Task đang mở | **T2 · Danh sách màn + luồng nghiệp vụ** — chủ sản phẩm chỉ định 20/08/2026 |
| Task đã xong | **T0 · Chuẩn hóa tài liệu + rà soát stack** (20/08) · **T1 · `DESIGN.md`** (20/08 — hướng **C · Thẻ mềm** đã chốt) |
| **Rủi ro cho T2** | **`B-8`** — 22 đề xuất UI/UX chưa phán quyết, trong đó **8 cái đổi cấu trúc màn**. Xem cảnh báo trong T2 |
| Chủ sản phẩm chỉ đạo làm trước | **Đặc tả CMS**: [`../ux/cms-spec.md`](../ux/cms-spec.md) (29 màn / 9 nhóm, trong đó 14 màn độc lập) |
| Prototype học viên | **21 màn** trong `client/`. Thiếu hẳn: màn 11 · Lộ trình. Màn 13 · Trước khi bắt đầu được nhúng vào từng màn thi thay vì tách file — chấp nhận được |
| Prototype CMS | **14 màn** trong `admin/` — **đã dựng 19/08/2026**, dùng chung `../client/styles.css` |

---

## T0 · Chuẩn hóa tài liệu + rà soát stack — ✅ xong 20/08/2026

Chủ sản phẩm phát biểu lại toàn bộ nghiệp vụ ngày 20/08 và chuyển kèm một bản nhận xét UI/UX của bên
thứ ba. Việc này rà toàn bộ tài liệu để khớp lại, **không tạo file mới**.

**Vấn đề gốc không phải thiếu tài liệu, mà là tài liệu mâu thuẫn với nhau và với thực tế.**

### Kết quả

| | |
|---|---|
| File sửa | **30** |
| File xoá | **4** (`docs/ux/*prompt*`, `v2-web-design-audit.md` — trỏ tới canvas project đã chết) |
| File mới | **0** |
| Tổng `.md` | 69 → **65** toàn repo · 51 → **47** trong `docs/` |

### Ba thứ đáng nhớ nhất

**1 · `docs/README.md` giờ là nơi duy nhất định nghĩa cách đánh trạng thái.** Bốn giá trị
`CONFIRMED` / `EXISTING` / `PROPOSED` / `UNCONFIRMED`, thang source precedence 8 bậc, quy tắc nguồn cho
`CONFIRMED`, và luật *"đã đặc tả ≠ đã triển khai"*. `CLAUDE.md` chỉ trỏ tới, không chép — hai bản sẽ trôi lệch.

`EXISTING` được thu hẹp có chủ ý: chỉ dùng cho (a) hành vi kiểm được trong prototype, (b) một ADR ở
trạng thái Accepted. **Văn xuôi trong tài liệu kiến trúc không phải `EXISTING`** — repo không có source
code, nên nó là đặc tả chưa xây → `PROPOSED`.

**2 · Ba mục từng bị đánh nhầm `CONFIRMED`.** `A-12a` (danh sách cấm AI feedback), `I-15b` (field AI
Parse trích xuất), `I-16` (Admin Review trước Publish) — cả ba có nguyên văn trong tài liệu spec chủ sản
phẩm gửi, nhưng chủ sản phẩm xác định đó là **phần do quá trình phân tích soạn vào**, không phải quyết
định của mình. Cả ba xuống `PROPOSED`.

> Bài học đã ghi thành quy tắc: một tài liệu spec do chủ sản phẩm gửi **không đồng nghĩa** mọi dòng
> trong đó là owner-confirmed. Mọi `CONFIRMED` phải trích được nguồn nguyên văn.

**3 · Ba phát hiện kỹ thuật**, không phải suy đoán:

| Phát hiện | Hệ quả |
|---|---|
| `nfr.md` ghi *"single MongoDB instance"*, nhưng transaction cần **replica set** | Trừ token + tạo phiên thi cần nguyên tử. Không có thì retry trên mạng di động trừ hai lần → `H-10`, threat `T22` |
| Prototype là **HTML/CSS/vanilla JS thuần** — chưa có dòng React nào | ADR-0002 chọn React nhưng chưa xây. Tái sử dụng chỉ ở mức **CSS token + markup**, không phải component |
| `.NET 10` EOL là **2028-11-14**, không phải 2028-11-10 | Sai ở 5 file. Đã xác minh nguồn Microsoft và sửa đồng bộ |

### Nghiệp vụ đã chốt

`E-11`…`E-14` Full Test (R→L→W→S) vs Single Skill · `A-11` R/L chấm answer key, AI explanation **không
sửa band** · `A-13a` Writing AI chấm · `M-22`…`M-25` Dictation · Documents · Articles · AI Chat ·
`T-1`…`T-5` Token · `I-14`/`I-15a` import + AI parsing.

**14 dòng `CONFIRMED`, tất cả có cột Source trích nguyên văn.** → [`../requirements/confirmed.md`](../requirements/confirmed.md)

### Đã hạ cấp

`A-4` Speaking AI pipeline → `[SUPERSEDED]`. `A-3` 4 tiêu chí Writing → `[CẦN XÁC NHẬN LẠI]`.
Brief mới liệt kê AI scoring cho R/L/W và ghi rõ *"Speaking: … ghi rõ UNCONFIRMED"*.

### Kiểm chứng đã chạy

8/8 pass — đếm file 69→65 · 0 link tới file đã xoá · **415 link tương đối, 0 gãy** · 0 mâu thuẫn phase ·
0 qualifier trong status · `.NET` EOL đồng bộ 5/5 · 0 dòng `CONFIRMED` thiếu Source · taxonomy chỉ ở
một nơi.

### Còn nợ

**Brief §19 — nghiên cứu design reference (Refero) mới xong ~30%.** Đã viết **ràng buộc** vào
[`../ux/DESIGN.md`](../ux/DESIGN.md) (dùng cho bố cục, không lấy palette, hai cửa kiểm contrast +
subset `vietnamese`). **Chưa** duyệt design system nào, **chưa** có ứng viên hướng thiết kế.

Đây là việc cần làm để mở khoá T1 — nhưng phải ở dạng **2–3 ứng viên có bằng chứng cho chủ sản phẩm
chọn**, không phải Claude chọn hộ. DoD của T1 là *"chủ sản phẩm ưng gu"*.

---

### Prototype — **đã đóng băng 20/08/2026**

`/Users/metacom/Documents/VNI/VNI IELTS AI Web design` có `client/` (21 màn học viên) và
`admin/` (14 màn CMS). Cả hai **đã chạy được**.

> **`[QUYẾT ĐỊNH]` Chủ sản phẩm, 20/08/2026: không động vào prototype nữa.**
>
> Mục đích của nó **chỉ là phác họa tính năng được trình bày như thế nào** — nó đã làm xong việc đó.
>
> **Hệ quả:**
> - Không sửa `styles.css`, không gộp `font-size` inline, không bỏ shadow trên prototype
> - `DESIGN.md` áp cho **bản build thật**, không dùng để retrofit prototype
> - Chỗ nào prototype lệch `DESIGN.md` thì đó là **yêu cầu cho bản build**, không phải bug cần vá
> - Prototype vẫn giữ nguyên giá trị làm **bằng chứng `EXISTING`**: tính năng nào đã được nghĩ tới, và trình bày ra sao

**Công nghệ thực tế — đã kiểm bằng grep 20/08:** HTML + CSS + JavaScript thuần. Không framework,
không build step, không `package.json`, không `tsconfig`. File `support.js` là **runtime canvas của
Claude Design**, không phải mã ứng dụng.

> **Hệ quả cho mọi ước lượng sau này:** [ADR-0002](../decisions/0002-client-capacitor-react.md) chọn
> React, nhưng **chưa có một dòng React nào tồn tại**. Cộng với việc prototype đã đóng băng, phần
> "tái sử dụng" thực chất là **ý tưởng trình bày**, không phải mã. Bản build web bắt đầu từ con số
> không về code, nhưng không bắt đầu từ con số không về thiết kế.

Đối chiếu **tính năng** (không phải giao diện): [`../product/web-demo-feature-map.md`](../product/web-demo-feature-map.md).

### Đã xoá `docs/ux/` — hai lần

**18/08/2026 — 191 file.** Lần làm Phase 1 thứ nhất bị bỏ hẳn vì hướng thiết kế không đạt. Xoá bộ
`DESIGN.md` cũ, danh sách màn, sơ đồ luồng, 22 brief, và cả ba prototype. **Không khôi phục được.**

**20/08/2026 — 4 file.** Bốn prompt Claude Design (`design-prompts.md`, `design-prompts-v2.md`,
`prompt-hoan-thien-man-con-lai.md`, `v2-web-design-audit.md`) trỏ tới canvas project đã ngừng dùng.

> **Repo vẫn không nằm dưới version control.** Đây là lý do cả hai lần xoá đều vĩnh viễn.
> → rủi ro `R13` trong [`../requirements/risks-and-dependencies.md`](../requirements/risks-and-dependencies.md).

---

## Những gì vẫn còn giá trị

Phần đáng giữ nhất của lần làm trước: **quyết định và câu hỏi thì sống sót, chỉ thiết kế là bỏ đi**.
Đừng phát hiện lại chúng lần nữa.

### Ràng buộc kỹ thuật đã kiểm chứng

| Phát hiện | Vì sao quan trọng |
|---|---|
| **Font `Outfit` không có subset tiếng Việt** | Mọi dấu rơi sang font khác giữa chừng một từ. Đã kiểm trên Google Fonts API. Bất kỳ font nào được đề xuất **đều phải kiểm subset `vietnamese` trước khi chọn** |
| **`Be Vietnam Pro`, `JetBrains Mono`, `Geist` có subset tiếng Việt** | Ba lựa chọn đã kiểm chứng |
| **Màu logo VNI không dùng làm chữ được** | Cam `#F48634` đạt 2.39, lá `#16AD54` đạt 2.79 trên nền sáng — ngưỡng cần là 4.5. Chữ trắng trên cam chỉ 2.53. Nếu tô nền bằng hai màu này thì **chữ trên đó phải đen** |
| **Mã màu logo chính xác** | Xanh `#2A6FB1` · Cam `#F48634` · Lá `#16AD54` — đã đối chiếu với file logo gốc, khớp trong sai số làm tròn. File và chi tiết: [`assets/brand/`](../../assets/brand/README.md) |

### Luật sản phẩm — không phải sở thích thẩm mỹ

Bốn luật dưới đây sinh ra từ ràng buộc nghiệp vụ, **không phụ thuộc phong cách thiết kế**, nên
`DESIGN.md` mới vẫn phải giữ:

1. **Đồng hồ không bao giờ chuyển đỏ, nhấp nháy hay rung.** Đỏ dành cho *việc đã hỏng*, không dành
   cho *thời gian đang trôi*. Và câu "đồng hồ không dừng khi mất mạng" **phải công bố ở màn briefing
   trước khi thi**, không phải để thí sinh tự phát hiện. → [ADR-0007](../decisions/0007-server-authoritative-exam-timer.md)
2. **Trạng thái "đã lưu" không được nói dối.** Câu trả lời còn nằm trong hàng đợi trên máy thì không
   được hiện dấu tick hay chữ "Đã lưu" — thí sinh sẽ không kiểm lại, và đó là mất dữ liệu do thiết
   kế gây ra.
3. **Chưa có điểm phải hiện gạch ngang, không bao giờ `0.0`.** Chấm thất bại thì hiện là thất bại,
   không hiện điểm giả, điểm ước lượng hay placeholder trông như thật.
4. **Điểm do AI luôn mang nhãn tham khảo**, không được trông ngang hàng với điểm chấm theo đáp án.

### Bài học về quy trình

- **Ngưỡng cỡ chữ tiếng Việt phải là ràng buộc cứng, khai báo một lần.** Khi để công cụ sinh tự
  quyết, cùng ba lỗi quay lại ở gần như mọi lần sinh: viết hoa tiếng Việt (làm mất dấu), chữ 11px,
  và line-height dưới 1.5.
- **Kiểm tra số học là bắt buộc với mọi màn có điểm.** Một màn kết quả từng hiện bốn band cộng ra
  6.625 ngay cạnh dòng ghi "trung bình 6.25". Mắt bỏ qua, phép cộng thì không.
- **Phép thử ảnh xám là kiểm tra rẻ nhất mà giá trị cao nhất.** Chuyển ảnh sang xám: nếu không còn
  phân biệt được các trạng thái thì thiết kế phụ thuộc màu quá mức.

### Câu hỏi mở sinh ra từ lần làm trước

**M-6 M-7 M-8 M-9 M-10 V-6 V-7** trong
[assumptions-and-open-questions.md](../requirements/assumptions-and-open-questions.md) đều phát
sinh khi viết brief màn hình. Brief đã xoá, **câu hỏi thì còn**.

---

## T1 · `DESIGN.md` — ✅ xong 20/08/2026

**Definition of Done**
- [x] `docs/ux/DESIGN.md` tồn tại và **chủ sản phẩm ưng gu** — chọn hướng **C · Thẻ mềm** ngày 20/08/2026
- [x] Font đã kiểm chứng có subset `vietnamese` — Archivo + JetBrains Mono
- [x] Palette kèm mã hex, vai trò từng màu, và **tỉ lệ tương phản đã tính** cho mọi cặp chữ/nền
- [x] Ngưỡng cỡ chữ và line-height cho tiếng Việt, ghi thành ràng buộc cứng
- [x] Bốn luật sản phẩm được thể hiện thành quy tắc thiết kế cụ thể
- [x] Danh sách anti-pattern — **23 mục**

**Cách chốt:** khảo sát 4 design system trên [Refero](https://styles.refero.design/) — Mercury (ngân
hàng) · Ventriloc (báo cáo dữ liệu) · Ditto (tuân thủ) · Duolingo (học ngôn ngữ). Chọn reference theo
**bài toán**, không theo **ngành**: người dùng lo lắng, ngồi 60 phút, phải đọc chính xác — gần ngân
hàng hơn ed-tech.

**Duolingo bị loại dù đúng ngành:** nhãn điều hướng viết hoa (mất dấu tiếng Việt) và `line-height 1.18`
(dưới sàn 1.5 của ta). Reference đúng ngành không đồng nghĩa reference đúng sản phẩm.

**Ba thứ mới được thêm vào `DESIGN.md`:**

| | |
|---|---|
| **Thang spacing 4px** | Phần thiếu lớn nhất. Trước đó `styles.css` **không có token khoảng cách nào** — 17 giá trị rời rạc đang dùng, và đó là nguyên nhân gốc của 118 `font-size` inline |
| **Bỏ toàn bộ shadow** | Cả 4 reference đều bỏ. Ta đang dùng **31 chỗ**. Thay bằng ba lớp nền `--page`/`--card`/`--sunk` + viền mảnh |
| **Hai chế độ mật độ** | `comfortable` (học viên) và `compact` (CMS) — một ngôn ngữ, hai mật độ. Đây là lý do `admin/` dùng chung được `client/styles.css` |

**`DESIGN.md` áp cho bản build thật, không áp ngược lại prototype.** Prototype đã đóng băng
(xem [§ Prototype](#prototype--đã-đóng-băng-20082026)) — 31 shadow, 17 giá trị spacing và 118
`font-size` inline trong `styles.css` **không phải nợ kỹ thuật cần trả**, vì không ai sẽ chạy tiếp
trên mã đó. Chúng là số đo cho biết *vì sao* cần thang spacing, không phải danh sách việc.

---

## T2 · Dựng lại danh sách màn và luồng — 🔄 đang mở

Bản cũ đã xoá 18/08. Cần dựng lại `screen-inventory.md` và `user-flows.md`.

> ### ⚠️ Đọc trước khi bắt đầu: `B-8` chưa được phán quyết
>
> Bản nhận xét UI/UX 20/08 có 22 đề xuất, **8 cái đổi trực tiếp cấu trúc màn**:
>
> | Màn | Đề xuất chưa phán quyết |
> |---|---|
> | Reading | highlight đoạn văn · ghi chú từ vựng · bỏ "Nộp bài sớm" |
> | Listening | câu hỏi bên trái · **hiện toàn bộ câu hỏi trước khi nghe** |
> | Writing | nút **Lập dàn ý** · đề thu về 1/3 màn |
> | Speaking | **2 câu warm-up** · Part 1 ≥ 6 câu/2 chủ đề · **Take Note** · bật/tắt hiển thị câu hỏi |
> | Kết quả | **chia section, click mới mở chi tiết** |
>
> Dựng danh sách màn trước khi chốt những cái này thì phần Reading/Listening/Writing/Speaking/Kết quả
> sẽ phải làm lại. **Đề xuất: phán quyết `B-8` trước, hoặc dựng phần không bị ảnh hưởng trước**
> (đăng nhập · trang chủ · tài liệu · bài viết · dictation · lịch sử · token · trạng thái lỗi).

**Definition of Done**
- [ ] Danh sách màn, **gồm cả trạng thái rỗng, đang tải, lỗi, mất mạng, và đang chờ chấm** — đây là
      chỗ phần mềm thi cử thật sự hỏng, và là điểm mạnh duy nhất của bản cũ đáng giữ lại
- [x] Luồng nghiệp vụ: ~~nhập đề (CMS)~~ → [`../ux/cms-spec.md`](../ux/cms-spec.md) (19/08/2026),
      kèm bảy câu hỏi mở mới **M-15…M-21**
- [ ] Luồng nghiệp vụ: đăng nhập · thi · kết quả
- [ ] **Luồng Full Test chaining** — `E-12`, R→L→W→S trong một session. Prototype **chưa có**;
      `exam.html?mode=full` hiện nhảy thẳng sang màn nộp. Sơ đồ đề xuất: [`../architecture/key-flows.md`](../architecture/key-flows.md) §2a
- [ ] Màn cho 4 module mới: **Dictation · Tài liệu · Bài viết · AI Chat** (`M-22`…`M-25`)
- [ ] Màn **Token** — nhưng **không hiện số token cụ thể** cho tới khi `B-5b` có câu trả lời
- [ ] Thứ tự ưu tiên dựng prototype — **theo hành trình người dùng**, không theo rủi ro kỹ thuật.
      *Bản cũ xếp theo rủi ro và kết quả là một tập màn rời rạc, không ai nhìn ra sản phẩm.*

`[QUYẾT ĐỊNH]` **Không có vai giáo viên** trong bản đầu — M-11, chốt 18/08/2026.

---

## T3 · Prototype

Chỉ bắt đầu **sau khi** T2 xong. Chọn công cụ khi tới lúc.

Ghi chú từ lần trước, để cân nhắc chứ không phải để lặp lại:

- **Stitch** sinh nhanh nhưng **không tất định** (cùng prompt ra kết quả khác nhau), **không có API
  xoá màn** nên canvas tích lũy mọi bản nháp, và nó **diễn giải lại** `DESIGN.md` theo cách riêng.
- **Viết thẳng HTML** thì tất định, sửa một dòng là xong, và `DESIGN.md` được tuân thủ đúng vì token
  nằm trong CSS. Đổi lại chậm hơn ở giai đoạn phác ý tưởng.

---

## Chạy song song — không chặn T1–T3

| Việc | Ai | Ghi chú |
|---|---|---|
| **B-2 · Hỏi luật sư về PDPL** | Bạn | **Làm sớm nhất có thể** — thời gian phản hồi không nằm trong tay bạn, và nó chặn B-1 |
| **B-1 · Chọn AI provider** | Bạn | Chặn toàn bộ Phase 7. Phụ thuộc B-2 |
| **Xcode + Apple Developer** | IT | Chặn kiểm chứng audio plugin — giả định rủi ro nhất của dự án |

Khi có quyết định, báo bằng `/req B-3` — sẽ tự cập nhật đúng chỗ và lan hệ quả sang tài liệu liên quan.

---

## Quy tắc làm việc

1. **Một task mỗi lần.** Xong → đối chiếu DoD → báo cáo → **dừng**. Không tự nhảy sang task kế.
2. **Không bịa quy tắc nghiệp vụ.** Chưa có thì gắn thẻ, đừng đoán.
3. **Thiết kế cả trạng thái lỗi**, ngang mức đầu tư với luồng thuận lợi.
4. **Chưa viết code ứng dụng.** Phase 1 là thiết kế.
