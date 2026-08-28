# Next Actions — hàng đợi công việc

> Tài liệu này viết bằng tiếng Việt vì đây là **checklist vận hành cho người thực thi**, không phải tài liệu kiến trúc. Toàn bộ `docs/` còn lại là tiếng Anh.

**Cách dùng:** làm **đúng một task** mỗi lần. Xong → đối chiếu Definition of Done → báo cáo → **dừng lại**. Không tự động nhảy sang task kế tiếp.

Session mới chỉ cần đọc file này. Mọi thứ cần biết đều nằm ở đây hoặc trong link.

---

## Trạng thái

| | |
|---|---|
| Phase hiện tại | **Phase 4 — Nền tảng triển khai** (Phase 2 chốt yêu cầu đã diễn ra 20/08/2026) |
| Task đã xong | **T7 · Lõi chấm điểm có cơ sở** (21/08) — `H-8` đã chốt; bằng chứng trích dẫn thành bắt buộc và kiểm được. Xem mục T7 |
| Chen ngang, đã xong | **A21 · Đăng ký xong là đăng nhập luôn** (27/08) — `[QUYẾT ĐỊNH]` chủ sản phẩm: *"tạo tài khoản với email pass cho login như bình thường nhưng sẽ xác minh ở trang hồ sơ học sinh sau cũng được"*. Bỏ màn cụt "kiểm tra hộp thư"; xác minh chuyển hẳn về trang hồ sơ; API **nói thật là chưa gửi email nào**. Câu chưa chốt ghi thành `M-45`. **439 backend + 215 web**. Xem mục A21 |
| Chen ngang, đang mở | **Cổng hạ tầng I0…I7 + UI0…UI11** (27/08) — chỉ đạo chủ sản phẩm kèm master todo. Hàng đợi riêng, một item mỗi lần: [`infrastructure-gate.md`](infrastructure-gate.md). **I0.1 xong** — suite web hết nhấp nháy; nguyên nhân là test chạm API thật trên `localhost:5099`, không phải máy yếu. **230/230 × 6 lần chạy liên tiếp** |
| Chen ngang, đang mở | **A20 · Ba lát cuối của lớp nền đúng đắn** (27/08) — **A3 · vá từng câu** thay cho thay-cả-tờ, hết `409` và hết ghi đè; **Gate C · CAS cho transition** — advance/submit/expire chỉ một người thắng, đóng trước rồi mới chấm; **Gate D · idempotency giành chỗ trước khi chạy** — hai request cùng key không còn cùng chạy handler. **398 backend + 209 web**. Xem mục A20 |
| Chen ngang, đã xong | **A19 · Cổng crash + 3 lỗi CAS** (27/08) — suite giờ **đỏ khi app crash**; đã sửa lỗi `<p><div>`, cổng flush cứng cho submit/advance, generation + hàng đợi single-flight, revision trong session view. **368 backend + 205 web**. Xem mục A19 |
| Chen ngang, đã xong | **A18 · CAS** (27/08) — **lát 1 xong**: revision cho answer sheet, `409 REVISION_CONFLICT`, test barrier trên MongoDB thật. **Lát 2 chưa làm**: revision cho transition của phiên. **368 test backend + 199 test web**. Xem mục A18 |
| Chen ngang, đã xong | **A17 · Sửa theo bản review độc lập** (27/08) — bốn P0 đã kiểm chứng rồi sửa: **chấm sai multiple-select và matching**, Kestrel chặn mọi bản ghi Speaking, token chết giữa bài thi, dashboard dẫn sai chỗ. **364 test backend + 196 test web**. Xem mục A17 |
| Chen ngang, đã xong | **A16 · Đội agent rà 4 kỹ năng** (27/08) — bốn agent, **345 test backend + 190 test web**. Năm lỗi ranh giới, không cái nào nằm trong một module: **Full Test chưa bao giờ chuyển kỹ năng** · phiên hết giờ không bao giờ được chấm · chuỗi Speaking đứt vô hình · **toàn bộ upload Speaking bị middleware chặn trước handler** · autosave Speaking chống lại chính server. Xem mục A16 |
| Chen ngang, đã xong | **A15 · Dọn data demo + hoàn thiện chấm 4 kỹ năng** (27/08) — `[QUYẾT ĐỊNH]` chủ sản phẩm: chỉ dùng nội dung do chủ sản phẩm cấp. Xoá hai đề demo, bộ dictation tổng hợp, catalogue bài viết và tài liệu; **đường chấm nối trọn cho cả bốn kỹ năng**, evaluator AI là khe cắm rỗng. Xem mục A15 |
| Task đang mở | **T6 · CMS quản trị** — khung + 12 màn, nhật ký audit và **toàn bộ hành động ghi đã chạy thật** (21/08). **Định nghĩa lại phạm vi 24/08**: CMS là hệ vận hành nội dung, không phải dashboard quản trị — 11 quyết định `C-14`…`C-24`, lộ trình 6 phase. Xem mục T6 |
| Task đã xong | **T5 giai đoạn B · Engine thi qua API** — bốn kỹ năng + nghe chép chính tả chạy end-to-end (21/08) |
| Chen ngang, đã xong | **T5 giai đoạn A2 · SSO Google — backend** (21/08) — chủ sản phẩm chỉ đạo làm trước. `M-1` đã chốt. Xem mục A2 |
| Chen ngang, đã xong | **T5 giai đoạn A3 · Trang học sinh `/students/dashboard`** (21/08) — chủ sản phẩm chỉ đạo làm trước, kèm một ảnh giao diện tham chiếu. Xem mục A3 |
| Chen ngang, đã xong | **T5 giai đoạn A4 · Refactor trang `/profile`** (21/08) — chủ sản phẩm gửi bản review 19 mục. Xem mục A4 |
| Chen ngang, đã xong | **T5 giai đoạn A5 · `/students/dashboard` có shell riêng** (21/08) — sidebar trái, bỏ menu header, tách khỏi hồ sơ. Xem mục A5 |
| Chen ngang, đã xong | **T5 giai đoạn A8 · Bốn module trên header + dựng lại trang chủ** (24/08) — `/dictation` ra khỏi cổng đăng nhập; khối **Dành cho học sinh** dựng mới; ảnh bìa bài viết; logo kênh thật; **`B-9` chốt — hero thành hai trạng thái, hết số bịa**. Xem mục A8 |
| Chen ngang, đã xong | **T5 giai đoạn A9 · Dựng lại `/practice`** (24/08) — workspace luyện tập thay cho picker phẳng: chọn kỹ năng, lọc theo facet thật, phân trang; SEO xuống dưới workspace theo đúng thứ hạng thị giác. Xem mục A9 |
| Chen ngang, đã xong | **T5 giai đoạn A10 · Dựng lại `/dictation`** (24/08) — thư viện bài nghe có tìm kiếm và lọc; bài luyện tách ra `/dictation/:setId`. **Chặn: metadata bộ câu chưa tồn tại** — xem mục A10 |
| Chen ngang, đã xong | **T5 giai đoạn A11 · Rà soát toàn bộ giao diện + sửa lỗi tận gốc** (26/08) — bốn agent duyệt độc lập (landing+auth · practice+dictation · articles+profile+dashboard · rà code tĩnh); **7 blocker và ~40 lỗi khác đã sửa và có test khoá lại**. Xem mục A11 |
| Chen ngang, đã xong | **T5 giai đoạn A12 · Giao diện làm bài thi cho đề thật** (26/08) — nạp bộ đề bốn kỹ năng đầu tiên; **`group` vào hợp đồng** (55/76 câu cần nó), `marks` cho câu nhiều điểm, ảnh đề bài, bảng chọn đáp án, ô điền trong đoạn văn. Xem mục A12 |
| Chen ngang, đã xong | **T5 giai đoạn A13 · Dựng lại trang học sinh** (26/08) — hai cột thay cho một cột 1080px; trang cao 2400px → 1890px. Màu riêng cho từng kỹ năng; phiên hết giờ thôi tô xanh lá. Xem mục A13 |
| Chen ngang, đã xong | **T5 giai đoạn A14 · Ba việc tồn đọng** (26/08) — vùng chạm, i18n cho hai component dùng chung, và **kết luận lại về suite nhấp nháy: lỗi ở máy, không ở test**. Xem mục A14 |

| Chen ngang, đã xong | **T5 giai đoạn A6 · Mỗi module một trang + menu tự tràn** (21/08) — `/documents`, `/articles`, `/articles/<slug>`; header chỉ gập vào **Thêm** khi thật sự hết chỗ. Xem mục A6 |
| Task đã xong | **T4 · Stage 0 Foundation** (20/08) — monorepo, CI, design system, khung backend + test kiến trúc, Mongo `rs0`, auth slice S1 chạy end-to-end |
| Task còn nợ từ Phase 1 | **T2 · Danh sách màn + luồng nghiệp vụ** — vẫn chưa xong, **chặn** mọi màn thi và Track A. Xem T2 bên dưới |
| Task đã xong | **T0 · Chuẩn hóa tài liệu + rà soát stack** (20/08) · **T1 · `DESIGN.md`** (20/08 — hướng **C · Thẻ mềm** đã chốt) · **Final audit + đợt fix hậu audit** (20/08 — roadmap cập nhật theo brief 20/08; DESIGN.md về đúng chuẩn nguồn; quét sạch wording "B-1 chưa chốt" và "đã triển khai"; gate CONFIRMED-Source trong CI mở rộng toàn repo) |
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

> **Repo đã vào git và đẩy lên GitHub private ngày 20/08/2026** — `thang-dev-aptech/vni-ielts-ai`.
> Trước đó không có version control; đó là lý do cả hai lần xoá đều vĩnh viễn. `R13` đã đóng.
>
> **Trước khi commit, chạy `python3 scripts/check-docs.py`.** CI chạy đúng bộ kiểm đó và fail build nếu
> có link gãy, qualifier trong status, dòng `CONFIRMED` thiếu Source, hoặc chuỗi giống credential.

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

## T5 · Hoàn thiện end-user — 🔄 đang mở

**Thứ tự:** end-user trước, CMS sau → [ADR-0012](../decisions/0012-learner-first-sequencing.md).

**Ràng buộc cứng:** nội dung đề nạp qua `contracts/schemas/exam.schema.json`, không phải object graph
viết tay. Seeder, nhập ZIP, và soạn tại chỗ đều là *producer* của cùng một `ExamVersion` bản nháp qua
**cùng một bộ kiểm**.

**Quy tắc cổng — áp cho từng mục, không phải cuối task:**

> Một mục chỉ đóng khi qua **cả bốn**: ① test xanh · ② **đọc lại code với con mắt tấn công**
> · ③ **đo thứ đo được** (timing, giới hạn, hành vi đồng thời) · ④ **bắn thật vào API/UI đang chạy**.
>
> Hai vòng audit đầu tiên tìm được 12 rủi ro trên một codebase mà **100% test đang xanh**. Chín trong
> số đó chỉ lộ ra ở bước ② và ③. "Test pass" chứng minh những gì người viết nghĩ ra để kiểm, không
> chứng minh hệ thống an toàn.

---

### Giai đoạn A — Hoàn thiện end-user — ✅ **XONG 20/08/2026**

Một người lạ mở web, tự đăng ký, xác minh, đăng nhập, đi lại, xem hồ sơ, đổi ngôn ngữ, đăng xuất —
**không cần `curl`**. Đã tự đi hết vòng bằng trình duyệt, không chỉ đọc code.

| # | Việc | Xong |
|---|---|---|
| A1 | `packages/ui` — Button · Field · Alert · Card · Spinner · EmptyState · ErrorState · PageHeader | ✅ |
| A2 | i18n `vi`/`en` gõ kiểu chặt — thiếu một key ở một ngôn ngữ là **lỗi biên dịch** | ✅ |
| A3 | React Router + guard cần-đăng-nhập / chỉ-khách | ✅ |
| A4 | Màn đăng ký | ✅ |
| A5 | Màn xác minh email | ✅ |
| A6 | App shell — header, điều hướng, footer, skip-link | ✅ |
| A7 | Trang chủ | ✅ |
| A8 | Trang hồ sơ | ✅ |
| A9 | 404 + ErrorBoundary | ✅ |
| A10 | 31 test frontend (12 types · 12 ui · 7 luồng) | ✅ |

#### Ba lỗi tìm ra trong lúc làm — đáng đọc trước khi viết màn mới

**1 · Hai nguồn điều hướng đánh nhau.** `RequireAnonymous` chuyển hướng về trang chủ ngay khi trạng
thái đổi, **đè lên** đích người dùng định vào. Ai mở link tới `/profile` khi chưa đăng nhập thì sau khi
đăng nhập bị ném về trang chủ. Cách sửa **không** phải làm trang điều hướng nhanh hơn, mà là để **một
chỗ** quyết định — hai nguồn sự thật về điều hướng luôn tạo cuộc đua mà kẻ thắng phụ thuộc thứ tự
render. *Test bắt được.*

**2 · Màn xác minh treo vĩnh viễn.** API trả 400 đúng, UI đứng ở "Đang xác minh…". StrictMode gọi
effect hai lần: lần một bắn request rồi đặt `attempted.current`, cleanup đặt `cancelled = true`, lần
hai thoát sớm — kết quả trả về bị vứt bỏ. **Test không bắt được** vì nó render `<App/>` trần trong khi
`main.tsx` bọc StrictMode. *Môi trường test khác môi trường thật.* Test giờ render trong StrictMode.

**3 · "VI" hiện hai lần** trên header — chỉ nhìn ảnh chụp mới thấy.

> **Bài học chung:** ① test xanh · ② đọc lại với con mắt tấn công · ③ **đo** · ④ **bắn thật vào
> UI/API đang chạy**. Lỗi 2 và 3 chỉ lộ ở bước ④.

---

### ~~Giai đoạn A (mô tả gốc)~~ — giữ để tra cứu

**Mục tiêu:** một người lạ mở web lên, tự đăng ký, xác minh, đăng nhập, đi lại trong app, sửa hồ sơ,
đăng xuất — **không cần `curl`, không cần ai hướng dẫn**.

| # | Việc | Cổng cần qua |
|---|---|---|
| A1 | `packages/ui` — Button · Input · Card · Alert · Spinner · EmptyState · ErrorState · PageHeader | Mọi component dùng token, không hard-code màu/cỡ chữ |
| A2 | **i18n** từ màn đầu tiên, `vi` + `en` | Không còn chuỗi hard-code; `M-4` chưa chốt nên phải là **cấu trúc**, không phải chọn ngôn ngữ |
| A3 | **React Router** — tuyến công khai vs tuyến cần đăng nhập | Gõ thẳng URL khi chưa đăng nhập → chuyển hướng, **không nháy nội dung**; đăng nhập xong quay lại đúng trang định vào |
| A4 | **Màn đăng ký** | Trùng email → 409 hiển thị đúng · mật khẩu yếu báo rõ · **retry mạng không tạo hai tài khoản** |
| A5 | **Màn xác minh email** (đọc token từ query) | Token dùng lần hai → thông báo **giống hệt** token bịa |
| A6 | **App shell**: header + điều hướng + footer, và **chrome riêng cho phiên thi** | Chrome phiên thi không có link thoát — là chuyện của tuyến, không phải render có điều kiện |
| A7 | **Trang chủ** — lời chào, trạng thái xác minh, lối vào các mục | Mục chưa xây hiện **EmptyState trung thực**, không phải nút chết |
| A8 | **Trang hồ sơ** — xem thông tin, đổi tên hiển thị, đăng xuất | Đổi tên xong `/me` phản ánh ngay |
| A9 | **404 + ErrorBoundary** | Lỗi render không làm trắng màn |
| A10 | **Test component + luồng** (vitest + testing-library) | Phủ: chuyển hướng, lỗi 409, replay, i18n |

**Chưa làm ở giai đoạn A:** màn thi (chặn bởi `B-8`) · dictation/tài liệu/bài viết (cần backend nội dung) · CMS.

### Giai đoạn A2 — SSO Google, phần backend — ✅ **XONG 21/08/2026**

Chủ sản phẩm chỉ đạo làm trước, chen ngang giai đoạn B: *"hiện tại mình đang làm login SSO với
google, facebook, microsoft"* (21/08/2026). Phạm vi thu lại ngay trong cùng phiên trao đổi: **Google
trước, Facebook sau, Microsoft chưa** — *"làm google với facebook và sso key của google trước đi có
bổ sung thì mình sẽ báo sau"*.

**Chỉ backend.** Giao diện do một phiên khác làm; hợp đồng API bàn giao ở
[`../api/sso-contract.md`](../api/sso-contract.md).

| # | Việc | Cổng đã qua |
|---|---|---|
| A2-1 | `M-1` chốt + [ADR-0013](../decisions/0013-one-email-one-account-silent-linking.md) · [ADR-0014](../decisions/0014-backend-mediated-oidc-handoff-code.md) | `threat-model.md` `T1` và `key-flows.md` §1 đã viết lại theo quyết định mới, không để tài liệu nói ngược |
| A2-2 | 4 endpoint: `providers` · `{provider}/start` · `{provider}/callback` · `complete` | Backend giữ toàn bộ secret, PKCE verifier, `state`, `nonce`; client chỉ thấy URL và **mã trao tay 60 giây** |
| A2-3 | Adapter OIDC + discovery + JWKS (Google) | 12 test: chữ ký lạ · sai `aud` · sai `iss` · hết hạn · sai `nonce` · thiếu `nonce` · thiếu `sub` — **trượt cả 12** |
| A2-4 | Store `state` và mã trao tay trên Mongo, băm + TTL | `FindOneAndDelete` nguyên tử; dùng lại lần hai đều trượt |
| A2-5 | Provider giả cho Development | **API từ chối khởi động** nếu bật ngoài Development |
| A2-6 | 5 test tích hợp end-to-end qua HTTP + Mongo thật | Tự bỏ qua khi không có Mongo `rs0`, không fail build của người chưa dựng stack |
| A2-7 | **Phạm vi chốt lại: chỉ Google** (`AU-8`) | Nền móng đã dựng cho Facebook đã **hoàn nguyên trọn vẹn** — `User.Email` về lại bắt buộc, index duy nhất như cũ. `AU-5`: không dựng sẵn cấu trúc cho tính năng chưa làm |
| A2-8 | **Khóa Google thật đã có và đã chạy** (21/08) | Google trả về màn đăng nhập thật, hiện đúng *"Tiếp tục tới VNIIELTS"*. **Không cần domain** — `localhost` được Google miễn trừ HTTPS |
| A2-9 | **Giao diện đã nối** — nút Google, tuyến `/login/sso`, 9 mã lỗi có câu tiếng Việt | 12 test frontend mới. Danh sách provider **hỏi server**, không hard-code: nơi nào chưa có khóa thì nút tự tắt |
| A2-10 | Route chuẩn hóa tiếng Anh | `/login` `/register` `/dashboard` `/profile` `/verify-email` `/login/sso`. Sửa cả `Sso:ClientCallbackPath` phía server — lệch một chữ là mọi lần đăng nhập rơi vào 404 |
| A2-25 | **Trả "Tiến độ học tập" về menu tài khoản** | Nó bị gỡ khỏi menu và chỉ còn là một tab bên trong `/profile` — chủ sản phẩm báo *"người dùng không để ý cũng không biết"*. Menu giờ đúng bản mô tả gốc 21/08: Hồ sơ · Trang học sinh · Tiến độ · Đăng xuất |
| A2-24 | **Xác minh email cập nhật ngay, không cần tải lại trang** | Trước đó ứng dụng **tự mâu thuẫn**: màn xác minh nói "đã xác minh", hồ sơ ngay sau đó vẫn nói "chưa xác minh". Ba lớp: làm mới ngay trong tab, `BroadcastChannel` sang tab khác, và làm mới khi quay lại tab. 4 test |
| A2-23 | **Đổi email khi chưa xác minh · khoá lại sau khi xác minh** · bỏ nút "Chỉnh sửa" chung | `POST /me/email`. Địa chỉ nằm ở **hai** chỗ — `User.Email` và `providerUserId` của dòng danh tính Email mà đăng nhập mật khẩu tra cứu — đổi thiếu một chỗ là hỏng đăng nhập **lặng lẽ**. 7 test |
| A2-22 | **Số điện thoại** (thêm/sửa/xoá) + **nút gửi lại email xác minh** trong hồ sơ | `POST /me/phone` · `POST /me/verify-email/resend`. `PhoneNumber` chuẩn hoá `091 234 5678` → `+84912345678`, hiển thị ngược lại. 18 test |
| A2-21 | **Quên mật khẩu + Tạo mật khẩu** — backend và giao diện | `POST /auth/forgot-password` · `/auth/reset-password` · `/me/password`. Tài khoản tạo bằng Google **đặt được mật khẩu** mà không ai phải tin một lời khai chưa xác minh — Google đã xác minh hòm thư đó. 13 test Application |
| A2-20 | **Gỡ ngõ cụt cho tài khoản Google**: đăng ký lại bằng email đó bị chặn, mà đăng nhập bằng mật khẩu cũng không được | Chủ sản phẩm báo 21/08. Không phải lỗi kỹ thuật — hệ thống làm đúng `AU-7`. Lỗi nằm ở chỗ **không thông báo nào chỉ ra nút Google** |
| A2-19 | **Quản lý thiết bị — làm xong cả backend lẫn giao diện** | `GET/DELETE /api/v1/me/sessions`. Một "thiết bị" = **một họ refresh token**, khái niệm đã có sẵn chứ không bịa ra. `/me` trả thêm `providers` + `hasPassword`. 5 test Application · 5 test tích hợp qua HTTP thật |
| A2-18 | **Thiết kế lại bề mặt trang `/profile`** | Cấu trúc giữ nguyên, chỉ viết lại `profile.css`. Sáu tông màu → **một accent**. Sửa 3 vi phạm ràng buộc cứng: chữ 12–13px dưới sàn 14, `line-height` 1.3–1.45 dưới 1.5, và khoảng cách `6/10/18/22/28px` — sáu trong bảy giá trị `DESIGN.md` cấm đích danh |
| A2-17 | Avatar **một chữ** (lấy tên gọi, không lấy họ) + **nền đổi mỗi phiên đăng nhập** | 8 màu **đã tính tỉ lệ tương phản**, tất cả đạt ≥5.0 với chữ trắng. Hai màu logo bị loại vì chỉ đạt 2.94 và 2.53 — đúng điều `DESIGN.md` đã ghi. Màu giữ nguyên khi tải lại trang, và **luôn khác lần đăng nhập trước** |
| A2-16 | Nút tài khoản **bỏ hiệu ứng rê chuột và bỏ mũi tên**; **Đăng xuất thôi đỏ thường trực** | Đỏ chỉ xuất hiện khi rê chuột/focus. Vòng `focus-visible` **giữ lại** — người dùng bàn phím không có con trỏ để biết mình đang đứng đâu |
| A2-15 | Avatar **vuông bo góc** + **icon cho từng mục** trong cả hai dropdown | Sáu icon vẽ tay cùng một lưới 24, nét 1.7, ăn theo `currentColor`. Không thêm thư viện icon cho sáu hình |
| A2-14 | **Header đăng nhập theo bố cục chủ sản phẩm chốt 21/08**: `logo · menu · thông báo · avatar+dropdown` | Bỏ hẳn mép ngoài · chữ menu 17px/800 (khách vẫn 14px) · menu tài khoản 4 mục **Hồ sơ học sinh · Trang học sinh · Theo dõi · Đăng xuất**, vạch 2px trước Đăng xuất |
| A2-13 | **Header học theo tham chiếu chủ sản phẩm gửi** (Edly, 21/08) — gom link phụ vào **"Thêm ⌄"**, thêm vạch ngăn trước khối tài khoản | Ba link chính hiện, hai link tra cứu gấp lại. Hai menu dùng chung `useDisclosure`: Escape, bấm ra ngoài, và **mở cái này thì cái kia tự đóng**. 4 test mới |
| A2-12 | **Đăng nhập luôn về trang chính**, kể cả khi bị đá ra từ trang cần đăng nhập | Chủ sản phẩm báo *"vẫn thế"* hai lần. Nguyên nhân không phải một lỗi mà là **hai**: mặc định của trang callback Google trỏ vào dashboard, và guard trả người dùng về trang họ bị đá ra. Cái thứ hai đúng theo thiết kế cũ nhưng nhìn từ ngoài y hệt cái thứ nhất |
| A2-11 | **Đăng nhập xong ở lại trang chính**, header đổi thành menu tài khoản | `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08: *"login sẽ không nhảy vào dashboard nữa mà sẽ là vẫn ở trang chính"*. Logo ra sát mép, avatar chữ cái + tên + mũi tên, dropdown: Hồ sơ · Theo dõi · Đăng xuất. 10 test mới, gồm phím Escape và trả lại focus |

> **Bài học từ A2-12, đáng ghi hơn cả bản vá:** lần sửa đầu mình chỉ tìm *một* đường dẫn tới
> dashboard rồi kết luận đã xong, còn test thì mình viết `lands on the dashboard` — tức là mã hoá
> đúng cái sai vào cả code lẫn test, nên nó xanh. Chỉ khi **tái hiện bằng trình duyệt thật, đi trọn
> vòng từ ba điểm xuất phát khác nhau**, đường thứ hai mới lộ ra. Test chỉ bảo vệ được điều mình đã
> hiểu đúng.
>
> **Cái đã đánh đổi, nói thẳng:** link chia sẻ tới một trang cần đăng nhập không còn sống sót qua
> bước đăng nhập. Ai mở link tới hồ sơ sẽ đăng nhập xong rồi về trang chính. Trạng thái `from` và
> `returnTo` phía server vẫn còn nguyên, nên đảo lại chỉ là một dòng.

**A2-25 — một lập luận hợp lý bị thực tế bác bỏ, ghi lại để khỏi lặp:**

Mục "Tiến độ" từng bị gỡ khỏi menu tài khoản với lý do **hoàn toàn hợp lý**: hai mục menu mở ra cùng
một màn hình, chỉ khác tab, thì trông như trùng lặp. Cái phản bác không phải một lập luận hay hơn —
mà là kết quả thật: *"có khi người dùng không để ý cũng không biết tiến độ học tập ở trang cá nhân"*.

> **Một menu gọn mà giấu mất thứ người ta đang tìm thì không phải gọn, mà là rỗng.**
>
> Đã ghi thẳng vào code điều kiện để gỡ nó lần sau: **phải vì người dùng đã có đường khác để tới tiến
> độ**, chứ không phải vì menu trông đẹp hơn khi thiếu nó.

Mục menu dùng **chính nhãn của tab** (`profile.tab.progress`) chứ không thêm khoá riêng — một đích đến
mang hai cái tên sẽ lệch ngay khi ai đó sửa một bên, mà đó đúng là chuyện đã xảy ra: "Theo dõi" và
"Tiến độ" từng cùng trỏ về một chỗ.

**A2-24 — ba đường, ba hành trình khác nhau, không đường nào là polling:**

| Đường | Kịch bản nó cứu |
|---|---|
| Làm mới ngay sau khi xác minh | Bấm link trong **cùng tab**. Đây là đường sửa cái mâu thuẫn: màn xác minh báo thành công còn hồ sơ vẫn hiện trạng thái cũ |
| `BroadcastChannel` | Mở hòm thư ở **tab khác**, bấm link. Tab hồ sơ cập nhật **ngay, không cần quay lại tab đó** — đã kiểm riêng để chắc chắn không phải nhờ sự kiện focus |
| Làm mới khi tab được focus lại | Link mở ở nơi ứng dụng không nghe được — ô xem trước của app mail, hoặc trình duyệt khác. Có tiết chế 5 giây, nếu không thì mỗi lần alt-tab là một request |

**Không dùng polling.** Nó sẽ tốn một request mỗi vài giây trên mọi tab đang mở, cho một sự kiện xảy
ra đúng một lần trong đời một tài khoản. Chỗ duy nhất còn hở là một tab không ai quay lại — và tab đó
thì cũng chẳng ai đang nhìn.

> **Bài học về test lấy từ đúng hôm nay:** test của mình bám vào **nhãn tab**, mà nhãn là thứ phiên
> khác đổi ba lần trong một buổi chiều — "Theo dõi" → "Tiến độ" → "Tiến độ học tập". Đã chuyển sang
> điều hướng bằng **`?tab=…`**, vì đó mới là hợp đồng thật: nó quyết định link chia sẻ và bookmark có
> chạy hay không. Chữ hiển thị thuộc về người làm giao diện; hành vi thuộc về test.

**A2-23 — vì sao đổi email không đơn giản như nhìn:**

> Địa chỉ email được lưu ở **hai chỗ**: trên `User`, và làm `providerUserId` của dòng danh tính
> Email — vì `LoginWithPassword` tra cứu theo chính giá trị đó, **không** theo `User.Email`. Đổi một
> chỗ mà quên chỗ kia thì tài khoản vẫn còn, hồ sơ vẫn hiện địa chỉ mới, và **không đăng nhập được
> bằng mật khẩu ở cả địa chỉ cũ lẫn mới**. Có test riêng canh đúng chuyện đó, và đã chạy thật trên
> trình duyệt: đăng nhập bằng địa chỉ mới sau khi đổi — được.

**Luật:** chưa xác minh thì đổi được (người gõ `gmial.com` không còn đường nào khác — link sửa lỗi
gửi tới chính địa chỉ sai). Xác minh xong thì **khoá**, vì lúc đó nó là đường lấy lại tài khoản, và
một phiên bị đánh cắp không được phép dời tài khoản sang hòm thư khác.

Nút "Đổi" lúc bị khoá thì **biến mất chứ không xám đi** — nút xám mời người ta đi tìm cách bật nó lên,
mà không có cách nào cả.

**A2-22 — số điện thoại là thông tin TỰ KHAI, và giao diện phải nói đúng điều đó:**

> Email có nhãn *"đã xác minh"*; **số điện thoại thì không, và sự bất đối xứng đó là chủ đích**.
> Email được chứng minh bằng một link người dùng bấm. Số điện thoại là thứ họ gõ vào — chưa có
> requirement nào đòi OTP, và tự dựng luồng xác minh là tự quyết luôn chính sách đằng sau nó. Gắn
> nhãn "đã xác minh" cạnh cả hai sẽ biến cái trung thực thành lời nói dối.
>
> Có test riêng khẳng định hàng số điện thoại **không bao giờ** mang nhãn xác minh — để người sửa
> markup sau này không vô tình thêm vào cho "cân đối".

`[BUSINESS DECISION]` còn treo: **có bắt xác minh số điện thoại bằng OTP không?** Nếu có thì đó là
một nhà cung cấp SMS, một khoản chi phí, và một nghĩa vụ PDPL nữa.

**Lưu một dạng, hiển thị một dạng khác — và đó là đúng.** Lưu `+84912345678` để `091 234 5678` và
`+84 912 345 678` không thành hai số khác nhau trong CSDL. Hiển thị lại thành `0912 345 678` vì đó là
cách chủ nhân đọc số của mình.

**A2-21 — bốn quyết định, mỗi cái đều có một cách làm sai rất tự nhiên:**

| Quyết định | Cách làm sai mà nó tránh |
|---|---|
| `forgot-password` **luôn trả 202**, dù email có tồn tại hay không | Phân biệt hai trường hợp là biến endpoint này thành công cụ dò xem ai có tài khoản (`T4`). Người dùng thật không cần biết — họ sắp mở hòm thư dù sao |
| **Đặt lại mật khẩu thì thu hồi mọi phiên**; **tạo/đổi từ hồ sơ thì giữ lại phiên đang dùng** | Đặt lại là việc người ta làm khi nghi bị chiếm tài khoản — để phiên kẻ tấn công sống là làm màu. Còn đổi từ hồ sơ mà tự đăng xuất chính mình khỏi trang đang đứng thì là lỗi |
| **Kiểm mật khẩu mạnh/yếu TRƯỚC khi tiêu token** | Kiểm sau thì gõ mật khẩu ngắn một lần là mất luôn liên kết, người dùng cầm một link chết và vẫn mật khẩu cũ |
| **Đổi mật khẩu bắt nhập mật khẩu hiện tại; tạo lần đầu thì không** | Bắt nhập "mật khẩu hiện tại" với tài khoản Google là hỏi một thứ chưa từng tồn tại — đó chính là ngõ cụt `A2-20`. Nhưng bỏ luôn bước đó thì một access token bị đánh cắp đủ để khoá chủ tài khoản ra ngoài |

**A2-20 — hai câu thông báo đều đúng, nhưng ghép lại thành cái bẫy:**

Chủ sản phẩm đăng nhập bằng Google với `ngdthang.dev@gmail.com`, rồi thử tạo tài khoản bằng chính
địa chỉ đó:

1. Đăng ký → *"Email này đã được đăng ký"* — đúng theo `AU-7`, một email một tài khoản.
2. Sang đăng nhập, gõ email + mật khẩu → *"Email hoặc mật khẩu không đúng"* — cũng đúng, vì tài khoản
   tạo qua Google **không có mật khẩu nào cả**.
3. Không có chỗ nào nói *"hãy bấm nút Google"*.

> **Từng câu một thì không sai, nên không ai để ý.** Chỉ khi đi hết cả hai bước mới thấy đó là vòng
> kín. Đây là kiểu lỗi test không bắt được vì mỗi test chỉ kiểm một bước.

**Cách sửa, và ranh giới về bảo mật:** thông báo mới **không tiết lộ tài khoản đó dùng provider nào**
— nó chỉ nói *"nếu trước đây bạn vào bằng nút Google thì bấm nút đó"*, câu chữ giống hệt nhau cho mọi
tài khoản. Nói thẳng "tài khoản này dùng Google" sẽ lộ thêm thông tin cho kẻ lừa đảo so với mức hiện
tại. Gợi ý ở màn đăng nhập cũng là câu tĩnh, in ra với mọi lần sai.

**Thứ đóng hẳn vấn đề này là quên mật khẩu** — gửi link đặt mật khẩu tới chính hòm thư đó, và Google
đã xác minh người dùng sở hữu nó. Vẫn đang nợ.

**Bốn quyết định trong A2-19 đáng ghi lại:**

- **Không lưu địa chỉ IP.** Chỉ User-Agent. Mục đích của màn này là để chủ tài khoản *nhận ra máy của
  mình*, mà IP không giúp gì cho việc đó — nó đổi theo từng mạng — và nó chính là trường biến một
  danh sách phiên thành lịch sử vị trí. Thu thập ít hơn là chủ đích, không phải thiếu sót. → `B-2`
- **Nhãn thiết bị dựng lúc đọc, không lúc ghi.** Chuỗi User-Agent thô được giữ nguyên; cải thiện bộ
  phân tích sau này không cần migration, và các phiên cũ được hưởng ngay.
- **Không tự đăng xuất chính mình từ danh sách.** Làm vậy để lại trình duyệt cầm một token đã chết
  trong khi header vẫn hiện đã đăng nhập. Đó là việc của nút Đăng xuất, nơi còn dọn cả trạng thái
  cục bộ. **Server cũng từ chối**, nên luật giữ được kể cả khi giao diện sai.
- **`DELETE /me/sessions/{id}` được miễn `Idempotency-Key`.** Thu hồi một họ đã thu hồi rồi thì không
  đổi gì — không có hành động thứ hai nào để một cái khoá ngăn chặn.

> **Chạy thật lộ ra một lỗ thiết kế mà test không thấy: 170 phiên.** Tài khoản dev tích tụ 170 phiên
> sống chỉ sau một ngày script tự đăng nhập — mọi phiên đều thật, và không ai cuộn hết được. Người
> dùng máy chung cũng tích tụ như vậy, chỉ chậm hơn. Đã thêm **"Đăng xuất khỏi N thiết bị khác"** và
> giới hạn hiển thị 6 dòng. Nút hàng loạt đặt **trên** danh sách, vì giấu nó sau 170 dòng là giấu
> giải pháp đằng sau chính vấn đề.

**Sáu thứ đã sửa ở A2-18, ghi lại vì đây là kiểu lỗi hay quay lại:**

| Vấn đề | Vì sao là lỗi chứ không phải khẩu vị |
|---|---|
| **Hai màu xanh chạm nhau ở mép thẻ** | Dải hero chuyển sắc xanh-dương-sang-lá, banner thẻ xanh lá phẳng — hai màu xanh khác nhau gặp nhau trên một đường. Đây là thứ trông "thô" rõ nhất |
| **Sáu tông trên một màn** | Nghiên cứu trong `DESIGN.md` kết luận cả bốn hệ tham chiếu đồng ý *một accent duy nhất*. Trang này chưa có accent, nó có một bộ sưu tập |
| **Màu avatar rơi lên nền xanh** | 8 màu đó được kiểm tương phản **với nền trắng**, và chỉ nền trắng. Đặt lên banner xanh là vứt bỏ phép kiểm đó — kết quả là hồng cánh sen trên nền lá |
| **Chữ 12–13px, `line-height` 1.3–1.45** | Vi phạm ràng buộc cứng cho tiếng Việt. Dấu chồng hai tầng không đọc được ở cỡ đó trên điện thoại |
| **Nút "Chỉnh sửa" là khối xám kín chiều ngang** | Phần tử nặng nhất trong thẻ, dành cho thứ duy nhất **không hoạt động** |
| **Tab cuộn ngang trên điện thoại** | Và tab đang chọn — thứ trả lời "tôi đang ở đâu" — chính là tab bị cắt mất ở mép phải |

**Hai thứ trong A2-14 chưa có gì phía sau, và được dựng thành trạng thái rỗng trung thực thay vì nút chết:**

| Thành phần | Cách xử lý |
|---|---|
| **Chuông thông báo** | Chủ sản phẩm yêu cầu trực tiếp, nên dựng. Nhưng **không có endpoint nào sinh thông báo** → mở ra hiện *"Chưa có thông báo nào"*. **Không có badge số**: một con số đỏ không có dữ liệu đằng sau là bịa đặt, ở đúng chỗ hút mắt nhất trang |
| **"Theo dõi"** | Một mục menu phải có chỗ để tới. Tiến độ dựng từ các lần làm bài, mà engine thi là `T5` giai đoạn B chưa làm → `/progress` hiện `EmptyState` nói thẳng. Vẽ biểu đồ số liệu bịa ở màn này là tệ nhất, vì đây là màn học viên sẽ tin |

> **Ba thứ trong tham chiếu đó cố tình KHÔNG dựng**, vì dựng là bịa ra cam kết chưa có:
>
> | Thành phần | Vì sao không |
> |---|---|
> | Chip số token (🌰 17) | `T-1` đã CONFIRMED nhưng **chưa có endpoint nào trả số dư**, và `T-4` (số lượng mỗi giao dịch) vẫn UNCONFIRMED. Vẽ một con số ra là bịa dữ liệu — trái luật L3 của `DESIGN.md` |
> | Nút "Nạp tiền" | **Không có requirement nào.** Thanh toán đang là `[OPEN QUESTION]` `B-4`/`B-5`: *"tokens có bán được hay không vẫn chưa quyết"* |
> | Chuông thông báo | Không có requirement nào |
>
> Chỗ trống cho chip token nằm ngay cạnh khối tài khoản. Ngày `GET /me` trả về số dư thì thêm vào là
> một component, không phải sửa header.

**Hai thứ đi kèm quyết định A2-11, không phải phát sinh thêm:**

- **Hai CTA trong trang trỏ tới `/register`.** Với người đã đăng nhập, `RequireAnonymous` đá ngược
  về `/` — tức là nút bấm vào không làm gì cả. Đã chuyển sang `/dashboard` khi đã đăng nhập.
- **Hai test cũ khẳng định hành vi cũ** (đăng nhập xong thấy tiêu đề dashboard). Đã sửa theo hành vi
  mới chứ không xoá — mục đích ban đầu của chúng vẫn còn giá trị.

**Toàn bộ 177 test của solution xanh.** 56 Application · 43 Infrastructure · 69 Domain · 4 Architecture
· 5 Integration.

#### Rà responsive — 21/08

56 lượt đo, **8 cỡ màn** (320 → 1440) × **7 trang**, kiểm theo ràng buộc cứng của `DESIGN.md` chứ
không theo cảm tính. Kết quả cuối: **0 trang cuộn ngang · 0 chữ dưới sàn 14px · 0 `line-height` dưới
1.5 · 0 `text-transform: uppercase` · 0 vùng chạm dưới 44px.**

Hai lỗi thật đã sửa, cả hai chỉ lộ ra khi **đo**, không lộ khi nhìn:

| Lỗi | Vì sao |
|---|---|
| **Mọi trang cuộn ngang ở màn ≤384px** | Hàng header là flex `nowrap` chứa 370px nội dung trong 292px chỗ trống. 360px là cỡ máy Android rất phổ biến ở Việt Nam | 
| **Trang hồ sơ tràn thêm 113px** | Mã người dùng 32 ký tự không có chỗ ngắt, và flex item mặc định `min-width: auto` nên từ chối co lại. Phải có **cả hai**: `minWidth: 0` và `overflow-wrap: anywhere` |

> **`DESIGN.md` không có mục nào về responsive** — không breakpoint, không ngưỡng vùng chạm. Bộ kiểm
> trên vì thế dựa vào ràng buộc kiểu chữ đã có, cộng chuẩn ngoài (44px của iOS/Android, 24px của
> WCAG 2.2 AA). Đây là **khoảng trống trong đặc tả thiết kế**, thuộc về chủ sản phẩm — ghi ra chứ
> không tự đặt luật.

##### Trang chính lọt vào bộ đo lần đầu — và nó không đạt

Trước A2-11, người đã đăng nhập bị đá sang dashboard nên **landing page chưa từng được đo**. Sau khi
họ ở lại trang chính, nó vào bộ kiểm và ra con số này:

| | Các trang đã làm (dashboard · hồ sơ · xác minh · 404) | **Trang chính** |
|---|---|---|
| Cuộn ngang | 0 | **6** — ở 320px và 360px |
| Chữ dưới sàn 14px | 0 | **1134** |
| `line-height` < 1.5 | 0 | **228** |
| `text-transform: uppercase` trên tiếng Việt | 0 | **96** |
| Vùng chạm < 44px | 0 | **135** |

**Đã kiểm chứng đây là lỗi có sẵn, không phải do A2-11 gây ra:** đo trang chính ở trạng thái *chưa
đăng nhập* cho ra đúng cùng con số tràn (363px trong khung 320px), tức là nó tràn từ trước.

Riêng phần **do A2-11 gây ra thì đã sửa**: hàng header lúc đăng nhập rộng 337px trong khung 320px, vì
menu tài khoản to hơn cái link "Đăng nhập" mà nó thay thế. Ẩn chữ "IELTS AI" dưới 400px là đủ.

> **Không tự ý viết lại trang chính.** Nó được port từ bản redesign chủ sản phẩm đã duyệt bằng mắt,
> và 1134 mục là một đợt làm lại chứ không phải một bản vá. Việc này thuộc phiên đang làm UI hoặc
> thuộc một quyết định riêng của chủ sản phẩm: **giữ nguyên bản đã duyệt, hay áp `DESIGN.md` lên nó**.

##### Đã sửa trên trang chính — chỉ những gì là *lỗi*, không đụng vào *thẩm mỹ*

Ranh giới tự đặt: sửa cái hỏng, không sửa cái chủ sản phẩm đã duyệt bằng mắt.

**1 · Cuộn ngang ở 320px và 360px — đã hết.** Ô lưới của khối AI được co theo nội dung và từ chối
thu nhỏ, vì `min-width: auto` là mặc định của grid/flex item. Ba khai báo là đủ, và **không đổi gì ở
các cỡ vốn đã ổn** — đo 390px và 430px trước/sau cho kết quả giống hệt.

> Lần sửa đầu tiên *trông như* đã xong: trang hết cuộn, đúng thứ đang đo. Nhưng ô điểm thành phần
> bị cắt chữ và mọi band số văng ra ngoài khung. **Chỉ kiểm đúng chỉ số mình định sửa là cách để
> đẩy lỗi đó lên production.** Phương án cuối: dưới 430px các ô xếp dọc.

**2 · Nút hamburger không làm gì cả.** Dưới 980px, CSS ẩn hàng link và hiện hamburger — nhưng nút đó
**không có `onClick`**. Nghĩa là trên mọi điện thoại và máy tính bảng, cả 5 mục điều hướng của trang
đều không có cách nào chạm tới, sau một cái nút trông như sống. Đã dựng panel: mở/đóng, `Escape`,
bấm một mục thì tự đóng, vùng chạm 44px, và **đặt tên landmark khác** với hàng link desktop — hai
`<nav>` cùng tên là mơ hồ với trình đọc màn hình. 5 test mới.

**Còn lại, cố tình không đụng** — đây là mật độ và kiểu chữ của bản thiết kế đã duyệt:

| Mục | Số đo | Ghi chú |
|---|---|---|
| Link chân trang | 19–20px cao | Dưới cả sàn 24px của WCAG 2.2 AA, không chỉ dưới 44px |
| Icon mạng xã hội | 40px | Thiếu đúng 4px |
| 1134 chữ dưới sàn 14px · 228 `line-height` · 96 `uppercase` | | Một đợt làm lại, không phải bản vá |

#### Hai thứ đáng nhớ

**1 · Test tích hợp bắt được lỗi mà 172 test đơn vị không thấy.** `IdempotencyMiddleware` bắt buộc
header `Idempotency-Key` trên mọi `POST`, nên `POST /auth/sso/google/start` trả `400` trước khi chạm
tới handler. Không test đơn vị nào nhìn thấy vì không test nào đi qua pipeline HTTP. Đây đúng là rủi
ro `C3` đã ghi trong giai đoạn C — và nó có thật.

**2 · Quyết định của chủ sản phẩm có một cạnh sắc, đã bịt lại chứ không bỏ qua.** *"cùng gmail thì
chung một tài khoản"* là hợp lý, nhưng đăng ký **không** đòi xác minh trước khi tài khoản tồn tại. Kẻ
xấu đăng ký sẵn email của nạn nhân, đặt mật khẩu, rồi chờ. Nạn nhân đăng nhập Google → gộp → mật khẩu
của kẻ xấu vẫn mở được tài khoản đã gộp. Cách bịt **không thêm bước nào cho người dùng thật**: gộp vào
tài khoản chưa xác minh thì xóa mật khẩu cũ và thu hồi mọi phiên. → [ADR-0013](../decisions/0013-one-email-one-account-silent-linking.md)

#### Còn nợ của A2

| Việc | Ai làm |
|---|---|
| ~~Tạo Google OAuth client~~ | ✅ **xong 21/08** — client `WEBIELTS`. Khóa nằm ở `user-secrets`, bản gốc Google cấp để ngoài repo |
| ~~Nối nút SSO + tuyến callback~~ | ✅ **xong 21/08** |
| **Xoay lại client secret trước khi lên production** | Secret hiện tại từng nằm trong thư mục repo và đi qua khung chat trước khi được chuyển ra ngoài. Client này ở trạng thái `Testing` và chỉ nhận `localhost` nên rủi ro thấp — nhưng production **phải là một OAuth client riêng**, với secret chưa từng xuất hiện ở đâu |
| **Quên mật khẩu** | Chưa có. Sau A2 nó nặng hơn trước: tài khoản chưa xác minh bị gộp sẽ mất mật khẩu và hiện chỉ vào lại được bằng Google |
| ~~Facebook · Microsoft~~ | **Hoãn** — chủ sản phẩm 21/08: *"trước mắt chỉ làm cho google thôi mấy phần khác bỏ hoàn thiện mượt app rồi bổ sung thêm"* (`AU-8`). Nghiên cứu Facebook đã kiểm chứng và cất ở [`sso-provider-setup.md`](sso-provider-setup.md) §4 để không phải tra lại |

---

### Giai đoạn A3 — Trang học sinh — ✅ **XONG 21/08/2026**

Chủ sản phẩm chỉ đạo làm trước, chen ngang giai đoạn B, kèm một ảnh chụp giao diện tham chiếu
(rail trái gập được · thanh trên có tìm kiếm, ví tiền, chuông, avatar · nút *Hỏi đáp AI* ở chân rail)
và một câu khoanh vùng phạm vi: *"hiện tại chỉ có hỏi đáp AI với là thi 4 kỹ năng"*.

`/students/dashboard` trước đó là một vỏ rỗng trung thực. Giờ nó là **trang chủ của khu vực học
sinh**: rail điều hướng, hai chế độ luyện tập, và lối vào AI Chat.

| # | Việc | Cổng đã qua |
|---|---|---|
| A3-1 | Rail trái gập được, ghi nhớ trạng thái qua `localStorage` | Mọi đích đến đều **có thật** — ba neo trong trang, hai tuyến đã tồn tại. Không có mục nào dẫn tới 404 |
| A3-2 | **Full Test và Single Skill dựng thành hai khối tách bạch** (`E-11`) | Thẻ Full Test in thứ tự R→L→W→S thành danh sách có số (`E-12`); khối từng kỹ năng nói thẳng là **không tự chuyển** (`E-13`). Có test khoá cả hai |
| A3-3 | Bốn thẻ kỹ năng, mỗi thẻ mang **cách chấm** của nó | `Chấm theo đáp án` (`A-11`) là chip nền đặc; `AI chấm · tham khảo` (`A-13a`, `F-1`) là **viền gạch đứt, không nền** — đúng L4, và phân biệt được khi chuyển ảnh xám vì khác **kiểu viền**, không chỉ khác màu |
| A3-4 | Panel **Hỏi đáp AI** — mở từ chân rail, Escape đóng và trả focus về nút | Ô nhập **tắt** và nói rõ vì sao: `B-6a`…`B-6e` và `B-2` đều chưa chốt. Dựng khung để chủ sản phẩm góp ý về hình dạng, không dựng một ô nhận câu hỏi mà không có gì trả lời |
| A3-5 | Trạng thái rỗng trung thực cho *bài đang làm dở* và *kết quả* | Không `0.0`, không skeleton ở ô điểm (L3). Thẻ bài dở nói **đồng hồ không tạm dừng**, không nói ngược lại (L1, [ADR-0007](../decisions/0007-server-authoritative-exam-timer.md)) |
| A3-6 | 7 test frontend mới · 65 test toàn app xanh | Một test đếm chữ trên toàn trang để chặn `0.0`, chữ "token" và nút "Nạp tiền" quay lại |
| A3-7 | Đo trên trình duyệt thật, không đọc code | 26 phần tử: **0 cặp chữ/nền dưới 4.5** (thấp nhất 4.72) · **0 cỡ chữ ngoài thang đóng** · **0 `line-height` dưới 1.5** · **0 uppercase** · 0 cuộn ngang ở 500px |

**Ba thứ trong ảnh tham chiếu cố tình không dựng** — cùng lý do đã ghi ở A2-14, nhắc lại vì lần này
chúng nằm trong ảnh chủ sản phẩm gửi:

| Thành phần | Vì sao không |
|---|---|
| Chip số dư ví | `T-1` đã CONFIRMED, nhưng **không endpoint nào trả số dư** và `T-4`/`B-5b` (số token mỗi giao dịch) vẫn UNCONFIRMED. Anti-pattern 16 của `DESIGN.md` cấm đích danh việc hiện số token cụ thể |
| Nút **Nạp tiền** | Không có requirement nào. Thanh toán vẫn là `[OPEN QUESTION]` `B-4`/`B-5` |
| Ô tìm kiếm nội dung | Chưa có nội dung nào để tìm — không có API đề, tài liệu hay bài viết |

Chỗ trống cho cả ba vẫn còn: ví nằm cạnh khối tài khoản trên header, tìm kiếm nằm đầu cột chính.

**Bốn lỗi chỉ lộ ra ở bước ④ — bắn thật vào UI đang chạy:**

| Lỗi | Vì sao test không bắt được |
|---|---|
| Icon *Nghe chép chính tả* đọc ra thành **dấu hỏi** | Hình cái tai ở 22px trông giống `?`. Không có xác nhận nào sai; chỉ nhìn ảnh chụp mới thấy. Đã đổi sang dạng sóng âm trên một dòng kẻ |
| Câu dẫn của mục nằm **cạnh** tiêu đề thay vì dưới | `flex-basis: 100%` không ép xuống dòng khi `max-width` đã kẹp kích thước giả định của item. Đã đổi sang grid với hàng thứ hai gọi tên |
| Rail gập lại làm **mất tên** của sáu nút icon | `display: none` bỏ nhãn khỏi cả accessibility tree, không chỉ khỏi mắt. Đã đổi sang ẩn-nhìn-thấy + `title` cho chuột. Có test mới khoá lại |
| Viền gạch đứt dùng cho **cả** trạng thái rỗng lẫn nhãn AI | Làm loãng đúng chỗ duy nhất nó mang nghĩa (L4). Trạng thái rỗng chuyển sang viền liền + nền lõm |

> **Cái đáng ghi nhất:** ba trong bốn lỗi trên nằm ở lớp *trình bày*, nơi 65 test xanh không nói gì
> cả. Đây đúng là bài học đã ghi ở giai đoạn A — và nó lặp lại y hệt.

**Còn nợ trên trang này**, có chủ đích, không phải quên: mọi lối vào đang ở trạng thái *Chưa có đề*
vì chưa có API đề (giai đoạn B). Ngày `GET /exams` chạy được thì việc phải làm là **gắn dữ liệu vào
các thẻ đã có**, không phải dựng lại trang.

---

### Giai đoạn A4 — Refactor trang hồ sơ học sinh — ✅ **XONG 21/08/2026**

Chủ sản phẩm gửi một bản review 19 mục cho `/profile`. Chẩn đoán chính xác và không phải về tính năng:
trang đọc ra như **màn quản lý tài khoản của một LMS**, không như hồ sơ của một sản phẩm luyện IELTS.

| # | Việc | Cổng đã qua |
|---|---|---|
| A4-1 | **Bỏ hẳn dải xanh 160px**, không phải thu nhỏ | Chủ sản phẩm cho phép giữ ở 70–100px. Bỏ hẳn vì một mảng màu thương hiệu **không mang thông tin** thì thu nhỏ chỉ còn là phiên bản nhỏ hơn của cùng một lỗi. Xanh giờ chỉ còn ở eyebrow, tab đang chọn, thanh tiến độ và nút chính |
| A4-2 | **Khoảng trống là do cấu trúc, không do thiếu nội dung** | Thẻ trái cao cạnh cột phải ngắn tạo lỗ hình chữ L; `min-height: 100vh` kéo nốt phần còn lại thành một màn hình trống. Cân lại hai cột đóng được lỗ mà **không cần thêm gì để lấp** |
| A4-3 | **Thẻ hồ sơ giảm 623 → 470px (−24,6%)** | Chỉ tiêu chủ sản phẩm đặt là 25–30%. Bỏ bốn thứ tự lặp lại: hai pill xếp chồng, dòng "Tài khoản VNI IELTS AI", tiêu đề "Thông tin cá nhân" bên trong một thẻ vốn đã toàn thông tin cá nhân, và alert nhắc lại đúng cái tag cách đó hai dòng |
| A4-4 | **Tách nhóm điều hướng** — `Tài khoản`(Bảo mật · Thiết bị) / `Học tập`(Tiến độ học tập) | Chuyển từ tab ngang sang **nav dọc có tiêu đề nhóm**: một dải ngang chỉ *ngụ ý* được sự khác nhau, tiêu đề thì *nói ra*. `?tab=progress` giữ nguyên — rail dashboard trỏ vào đó và `/progress` chuyển hướng về đó |
| A4-5 | **Khối `Mục tiêu IELTS` + `Bốn kỹ năng`** | Phần trả lời nửa sau câu hỏi của trang (`§15` trong brief). Đây cũng là thứ lấp chỗ trống bằng **nội dung thật của trang**, không phải bằng thẻ trang trí |
| A4-6 | 5 test mới · 83 test toàn app | Một test khoá lại đúng điều dễ mất nhất: khối mục tiêu **không được chứa một chữ số nào** |
| A4-7 | Đo trên trình duyệt thật ở 390 / 834 / 1440px | 49 phần tử chữ: **0 dưới sàn 14px · 0 ngoài thang đóng · 0 `line-height` dưới 1.5 · 0 uppercase · 0 cặp dưới 4.5** (thấp nhất 4,72) · 0 vùng chạm dưới 44px · 0 cuộn ngang |

**Ba chỗ brief và ràng buộc repo mâu thuẫn — đã chọn theo ràng buộc và ghi lại lý do:**

| Brief yêu cầu | Đã làm | Vì sao |
|---|---|---|
| Xanh chính `#58CC02` | Giữ `#10b050` | `§4` của chính brief yêu cầu *"giữ visual language đang dùng ở các trang VNI hiện tại"*, mà toàn bộ landing/auth/dashboard đang chạy `#10b050`. `#58cc02` đến từ `redesign/vni-ielts-home-redesign.css` — file `DESIGN.md` đã ghi là **bản cũ, không được HTML tham chiếu** — và trùng đúng màu Duolingo, thứ `§18` cấm. Đổi cả sản phẩm chỉ cần sửa **một dòng** `--pf-green` |
| Page title 36–40px · caption 11–12px | 32px · sàn 14px | Thang cỡ chữ là thang đóng, 36/40 không có trong thang. 11–12px vi phạm sàn tiếng Việt — và mâu thuẫn với chính yêu cầu *"text footer đang bị nhỏ tăng lên"* của chủ sản phẩm cùng ngày |
| Band 6.3 → 7.0 · R 78% L 64% W 81% S 62% · `+40 XP` · `Gói đã mua` | Tất cả là `—` · không có XP · không có gói | Chưa có engine thi, chưa có lần làm bài nào, chưa có endpoint tiến độ. Luật L3 và chính `§18` của brief (*"không thêm chart nếu không có data"*) đều cấm. `Gói đã mua` còn kéo theo mô hình thương mại mà `B-4`/`B-5` chưa chốt |

**Một mục cố ý không dựng: `Activity` / hoạt động gần đây.** `§15` của brief nói mỗi trang trả lời đúng
một câu hỏi và Profile **không được thành dashboard thứ hai** — mà "Kết quả gần đây" thì dashboard đã có.
Thêm một bản rỗng thứ hai ở đây là tăng chiều cao để lấp chỗ trống, đúng thứ `§1.1` và `§18` cấm.

> **Ghi chú về test:** `verify-realtime.test.tsx` (do phiên khác thêm) thỉnh thoảng trượt trong lần chạy
> đầy đủ — đúng hiện tượng mà chính comment của test mô tả: nó chờ một thông điệp qua `BroadcastChannel`.
> Chạy riêng và 4 lần chạy đầy đủ liên tiếp đều xanh. Không liên quan tới đợt refactor này.

---

### Giai đoạn A5 — Dashboard học sinh có shell riêng — ✅ **XONG 21/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 21/08/2026**, hai lượt:

> *"phần /students/dashboard trình bày theo dạng dashboard ko có menu luôn dashboard hết và hãy bỏ
> các phần của hồ sơ cá nhân đi ko nên để thêm chung vào nữa"*
>
> *"dạng dashboard là sidebar bên trái nội dung bên phải và full như trang mới không có menu header cơ mà"*

**Lượt đầu mình hiểu sai "ko có menu" thành bỏ sidebar**, và gỡ luôn rail. Câu thứ hai làm rõ: *menu*
ở đây là **menu trên header**, còn sidebar thì phải có. Đã dựng lại đúng hình:
**sidebar trái · nội dung phải · không header menu**.

| # | Việc | Cổng đã qua |
|---|---|---|
| A5-1 | **`DashboardShell` — chrome riêng cho khu vực học sinh** | Tuyến layout riêng, không phải cờ điều kiện trong `LearnerShell`: hai shell chỉ dùng chung mỗi menu tài khoản, mà một shell render hai header tuỳ điều kiện là **hai shell đội một tên** |
| A5-2 | **Bỏ menu header** — các link trỏ vào neo marketing trên `/` | Học viên đang đăng nhập cần điều hướng của **ứng dụng**, không phải của tờ rơi. Chạy cả hai là xếp hai hệ điều hướng lên một màn |
| A5-3 | Hai thứ sống sót từ header: **chuông thông báo + menu tài khoản** | Menu tài khoản là **đường duy nhất tới đăng xuất** — bỏ nốt để "đúng chữ không header" là nhốt người dùng trong app. Nó cũng là chỗ **link tới hồ sơ thuộc về**, nên thân trang không phải mang nữa |
| A5-4 | Logo VNI chuyển vào đầu sidebar, vẫn trỏ `/` | Bỏ header là bỏ mất dấu hiệu thương hiệu duy nhất và đường duy nhất về trang công khai |
| A5-5 | Sidebar **không có mục nào của hồ sơ** | Bốn mục đều là neo trong trang. *"Hồ sơ học sinh"* và *"Theo dõi"* của bản trước đã bỏ hẳn |
| A5-6 | Gập/mở sidebar, nhớ qua `localStorage` | Gập lại thì nhãn **ẩn khỏi mắt nhưng còn trong accessibility tree**, kèm `title` cho chuột. Logo đổi sang bản crop favicon — lockup đầy đủ ở 72px là một vệt mờ |
| A5-7 | 11 test dashboard | Ba test khoá lại: sidebar đúng bốn mục · **0 thẻ `<a>` bên trong `.dash`** · nhãn còn tên khi gập |
| A5-8 | Đo lại ở 390 / 1440px, cả hai trạng thái gập | 58 phần tử chữ: **0 dưới sàn 14px · 0 ngoài thang đóng · 0 `line-height` dưới 1.5 · 0 uppercase · 0 cặp dưới 4.5** (thấp nhất 4,72) · 0 vùng chạm dưới 44px · 0 cuộn ngang |
| A5-9 | **Trên điện thoại sidebar thành drawer sau nút ba gạch** | Chủ sản phẩm: *"reposive mobile của trang đó chưa oke để dạng dấu 3 gạch chứ"*. Ba đường đóng: Escape · bấm nền mờ · chọn một mục. Khoá cuộn nền, trả focus về nút ba gạch |
| A5-10 | **Hỏi đáp AI thành một module trong danh sách; chỗ cũ thành *Quay lại trang chủ*** | Chủ sản phẩm: *"để hỏi đáp với ai thành 1 module đi và chuyển phần đó thành quay lại trang chủ"*. Nó vẫn là `<button>` mở panel chứ không phải tuyến — hội thoại nằm **cạnh** việc đang làm, không phải nơi để đi tới. Đặt cạnh *Luyện tập* vì hai cái đó là thứ mình **làm**; kết quả và sắp mở là trạng thái. **86 test toàn app** |

**Lối ra nằm ngoài `<nav>` có chủ đích:** *Quay lại trang chủ* không phải một module, nó là **cửa ra**.
Logo ở đầu sidebar dẫn tới cùng chỗ, nhưng logo là quy ước người dùng phải biết trước, còn một dòng có
nhãn thì không.

**Vì sao là drawer chứ không phải dải ngang:** bản đầu cho sidebar xuống dòng thành một dải ngang, và
nó đặt **logo cộng bốn nút phía trên lời chào** — đẩy phần việc thật ra khỏi màn hình đầu tiên, trên
đúng trang có nhiệm vụ trả lời *"giờ mình làm gì"*. Nút ba gạch tốn một lần chạm và **không tốn gì phía
trên nếp gấp**.

**Một lỗi thứ hai, chỉ lộ khi kiểm bằng bàn phím:** `transition: visibility` là thuộc tính rời rạc nên
nó lật ở **giữa** hiệu ứng trượt — lúc effect gọi `focus()` thì drawer vẫn đang `hidden`, mà trình
duyệt từ chối focus vào phần tử ẩn. Focus im lặng nằm lại `<body>` và người dùng bàn phím bị kẹt. Sửa
bằng `visibility 0s var(--dur)` khi đóng và `0s 0s` khi mở: hiện ngay lúc vào, ẩn sau khi trượt xong.

**Một lỗi chỉ lộ khi cuộn thật:** cột trắng của sidebar **dừng đúng một màn hình** rồi để lộ nền xám
bên dưới — `position: sticky` + `height: 100vh` đặt thẳng trên `<aside>`. Cách sửa là tách làm hai:
`<aside>` giãn hết chiều cao lưới để mặt nền liền mạch, phần **bên trong** mới là phần dính.

**Một mục giữ lại, nói rõ để chủ sản phẩm phán quyết nếu không đồng ý:** dải cảnh báo *"Email chưa được
xác minh"*. Nó nói về tài khoản, nên có thể xem là *"phần của hồ sơ cá nhân"* — nhưng đây là **lời gọi
hành động về trạng thái kích hoạt**, và chỗ thao tác thật (nút gửi lại) nằm ở `/profile`. Bỏ nốt ở đây
thì người không bao giờ mở hồ sơ sẽ không biết mình cần xác minh.

---

### Giai đoạn A6 — Mỗi module một trang + menu tự tràn — ✅ **XONG 21/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 21/08/2026**, hai ý trong một câu:

> *"mỗi 1 module là 1 trang, ví dụ tài liệu 1 trang riêng để học sinh tải tài liệu, bài viết cũng 1
> trang riêng chứ không phải để dạng SPA"*
>
> *"mục thêm chỉ dành cho là khi menu bị thiếu responsive mới thành thêm chứ bình thường đủ thì cứ
> hiển thị đầy đủ ra"*

Đã ghi thành `N-1` và `N-2` trong [`../requirements/confirmed.md`](../requirements/confirmed.md).

**Ý cốt lõi của ý thứ nhất: một module không có địa chỉ thì không tồn tại.** Tài liệu và bài viết trước
đó là hai khối cuộn trên trang chủ — không đánh dấu được, không gửi link cho ai được, không có gì cho
máy tìm kiếm đáp xuống, và **số tài liệu tồn tại đúng bằng số thẻ có chỗ để bày**.

| # | Việc | Cổng đã qua |
|---|---|---|
| A6-1 | **`/documents`** — kho tài liệu: tìm kiếm, lọc theo kỹ năng, danh sách dạng dòng | Dòng chứ không phải lưới thẻ: chọn tài liệu là **so dòng này với dòng kia** (tên · định dạng · dung lượng), mà so thì cần cột thẳng hàng |
| A6-2 | **`/articles`** + **`/articles/<slug>`** | Slug là địa chỉ, không phải id. Slug lạ trả **404**, không phải trang trắng — link cũ phải tự nói là nó cũ |
| A6-3 | Trang chủ giữ **3 thẻ mỗi loại** kèm đường dẫn sang trang đầy đủ | Xem trước, không phải module |
| A6-4 | `SiteHeader` / `SiteFooter` / `PublicShell` — **một bản header cho mọi mặt công khai** | Trước đó có **hai bản chép tay** và chúng đã trôi lệch: `LearnerShell` vẫn quảng cáo *"Lộ trình"*, mục mà trang chủ đã xoá theo `H-1` — link cuộn tới hư không |
| A6-5 | **Nút tải về nói thật** | Chưa có endpoint tài liệu và chưa có CMS đăng file, nên mọi mục hiện **"Sắp có"** thay vì một nút 404. `fileUrl` được mô hình hoá là **tuỳ chọn**: khi CMS đăng file, mục đó thành nút tải thật mà không phải sửa trang |
| A6-6 | **Ảnh bìa bài viết vẽ bằng CSS**, bỏ ảnh từ CDN bên thứ ba | Bản thiết kế tải ảnh chụp từ một CDN ngoài trên **từng thẻ**: một request sang máy chủ công ty khác, một phụ thuộc mạng nằm ngoài tầm kiểm soát, và ảnh người lạ minh hoạ tài liệu IELTS tiếng Việt |
| A6-7 | **`OverflowNav` — đo, không phải đoán** | Bề rộng nhãn đổi theo ngôn ngữ giao diện, theo font và theo mức zoom của người đọc, nên **không breakpoint nào biết được bao nhiêu mục vừa**. Một bản sao ẩn của hàng (`visibility: hidden`, ngoài accessibility tree) giữ bề rộng tự nhiên — vì mục đã gập vào menu thì không còn trong hàng để đo lại, và lần gập đầu tiên sẽ thành vĩnh viễn |
| A6-8 | Header trên điện thoại của khách: bỏ *"Đăng nhập"* khỏi hàng dưới 640px | Brand + hai nút + ba gạch đo khoảng 510px. Trên màn 390px chữ trong nút vỡ làm hai dòng bên trong viên thuốc chỉ cao một dòng. *"Đăng nhập"* vẫn là dòng cuối trong panel ba gạch |
| A6-9 | **104 test toàn app**, thêm `OverflowNav.test.tsx` (bề rộng được giả lập) và `module-pages.test.tsx` | jsdom không có layout, nên phần số học của việc gập được kiểm **thẳng vào component** với bề rộng khai báo rõ. Việc nhãn thật rộng bao nhiêu là câu hỏi cho trình duyệt |
| A6-10 | Đo lại trên Chrome ở **1440 / 1050 / 390px** | 1440: đủ 5 mục, **không có "Thêm"**. 1050: gập đúng 2 mục cuối. 390: hàng ẩn, ba gạch mở đủ 5 mục + *"Đăng nhập"*. **0 lỗi console** |

**`N-3` là quyết định của mình, không phải của chủ sản phẩm:** hai trang này **công khai**, không sau
cổng đăng nhập. Kho tài liệu chính là thứ khách đang cân nhắc, và `M-23`/`M-24` mô tả việc *đọc và
tải*, không mô tả một quyền phải mua. Nếu chủ sản phẩm muốn chặn, đó là sửa một dòng ở tuyến.

**Nội dung trong `documents.ts` và `articles.ts` là dữ liệu mẫu**, đứng thay cho phản hồi API sau này.
Tên trường đúng tên bản ghi thật, nên thay bằng `fetch` là sửa một dòng `import` chứ không phải viết
lại trang. Không có lượt tải, không có đánh giá, không có *"tải nhiều nhất"* — đó là những con số chưa
ai đo.

---

### Giai đoạn A8 — Bốn module trên header + dựng lại trang chủ — ✅ **XONG 24/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 24/08/2026**, hai yêu cầu:

> *"các module ở menu header hiện tại cần 4 mục chính (luyện 4 kĩ năng, nghe chép chính tả, tài liệu,
> bài viết). mỗi module này sẽ đảm nhiệm 1 trang khác nhau chứ không ở 1 trang dạng spa nữa"*
>
> *"về trang chủ mình đánh giá chưa chuyên nghiệp giao diện để thừa khoảng trắng quá nhiều, cần bổ
> sung các nét vẽ thêm sinh động hơn, các hình ảnh cho bài viết"* — kèm brief bố cục cho khối
> **Dành cho học sinh** và yêu cầu sửa logo kênh ở khối **Luôn có người đồng hành**.

Cùng luật với `N-1` ở A6, mở rộng cho module thứ tư.

**Chỉ có nghe chép chính tả phải di chuyển, và lý do đáng ghi lại:** ba module kia đã có địa chỉ công
khai từ A6. Nghe chép chính tả nằm ở `/students/dictation`, **sau cổng đăng nhập, trong shell
dashboard** — nên đưa nó lên header công khai là đưa một cái link dẫn tới bức tường. Đây đúng là lỗi
`/practice` đã mắc và đã sửa ngày 22/08, lặp lại ở module cuối cùng.

| # | Việc | Cổng đã qua |
|---|---|---|
| A8-1 | **`/dictation`** — trang công khai, `/students/dictation` redirect sang | Bookmark cũ vẫn chạy. Trang đọc được khi chưa đăng nhập; **chỉ khối làm bài** đòi token, tự dựng thẻ mời đăng ký — cùng kiểu `PraticeExamPicker` trên `/practice`, không phải kiểu thứ hai |
| A8-2 | Header **và footer** liệt kê đủ bốn, tất cả là route | Footer liệt kê ba trên bốn là chỗ người ta kết luận cái thứ tư không tồn tại. `OverflowNav` đo lại: 1440 đủ bốn, ~990 gập hai mục cuối vào **Thêm**, dưới 980 vào panel ba gạch |
| A8-3 | **`StudentsSection`** — dải nền tối, sóng mềm hai đầu, 1 thẻ nổi bật + lưới 2×2 + hàng dưới | Thứ hạng thị giác **đo được**, không phải cảm giác: heading 15.15 ▸ thẻ nổi bật 7.58 ▸ lưới 1.41 ▸ thẻ tiến độ 12.91 ▸ thẻ gợi ý 2.38 so với nền. Bản đầu để thẻ tiến độ trắng tinh (15.15) và thẻ gợi ý cyan đặc (8.78) — cả hai **át** thẻ nổi bật mà chúng phải đứng dưới |
| A8-4 | **Ảnh bìa bài viết** — 5 bố cục SVG vẽ tại chỗ, 5 tông, chọn theo vị trí trong catalogue | Không request ra CDN ngoài, không ảnh người lạ (giữ nguyên luật A6-6). Chọn theo vị trí chứ không theo hash slug: hash đụng 2/3 ngay lần đầu. Tông cũng theo vị trí chứ không theo chuyên mục — ba bài trang chủ preview **đều là `huong-dan`**, nên theo chuyên mục là ba thẻ cùng một sắc xanh |
| A8-5 | **Logo kênh thật** ở khối đồng hành | Ba ô đang là `▶`, `✦`, `✱` — một glyph play và hai ký tự trang trí — trong khi footer cách đó ba mét đã dùng mark đúng từ `BrandIcons` |
| A8-6 | Bớt khoảng trắng, thêm nét vẽ | Section 96→84px; heading cap 53→44px + `text-wrap: balance` (hết cảnh xuống dòng còn trơ một chữ: *"lại."*, *"luyện."*, *"chấm."*); lưới chấm + ánh sáng mềm dưới hai section; nét gạch chân vẽ tay dưới **một** heading; **rail nối ba mốc** ở khối "Về VNI" — comment trong CSS mô tả cái rail này từ lâu mà chưa ai vẽ |
| A8-7 | **Tiêu đề trang riêng cho từng route** | Năm trang cùng trả `document.title === "VNI IELTS AI"`: năm tab giống hệt, lịch sử đọc như một mục lặp năm lần. A6 và A8 tách chúng thành trang riêng thì chúng phải phân biệt được trong dải tab |
| A8-8 | **111 test xanh**, thêm 3 test module-pages và 3 test `hero-panel` | Bao gồm: `/dictation` công khai kèm thẻ mời đăng ký, redirect địa chỉ cũ, và header liệt kê đúng bốn cặp nhãn→đường dẫn |
| A8-9 | **Rà soát thiết kế bằng agent trước khi trình chủ sản phẩm** | 33 phát hiện. Đã sửa 31; `B-9` chuyển cho chủ sản phẩm và được chốt trong ngày. Còn 2 ghi lại có lý do — xem ngay dưới |

**Rà soát bắt được hai lỗi cùng một họ, và đó là điều đáng nhớ nhất của đợt này.** `reset.css` đặt
`color: var(--ink)` **thẳng lên** mọi thẻ heading, và một giá trị khai báo thắng giá trị thừa kế —
nên mọi `h3` trên nền tối là chữ gần-đen trên nền gần-đen. Trong `.stu` việc này đã được lường trước
và ghi vào comment; **thẻ Zalo ở khối đồng hành ngay dưới đó đo được 1.06:1** — chữ có trong DOM và
không có trên màn hình. Cùng cái bẫy, cách nhau một section, và cái comment cảnh báo không cứu được
cái ở ngoài phạm vi nó.

**Ba thứ khác chỉ lộ ra khi đo, không lộ khi nhìn:**

| Phát hiện | Vì sao mắt không bắt được |
|---|---|
| **29 chuỗi tiếng Việt dưới ngưỡng 14px**, thấp nhất 9px | Trên màn Retina 9px vẫn "đọc được" nếu biết trước nó viết gì. Dấu thanh chồng của *"kỹ năng"*, *"phản hồi"* mới là thứ vỡ, và người đọc lần đầu là người chịu |
| **21 cặp màu chữ dưới 4.5** trên riêng khối hero | Phần lớn nằm trong khoảng 3.3–4.4 — vùng "trông vẫn ổn". Chỉ có máy tính mới phân biệt được 4.38 với 4.5 |
| **Vòng focus cyan trên thẻ sáng đo 1.73** | Nó được thêm vào để *cải thiện* focus, và với hai nút trên nền tối thì đúng (8.78). Cùng một dòng CSS đó áp lên link nằm trong thẻ sáng thì **tệ hơn** mặc định nó thay thế (5.79) |

**Ba việc rà soát nêu mà chưa làm, có lý do:**

- **Font Google tải từ `fonts.googleapis.com`** — có từ trước, nhưng đáng nhắc lại ở đây vì `B-2`
  (lập trường PDPL xuyên biên giới) đang chặn phát hành: mỗi lần tải trang là một IP học viên Việt
  Nam gửi sang Google. Tự host là việc riêng, kèm giấy phép font và một bước build.
- **`/documents` dùng `<h2>` cho cả tiêu đề section lẫn tiêu đề thẻ** — mười `<h2>` 16px trong một
  cấu trúc phẳng. Là lỗi có thật, nhưng nằm ngoài phạm vi task này và có test đang truy vấn
  `.doc-row h2`.
- ~~**Khối hero của trang chủ đang in số bịa**~~ — chủ sản phẩm đã chốt trong ngày và đã sửa. Xem
  `B-9` ngay dưới.

**`B-9` — hero trang chủ in 11 con số không có nguồn — ✅ đã chốt và đã sửa cùng ngày.**

Khối hero in `Band 8.0` · `Band 7.5` · `Band 7.0` · `Dự đoán Band 4.5 – 8.5` · `Độ chính xác 98%` ·
`Phản hồi trong < 3 giây` · `Sẵn sàng 24/7` · `100% học theo mục tiêu` · `40 câu · Đề Cam 19 mới
nhất` · **`Chuẩn IDP / BC`**. Chúng đến từ bản redesign đã chốt, port nguyên văn 21/08, có gắn cờ
trong doc comment chứ chưa xử lý. Cùng trang đó, cách 800px, khối **Dành cho học sinh** dán nhãn một
hình vẽ là *"không phải dữ liệu của bạn"*, và `/practice` in hẳn *"Không có con số nào được bịa"*.

`Chuẩn IDP / BC` nặng hơn một con số: đó là tuyên bố chuẩn hoá theo hai đơn vị tổ chức IELTS chính
thức, không có căn cứ nào trong repo. `Phản hồi trong < 3 giây` mâu thuẫn với chính mô hình chấm bất
đồng bộ của sản phẩm, và `Band 8.0` cạnh `Reading Test Suite` mâu thuẫn với luật `A-11`.

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 24/08/2026:**

> *"phần này mình có thể làm cho 2 trạng thái chưa login và đã login. khi chưa login thì mình để gì
> đó làm đẹp cũng được. Khi đã login thì mình thay tên của user và thay số liệu thật trông sẽ oke
> hơn"*

**Và đó là lời giải tốt hơn cả hai phương án "xoá" hoặc "giữ".** Vấn đề không phải là khối hero có
số — mà là nó có số *của không ai cả*. Khách chưa đăng nhập thì không có số nào để hiển thị, nên họ
nhận một khối mô tả phòng thi: bốn kỹ năng, cách chấm từng kỹ năng, và chip `Chưa làm` — đúng trạng
thái của người chưa làm gì. Người đã đăng nhập thì **có** số thật, nên họ nhận số của chính mình.

| # | Việc | Cổng đã qua |
|---|---|---|
| B9-1 | `HeroPanel` — hai trạng thái, thay toàn bộ markup cũ của `.hero-visual` | Không còn chuỗi nào khớp `Band \d`, `\d+%`, `\d+ giây`, `24/7`, `IDP`, `Cam \d+` trong khối hero khi chưa đăng nhập |
| B9-2 | Trạng thái đã đăng nhập: tên người dùng + band gần nhất **từng kỹ năng** | Lấy `GET /api/v1/sessions`. **Gần nhất, không phải trung bình** — các buổi là các đề khác nhau, trung bình cộng band giữa chúng không mô tả cái gì. Buổi đang dở quá hạn **không** được mời "tiếp tục": đồng hồ do máy chủ giữ và nó không dừng lại chờ (ADR-0007) |
| B9-3 | Kỹ năng chưa chấm hiện `—`, không hiện `0.0` | Band 0 là band có thật mà người không trả lời gì thực sự nhận được — đó chính là lý do band vắng mặt không được mượn hình dạng của nó. Luật sản phẩm `L3` |
| B9-4 | Lỗi mạng → quay về khối của khách, không phải màn báo lỗi | Đây là đỉnh trang chủ. Người không tải được lịch sử vẫn phải thấy sản phẩm là gì |
| B9-5 | Hàng ba số dưới hero cũng sửa | `AI · phản hồi tức thì` là cam kết tốc độ mà `M-8` chưa chốt; `100% · học theo mục tiêu` là phần trăm của không gì cả. Thay bằng hai điều `/practice` đã nói: miễn phí không giới hạn lượt, và R/L chấm theo đáp án |
| B9-6 | **3 test mới** trong `hero-panel.test.tsx` | Test đầu quét chính khối hero đã render và **từ chối mọi chuỗi có hình dạng của một lời tuyên bố** — không phải khớp chuỗi cứng, nên một phiên bản viết lại cùng nội dung cũng trượt |

**Điều đáng nhớ:** không có gì trong hệ thống kiểu ngăn ai đó dán một con số trở lại trong một lượt
sửa copy. Cái ngăn được là một test đọc DOM đã render và bắt theo *hình dạng*.


---

### Giai đoạn A9 — Dựng lại `/practice` — ✅ **XONG 24/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 24/08/2026** — brief chi tiết theo layout tham chiếu (Edly),
được diễn giải lại chứ không sao chép: giữ tinh thần bố cục và luồng, đổi hệ màu/typography/nội dung.

**Vấn đề cốt lõi không phải thẩm mỹ mà là cấu trúc.** Trang cũ là một trang marketing với cái picker
nhét gần đầu — khoảng 4/5 chiều cao là phần thuyết phục, phần luyện tập chỉ là một dải mỏng. Học sinh
đến để làm đề Reading gặp một tấm poster, rồi một control panel, rồi năm section thuyết phục nữa.

| # | Việc | Cổng đã qua |
|---|---|---|
| A9-1 | **`PracticeWorkspace`** thay `PracticeExamPicker`: chọn kỹ năng · chuyển chế độ · lọc · lưới thẻ · phân trang | Chọn kỹ năng và chế độ nằm trong URL (`?skill=&mode=`), không nằm trong URL là lựa chọn lọc — chia sẻ link không kéo theo bộ lọc riêng của người gửi |
| A9-2 | **`practiceCatalogue.ts`** — bộ lọc dựng từ facet mà catalogue trả lời được (loại đề, thời lượng), không bịa Band/Question type/Topic/Difficulty | `ExamCatalogueItem` không có bốn field đó và CMS chưa có màn để gắn — bịa nghĩa là bốn control không lọc được gì, tệ hơn không có. Ghi thành seam `FACET_SEAM`: field nào xuất hiện trong data thì bộ lọc tự hiện, không sửa component |
| A9-3 | Thứ hạng thị giác đảo ngược — sửa bằng type scale | Bản đầu để heading workspace 26px trong khi năm heading SEO bên dưới 40px và CTA cuối 45px — thang chữ, tín hiệu mạnh nhất trên trang, đang chạy ngược. Giờ heading workspace 30–33px, SEO 24–30px, CTA cuối 22–25px |
| A9-4 | Hero rút ngắn thật | Đo được 449–712px tuỳ độ rộng, không thẻ luyện tập nào lọt màn hình đầu trên laptop. Bớt padding, bỏ hẳn hình minh hoạ dưới 980px (nó vẽ lại chính cái workspace nằm 400px phía dưới) |
| A9-5 | **111→113 test xanh**, viết lại 2 test theo markup mới, thêm test đếm bộ lọc không nói dối | `tsc` sạch, build 1.55s |
| A9-6 | **Rà soát thiết kế bằng agent trước khi trình chủ sản phẩm** | 33 phát hiện qua hai vòng (trang chủ 24/08 sáng, `/practice` 24/08 chiều). Đã sửa hết phần có thể sửa trong phạm vi UI |

**Hai lỗi rà soát bắt được đáng nhớ nhất, vì cả hai đều là chỗ comment trong code khẳng định đã sửa
mà trang chạy thật chứng minh chưa:**

| Lỗi | Vì sao |
|---|---|
| **Bộ đếm bộ lọc nói dối đúng lúc quan trọng nhất** | Tick "General Training" (2 kết quả) thì "Dưới 20 phút" vẫn hiện đếm 2 — bấm vào thì ra 0. `buildFacets` đếm trên danh sách chỉ lọc theo kỹ năng+chế độ, chưa lọc theo *nhóm facet khác*. Nhánh code viết để chặn việc này (disable ô đếm 0) không bao giờ chạy được, vì đếm luôn ≥1 theo cách dựng |
| **Phân trang xoá mất focus bàn phím** | Trang hiện tại render bằng `<span>`, không phải nút — bấm sang trang khác là unmount đúng cái đang giữ focus. Cùng họ lỗi với nút bị disable ở hai đầu: chạm biên là disable chính cái vừa bấm. Sửa bằng `aria-disabled` + handler chặn thay vì `disabled`/`<span>` — nút vẫn ở trong tab order |

**Việc khác đã sửa:** selector giữ trạng thái "đã chọn" giả khi ở chế độ thi thử full (không skill nào
đang quyết định gì); `role="tablist"` không có tabpanel nào trên trang; state URL bị ghi đè toàn bộ
thay vì merge (`?utm_source=fb` mất ngay lần bấm đầu); card thi thử full lặp một câu 24 từ y hệt (đổi
sang breakdown phút từng kỹ năng — dữ liệu thật, không bịa); số phút set nhầm font monospace lẫn với
chữ Việt; anchor `#work`/`#how` không di focus theo và bị header sticky che khuất; breadcrumb tự trỏ
vào chính trang đang đứng.

---

### Giai đoạn A10 — Dựng lại `/dictation` — ✅ **XONG 24/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 24/08/2026** — brief chi tiết theo một layout tham chiếu là **thư viện
bài nghe chép chính tả** có tìm kiếm, lọc theo band/level/topic/difficulty, phân trang, và thẻ mang
thời lượng audio + tiến độ cá nhân.

**Trang cũ là một trang chi tiết đội lốt địa chỉ thư viện.** `/dictation` render thẳng bài tập với
`sets[0]` — bộ nào máy chủ sắp trước thì học sinh làm bộ đó. Đúng khi kho có một bộ, sai ngay khi có
hai: không chọn được, không gửi link cho ai được, và không trả lời được câu "tôi đang làm bài nào".
Bài tập giờ ở `/dictation/:setId`, còn `/dictation` là đường tới nó.

| # | Việc | Cổng đã qua |
|---|---|---|
| A10-1 | **`/dictation`** — thư viện: tìm kiếm, lọc chip ngang, lưới 2 cột, phân trang | Tìm kiếm bỏ dấu qua `fold` — "cau hang ngay" ra "Câu hằng ngày". Nửa số truy vấn tiếng Việt gõ không dấu |
| A10-2 | **`/dictation/:setId`** — bài tập, và chỉ bài tập | Không section giáo dục, không FAQ, không CTA. Người ở đây đã chọn xong. `DictationPractice` nhận `setId` thay vì tự lấy `sets[0]` |
| A10-3 | **`dictationCatalogue.ts`** — facet dựng từ dữ liệu thật | Hôm nay chỉ ra được **Độ dài** (suy từ `sentenceCount`). Đếm của mỗi nhóm đo trên tập đã lọc bởi *nhóm khác* — đúng luật `/practice` học được, nên không có chuyện hứa N trả 0 |
| A10-4 | 3 component dùng chung ra `features/chrome/` | `FaqAccordion`, `Pagination`, `jumpToSection` — `features/dictation` import từ `features/exam/practice` là một phụ thuộc chéo không nên có |
| A10-5 | **119 test xanh**, thêm `dictation-library.test.tsx` (5 test) | Gồm test quét khối thư viện và **từ chối mọi chuỗi có hình dạng band / phần trăm / `mm:ss` / level** |
| A10-6 | Sửa va chạm tên class do chính mình gây ra | `.dict-card`, `.dict-chip`, `.dict-note` đã thuộc về `dictation.css` (panel bài tập). Hai stylesheet cùng ship trên mọi trang không thể cùng định nghĩa. Đổi nhóm mới sang `dset-*` |

**Va chạm class là lỗi đáng nhớ nhất của đợt này.** `.dict-card` của panel bài tập đặt
`flex-direction: column`; thẻ thư viện mới cũng tên `.dict-card` và cần `row`. Vite gộp mọi CSS nên
cả hai luật cùng có mặt trên mọi trang, và luật kia thắng — nút "Bắt đầu" rơi xuống dưới thay vì nằm
bên phải. Không có test nào bắt được, vì jsdom không tính layout; chỉ nhìn trình duyệt mới thấy.

> **⚠ `B-10` — thư viện bài nghe hiện là khung, vì chưa có dữ liệu để đổ vào.**
>
> Một `DictationSet` mang đúng bốn thứ: `Id`, `Title`, `Description`, `Sentences` — trong API view,
> trong record domain, và trong định dạng fixture. `fixtures/dictation` có **một file, sáu câu**,
> audio sinh bằng `say` của macOS chứ không phải giọng thu thật.
>
> Nên trong những gì brief yêu cầu: **topic, level, difficulty, thời lượng audio** không có nguồn ở
> bất kỳ tầng nào, và **tiến độ / độ chính xác cá nhân** cũng không — `checkSentence` so sánh rồi trả
> về, không lưu gì cả, không có bảng attempt nào. Chúng được ghi thành `FACET_SEAM`: thêm field vào
> API là bộ lọc tự hiện, không sửa component nào.
>
> **`band` thì khác — nó bị từ chối chứ không phải đang chờ.** Record domain nói thẳng: nghe chép
> chính tả không có timer, không có phiên, **không có band**, không có entitlement. Một chip "Band
> 6.0+" ở đây không phải một ô trống chờ dữ liệu, mà là khẳng định một chiều chấm điểm mà tính năng
> này cố ý không có. Nên mục "lộ trình" trên trang chia theo **cách luyện** (nghe thoải mái → giảm số
> lần nghe → nghe một lần) chứ không chia theo band.
>
> **Cần chủ sản phẩm quyết:** kho bài nghe cần bao nhiêu bộ, gắn những metadata nào, và ai soạn. Đây
> là việc của CMS (`T6`) — hiện chưa có màn nào tạo được một bộ câu.

---

### Giai đoạn A11 — Rà soát toàn bộ giao diện + sửa lỗi tận gốc — ✅ **XONG 26/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 26/08/2026**: *"cần một đội ngũ Design + test review lại tất cả giao
diện một lượt nữa mọi ngóc ngách… dò lỗi bug, warning để triển khai fix triệt để trước khi dự án
phình to ra… hoàn thiện giao diện tất cả các trang đạt 100% product"*.

Bốn agent duyệt độc lập, không chồng lấn: landing + auth · `/practice` + `/dictation` + phòng thi ·
`/articles` + `/profile` + dashboard + chrome · rà code tĩnh. **Mọi phát hiện đều được kiểm chứng
lại trước khi sửa** — và một trong các "blocker" là dương tính giả (xem dưới).

#### Bảy blocker

| # | Lỗi | Hậu quả |
|---|---|---|
| 1 | `AuthContext` gọi `signOut()` cho **mọi** lỗi khi khôi phục phiên | Mở app một lần lúc mất mạng là **xoá refresh token**. Đăng nhập thứ Hai, thứ Ba bị đẩy ra, không có lời giải thích nào trên màn hình |
| 2 | Nút chính của form đăng nhập đo được **2.86:1** | Nhãn trên control quan trọng nhất sản phẩm, dưới ngưỡng 4.5 |
| 3 | `--green` khai báo `:root` ở **hai** file cùng ship | Màu thương hiệu trang auth do thứ tự bundle quyết định, không do file nào quyết định |
| 4 | `/login` in ra dial 74%, chuỗi 12 ngày, bốn band 85/72/64/78 | Số bịa trên màn hình đầu tiên khách thấy — đúng loại trang chủ đã xoá 11 cái |
| 5 | `applyError` map mọi mã lạ về "Email hoặc mật khẩu không đúng" | Lúc máy chủ sập, mọi người được bảo là sai mật khẩu. Họ đi đổi mật khẩu đang đúng, rồi gọi hỗ trợ |
| 6 | Nộp bài: `setSubmitting` bất đồng bộ + `crypto.randomUUID()` **trong** handler | Ba lần bấm = ba POST, mỗi cái một `Idempotency-Key` khác nhau — đúng cơ chế mà key sinh ra để chặn |
| 7 | Hết giờ không flush | Câu trả lời cuối bị bỏ, trong khi chân trang nói "phần đã lưu vẫn được giữ" và thanh trạng thái nói "chưa gửi được" |

#### Một "blocker" là dương tính giả — và đó là lý do phải kiểm chứng

Agent rà code báo `.num` không có rule nào và 23 phần tử đang hỏng, xếp hạng cao nhất. Rule tồn tại
ở `packages/design-system/src/reset.css`; agent chỉ quét `apps/web/src/styles` và `index.css`. Làm
theo báo cáo sẽ thêm một rule trùng. **Báo cáo của agent là đầu vào, không phải bằng chứng.**

#### Ba nhóm lỗi lặp lại — đáng nhớ hơn từng lỗi lẻ

**1 · "Không nhận được câu trả lời" bị coi là "câu trả lời là không".** Blocker 1 và 5, cộng với
`/verify-email` báo link hỏng khi gateway trả HTML, và `packages/auth` `JSON.parse` không bọc.
Một helper `isUnreachable()` gom ba hình dạng thất bại vận chuyển — fetch reject, body không phải
JSON, và 5xx — vào một câu hỏi duy nhất mà mọi màn có thể hỏi.

**2 · Hành vi được viết một lần, hiểu rồi, và không áp lại ở chỗ kế tiếp.** `alive.current` phải set
true **cả lúc vào**: ba nơi đã sửa, `DevicePanel` là nơi thứ tư và mang shape cũ. `role="tablist"`
không có `tabpanel`: `/practice` đã gỡ và ghi chú lý do, phòng thi vẫn còn. Class dùng ở component
này nhưng định nghĩa trong stylesheet của trang kia — chỉ chạy được vì `App.tsx` import mọi route.

**3 · ARIA hứa một mô hình bàn phím mà code không giữ.** Bốn `role="menu"` và hai `role="radiogroup"`
không có roving tabindex, không có phím mũi tên. Đã hạ xuống `aria-expanded` / `aria-pressed` —
tức là hứa đúng thứ component thật sự làm được.

#### Đã làm

- **Auth**: gộp năm màn phụ (`/forgot-password`, `/reset-password`, `/verify-email`, `/login/sso`,
  404) từ **năm ngôn ngữ thị giác** về một khung `AuthSimple` — trước đó chúng dùng hai màu xanh cho
  cùng một hành động và bốn bộ chrome khác nhau. `AppShell` đã xoá.
- **404** ra khỏi shell riêng, mặc header/footer thật, và liệt kê bốn module — thứ hữu ích nhất một
  trang 404 làm được.
- **`AuthVisual`** thay dashboard giả bằng hai bảng nói thật: cách chấm từng kỹ năng, và bốn module.
- **Phòng thi**: latch đồng bộ + key ổn định cho nộp bài; flush lúc hết giờ; `role="alert"` riêng cho
  nộp thất bại; cảnh báo thời gian đọc được bằng screen reader; tách `pending` khỏi `queued`; tắt
  spellcheck/autocorrect/autocapitalize trên ô trả lời; mỗi ô có tên riêng theo câu hỏi.
- **Micro**: `NotAllowedError` / `NotFoundError` / `NotReadableError` tách ba đường khác nhau kèm chỉ
  dẫn cấp quyền; bản ghi được giữ để **gửi lại** thay vì ghi lại từ đầu.
- **Nội dung**: gỡ mọi khẳng định về giá, hạn mức, "bốn tiêu chí IELTS", và một cơ chế đối chiếu
  trích dẫn **chưa tồn tại**. → `B-5a`, `B-5b`, `H-8`
- **Dọn**: 64 rule CSS chết (đã kiểm chứng từng cái, **trừ mọi class thuộc trang tài liệu** vì phiên
  khác đang viết lại nó), ba bản `.sr-only` gộp còn một trong design system, danh sách `SKILLS` trùng
  lặp xoá đi.

#### Đợt hai (26/08, cùng ngày) — hoàn thiện nốt

- **Xem lại từng câu trên trang kết quả.** FAQ `/practice` hứa *"bạn xem được từng câu mình đã trả
  lời gì"*, và `submitted` + `isCorrect` đã nằm trong payload từ ngày đầu — không ai dựng màn cho
  nó, nên lời hứa được đưa ra mà không giữ. Lưới chip số kèm dấu ✓/✕ và một dòng `sr-only` đầy đủ.
  **Đáp án đúng không hiển thị**, để đề còn làm lại được (`A-11`).
- **Kết quả có nút kiểm tra lại** cho phần AI chấm bất đồng bộ — trang trước đó fetch một lần rồi
  đứng yên vĩnh viễn dưới một dòng thông báo nói AI đang chấm.
- **Audio**: cả hai player thêm `onError` (hỏng codec giữa bài Listening trước đó im lặng hoàn
  toàn), trạng thái đang tải có nhãn, và nút của bài chép chính tả có trạng thái đang phát + dừng.
- **Phân trang**: nhãn theo từng trang thay vì hard-code "Trang bài luyện" trên cả ba nơi, và đổi
  trang giờ đưa cả khung nhìn lẫn bàn phím tới kết quả mới.
- **Tuỳ chọn lọc 0 kết quả** đổi từ `disabled` sang `aria-disabled` — trước đó số `0` là thông điệp
  duy nhất và nó bị screen reader bỏ qua hoàn toàn.
- **Chuyên mục bài viết**: nhóm `bai-viet` đổi nhãn thành **Hậu trường** (nó trùng tên trang, nên lọc
  vào đó trông như lọc không ăn), và nhóm rỗng `Tuyển dụng` xuống cuối thay vì đứng thứ hai.
- **Đếm câu đã trả lời** trong phòng thi tính theo phần đang xem, không phải cả đề — chip nằm ngay
  dưới tiêu đề "Câu hỏi phần 1" mà đếm 8 câu của cả bài.
- **`useAlive()`**: tám bản sao của cùng một ref gộp về một hook. Đây là đoạn đã gây ra lỗi
  `DevicePanel`. Cố tình **không** gộp thành `useLoad<T>` — bốn màn có bốn state union khác nhau,
  ép chung là gồng abstraction chứ không phải chia sẻ mối quan tâm.
- **Kiểm chứng một khẳng định về máy chủ**: FAQ nghe chép mô tả chính xác quy tắc so khớp
  (`DictationComparer.Normalise`). Đúng — và đã ghi nguồn để lần sau không phải suy luận lại.

#### Bộ test: 144 → 159

`auth-resilience.test.tsx` mới (5 test) khoá blocker 1, 5 và validation form. `exam-flow` thêm 6 test
cho blocker 6, 7, nhóm nhập liệu và màn xem lại từng câu. `page-chrome` thêm skip link và khoảng
trắng trong tiêu đề.

**Suite còn nhạy với tải máy, và đã đo chứ không đoán**: 12 worker → 8 fail · 6 worker → 3 fail ·
3 worker → 0 fail. Đã chốt 3 worker. Nguyên nhân là cấu trúc — mọi test mount cả `<App/>` — nên khi
máy chạy container không liên quan (load 15) vẫn còn một test lẻ thua cuộc đua. Cách sửa đúng là
cho các file nặng fetch render cây con thay vì `<App/>`, không phải nâng thêm một con số.

Một cạm bẫy đáng ghi: **`{ timeout: n }` cục bộ trong test đè lên `asyncUtilTimeout` chung**, nên ba
file mang số riêng là đúng ba file mà ngân sách chung không với tới được.


### Giai đoạn A12 — Giao diện làm bài thi cho đề thật — ✅ **XONG 26/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 26/08/2026**: *"đã có 1 đề … đọc các tài liệu và đề trong này rồi
triển khai làm các giao diện làm bài thi theo phong cách của bên mình hiện tại"* — bốn kỹ năng, chưa
gồm nghe chép chính tả.

#### Đề tự khai ba khuyết điểm chặn, và một trong đó không phải việc kỹ thuật

`exam/Exam1/manifest.json` ghi `readyToPublish: false`:

1. **Nguồn gốc.** Tài liệu có watermark *"REAL IELTS TESTS"* và dẫn về một kênh Telegram; **quyền sử
   dụng chưa được xác lập**. → `exam/` và fixture sinh ra từ nó **không vào git**; xem `.gitignore`.
2. **`H-4`** — bảng quy đổi điểm thô sang band là bảng chung, chưa hiệu chuẩn cho đề này, nên **chưa
   được báo band cho học viên**.
3. Phần Speaking do máy soạn, chưa ai duyệt nội dung.

#### Khoảng trống thật: 55 trên 76 câu là câu nhóm

Schema đã có đủ mười loại câu hỏi — nhưng **không có `group`**. Không có nó, một câu *matching* là
một câu hỏi không có gì để chọn, và một câu *labelling* hỏi phòng nào ở đâu mà không đưa bản đồ.
Đó không phải "hiển thị đơn giản hơn", đó là **không làm được bài**.

`group` giờ có mặt từ schema → domain → reader → persistence → view → client → giao diện, mang
`title`, `instruction` (nguyên văn, vì nó là luật chấm nói cho thí sinh), `image`, `text` và
`eachLetterOnce`.

#### Reader bắt được một lỗi tính điểm mà không ai nhìn thấy

Câu *"Choose TWO letters"* là **một object và hai điểm** — nó chiếm hai dòng trên phiếu trả lời, và
bảng band được hiệu chuẩn theo dòng phiếu. Mô hình đang đếm object, nên phần Listening 40 điểm bị
chấm trên thang 36: **bốn band trên cùng của chính bảng đó không ai với tới được**, và mọi điểm bên
dưới đọc lệch hàng. Số học nhất quán từ trong ra ngoài, nên nó không hề trông giống lỗi.

Đã thêm `marks` (mặc định 1), `AutoScoredCount` đổi tên thành `AutoScoredMarks` — vì cái tên cũ nói
"đếm câu" và giờ nó là "tổng điểm". **Chia điểm một phần trong câu nhiều điểm thì chưa quyết**: đáp
án là một tập phải khớp trọn, và bịa ra luật nửa điểm là bịa ra chính sách. → `[OPEN QUESTION]`

#### Hai thứ đã ở trong hợp đồng nhưng chưa bao giờ được vẽ

- **`imageKey`** có trên `PartView` từ ngày engine chạy và không component nào render. Vô hình khi
  mọi fixture đều là chữ; lộ ngay khi có đề thật, vì **Writing Task 1 *chính là* cái biểu đồ**.
- **Nhãn đoạn văn** `**A**`, `**B**` hiện ra nguyên dấu sao, trong khi câu hỏi bảo *"chọn tiêu đề cho
  đoạn B"*. `PassageBody` hiểu đúng **một** cấu trúc markdown — các package chỉ dùng `**đậm**` — và
  không bao giờ set HTML: nội dung đến từ ZIP người dùng tải lên.

#### Giao diện

| | |
|---|---|
| Bảng chọn đáp án | Hiện **một lần phía trên** nhóm để đọc lướt, đúng như đề giấy. Trước đó mười tiêu đề chỉ nằm trong dropdown — đọc bảng nghĩa là mở/đóng mười lần |
| Câu matching / labelling | `<select>` thay vì 60 radio lặp lại cùng mười tiêu đề |
| `eachLetterOnce` | Ghi *"(đã dùng ở câu N)"*, **không khoá**. Đổi một chữ cái từ dòng này sang dòng khác là việc bình thường giữa chừng |
| Điền vào đoạn tóm tắt | Ô nhập **nằm trong câu**. Năm ô liệt kê bên dưới là cùng lượng thông tin nhưng khó hơn hẳn: đó là bài kiểm tra trí nhớ làm việc, không phải đọc hiểu |
| Ảnh bản đồ / biểu đồ | Tải qua endpoint có xác thực như audio, và **nói thẳng là chưa có mô tả bằng chữ** thay vì tự bịa một caption — package này mang sẵn ví dụ: alt text của Task 1 mô tả một đề hoàn toàn khác |

#### Kiểm chứng

`scripts/import-exam-package.py` chuyển package thành fixture. Bốn kỹ năng đã chạy end-to-end trên
trình duyệt với nội dung thật: đoạn văn có nhãn, bảng tiêu đề, đoạn tóm tắt có ô điền, bản đồ, bốn
file audio, biểu đồ Writing, thẻ đề Speaking kèm đồng hồ chuẩn bị.

Backend 272 test · web 163 test.


### Giai đoạn A13 — Dựng lại trang học sinh — ✅ **XONG 26/08/2026**

`[QUYẾT ĐỊNH]` **Chủ sản phẩm, 26/08/2026**: *"giao diện student chưa đẹp, design lại trang này đi"*.

#### Vấn đề đo được, không phải cảm giác

Ở 1440px mọi thứ nằm trong **một cột 1080px**: thẻ "đang làm dở" chứa một tiêu đề và một nút, rộng
1080px với khoảng 800px trống ở giữa. Ba ô chỉ số là ba hộp viền rỗng, mỗi số có 350px trắng để
đứng — khiến **`0.0` thành thứ to nhất trên trang** mà việc của trang là mời người ta luyện tập. Cả
trang cao **2407px** cho "một phiên đang dở, năm kết quả, bốn kỹ năng, ba module": muốn xem bài tối
qua đã chấm chưa thì phải cuộn qua hết catalogue.

#### Đã sửa

| | |
|---|---|
| **Hai cột từ 1180px** | Trái là *thứ có thể bắt đầu* — được duyệt qua nên cần bề ngang. Phải là *thứ đã làm* — được tra nên cần **dính** lại trong tầm mắt lúc đang duyệt. Trang còn **1890px** |
| **Chỉ số thành một thẻ** | Ba lần đọc cùng một thứ thì đọc như một khối; giá trị đứng cạnh nhãn thay vì lừng lững phía trên nó |
| **Phiên hết giờ đổi màu** | Đang là xanh lá — màu palette này dùng cho "xong, và tốt". "Đã hết giờ" trên nền xanh là giao diện tự mâu thuẫn, và người đọc tin màu trước khi đọc chữ. Hổ phách: không phải đỏ, vì không có gì hỏng — đồng hồ chạy hết là đề đang hoạt động đúng |
| **Bốn kỹ năng lấy màu của chính nó** | Phòng thi, `/practice` và trang kết quả đều tô Reading xanh dương, Listening cam, Writing tím, Speaking hồng. Dashboard là màn duy nhất bắt người ta *đọc* thay vì *nhận ra*. Bốn cặp đo được 5.2–6.1 |
| **Thẻ "đang làm dở" vào trong cột** | Trải hết chiều ngang thì tiêu đề và nút ở hai đầu 1580px — panel càng rộng màn hình càng trông rỗng |

#### Ba lỗi bố cục lộ ra trong lúc làm

- **Hàng "buổi gần đây" xuống dòng theo độ dài nhãn.** Là flex-wrap nên "Xem chi tiết" rớt xuống
  dòng riêng còn "Đang làm" thì không — năm buổi ba hình dạng. Đổi sang grid: *cái gì và khi nào* ở
  dòng trên, *được bao nhiêu và làm gì tiếp* ở dòng dưới, mọi hàng cùng chiều cao.
- **`auto-fit` cho một tập cố định bốn phần tử** cho ra 3+1, bỏ Speaking một mình. `auto-fit` dành
  cho danh sách không biết trước độ dài; danh sách này là IELTS và độ dài của nó đã chốt. → 2×2.
- **Thẻ nhàn rỗi có tiêu đề và câu mô tả cách nhau 400px** vì `.dash-now` là flex-row
  `space-between` và ba phần tử con thành ba cột.

#### Một sửa đổi của chính em không được ghi

Một script python đặt `assert` **trước** `write_text` và ném lỗi ở lệnh sau, nên thay đổi
`.dash-tag` im lặng không vào file — đúng lỗi em đã ghi ở A11. Lần này mỗi lần ghi đều đọc lại file
để xác nhận, thay vì tin vào `assert` trước đó.

Web 165 test.


### Giai đoạn A14 — Ba việc tồn đọng — ✅ **XONG 26/08/2026**

`[QUYẾT ĐỊNH]` chủ sản phẩm, 26/08/2026: duyệt ba việc kỹ thuật không phải chờ ai.

#### 1 · Hai vùng chạm

`.dict-speed` 36px và `.dict-filters-clear` 40px → 44px. Cái đầu nằm cạnh nút phát, trên màn hình
mà toàn bộ tương tác là "nghe lại, chậm hơn".

#### 2 · i18n cho `Pagination` và `Breadcrumb`

Hai component dùng chung nên sai của chúng nhân lên khắp nơi: `Trước`, `Sau`, `Trang {n}`,
`Đường dẫn`. `label` của pager vẫn do nơi gọi truyền vào và **đó là cố ý** — nơi gọi biết danh sách
của nó là gì, component chỉ biết nó có nhiều trang.

Còn lại 27 component in tiếng Việt mà không có `t()`. → `M-4`

#### 3 · Suite "nhấp nháy" — kết luận trước đó của em là sai

**Suite không nhấp nháy. Cái máy nhấp nháy.** Và phải đo hai vòng mới nói được câu đó cho tử tế.

Vòng đầu thấy 2–8 test fail mỗi lần chạy, luôn ở file đang chờ `fetch`, không bao giờ trùng nhau,
và chạy riêng thì cái nào cũng pass. Em kết luận là do số worker, vì giảm worker thì lỗi biến mất.
**Baseline đó bị nhiễu**: cùng lúc máy đang chạy Docker stack của dự án, một API dotnet, ba container
không liên quan, và một trình duyệt giữ 19 trang từ phiên review.

Đo lại sau khi tắt hết, 12 nhân, cùng commit:

| | |
|---|---|
| máy rảnh, 3 worker | 165/165 · 14,1s · 14,5s |
| máy rảnh, 6 worker | 165/165 · 14,4s · 15,7s |
| máy rảnh, 12 worker | 165/165 · 14,6s · 15,3s |
| máy tải nặng, 3 worker | thỉnh thoảng 1 lỗi lẻ · 55–85s |

Số worker **không mua được gì** trên máy rảnh, và **không cứu được hẳn** máy bận. Giữ 3 vì nó miễn
phí khi còn dư và thu hẹp cửa sổ lỗi khi hết dư.

**Điều quan trọng hơn: đây không phải lý do để cấu trúc lại test.** Ghi chú cũ ở
`vitest.config.ts` đề xuất cho các file nặng `fetch` render cây con thay vì `<App/>` — theo bằng
chứng này thì đó là một ngày công đổ vào một con ma. Nếu lỗi quay lại: **kiểm tra cái máy trước**
(`uptime`, `docker ps`) rồi mới động vào test.


### Giai đoạn A15 — Dọn data demo + hoàn thiện chấm 4 kỹ năng — ✅ **XONG 27/08/2026**

`[QUYẾT ĐỊNH]` chủ sản phẩm, 27/08/2026: *"xóa các phần data demo mình sẽ làm dần dần chỉ sử dụng
các data mình cấp như exam. Và hoàn thiện các phần chấm của 4 kĩ năng"*.

#### Phần 1 — data demo

| # | Việc | Cổng đã qua |
|---|---|---|
| A15-1 | Xoá `full-demo.json`, `reading-demo.json` và hai file audio của chúng | Seeder nạp **mọi** `.json` trong `fixtures/exams`, nên xoá file là đủ để danh mục chỉ còn đề thật |
| A15-2 | Xoá `fixtures/dictation` — audio do macOS `say` sinh, câu tự bịa | `FixtureDictationCatalogue` đã xử lý sẵn trường hợp thiếu thư mục và ghi log *"dictation is empty"*. Không sửa dòng mã nào |
| A15-3 | `ARTICLES` và `DOCUMENTS` thành mảng rỗng, **giữ nguyên kiểu** | Kiểu chính là thứ bàn giao: tên trường là tên một bản ghi thật, nên đổi sang `fetch` sau này chỉ động vào một dòng `import` |
| A15-4 | **Ba trạng thái rỗng, không phải hai** | Kho trống không phải lỗi của người đọc. Bảo họ *"thử từ khoá khác"* khi chưa có gì được đăng là bắt họ đi tìm một lỗi họ không gây ra — đúng cái mà thư viện dictation đã sửa, và bài học chưa đi tới hai trang kia |
| A15-5 | Giấu ba khối tự khoe số 0 | *"0 tài liệu · 0 miễn phí · 0 nhóm kỹ năng"* đọc như một con số tải hỏng. Khối bài viết trên trang chủ cũng biến mất thay vì để tiêu đề *"Đọc thêm…"* treo trên lưới rỗng cạnh một link dẫn tới trang trống |
| A15-6 | **Tách fixture của test ra khỏi danh mục sản phẩm** | `reading-demo.json` vừa là nội dung app nạp vừa là mẫu đề hợp lệ cho test schema — một thư mục gánh hai vai, nên không thể dọn cái này mà không mất cái kia. Nay nó thuộc `Vni.Ielts.Infrastructure.Tests`; test bài viết/tài liệu tự mang catalogue của mình qua `vi.mock` |
| A15-7 | `empty-catalogues.test.tsx` — 3 test **không** mock | Khoá lại đúng cái đang ship. Một bản sửa sau này nhét nội dung mồi vào *"cho trang đỡ trống"* sẽ đỏ ở đây trước |

#### Phần 2 — chấm 4 kỹ năng

Luật Domain (`CriterionMarking`, `Rubric`, `SectionMarking`) đã dựng đủ từ `T7` nhưng **chưa có gì
gọi nó**: không cổng, không kho lưu, không nối vào luồng nộp bài. Việc này nối trọn.

| # | Việc | Cổng đã qua |
|---|---|---|
| A15-8 | Cổng `ISectionEvaluator` · `IRubricSource` · `ITranscriptSource` · `ISectionMarkingStore` | Không SDK nào của nhà cung cấp chạm tới Domain hay Application — test kiến trúc giữ ranh giới đó |
| A15-9 | **`SectionMarking` mang `TaskNumber`** | IELTS chấm Task 1 và Task 2 **riêng**, mỗi task đủ bốn tiêu chí. Chấm cả section làm một sẽ trả lời `H-8b` bằng phép trung bình — đúng thứ `G-11` cấm. Writing ra **hai** bài chấm, Speaking ra **một** |
| A15-10 | `SectionMarkingRunner` — đối trọng của `ScoreIfDeterministic` | Cùng hình dạng, cùng chỗ gọi, không cái nào biết cái kia. Đó là thứ khiến *"cả bốn kỹ năng đều được chấm"* là một câu về **một** đường ống |
| A15-11 | **Sáu trạng thái khi chưa có band**, không phải im lặng | `Marked` · `NothingSubmitted` · `AwaitingRubric` · `AwaitingEvaluator` · `AwaitingTranscript` · `Rejected`. Một dấu gạch ngang cần một lý do: bài chưa nộp, chưa nối gì, hay model đã trả lời và bị từ chối — ba tình huống, ba cách sửa |
| A15-12 | Speaking nêu **thiếu transcript** trước, không nêu thiếu evaluator | Hai vật cản, không cách đích như nhau: evaluator chờ một câu trả lời pháp lý đang tới, còn speech-to-text **chưa chọn gì cả** và Pronunciation cần word-level timings. Báo cái được kiểm trước sẽ giấu mất cái thật sự phải quyết |
| A15-13 | `BandScore.Weighted`, dùng chung luật làm tròn với `Overall` | Hai bản cài của một luật bất đối xứng là một bản sẽ sai về 6.75. Hàm **bắt buộc** nhận trọng số — không có overload nào tự giả định, vì tỉ lệ Task 1 : Task 2 không được công bố |
| A15-14 | `ToResults` ghép cả `SectionScore` lẫn `SectionMarking` | Màn kết quả đọc một trong hai sẽ hiện hai kỹ năng rồi gọi đó là kết quả. Writing chỉ góp band khi phiên bản đề mang tỉ lệ; không có thì hai band task được báo đúng như chúng vốn là |
| A15-15 | Khe cắm rỗng **được đăng ký**, không phải cổng bỏ trống | `UnconfiguredEvaluator` trả lời thật (`IsConfigured == false`) và từ chối ầm ĩ nếu bị gọi. Một cổng không đăng ký là một `null` mà mọi chỗ gọi phải nhớ kiểm |
| A15-16 | Rubric đọc từ cấu hình, **không mặc định** | Bộ tiêu chí đã chốt (`A-13b`) nên lấy từ mã. `DescriptorSource` thì chờ `H-8a` — bản quyền mô tả band là câu hỏi pháp lý — nên không đặt thì báo `AwaitingRubric`, không phải điểm 0 |
| A15-17 | 16 test mới backend + 2 test client | Phần lớn là test về việc **từ chối**. Đường ống chạy khi đủ mọi thứ là nửa dễ, và chưa bao giờ là nguồn của một điểm sai. Cái gây hại là một band xuất hiện khi có thứ đang thiếu |
| A15-18 | Client: Writing hiện `6.5 · 7.0`, **không bao giờ** `6.75` | Có test khoá đúng con số đó. Màn hình là nơi lối tắt rẻ nhất, và cũng là nơi học viên ít có khả năng nhận ra một câu hỏi kinh doanh vừa được trả lời hộ |

**Chưa làm, và biết rõ vì sao:** adapter gọi nhà cung cấp AI (`B-2`) · speech-to-text (chưa chọn) ·
band Writing gộp (`H-8b`) · band Reading/Listening (`H-4`).

---

### Giai đoạn A20 — Ba lát cuối của lớp nền đúng đắn — 🔄 27/08/2026

Chủ sản phẩm duyệt làm liền cả ba phần còn lại trước khi chuyển sang chức năng chính: **A3 ·
conflict reconciliation**, **Gate C · CAS cho transition của phiên**, **Gate D · idempotency giành
chỗ trước khi chạy**.

Điểm chung của cả ba, và là lý do chúng đi cùng nhau: **mỗi cái đều là một thao tác đọc–quyết
định–ghi mà một người ghi khác chen được vào giữa.** Ba chỗ khác nhau, cùng một hình dạng.

---

#### A20-1 · A3 — autosave vá từng câu, thay cho thay cả tờ

**Vấn đề mà CAS phát hiện được nhưng không chữa được.** A18 thêm revision, nên một autosave đến
muộn bị `409` thay vì âm thầm xoá. Nhưng client trả lời cái `409` đó bằng cách **lấy revision mới
rồi gửi lại đúng cả tờ cũ** — thực hiện đúng cái ghi đè mà server vừa từ chối, chậm một nhịp, với
chip xanh phía trên.

Gốc rễ nằm ở **đơn vị ghi**, không ở việc so sánh. Trong một tờ đầy đủ, một ô trống mang hai nghĩa
không phân biệt được: *học viên vừa xoá câu này* và *tab này chưa từng biết câu này tồn tại*. Không
quy tắc nào áp lên một tờ đầy đủ tách được hai cái đó.

| | |
|---|---|
| **Hợp đồng** | `PUT /answers` nhận `changes` (chỉ câu đã đổi) + `baseRevision`. Không còn `answers` |
| **Vắng mặt** | Không đụng tới |
| **Có mặt, giá trị `null`** | Học viên đã xoá — và đây là chỗ duy nhất một ô trống có thể xuất hiện |
| **Kết quả** | `{ revision }`, kèm `answers` (tờ đã hợp nhất) **chỉ khi** `baseRevision` lạc hậu |
| **`409`** | **Không còn.** Hai bản vá không mâu thuẫn nhau nên không có gì để từ chối |

**Lưu trữ đổi từ mảng sang map.** Một phần tử mảng chỉ định địa chỉ được bằng chỉ số hoặc bằng khớp
vị trí, và không cái nào tạo được phần tử nó không tìm thấy — nên ghi một câu có nghĩa là đọc cả
tờ, thêm vào, rồi ghi ngược lại. Với map, `$set: { "entries.r-7": "true" }` là **một câu lệnh** dù
`r-7` có sẵn hay chưa.

- **Không có script migration.** Mảng cũ được đọc **dưới** map và không bao giờ bị ghi lại. Sheet
  cũ giữ nguyên đáp án làm lớp nền, mọi ghi mới nằm trên. Hai dòng code, không cửa sổ nào mà một
  phiên đang thi không lưu được.
- **`SetAnswerAsync` hết đua.** Bản cũ phải thử cập nhật theo vị trí rồi mới push, và hai phần
  Speaking kết thúc cùng lúc có thể cùng vào nhánh push → hai entry cho một câu. Với map không có
  nhánh nào.
- **Question id giờ là đường dẫn database**, nên nó thôi là văn bản tự do: handler đối chiếu với
  chính danh sách câu hỏi của đề, store chặn thêm lần nữa `.` và `$` đứng đầu.

**Client hợp nhất thay vì ghi đè.** `pendingChanges` giữ những câu chưa được server xác nhận. Khi
response mang tờ đã hợp nhất về, mọi câu **không** nằm trong `pendingChanges` được nhận vào và hiện
lên màn hình — câu học viên đang gõ dở thì không đụng tới. Chỉ những entry còn đúng giá trị đã gửi
mới bị xoá khỏi hàng đợi, nên một đáp án sửa lại giữa lúc request đang bay sẽ được gửi lại.

**Một lỗi thật lộ ra khi viết test:** timer debounce **không được dọn khi unmount**. Nó vẫn chạy,
vẫn bắn request từ một trang không còn tồn tại — và trong suite, autosave của bài test này rơi vào
giữa bài test kế tiếp, làm một khẳng định về single-flight **pass mà chẳng đo gì cả**.

---

#### A20-2 · Gate C — CAS cho transition của phiên

Ba đường đóng một section — `advance`, `submit`, sweep hết giờ — đều làm cùng một việc: đọc phiên,
quyết định, `ReplaceOne` cả document. Hai cái chạy cùng lúc thì cả hai quyết định trên cùng một
trạng thái và cả hai đều được ghi.

**Thiệt hại không phải là document hỏng** — các bản replace phần lớn đồng ý với nhau. Thiệt hại là
**cả hai đều đi tiếp và chấm section**, mà chấm là chỗ có tiền và có band.

| | |
|---|---|
| **Cổng** | `TrySaveAsync(session, from)` — filter gồm `_id` + `status` + *section đang mở là cái nào* |
| **Không dùng bộ đếm revision** | Một bộ đếm trả lời "có gì đổi không", quá mạnh — nó sẽ từ chối một `submit` chỉ vì một autosave vừa hạ cánh. Cái cần biết hẹp hơn và tự nói ra: phiên còn đang thi chứ, và section tôi định đóng còn mở chứ |
| **Đổi thứ tự** | **Đóng trước, chấm sau.** Trước đây chấm trước rồi mới ghi — cổng đặt sau một quyết định đã thi hành xong thì không cứu được gì |
| **Kẻ thua** | Không phải lỗi. Hai tab cùng bấm "Tiếp theo" thì một cái phải là cái thứ hai. Nó đọc lại và trả về **chỗ phiên đang thực sự ở**, nên cả hai tab cùng nhìn thấy một section |
| **Giá phải trả** | Một section có thể đóng mà chưa chấm nếu tiến trình chết giữa hai bước |
| **Trả giá** | `MarkSection.CatchUpAsync` chấm lại mọi section đã đóng mà chưa có điểm, khi đọc kết quả. **Chỉ Reading/Listening** — chúng chấm từ đáp án nên tính lại luôn ra cùng một số. Writing/Speaking cần chính sách retry có ngân sách, mà chưa ai quyết → `[OPEN QUESTION]` |

**`ExpirySweep` có ba kết quả chứ không phải hai.** `LostToAnotherWriter` không phải thất bại và
cũng không phải không-làm-gì: bản sao trong bộ nhớ **đã bị `Expire()` sửa** rồi, nên nó không còn
mô tả cái gì có thật. Gộp nó vào `false` thì caller sẽ đi tiếp và submit một phiên đã hết hạn; gộp
vào `true` thì đường submit sẽ báo "hết giờ" cho một học viên mà tab kia của họ vừa nộp bài xong.

**Và fake repository phải sửa cùng.** Nó trả về **đúng cái object** đã nhận, nên handler sửa
aggregate rồi ghi là đã đổi luôn thứ mà lần đọc sau nhận được — kể cả lần đọc lại của chính nó sau
một lần ghi bị từ chối. Một test cho "ghi thua nên phải báo cái đã lưu" sẽ đọc lại đúng cái sửa
vừa bị từ chối, và **đồng ý với lỗi nó được viết ra để bắt**. Giờ nó `Rehydrate` cả hai chiều.

---

#### A20-3 · Gate D — idempotency giành chỗ trước khi chạy

Middleware trước đây: **tra key → chạy handler → ghi bản ghi**. Hai request cùng key đều trượt lần
tra — vì cái thứ nhất chưa ghi xong — nên **cả hai đều chạy**. Hai phiên thi mở ra, hai section
được chấm, hai lần đánh giá AI, entitlement trừ hai lần. Lần insert thứ hai hỏng vì trùng `_id`, và
cái hỏng đó bị bắt rồi ghi log là *"một replay đến sớm hơn một chút"* — thứ duy nhất mà nó không
phải.

| | |
|---|---|
| **Thứ tự mới** | Insert bản ghi `in-flight` **trước**. Ai insert được thì sở hữu thao tác |
| **Trùng key, đã xong** | Phát lại đúng response đã lưu |
| **Trùng key, đang chạy** | `409 IDEMPOTENCY_KEY_IN_FLIGHT` + `Retry-After: 1`. Không chạy gì cả |
| **Trùng key, body khác** | `409 IDEMPOTENCY_KEY_REUSED`, như cũ |
| **Lease** | 2 phút. Đây là **giới hạn phục hồi**, không phải giới hạn đúng đắn — chỉ có ý nghĩa khi tiến trình giữ claim chết đi |
| **Cướp claim** | Chỉ khi lease hết, và **bằng một `UpdateOne` có filter trên `leaseUntil`** — đọc rồi ghi sẽ cho hai kẻ cướp cùng vào, tức là lỗi cũ trong một cái hộp nhỏ hơn |
| **Token** | Mỗi lần thử mang một token riêng. Ghi kết quả có filter trên token của chính mình, nên một handler sống lại từ cõi chết không ghi đè được response của request đã thay thế nó |
| **Thất bại** | **Xoá claim.** Giữ lại thì một `500` thoáng qua thành vĩnh viễn suốt đời cái key — client retry 24 giờ và luôn nhận đúng cái `500` đó mà chẳng có gì chạy lại |

**Client nuốt mã 409 mới.** `IDEMPOTENCY_KEY_IN_FLIGHT` là câu trả lời trung thực cho một câu hỏi
chưa có đáp án — nhưng nó là một lời từ chối, và một lời từ chối hiện ra lúc học viên bấm "Nộp bài"
đọc thành *bài của bạn chưa được nộp*, trong khi nó đang được nộp ngay lúc đó. `examApi` thử lại
**một lần** sau 700ms; lần đó gặp response đã lưu và phát lại.

---

#### Test — và một test được kiểm chứng là bắt được lỗi

| | |
|---|---|
| Backend | **392** (từ 368, và 398 sau đợt review) — thêm barrier trên MongoDB thật cho cả ba lát |
| Web | **207** (từ 205) |
| `tsc --noEmit` | sạch |
| `check-docs.py` | sạch |

Test quan trọng nhất — *hai request một key cùng lúc chỉ mở một phiên thi* — đã được **kiểm chứng
ngược**: tạm thời đưa middleware về thứ tự cũ, chạy, **test đỏ**; khôi phục, **test xanh**. Một
test hồi quy chưa từng thấy màu đỏ chỉ là một test hy vọng.

**Ba thứ chỉ MongoDB thật mới chỉ ra được**, và cả ba đều có test:

1. `{ revision: 0 }` không khớp document **thiếu** trường đó — nhưng `{ submittedAt: null }` **có**
   khớp trường thiếu. Hai luật đối nghịch nhau, cùng một driver.
2. Upsert là chỗ hai kẻ ghi cùng thấy "chưa có document". Cái thua nhận trùng `_id`, và cái đó phải
   thành một lần thử lại chứ không phải một exception rơi vào autosave của học viên.
3. Một dictionary trong bộ nhớ, dưới một cái khoá, **luôn** cho ra một người thắng dù code sắp xếp
   thế nào — nên một suite dựng trên nó sẽ đồng ý với lỗi suốt thời gian lỗi tồn tại.

#### Còn lại, và biết rõ vì sao

| Việc | Vì sao chưa làm |
|---|---|
| **Gate B · token coordinator** | `renewOnce` dùng chung đã có ở tầng transport; còn cờ `rotating` trong `AuthContext` đua với promise, khoá cross-tab, phục hồi mất response ở backend |
| **Gate E còn lại** | Artifact OpenAPI + `packages/api-client` sinh tự động; hết cảnh báo `act()`; một test HTTP chạy trọn Full Test R→L→W→S |
| **`ClosedAnswerRevision`** | Đóng băng revision của sheet lúc đóng section, để một lần chấm lại tái lập được. Cần đổi schema kết quả — một task riêng |
| **Retry cho Writing/Speaking chưa chấm** | Cần ngân sách và chính sách. `[OPEN QUESTION]` |
| **Suite web nhấp nháy khi máy tải cao** | Đã kết luận từ A14: **lỗi ở máy, không ở test**. Ba lần chạy đầy đủ trong đợt này: 1 đỏ, 2 đỏ, 0 đỏ — mỗi lần một file khác nhau, gồm cả file không đụng tới. `uptime` trước khi nghi ngờ test |

---


---

#### A20-4 · Ba agent review lại, và họ tìm được nhiều thứ

Ba agent đọc độc lập phần vừa viết: QA (đường autosave vá), backend (Gate C + D), frontend (máy trạng thái của trang thi). **Mọi lỗi nghiêm trọng họ tìm được đều nằm trong code tôi vừa thêm**, và mỗi lỗi đều được kiểm chứng bằng cách đọc code trước khi sửa.

##### Nghiêm trọng — mất dữ liệu hoặc kẹt cứng

| | Lỗi | Vì sao nó tồn tại |
|---|---|---|
| 1 | **Token xoay 14 phút một lần thì xoá sạch hàng đợi chưa gửi** | Effect nạp bài phụ thuộc vào *chuỗi* access token. `AuthContext` làm mới token trước hạn 60 giây, chuỗi đổi, effect chạy lại, và nó reset `pendingChanges` — tức là câu trả lời học viên vừa gõ mà server chưa nhận. Chip vẫn đỏ "Chưa gửi được" trên một đáp án từ giờ sẽ không bao giờ được gửi. Effect giờ khoá theo *có token hay không* |
| 2 | **Một lời từ chối vĩnh viễn khoá luôn nút Nộp bài** | Cổng flush đúng khi chặn vì mất gói tin, và sai khi chặn vì section đã đóng. Hết giờ với một phím chưa lưu → `SESSION_EXPIRED` mọi lần flush → "Nộp bài" từ chối, **mãi mãi**, trong khi `beforeunload` cãi lại chính cái reload sẽ cứu họ. Hai tab cho ra cùng cái bẫy qua `SECTION_NOT_OPEN`, đồng hồ vẫn chạy. Giờ có `TERMINAL_REFUSALS`: cái gì server không bao giờ nhận thì không được giữ kết thúc lại |
| 3 | **Submit thua cuộc đua trả về `200` kèm trang kết quả** | `TrySaveAsync` trả `false` bị vứt đi. Học viên bấm "Nộp bài" trong lúc tab kia bấm "Tiếp theo" → không nộp được gì, nhưng được đưa tới màn kết quả cho một phiên vẫn đang chạy. Và **idempotency lưu luôn cái `200` đó làm câu trả lời cho key**, nên mọi lần thử lại đều phát lại lời nói dối. Giờ submit thử lại trên trạng thái nó vừa thua, và nếu vẫn thua thì `409` chứ không bao giờ `200` |
| 4 | **Request bị huỷ giữa chừng **xoá** claim sau khi side effect đã xảy ra** | Mọi thứ sau `next()` chạy trên `RequestAborted` trong cùng khối `try`, với `catch` xoá claim. Điện thoại đổi mạng sau khi submit đã thành công → token huỷ → đọc buffer ném lỗi → **claim bị xoá** → lần thử lại chạy lại toàn bộ handler. Đúng cái tình huống mà header của chính file đó viết là lý do nó tồn tại. Giờ mọi thứ sau handler chạy trên `CancellationToken.None` |
| 5 | **Không có trần cho kích thước sheet** | Ghi cả tờ bị chặn bởi trần 1 MB của một request. Một bản vá thì `$set` vào document đang có, nên nó **lớn dần qua nhiều request** cho tới trần 16 MB của BSON — và lỗi đó không nằm trong catch nào, thành `500`, và từ đó **không autosave nào của section ấy còn thành công**. Giờ có trần 60.000 ký tự mỗi câu |

##### Vừa — sai lớp lỗi, hoặc trạng thái kẹt

| | Lỗi |
|---|---|
| 6 | `ModifiedCount` thay vì `MatchedCount` trong guard transition — một transition không đổi gì bị báo là **thua cuộc đua chưa từng xảy ra** |
| 7 | `ArgumentException` từ `GuardKey` thoát ra thành `500`. Một question id `1.1` xuất bản qua CMS làm mọi autosave trong section đó thành `500` |
| 8 | `change()` dựng tờ mới từ closure của state chứ không từ `latestSheet`. Một phím gõ trước khi React commit bước hợp nhất sẽ **xoá vĩnh viễn** đáp án của tab kia — revision đã đi tiếp nên server không bao giờ gửi lại |
| 9 | `recorded()` ghi ref **bên trong** một updater — tức là side effect ngay chỗ React đảm bảo thuần khiết, và chạy hai lần dưới StrictMode |
| 10 | Nhánh Speaking thoát sớm mà không đẩy `savedGeneration`, nên `flush` không bao giờ hội tụ và chip đứng ở "đang chờ lưu" hết section |
| 11 | Thông báo hết giờ ánh xạ `'clean'` thành `'failed'` — báo động mất dữ liệu giả, đúng lúc không còn gì để bấm |
| 12 | `stepFailed` chỉ được xoá khi bấm lại nút. Học viên gõ lại, chip xanh — mà dòng đỏ "chưa lưu được" vẫn nằm dưới nó |
| 13 | `submit()` điều hướng mà không kiểm tra `alive` (`advance()` có) |
| 14 | Lease 2 phút **ngắn hơn một submit sẽ mất** khi có evaluator: Writing là hai lần gọi model, Speaking là một lượt ASR trên tới 14 phút audio. Nâng lên 5 phút và ghi rõ phải đo lại |
| 15 | `AuthContext`: `new Date(undefined).getTime()` là `NaN`, `Math.max(0, NaN)` là `NaN`, và `setTimeout(fn, NaN)` chạy **ngay**. Một session lưu thiếu trường hết hạn sẽ làm mới token tức thì và đăng xuất người dùng ngay khi mở trang |

##### Test — bốn cái xanh mà không đo gì

Đây là phần đáng lo nhất, vì một test xanh sai còn tệ hơn không có test.

| Test | Nó thực sự đo gì | Đã sửa thành |
|---|---|---|
| `keeps one autosave in flight` | Tám phím trong một cửa sổ debounce **chỉ sinh đúng một request** dù code thế nào. Xoá hẳn khoá single-flight cũng không làm nó đỏ | Giữ request đầu mở, gõ tiếp để debounce thứ hai bắn **chồng lên** nó — cách duy nhất hai request có thể trùng nhau |
| `does not call an in-flight edit saved…` | Request thứ hai đến từ debounce mới, có hay không có bộ đếm generation | Giữ luôn request thứ hai, rồi khẳng định chip **không** nói "Đã lưu" trong đúng cửa sổ đó |
| `takes in another tab's answer…` | Mock trả về đúng giá trị client vừa gửi, nên `field` giữ nguyên là tất nhiên. Xoá dòng bảo vệ `pendingChanges` vẫn xanh | Server trả về một `r-1` **khác** trong lúc client đang có sửa đổi chưa xác nhận |
| `A_question_id_that_is_not_a_legal_key…` | Nằm sau `Skip.IfNot(MongoAvailable)` dù guard ném trước mọi I/O — trên CI không có Mongo nó **im lặng không chạy** | Chuyển sang unit test luôn chạy |

Và một lỗ hổng fixture: **không stub nào trong `exam-flow` trả lời `/auth/refresh`**, nên nếu bộ đếm làm mới token bắn, học viên bị đăng xuất và trang đăng nhập hiện ra giữa bài thi — trong bất kỳ test nào đang chạy lúc đó. Đọc như lỗi của bài thi, thực ra là lỗ của fixture.

Ba test sửa lại đều được **kiểm chứng ngược**: gỡ đúng dòng code chúng bảo vệ → đỏ; khôi phục → xanh.

##### Còn mở, và biết rõ vì sao

| Việc | Vì sao chưa làm |
|---|---|
| **Writing/Speaking đóng mà chưa chấm là mất vĩnh viễn** | Đảo thứ tự đóng-trước-chấm-sau tạo ra lỗ này; lưới `CatchUpAsync` chỉ vớt Reading/Listening. Vớt cả W/S nghĩa là chạy lại đánh giá mỗi lần mở trang kết quả — **miễn phí hôm nay** vì chưa có evaluator, **retry trả tiền không giới hạn** ngay khi có. Cần ngân sách retry, mà chưa ai quyết → `G-11`. **Đây là blocker của evaluator đầu tiên, không phải việc làm thêm** |
| Lease bị cướp không được phát hiện | `UpdateResult` của bước ghi kết quả bị bỏ qua, nên một handler sống lại từ cõi chết không ghi đè được ai — nhưng cũng không ai biết nó đã chạy. Cần log, và cần đo p99 thật |
| `ClosedAnswerRevision` | Đóng băng revision của sheet lúc đóng section. Một autosave qua được cửa kiểm *trước* transition vẫn có thể hạ cánh *sau* nó, nên điểm phụ thuộc vào việc tiến trình có chết hay không |
| `UpsertAsync` ghi đè bản đề đã xuất bản tại chỗ | Comment ở `Ports.cs` nói sửa bảng quy đổi thì **tạo bản mới**; code thì `ReplaceOne` theo id. Mâu thuẫn có sẵn từ trước, không thuộc đợt này |
| A5 · retry cho autosave | Thất bại thì `pendingChanges` được giữ lại nhưng **không có gì lên lịch gửi lại**, và nó chỉ nằm trong bộ nhớ — đóng tab là mất phần đuôi |
| `/advance` và `/submit` chưa có rate limit | Mọi route ghi khác trong nhóm đều có |
| Vị trí cuộn của khung đề khi `advance` lúc đang ở part 0 | `setActivePart(0)` không đổi giá trị nên effect khôi phục không chạy, và node DOM dùng lại giữ nguyên `scrollTop` |

##### Test sau đợt review

| | |
|---|---|
| Backend | **398** (từ 392) |
| Web | **209** (từ 207) |
| Ba lần chạy web đầy đủ liên tiếp | **xanh cả ba** |

> **Về suite web nhấp nháy.** A14 kết luận "lỗi ở máy, không ở test" và bảo `uptime` trước khi nghi ngờ test. Đợt này ba lần chạy liên tiếp đều xanh ở mức tải tương đương mức từng cho ra 1–2 lỗi rải rác — sau khi vá lỗ `/auth/refresh` trong fixture và lỗ `NaN` trong `AuthContext`. **Không đủ để khẳng định hai cái đó là nguyên nhân**, nhưng đủ để không còn nói chắc "lỗi ở máy" nữa. Lần tới có lỗi rải rác: xem cái stub có trả lời mọi thứ app tự gọi không, trước khi xem `uptime`.

---

### Giai đoạn A19 — Bản review thứ hai: cổng crash + ba lỗi CAS — ✅ xong 27/08/2026

Bản review độc lập thứ hai, đọc chính phần A18 vừa viết. **Ba lỗi Critical/High đều nằm trong code
tôi vừa thêm**, và cả ba đã kiểm chứng bằng cách đọc code trước khi sửa.

#### Cổng crash — làm trước, không để cuối

Bản review chỉ ra suite **199/199 xanh trong khi app đang crash bên trong nó**: một mock trong test
token trả `{sessions:[]}` nơi client đợi `{sittings:[…]}`, dashboard đọc `.length` của `undefined`,
React tụt về `ErrorBoundary`, và các assertion — vốn về token — vẫn xanh.

Đó đúng là thứ cả dự án này đang chống, nhìn từ phía ngược lại. Nên `test-setup.ts` giờ **ném lỗi
trong `afterEach`** khi có render crash hoặc DOM không hợp lệ, kèm `expectRenderError()` cho test mà
đường lỗi *chính là* chủ đề của nó.

> **Cố ý ném trong `afterEach`, không phải spy `console.error`.** Spy nuốt mất thông điệp, nên lỗi
> đến tay người đọc mà không có stack và họ phải tái hiện mới biết gì. Cách này để React in đủ, nhớ
> rằng nó đã in, rồi cho test đỏ **đích danh**.

Bật lên là nó lộ ngay **hai lỗi thật** suite đang che:

| Lỗi | Ghi chú |
|---|---|
| `<p>` chứa `<div>` ở `SsoCallbackPage` và `VerifyEmailPage` | `Spinner` render một `<div role="status">`; `<p>` chỉ chứa được phrasing content, nên trình duyệt **đóng đoạn văn sớm** và DOM thật không phải cái mà file lẫn CSS mô tả. Nó vẫn render — nên nó sống sót |
| Mock endpoint sai trong chính test token của tôi | `listMySittings` gọi `/api/v1/sessions/`, không phải `/me/sessions` |

Và trong lúc viết test cho A19 nó bắt thêm một lỗi thứ ba: `/api/v1/sessions/{id}/results` **cũng
chứa** `/api/v1/sessions/`, nên nhánh chung trả session view cho trang kết quả.

#### Ba lỗi CAS

| # | Lỗi | Interleaving |
|---|---|---|
| A19-1 | **Autosave cuối thất bại nhưng vẫn submit/advance** | `flush()` bắt mọi lỗi rồi resolve, trong khi comment ngay trên `submit()` **hứa** rằng thất bại sẽ chặn nó. Sửa đáp án cuối → bấm Nộp bài trước khi debounce chạy → mất một gói tin → bài được chấm từ ảnh chụp **trước** khi sửa. Mọi tín hiệu nhìn thấy được đều nói đã lưu |
| A19-2 | **Response cũ đánh dấu edit mới là "đã lưu"** | Ảnh A đang bay, học viên gõ B, response A về và xoá cờ `dirty` — B chưa từng tới server nhưng giờ không phân biệt được với việc đã lưu. Submit lọt qua cổng và chấm thiếu B |
| A19-3 | **Revision không có trong session view** | Client không có gì để gửi ở lần lưu đầu sau mỗi lần tải trang, nên server tự điền expectation của chính nó — **một compare-and-swap tự cấp kỳ vọng thì không so sánh gì cả** |

**Cách sửa A19-2 là điều đáng nhớ:** vấn đề không phải *có việc chưa lưu không* mà là **bản nháp nào
đã được xác nhận** — một câu hỏi mà biến boolean không trả lời được. Nay có `draftGeneration` /
`savedGeneration`, và chỉ response mang generation mới nhất mới được xoá cờ. Kèm hàng đợi
**single-flight có gộp**: hai PUT đồng thời từ **một tab** tự tạo ra đúng những conflict mà CAS sinh
ra để phát hiện — một tab tự gây tranh chấp với chính nó, trên đường truyền vốn đã đủ chậm để gây ra
chồng lấn.

Revision cũng thành **chỉ tiến**: hai response về sai thứ tự sẽ đẩy client lùi và biến lần lưu kế
tiếp thành conflict với một version chưa từng tồn tại.

#### Còn nợ trong Gate A

| # | Việc | Vì sao chưa |
|---|---|---|
| **A3** | **Conflict hiện mới chỉ trì hoãn việc xoá dữ liệu của tab khác** | Sau conflict, client lấy revision mới rồi gửi lại **toàn bộ** sheet cục bộ — tức tự tay thực hiện đúng phép ghi đè mà CAS vừa từ chối, chỉ chậm hơn một nhịp. Hướng đúng theo review: **autosave theo từng câu**, không gửi cả tờ. Đây là đổi API, nên là task riêng |
| A5 | Retry policy phân tầng | Transient backoff, 409 đi reconciliation, 4xx dừng hẳn |

Gate B (gộp proactive/reactive refresh thành một coordinator, auth generation, cross-tab lock,
backend lost-response recovery) · Gate C (transition CAS) · Gate D (idempotency lease) đều chưa động.

---

### Giai đoạn A18 — CAS lát 1: revision cho answer sheet — ✅ **XONG 27/08/2026**

`[QUYẾT ĐỊNH]` chủ sản phẩm 27/08/2026: **CAS trước, idempotency sau, và không gộp vào một task.**
Lý do chủ sản phẩm nêu, ghi lại vì nó đúng: CAS bảo vệ trực tiếp dữ liệu bài thi đang làm và xử lý cả
request khác idempotency key, nhiều tab, autosave sai thứ tự, race giữa advance/submit/expire.
Idempotency chỉ xử lý request dùng **cùng** logical operation key — nó không thay thế concurrency
control ở tầng domain/persistence.

#### Lỗi

Autosave gửi **cả tờ bài làm**, nên một cái đến muộn không hoà trộn — nó **xoá**. Hai tab, hoặc một
tab trên đường truyền mà request N+1 vượt request N, và bản thua sẽ xoá mọi đáp án gõ sau lúc nó được
soạn. Không gì nói ra: server trả 204, chip ghi *"Đã lưu"* đè lên đúng cái vừa bị xoá.

`updatedAt` không cứu được. Một timestamp ghi lại **khi nào** document được ghi, không ghi lại nó
được suy ra từ **trạng thái nào** — và dưới clock skew hoặc một lần retry nó còn chạy lùi.

#### Đã dựng

| # | Việc | Cổng đã qua |
|---|---|---|
| A18-1 | `AnswerSheetRevision` **riêng**, không dùng chung với phiên | Hai tài nguyên có nhịp ghi khác hẳn nhau: autosave vài giây một lần, transition vài lần một giờ. Một bộ đếm chung nghĩa là **mỗi lần gõ phím sẽ vô hiệu hoá một submit đang bay** |
| A18-2 | So sánh và ghi là **một câu lệnh** | Đọc revision → kiểm trong C# → ghi là ba câu lệnh, và một writer cạnh tranh lọt vào giữa bất kỳ hai câu nào — đúng race mà đoạn code đóng nó vừa mở lại |
| A18-3 | `409 REVISION_CONFLICT` mang theo **revision hiện tại** | Một client chỉ được bảo "không" thì chỉ còn cách đoán hoặc bỏ cuộc — mà bỏ cuộc giữa bài thi nghĩa là học viên tiếp tục gõ vào một trang đã lặng lẽ ngừng lưu |
| A18-4 | Autosave trả **200 kèm revision**, không còn 204 | Client không học được version mới thì phải đọc lại tờ bài trước mỗi lần lưu, hoặc ghi mù — mà ghi mù chính là thứ cơ chế này sinh ra để loại bỏ |
| A18-5 | **Migration là bộ lọc, không phải script** | Mongo **không** khớp `{revision: 0}` với document thiếu hẳn trường đó. Ở revision 0 bộ lọc nhận cả "bằng 0" lẫn "không tồn tại", nên tờ bài cũ ghi thành công đúng một lần rồi bộ đếm sống. Một migration script sẽ cần một khoảng thời gian mà phiên **đang thi** không lưu được |
| A18-6 | Client giữ nguyên bản nháp khi bị từ chối | Nạp lại bản của server sẽ **rút chữ ra khỏi tay người đang viết dở** — tệ hơn chính cái conflict, và là cách cài đặt hấp dẫn hơn |
| A18-7 | Client chịu được response **không có** revision | Server cũ trả 204 → destructure `undefined` ném `TypeError`, không phải `ApiError`, nên rơi vào nhánh lỗi chung và tô *"Chưa gửi được"* đè lên một lần lưu thành công — giữa bài thi, suốt thời gian deploy cuốn chiếu |
| A18-8 | **Test đồng thời có barrier** ở tầng repository, MongoDB thật | Hai lần ghi tuần tự chỉ kiểm bộ lọc, **không kiểm race**. Cả hai task bị giữ tới khi cả hai sẵn sàng, nên chỉ tính nguyên tử của chính database mới tách được chúng — đúng thứ đang được kiểm |

**Definition of Done của lát 1 — đủ:** autosave cũ không xoá được autosave mới ✅ · hai tab tạo
conflict quan sát được ✅ · barrier-based concurrency test thật ✅ · migration/default rõ ràng ✅ ·
không dựa vào `UpdatedAt` ✅.

#### Lát 2 — chưa làm

Revision cho **transition của phiên**: `advance`, `submit`, `expire` dùng `expectedRevision`;
repository update theo `{ id, revision, expected status/current module }`; chỉ một transition thắng;
marking/outbox **chỉ được tạo sau** transition thắng. Bốn dòng DoD còn lại nằm ở đây:

- advance và submit đồng thời chỉ một cái thắng
- expire và submit sát deadline không tạo hai trạng thái cuối
- AI marking không bị gọi hai lần từ hai transition cạnh tranh
- request thua đọc và trả trạng thái đã thắng, xử lý được như retry an toàn

> **`ExamSessionRevision` phải là bộ đếm riêng** với `AnswerSheetRevision`. Đây là ràng buộc thiết kế,
> không phải sở thích: dùng chung thì autosave liên tục sẽ đẩy revision của phiên và mọi transition
> đang bay đều hỏng.

---

### Giai đoạn A17 — Sửa theo bản review độc lập — 🔄 27/08/2026

Một bản review từ bên ngoài, đọc toàn bộ snapshot working tree. **Bốn P0 đã kiểm chứng bằng cách đọc
code trước khi sửa** — CLAUDE.md rule 11: không suy ra yêu cầu từ review của bên thứ ba. Cả bốn đều
đúng.

#### Đã sửa

| # | Lỗi | Vì sao không ai thấy |
|---|---|---|
| A17-1 | **Chấm sai hai dạng câu, ngay hôm nay** | Client gửi `A\|D` cho multiple-select; marker tách theo `,;space` → coi cả chuỗi là một lựa chọn. Client gửi `iv` cho matching; marker đòi `left:right` → **từ chối thẳng**. Trên Exam 1 đó là 6 câu Reading và 7 điểm Listening luôn sai bất kể học viên chọn gì. **Hai bên đều có test và đều xanh**: một bên chứng minh client viết `A\|D`, bên kia chứng minh marker nhận `A,D`. Không ai sở hữu câu ở giữa |
| A17-2 | **Kestrel chặn mọi bản ghi Speaking thật** | Giới hạn body toàn ứng dụng 1 MB, trong khi một phút giọng nói là vài MB. Hạn mức 12 MB của chính endpoint **không bao giờ tới lượt chạy** — nó canh một cánh cửa không gì tới được. Đây là **bức tường thứ hai** ngay sau bức tường idempotency mà QA vừa gỡ: mỗi bức vô hình khi bức kia còn đứng |
| A17-3 | **Token chết giữa bài thi** | Access token 15 phút, một section Reading 60 phút, server validate với `ClockSkew = Zero`. Refresh chỉ chạy lúc mount, sau đó token là chuỗi tĩnh. Từ bàn học: autosave hỏng từ phút 16, audio section sau không tải được, Speaking upload và submit đều bị từ chối ở cuối một bài vừa làm một tiếng. **Không test nào bắt được vì mọi test đăng nhập rồi kết thúc trong một giây** |
| A17-4 | **Mọi thẻ trên dashboard dẫn tới cùng một chỗ** | Tất cả link `/practice` trống, mà workspace mặc định là Single Skill Reading. Thẻ "Thi thử full" mở một bài Reading đơn; bốn thẻ kỹ năng cũng mở Reading. Thêm nữa: điều kiện hiện thẻ Full Test lấy **hợp** của cả danh mục, nên bốn đề đơn kỹ năng thoả mãn nó — và thẻ mời một bài thi engine không khởi động được |

**Cách sửa A17-1 là điều đáng nhớ nhất:** không thêm dấu `|` vào danh sách tách. `AnswerMatcher` hỏi
**loại câu hỏi** xem đáp án có hình dạng gì, trong khi `AcceptedAnswer` đã mang chính hình dạng đó —
schema đã mô hình hoá nó từ đầu. Giờ nó hỏi đáp án. "Matching" bao gồm cả *chọn tiêu đề cho đoạn văn*
(một giá trị) lẫn *ghép hai danh sách* (một cặp); đoán hộ là gốc của lỗi, và bỏ đoán là cách sửa.

Bộ đề mẫu đã commit nay mang cả hai dạng, và test round-trip chấm bằng **đúng chuỗi client gửi** thay
vì moi ra trường nào tình cờ có giá trị trong khoá — một round-trip tự chấm nội bộ của mình chứng
minh parser chạy và không nói gì về sản phẩm.

#### Chưa sửa, và vì sao

| Việc | Vì sao chưa |
|---|---|
| **Revision/CAS cho answer sheet và phiên** | Đúng là lỗ hổng thật: autosave đến chậm ghi đè bản mới hơn, hai tab làm mất đáp án. Nhưng đây là đổi schema **và** mô hình ghi của mọi đường ghi — một task riêng, không phải một bản vá kèm |
| **Idempotency chèn record trước khi chạy endpoint** | Hiện chèn *sau*, nên hai request đồng thời cùng chạy rồi một request thua lúc insert — quá muộn, side effect đã xảy ra. Sửa đúng cần record trạng thái `processing` có lease, và nó chạm **mọi** endpoint state-changing |
| **Production không khởi động được** | API cố ý throw ngoài Development vì chưa có mail provider. Đây là fail-fast **đúng**; cái thiếu là một **quyết định chọn nhà cung cấp**, không phải code |
| Articles · Documents · Progress | Cần API và quyết định nội dung. `E-15`…`E-17` và `B-12` còn đang chờ |

### Giai đoạn A16 — Đội agent rà 4 kỹ năng — ✅ **XONG 27/08/2026**

Bốn agent trên bốn vùng file rời nhau: BA (`docs/`) · backend (`backend/`) · frontend (`apps/web/`) ·
QA (ranh giới giữa hai bên). Kết quả: **345 test backend** (từ 298) · **190 test web** (từ 170).

#### Ba lỗi đáng nhớ nhất trong đợt rà

| # | Lỗi | Vì sao không test nào bắt được |
|---|---|---|
| 1 | **Full Test chưa bao giờ chuyển kỹ năng** | `ExamRunnerPage` gọi `/submit` ở cả hai chế độ, mà `/submit` đóng **cả phiên**. Làm xong Reading, bấm nút → ba kỹ năng còn lại bị nộp trắng. `advanceSection` đã có sẵn ở cả `examApi.ts` lẫn backend từ khi engine ra đời, **không chỗ nào gọi**. Mỗi bên tự test đều xanh — lỗi nằm ở chỗ hai bên không gặp nhau |
| 2 | **Phiên hết giờ không bao giờ được chấm** | `Expire()` giữ nguyên answer sheet, nên đọc code thấy "không mất gì". Không mất gì trong *storage*; band thì mất. Chấm chỉ chạy từ `AdvanceSection` và `SubmitExamSession`, hết giờ không đi qua đường nào |
| 3 | **Chuỗi Speaking đứt vô hình** | Bộ chấm bịa khóa `speaking-{order}`, không tồn tại ở đâu cả. Tra cứu luôn trượt và **không gì báo lỗi**, vì `NoTranscriptSource` trả null cho mọi câu hỏi — chuỗi hỏng trông y hệt chuỗi chạy đúng, và chỉ lộ ra vào đúng ngày cắm ASR vào |

Bài học chung, đáng ghi thành quy tắc: **ba lỗi trên đều nằm ở ranh giới, không nằm trong một
module.** Tầng Application — nơi quyết định *khi nào* chấm — trước đợt này **không có test nào**, dù
Domain có và bộ chấm có. Và tầng đó có **ba** cách đóng một section, chỉ **hai** cách chấm.

#### Ba việc chủ sản phẩm duyệt 27/08/2026 — đã làm xong sau QA

| # | Việc | Vì sao |
|---|---|---|
| ✅ A16-1 | **Bỏ band Reading/Listening khỏi màn kết quả**, giữ điểm thô | `exam/Exam1/exam.json` **tự khai** `provisional: true` kèm ghi chú *"H-4 must adjudicate before any band is reported to a learner"*. Điểm thô đã hiện sẵn ngay cạnh, nên bỏ band **không mất thông tin nào** — chỉ mất một con số chưa ai hiệu chuẩn. Bảng của Exam1 còn lệch nửa bậc ở raw 19 và 23 so với bảng thường công bố. Khi `H-4` được trả lời thì thêm lại là một dòng. Cần sửa một test đang khoá đúng con số đó |
| ✅ A16-2 | **Đồng hồ Listening thiếu 10 phút chép đáp án** | `TimingProfile.DurationFor` chỉ trả `SectionDurationSeconds`; `ListeningTransferSeconds` **không** được cộng vào deadline. Exam1: 1800 giây, audio 4 part hết **24 phút 33 giây** → học viên còn ~5,5 phút trong khi đề hứa 10. IELTS thật là 30 phút audio **cộng** 10 phút chuyển đáp án. Đây là lỗi ảnh hưởng trực tiếp điểm số: người học mất bài vì hết giờ sớm hơn đề hứa. Sửa ở backend — sửa câu chữ ở client sẽ **che mất** lỗi thật |
| ✅ A16-3 | **Đề Writing in prompt hai lần** | `scripts/import-exam-package.py` đặt `part.body` = `question.prompt`; runner render `body` ở cột trái và `prompt` ở đầu câu hỏi. Nguồn nằm ở script/dữ liệu, không ở client — cách sửa duy nhất ở client là so chuỗi, dễ vỡ |

#### Lỗi QA tìm ra ở ranh giới — thứ không bên nào tự thấy được

| # | Lỗi | Bên nào sai |
|---|---|---|
| 4 | **Toàn bộ đường upload Speaking trả `400 IDEMPOTENCY_KEY_MISSING`** | Backend. `IdempotencyMiddleware` chặn mặc định mọi POST không nằm trong danh sách miễn trừ, và `/recordings` chưa bao giờ được đưa vào. Nghĩa là **bốn cửa kiểm backend vừa thêm hôm nay không cửa nào chạy được** — request chết trước khi chạm handler. Doc-comment của chính middleware đã liệt kê ba endpoint từng dính đúng lỗi này; đây là cái thứ tư |
| 5 | **Client vẫn autosave sheet Speaking mà server vừa từ chối** | Client. Sau mỗi lần ghi âm, `PUT /answers` với `module: "speaking"` → `400` → chip header đỏ **"Gửi thất bại"** và ở nguyên đó. Không mất dữ liệu — server đã lưu bản ghi — nhưng giao diện nói ngược sự thật, giữa bài thi tính giờ, và không có gì để bấm |

> **Vì sao miễn trừ chứ không bắt client gửi key** (`IsRecordingUpload`): key **không thể hoạt động**
> ở route này. Guard nhận diện replay bằng hash body, mà `FormData` sinh boundary ngẫu nhiên mỗi lần
> gửi — cùng một bản ghi gửi lại với cùng key sẽ hash khác và bị trả `IDEMPOTENCY_KEY_REUSED`. Đúng
> tình huống key sinh ra để cứu lại thành 409 cứng giữa một section đang tính giờ. Ghi vào sheet là
> `SetAnswerAsync` theo `questionId` nên upload trùng để lại đúng một id; giá phải trả là **một blob
> GridFS mồ côi** — không tốn entitlement, không kích hoạt job AI. Muốn ngược lại thì phải đổi cách
> hash cho multipart, không phải chỉ thêm header ở client.

#### Còn nợ, cần `B-8` phán quyết trước

Hai chỗ QA kiểm ra nhưng cố ý không vá, vì cả hai cần **một câu chữ mới trên màn thi**:

| Việc | Vì sao chờ |
|---|---|
| `SECTION_NOT_OPEN` hiện chip đỏ "Gửi thất bại" vĩnh viễn | Autosave bị từ chối vì đến muộn nghĩa là những chữ cuối gõ ở section đã đóng **thực sự không kịp vào bài** — nên nói "Đã lưu" là nói dối (L2), mà để đỏ vĩnh viễn là báo động về một section học viên không còn tác động được. QA đã khoá bằng test tính chất **không phụ thuộc quyết định UX**: refusal này không được kết thúc bài thi |
| `SpeakingRecorder` mù với mã lỗi server | Mọi từ chối đều hiện một câu kèm nút **"Gửi lại bản ghi"** mà server sẽ từ chối y hệt, mãi mãi. Đường đi có thật: đồng hồ trả lời của recorder chạy độc lập với deadline section. Đây đúng là hình dạng "nút thử lại chết" mà doc-comment của chính file đó nói nó đã được viết lại để loại bỏ |

#### Không vá, có chủ ý — mỗi cái một lý do

| Việc | Vì sao để nguyên |
|---|---|
| `AdvanceSection` lưu phiên **sau** khi chấm | Bảng raw→band có lỗ thì người học kẹt trong section. Hai lối ra là "cho đi tiếp không band" và "chặn" — chọn nghĩa là quyết định người học có được vượt qua một đề hỏng hay không. `[BUSINESS DECISION]` |
| Idempotency + multipart | Middleware băm nguyên body kể cả chuỗi boundary; nhiều HTTP client sinh boundary mới mỗi lần gửi lại → retry thật nhận `409` thay vì replay. Sửa thì ảnh hưởng **mọi** endpoint, mà client mobile chưa tồn tại để biết nó thực sự làm gì |
| **Chưa có Worker quét phiên hết hạn** | Chấm đang chạy **lười** lúc đọc. Phiên không ai mở lại thì không bao giờ được chấm. `Vni.Ielts.Worker` đang rỗng và đó mới là chỗ đúng — **việc riêng, chưa lên lịch** |
| `FindOpenForUserAsync` vẫn không ai gọi | Không gì chặn mở phiên thứ hai khi phiên cũ còn dở. Quyết định sản phẩm chưa có → để nguyên làm khe cắm (`G-11`) |
| Vị trí cuộn của **cửa sổ** theo part | "Chuyển part có nên nhảy trang không" là phán quyết thiết kế thuộc `B-8`. Ngoài ra `window.scrollTo` là no-op trong jsdom nên không khoá được bằng test |

#### Yêu cầu mới: lớp chọn bài kiểm tra đầu vào

`E-15`…`E-17` đã CONFIRMED trong [`../requirements/confirmed.md`](../requirements/confirmed.md), đặc tả
luồng ở [`../ux/practice-entry-test-flow.md`](../ux/practice-entry-test-flow.md). **Chưa dựng gì** —
`B-12` phải trả lời trước, và nó phải trả lời **cùng lúc** với `M-34`: hễ bài test là "rút gọn" thì
nó là nội dung, và phương án "Full Test đóng khung khác" chết ngay.

> Con số làm rõ vấn đề hơn mọi lập luận: nếu bài test đầu vào **là** Full Test đóng khung khác, học
> viên bấm "luyện tập", đồng ý kiểm tra trình độ, rồi ngồi **~2 giờ 45 phút** để nhận về **hai con số
> điểm thô trên bốn kỹ năng** — vì Writing trả `AwaitingEvaluator` và Speaking trả
> `AwaitingTranscript` cho tới khi `B-2` đóng.

---

### Giai đoạn B — Engine thi (API trước, màn hình sau)

#### Lát D — Module trên trang chủ — ✅ **XONG 21/08/2026**

`[QUYẾT ĐỊNH]` chủ sản phẩm: *"giờ thiết kế giao diện cho các module ở trang chủ đi"*.

**Ý cốt lõi: landing và sidebar là *cùng một bản đồ, kể hai lần*.** Bảy mục, cùng thứ tự, cùng icon,
cùng bảng màu — khách thấy *"bạn sẽ có gì"*, học viên thấy *"đi đâu bây giờ"*. Màu lấy thẳng từ
`skills.ts`, nơi duy nhất chúng được định nghĩa và được đo.

| # | Việc | Cổng đã qua |
|---|---|---|
| D-1 | `ModuleMap` — 5 module đang chạy + 2 sắp mở | Không mục nào mà sidebar không có. Thẻ chưa có gì phía sau **không phải là link** — chỉ thẻ dẫn được đi mới nhấc lên khi rê chuột |
| D-2 | Link theo trạng thái đăng nhập | Đã đăng nhập → vào thẳng module. Chưa → `/register`, **không** cố đưa về sau đó, vì quyết định 21/08 là đăng nhập xong ở lại trang chính |
| D-3 | Bỏ mục **Lộ trình** | Nó quảng cáo ba lộ trình không tồn tại (*"1.500 từ vựng"*, *"Band 7.5+"*), minh hoạ bằng **ảnh stock người lạ tải từ CDN bên thứ ba**, trong khi `H-1` chưa chốt lộ trình là gì |
| D-4 | Thay bằng **Cách hoạt động** — ba bước | Mọi dòng kiểm được: đồng hồ thuộc máy chủ (`ADR-0007`), R/L chấm theo đáp án (`A-11`), điểm AI mang nhãn tham khảo (L4) |
| D-5 | Thay bốn con số bịa bằng bốn dữ kiện | Bản cũ ghi `∞ bài luyện` — đúng họ *"miễn phí không giới hạn"* mà tài liệu cấm đích danh — và `AI feedback ngay sau bài`, mâu thuẫn `M-8` vốn chưa có cam kết thời gian nào |
| D-6 | Header khớp bản đồ | `Sản phẩm · AI chấm bài · Cách hoạt động` + Thêm(Tài liệu, Bài viết) |
| D-7 | 92 test web xanh | 3 test điều hướng landing cập nhật theo nhãn mới |

**Ba vi phạm ràng buộc cứng nằm sẵn trong thẻ `.stat` đã port, phát hiện khi đo:**

| | Đo được | Đã sửa |
|---|---|---|
| Chữ mô tả | **11px** | 14px |
| `line-height` | **1.35** | 1.5 |
| Xanh `#4d9027` trên trắng | **3.93** | `#3f7a1f` — cùng tông, đạt **5.23** |

Đo lại toàn bộ phần mới: **43 phần tử chữ, 0 dưới sàn 14px, 0 `line-height` dưới 1.5, 0 cặp dưới 4.5**
(thấp nhất 4,53), 0 cuộn ngang ở 500px.

**Còn treo, cần chủ sản phẩm:** deep-link từ landing vào một module chỉ mượt nếu bật lại `returnTo`.
Đó là **một dòng** — state phía server vẫn còn — nhưng nó **đảo quyết định 21/08** nên mình không tự làm.

---

#### Lát C — Bốn kỹ năng + Nghe chép chính tả — ✅ **XONG 21/08/2026**

`[QUYẾT ĐỊNH]` chủ sản phẩm: *"làm full hết các màn của luyện 4 kỹ năng và nghe chép chính tả đi để
mình góp ý một lượt luôn"*.

**Nội dung: tự soạn, không lấy từ bộ đề xuất bản nào.** Một đề bốn kỹ năng
(`fixtures/exams/full-demo.json`) và một bộ nghe chép (`fixtures/dictation/everyday-1.json`).
**Audio do `say` của macOS sinh ra** — không phải giọng thu thật, và mô tả của đề nói thẳng điều đó.
Đây chính là chỗ mình từng nói cần bạn gửi file; sinh bằng TTS gỡ được nút thắt mà không phải chờ.

| # | Việc | Cổng đã qua |
|---|---|---|
| C-1 | Đề 4 kỹ năng, 36 câu, nạp qua schema | Trình kiểm từ chối lần đầu và **chỉ đúng chỗ sai**: `minWords` phải nằm trong `constraints` của part. Đó là bộ kiểm làm đúng việc của nó |
| C-2 | Endpoint asset + `FixtureAssetStore` | Đo được: ẩn danh **401** · traversal **404** · thiếu file **404** (không phân biệt được với traversal) · range request **206** |
| C-3 | **Listening** — trình phát **không có thanh kéo** | `<audio controls>` là vi phạm nghiệp vụ. Thanh dưới là chỉ báo tiến độ, không có `role="slider"`, không handler. Phát một lần, công bố trước khi bấm |
| C-4 | **Writing** — đếm từ trực tiếp | Dưới ngưỡng dùng `--warn`, **không phải** `--bad`: bài ngắn là *chưa xong*, không phải *hỏng* |
| C-5 | **Speaking** — cue card, đồng hồ chuẩn bị + trả lời, ghi âm và tải lên | Bản ghi vào GridFS, chỉ **id** đi vào sheet đáp án — audio không cưỡi theo mỗi lần autosave. Xin quyền micro **trước** khi đồng hồ chạy |
| C-6 | **Nghe chép chính tả** (`M-22`) — module riêng | Không đồng hồ, không band, **nghe lại thoải mái** — ngược hẳn Listening, và đó là chủ đích |
| C-7 | So khớp từ bằng **LCS**, chấm trên server | 8 test. Bỏ một từ ở đầu câu chỉ báo **một** từ thiếu, không bôi đỏ toàn bộ phần sau |
| C-8 | 94 test Domain · **238 test backend** · 92 test web | Đi trọn cả năm màn trên trình duyệt thật |

**Ba quyết định đáng ghi:**

- **Listening cấm tua, Dictation cho nghe lại vô hạn.** Hai component **tách rời**, không dùng chung một
  cờ `canReplay`: một prop truyền sai sẽ biến bài Listening thành có thể tua, mà đó là sự cố chấm điểm
  chứ không phải bug giao diện.
- **`MediaRecorder` cho web, plugin cho mobile.** [ADR-0006](../decisions/0006-speaking-audio-capture-native-plugin.md)
  loại `MediaRecorder` vì WKWebView **tắt micro không báo** khi app xuống nền. Đó là phát hiện về
  WebView trên iOS; trên trình duyệt desktop không có API nào khác.
- **GridFS là tạm, và ghi rõ là tạm.** Audio thuộc về object storage (MinIO đã có trong stack).
  GridFS được chọn vì không thêm dependency nào, để Speaking dựng được ngay thay vì chờ một quyết định
  hạ tầng chưa ai ra.

**Hai lỗi cùng một họ, lộ ra khi bấm thật:** autosave và `dictation/check` đều bị middleware idempotency
chặn. Cả hai **không ghi gì thêm khi gọi lại** — một cái thay cả sheet, một cái là *read có body*. Đã
miễn trừ, cùng lý do đã ghi cho `DELETE /me/sessions`.

**Còn nợ:** Writing và Speaking **chưa được chấm** — chưa nối mô hình nào, nên hiện `—` đúng luật L3.
Đề mẫu Reading cũ (`reading-demo.json`) vẫn là nội dung placeholder; đề mới thì đọc được.

---

#### Lát A + B — ✅ **XONG 21/08/2026**

`[QUYẾT ĐỊNH]` chủ sản phẩm: dựng thẳng các màn thi vào khu vực học sinh, **không chờ `B-8`**.
Ghi lại rõ: `B-8` (22 đề xuất UI/UX) và `H-1` (Speaking một phiên hay ba lần nộp) vẫn chưa phán quyết —
đây là quyết định của chủ sản phẩm, không phải blocker đã được gỡ.

**Phát hiện làm đổi hẳn ước lượng:** tầng domain đã dựng xong từ trước — `ExamVersion`, 10 loại câu hỏi,
`ExamSession`, `DeterministicScorer`, `BandScore.Overall`, `ExamPackageReader`, và một đề Reading mẫu
40 câu. Thiếu là **lớp giữa**: persistence, application, API. Nên "làm màn thi" thực chất là hoàn thành
`T5 giai đoạn B` rồi mới đặt UI lên trên.

| # | Việc | Cổng đã qua |
|---|---|---|
| B-1 | 4 repo Mongo + 4 collection + 3 index | Sheet đáp án tách khỏi aggregate: autosave ghi vài giây một lần, gộp vào phiên là ghi lại cả buổi thi mỗi lần gõ |
| B-2 | 7 handler Application | Vào/ra qua view model **không có trường nào để đáp án lọt qua** — chặn `T7` bằng *hình dạng*, không bằng kỷ luật |
| B-3 | 6 endpoint + 6 mã lỗi ổn định | Phiên của người khác trả **404 chứ không 403**: 403 xác nhận id có tồn tại, biến không gian id thành oracle |
| B-4 | Seeder nạp `fixtures/exams` **qua `ExamPackageReader`** | Cùng bộ kiểm với nhập ZIP và soạn tại chỗ. Seeder tự dựng object graph là con đường thứ tư không có schema |
| B-5 | Màn **Luyện tập** (kho đề, hai chế độ) | Đề thiếu module thì **nói rõ thiếu gì**, không thi full được |
| B-6 | Màn **Reading** hai cột, chạy thật | Chrome riêng, **0 thẻ `<a>` bên trong** — đo trên trình duyệt |
| B-7 | Màn **Kết quả** | Điểm tổng `—` khi chưa đủ bốn kỹ năng; nhãn nguồn điểm theo L4 |
| B-8 | 6 test frontend · 92 test web · **230 test backend** | Bắn thật qua `curl`: list → start → save → advance(409) → submit → results |

**Ba lỗi chỉ lộ khi chạy thật, không lỗi nào do test bắt:**

| Lỗi | Vì sao ẩn |
|---|---|
| **Màn hình đứng ở "Đang tải…" trước một API đã trả 200** | `alive.current` chỉ được đặt `false` lúc cleanup, không đặt lại `true` lúc mount. StrictMode gọi effect hai lần — lần cleanup đầu tắt cờ vĩnh viễn, mọi `setState` sau đó bị bỏ. Đúng họ lỗi đã ghi ở `VerifyEmailPage` |
| **Autosave bị chặn bởi `IDEMPOTENCY_KEY_MISSING`** | Middleware bắt buộc key cho mọi `PUT`. Nhưng autosave là **thay cả sheet** — không có hành động thứ hai để một cái khoá ngăn, và gắn key cho mỗi lần gõ là ghi một bản lưu 24 giờ cho từng đợt gõ của từng học viên. Đã miễn trừ, cùng lý do với `DELETE /me/sessions` |
| **5 test tích hợp gãy vì `JsonSchema.FromFile`** | Nó đăng ký schema vào một registry **toàn tiến trình**; host thứ hai đăng ký lại là ném. Vô hình khi chỉ có một host. Cache bằng `Lazy` — `ConcurrentDictionary.GetOrAdd` **không hứa** chỉ gọi factory một lần, mà xUnit chạy song song |

**Còn nợ, và một cái cần chủ sản phẩm:**

- **Listening cần một file audio thật.** Không có thì màn Listening chỉ là cái khung. Một file mp3 bất kỳ là đủ để dựng và kiểm.
- Writing · Speaking: cần fixture đề bài và cue card — tự soạn được.
- Đề mẫu hiện có **cấu trúc thật nhưng nội dung placeholder** (đoạn văn một dòng). Đủ để kiểm luồng, chưa đủ để đọc.

---


`B-8` chặn **màn thi**, không chặn **engine**. Phần dưới làm và kiểm chứng qua API được ngay.

| # | Việc | Cổng cần qua |
|---|---|---|
| B1 | **Seeder** nạp `fixtures/exams/reading-demo.json` vào Mongo qua schema | Đề sai schema bị từ chối kèm JSON Pointer, không ghi gì vào DB |
| B2 | Lưu trữ `ExamDefinition`/`ExamVersion` + `POST /exams`, `GET /exams/{id}` | Bản nháp **không** xuất hiện với học viên |
| B3 | `POST /exams/{id}/sessions` — kiểm entitlement, tạo phiên | **Trừ token và tạo phiên phải nguyên tử**; retry 5 lần → một phiên, một lần trừ |
| B4 | `PUT /sessions/{id}/answers/{qid}` với `revision` | Replay bản cũ **không** ghi đè bản mới |
| B5 | `POST /sessions/{id}/next-section` | Không tham số nào cho client chọn skill; deadline mỗi section **tươi** |
| B6 | `POST /sessions/{id}/submit` | Nộp trễ 1 giây → `409 SESSION_EXPIRED` |
| B7 | Chấm R/L + `GET /results/{id}` | **Tắt hẳn provider AI, R/L vẫn ra band** — test hồi quy vĩnh viễn |

### Giai đoạn C — Rủi ro đã biết, chưa đóng

Ghi ra để không bị quên. Mỗi mục là một rủi ro thật, không phải "nice to have".

| # | Rủi ro | Vì sao chưa đóng | Mức |
|---|---|---|---|
| ~~C1~~ | ~~Không có khoá theo tài khoản khi đăng nhập sai nhiều lần~~ | ✅ **ĐÃ ĐÓNG** (rà lại 27/08). `MongoLoginThrottle` đếm theo **email**, không theo địa chỉ IP: 10 lần sai liên tiếp trong cửa sổ 15 phút thì khoá. Mười chứ không phải ba — người gõ sai mật khẩu trên bàn phím điện thoại vài lần là chuyện thường, và một cái khoá bật lên vì sự vụng về sinh ra ticket hỗ trợ chứ không sinh ra an toàn. Có test trong `LoginWithPasswordTests.cs`. Bộ đếm **không phải** ranh giới an toàn — nó biến việc đoán không giới hạn thành đoán có giới hạn, không thay thế được chính sách mật khẩu | ✅ ĐÃ ĐÓNG |
| C2 | Idempotency **đệm toàn bộ response trong RAM** | Không giới hạn kích thước. Response hiện nhỏ, nhưng một `ExamVersion` 40 câu thì không | TRUNG BÌNH |
| C3 | **Không có test tự động cho middleware** | Idempotency, rate limit, `X-Server-Time` mới chỉ kiểm bằng `curl` tay. Refactor sẽ không ai bắt được. **21/08: đã bớt một phần** — bộ test tích hợp SSO đi qua pipeline HTTP thật và chính nó bắt được lỗi idempotency chặn `/auth/sso/*/start`. Vẫn còn hở: rate limit và `X-Server-Time` chưa có test nào | **CAO** |
| C4 | **Chưa có nhà cung cấp email production** | Cơ chế token xong; phần gửi mới log ở DEV. API **từ chối khởi động ngoài Development** — có chủ ý | TRUNG BÌNH — chặn deploy |
| C5 | Không ép HTTPS / HSTS | `nfr.md` yêu cầu từ MVP | TRUNG BÌNH |
| C6 | Quyền nằm trong token → thu hồi trễ tới 15 phút | Đã cân nhắc và chấp nhận; đó là lý do access token ngắn. Ghi lại để không ai "tối ưu" thành 8 tiếng | THẤP — có chủ ý |
| C7 | Chưa có `AuditEvent` | `C-12` yêu cầu | TRUNG BÌNH |
| C8 | **Chưa test chịu tải và test bảo mật** | Cần endpoint engine thi tồn tại đã — bắn vào domain thuần sẽ đo sai thứ | Sau giai đoạn B |

### Giai đoạn D — Sau khi B xong

| # | Việc |
|---|---|
| D1 | **Test chịu tải**: phiên thi đồng thời, autosave dồn dập, nộp bài. Đối chiếu ngưỡng `nfr.md` |
| D2 | **Test bảo mật**: IDOR trên phiên người khác · replay nộp bài · giả mạo deadline · rò rỉ answer key trước khi chấm · leo thang quyền |
| D3 | Sinh OpenAPI + thay client viết tay bằng `packages/api-client` |
| D4 | Màn thi R/L/W/S + Kết quả — **chỉ sau khi `B-8` được phán quyết** |

### Không làm ở T5

CMS (hoãn theo ADR-0012) · adapter AI (chờ spike `V-11`) · Speaking (chờ `H-1` + ASR `V-10`) · màn thi (chờ `B-8`).

---

## ~~T5 (bản cũ)~~ — thay bằng danh sách trên

### Bản cũ, giữ để tra cứu


**Quyết định thứ tự:** làm end-user trước, CMS sau → [ADR-0012](../decisions/0012-learner-first-sequencing.md).

**Ràng buộc cứng, đây là lý do ADR tồn tại:** nội dung đề nạp vào **phải đi qua
`contracts/schemas/exam.schema.json`**, không phải object graph viết tay. Seeder, nhập gói ZIP, và
soạn tại chỗ trong CMS đều là *producer* của cùng một `ExamVersion` bản nháp qua **cùng một bộ kiểm**.
Nạp bằng JSON tuỳ ý sẽ tái tạo đúng cái trôi lệch mà thứ tự cũ sinh ra để ngăn.

**Definition of Done**
- [ ] `contracts/schemas/exam.schema.json` — 10 loại câu hỏi, `rawToBand`, `timingProfile`, `answerMatching`
- [ ] Seeder nạp đề **qua schema**, từ chối file không hợp lệ với finding có đường dẫn JSON Pointer
- [ ] Domain: `ExamDefinition` · `ExamVersion` · `Section` · `SectionPart` · `Question` · `AnswerKey` · `ScoringProfile` · `TimingProfile` — bất biến sau khi xuất bản
- [ ] Phiên thi: `startedAt`/`deadlineAt` **suy ra từ đồng hồ server**, deadline riêng mỗi section, không bao giờ mang deadline cũ sang section sau
- [ ] Lưu đáp án theo `revision`, nộp bài idempotent, quá hạn trả `409 SESSION_EXPIRED`
- [ ] Chấm Reading/Listening theo answer key — **không có AI trong đường chấm**
- [ ] Luật làm tròn band có **hàm riêng + test bảng** phủ `.25` và `.75`
- [ ] Test chịu tải: phiên thi đồng thời, autosave, nộp bài
- [ ] Test bảo mật: giả mạo đồng hồ, IDOR phiên thi, replay nộp bài, rò rỉ answer key

**Chưa làm ở T5** — màn thi R/L/W/S + Kết quả (chặn bởi `B-8` → T2) · CMS (hoãn theo ADR-0012) · adapter AI (chờ `V-11`).

---

## T7 · Lõi chấm điểm có cơ sở — ✅ xong 21/08/2026

`[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"sẽ chấm theo cách chấm của ielts luôn chứ không phải là
chấm bừa phải có cơ sở đến chấm và cho điểm ... đây là luyện tập nên cứ chấm 1 cách chuẩn nhất là
được"*.

**Phát biểu này đóng `H-8`, và đóng luôn một câu hỏi khác đang treo dưới tên khác.** Sau khi `M-11`
bỏ vai giáo viên, không còn ai đứng sau điểm số của AI. Câu trả lời hoá ra không phải một con người:
**là bản tiêu chí cộng với một trích dẫn học viên tra lại được.**

### Đã dựng

| # | Việc | Cổng đã qua |
|---|---|---|
| T7-1 | `Rubric` — bộ tiêu chí là **dữ liệu có version**, kèm `DescriptorSource` | `rubricVersion` được ghi trên mọi đánh giá chính là để giải thích lại được một band chấm tháng trước. Hard-code bộ tiêu chí sẽ biến trường version đó thành đồ trang trí |
| T7-2 | `evidence` từ **tuỳ chọn thành bắt buộc**, tối thiểu 1 trích dẫn mỗi tiêu chí | Một band kèm đoạn văn nhận xét là **ý kiến**; một band kèm trích dẫn từ chính bài của học viên là thứ **tra được và cãi được** |
| T7-3 | **Kiểm định 9** — mỗi trích dẫn phải thật sự nằm trong bài của học viên | Bắt buộc trích dẫn thôi thì chưa đủ: model được bảo trích thì nó sẽ diễn giải lại, và **một diễn giải trình bày như trích dẫn còn tệ hơn không trích dẫn**, vì nó trông như kiểm chứng được. Chuẩn hoá đúng những khác biệt mà một trích dẫn không kiểm soát được — khoảng trắng, nháy cong, hoa thường — và **không stemming, không so khớp theo độ trùng từ**: *"cleaner air means fewer cars"* trùng mọi từ với *"fewer cars means cleaner air"* và không phải cùng một câu |
| T7-4 | Bộ tiêu chí sai → **từ chối**, không chấm trên 3 tiêu chí | Thiếu một tiêu chí là thiếu một phần tư điểm; thừa một tiêu chí là model trả lời câu hỏi khác |
| T7-5 | Band ngoài thang → **ném lỗi, không kẹp** (kiểm định 4) · `sectionBand` **tính lại trong code** (kiểm định 5) | Kẹp 47 về 9 biến sự cố nhìn thấy được thành điểm sai trông hợp lý mà không ai đi tra |
| T7-6 | **Bỏ trọng số Task 1 : Task 2 mặc định ở cả ba chỗ** — record, Mongo document, và `?? 2m` trong package reader | Đây là lỗi thật, không phải cải tiến. Tỉ lệ 1:2 là `[ASSUMPTION]` mang `[NEEDS VALIDATION]`, nhưng vì nó là **giá trị mặc định**, mọi đề không khai trọng số đều bị chấm theo một phỏng đoán và **không chỗ nào nói ra**. Giờ nó từ chối, giống hệt cách `BandFor` từ chối một raw score ngoài bảng. → `G-11` |
| T7-7 | 21 test mới, tổng **259 test backend xanh** | Mỗi test mô tả một kiểu trả lời sai đã từng thấy ở model, và **không cái nào bị JSON Schema bắt** — đó là lý do chúng nằm trong code |

**Không có adapter AI, không có provider SDK, không có khoá.** `CriterionMarking` nhận primitive — khoá
tiêu chí, decimal model khai, chuỗi trích dẫn, bài của học viên — nên toàn bộ luật chạy được không cần
mạng và không vi phạm ADR-0005. Test kiến trúc vẫn xanh.

### Ba thứ chủ sản phẩm còn phải trả lời

| Mã | Câu hỏi | Vì sao chưa tự quyết được |
|---|---|---|
| `H-8a` | **Bản mô tả band lấy từ đâu** | IELTS có công bố công khai, nhưng bản quyền thuộc chung British Council · IDP · Cambridge và trang công bố **không nêu điều khoản nào cho bên thứ ba tái sử dụng**. Nhúng nguyên văn vào sản phẩm thương mại là câu hỏi pháp lý. `Rubric.DescriptorSource` ghi câu trả lời **theo từng version**, nên luôn tra được đánh giá nào sinh ra dưới câu trả lời nào |
| `H-8b` | **Trọng số Task 1 : Task 2** | Không công bố. Hiện từ chối thay vì mặc định |
| `H-8c` | **Bộ hiệu chuẩn** | *"Chuẩn nhất"* là tuyên bố về độ chính xác, và độ chính xác chỉ đo được bằng cách so với bài đã có người chấm. Cần **30–50 bài Writing** do giáo viên IELTS có kinh nghiệm chấm, giữ ngoài mọi prompt, chấm lại mỗi lần đổi model / prompt / rubric. **Không có gì trong schema thay thế được việc này**, và nó bắt đầu được ngay — không chờ API AI |

---

## T6 · CMS quản trị — 🔄 đang mở

`[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"làm phần giao diện trước tính năng làm sau"*.

**Điểm quan trọng nhất phát hiện khi lập kế hoạch: đề của sếp hiện chưa có đường vào sản phẩm.**
Bốn đường nạp nội dung, không đường nào chạy: JSON đúng schema (chỉ soạn tay được) · ZIP nhiều đề
(`I-14`, **`grep ZipArchive` = 0**) · AI phân tích đề thô (`I-15a`, chờ API AI **và** cần thiết kế
riêng) · soạn tại chỗ (chặn bởi `B-8`).

### Đã dựng — giao diện

| # | Việc | Cổng đã qua |
|---|---|---|
| T6-1 | **Tách `@vni/auth`** — transport + lõi phiên dùng chung hai app | Không sao chép: hai thứ package này giữ là hai thứ **không được phép lệch** giữa hai app — cách lỗi máy chủ thành lỗi có kiểu, và cách hiệu chỉnh đồng hồ mà đồng hồ thi dựa vào. `apps/web/src/lib/api.ts` thành shim nên **không phải sửa một import nào**; 92 test web vẫn xanh |
| T6-2 | Shell CMS: sidebar **lọc theo quyền**, bar mỏng | Sidebar chứ không tab: tám nhóm quá nhiều cho tab, và **số mục phụ thuộc quyền** nên chiều rộng không đoán trước |
| T6-3 | Ba cổng: chưa đăng nhập → form · đã đăng nhập nhưng không có quyền → **màn 1.2** · là vận hành viên → shell | Học viên mở URL admin **không phải lỗi**, không đưa lại form họ đã qua |
| T6-4 | 12 màn | Tổng quan · Đề thi · Chi tiết đề · Nhập đề · Người dùng · Vai và quyền · 4 màn chờ + đăng nhập + 1.2 |
| T6-5 | **Chi tiết đề là dòng thời gian version, không phải form** | Version đã xuất bản là bất biến. Một form sửa bản ghi là nói dối về việc hệ thống làm gì — và người đầu tiên tin nó sẽ sửa một lỗi chính tả trong đề đang có người thi |
| T6-6 | Ma trận quyền lấy cột **từ `PermissionKeys.All`** | 24 quyền × 4 vai, tick/dash chứ không phải ô màu. Khai lại danh sách trong TypeScript nghĩa là thêm một quyền mới thì **cột không xuất hiện** và không ai biết |
| T6-7 | 3 endpoint đọc: `admin/exams` (kèm bản nháp) · `admin/users` (phân trang server) · `admin/roles` | 403 kèm mã ổn định, **không phải 404** — ngược với phiên thi, và vì lý do ngược lại: vận hành viên có tên, cần biết mình thiếu quyền nào |
| T6-8 | Đo trên trình duyệt | 56 phần tử chữ: **0 dưới sàn 14px · 0 ngoài thang đóng · 0 `line-height` dưới 1.5 · 0 uppercase · 0 cặp dưới 4.5** (thấp nhất 5,08) · 0 cuộn ngang · 0 lỗi console |

### Đã dựng — nhật ký audit và các hành động ghi (lát 2 + 3, 21/08)

`[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"cms quản trị cho product dùng mà sao toàn cái gì vậy
triển khai chuyên nghiệp lên"*. Bản trước để nút tắt kèm ghi chú *"chờ nhật ký audit"* — đúng theo
ràng buộc 6, nhưng ràng buộc đó nói phải **làm nhật ký trước**, không nói phải để CMS ở trạng thái
tắt. Nên nhật ký được làm, và mọi nút mở.

| # | Việc | Cổng đã qua |
|---|---|---|
| T6-9 | `AuditEntry` — bản ghi **chỉ ghi thêm**, cùng `MongoAuditLog` chỉ có insert + đọc phân trang | Không có update, không có delete, **cố ý không có TTL**. Một nhật ký mà vận hành viên dọn được là nhật ký không chứng minh được gì về vận hành viên. → threat `T21` |
| T6-10 | 5 endpoint ghi: xuất bản · gỡ xuất bản · khoá · mở khoá · gán/gỡ vai trò. **Mỗi cái ghi audit trong cùng request** | Tự khoá mình và tự gỡ vai admin của mình trả **409**, không phải 403 — đây là luật nghiệp vụ chứ không phải thiếu quyền |
| T6-11 | JWT mang thêm claim `email`; audit ghi **địa chỉ**, không ghi tên hiển thị | Tên hiển thị người dùng tự sửa được, và nhật ký phải đọc được **sau khi** tài khoản đổi tên hoặc bị xoá. Khoá ngoại trỏ vào bảng có thể mất dòng thì không phải bản ghi |
| T6-12 | Màn **Nhật ký** thật: lọc theo email người thực hiện và theo loại hành động, phân trang **trên server**, không có nút sửa/xoá ở bất kỳ đâu | Câu hỏi đáng hỏi là *"tám tháng trước ai đụng vào tài khoản này"* — đúng cái mà lọc phía trình duyệt trên trang đầu không trả lời được |
| T6-13 | Màn **Chi tiết người dùng**: khoá/mở khoá, gán/gỡ vai trò | Không có ô đổi mật khẩu và không có ô đổi email: vận hành viên đặt được mật khẩu người khác là trở thành được người đó, và nhật ký sẽ ghi trung thực **sai tên** |
| T6-14 | Mọi hành động ghi đi qua **hộp thoại xác nhận nêu hậu quả**, kèm thông báo kết quả tự tắt | Nêu *"Học viên sẽ thấy và làm được đề này"* chứ không nêu *"đặt trạng thái sang Published"* — vận hành viên hành động trên cái thứ nhất |
| T6-15 | **Tổng quan** bỏ bảng "Việc cần biết" (blocker nội bộ), thay bằng hoạt động gần đây từ nhật ký | Bảng blocker là ghi chú kỹ thuật cho người xây, không phải thông tin cho người vận hành |
| T6-16 | Đo lại trên trình duyệt, 4 màn mới | **0 dưới sàn 14px · 0 `line-height` dưới 1.5 · 0 cặp dưới 4.5 · 0 uppercase · 0 cuộn ngang**. Một lỗi thật bắt được: trả focus sau khi đóng hộp thoại rơi về `<body>` vì nút mở đã bị React thay khi nạp lại — sửa bằng cách trả focus **sau** commit, và lùi về tiêu đề màn nếu nút cũ không còn |

### Định nghĩa lại phạm vi — 24/08/2026

`[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: CMS này là **hệ thống vận hành nội dung của nền tảng luyện
thi**, không phải dashboard quản trị website. Nghiên cứu đầy đủ:
[`../ux/cms-content-operations.md`](../ux/cms-content-operations.md).

**11 quyết định đã chốt** → `C-14`…`C-24` trong [`../requirements/confirmed.md`](../requirements/confirmed.md).
Đáng nhớ nhất:

| | |
|---|---|
| Vai giáo viên | `M-11` **tách làm hai**: quản lớp vẫn ngoài phạm vi (`M-11a`), **soạn đề vào phạm vi** (`M-11b`). Vai mới: `exam-author` |
| Ai xuất bản | `academic-lead` **duyệt hoặc trả lại**, không xuất bản. **Admin** là người cuối cùng đưa nội dung lên production |
| `B-9` | **Đóng.** Cổng duyệt là bắt buộc, và áp cho **mọi nguồn nội dung** chứ không riêng nội dung do AI sinh |
| `M-18` | **Đóng.** Xem thử như học viên nằm trong luồng của người soạn |
| Trình soạn đề | **Không chờ `B-8`** — bám đúng 10 dạng câu đã khoá trong `exam.schema.json` |

**Thứ tự ưu tiên đảo lại theo chỉ đạo:** mô hình nội dung → vòng đời → quyền → vai → giao diện →
nhật ký. Lý do: nếu `ExamVersion`, `Question`, `Media`, `Article`, `Dictation` chưa rõ thì vai có
chính xác đến đâu cũng chưa xây được CMS tốt.

**Bốn câu hỏi mới — `M-30`…`M-33` — không chặn việc xây.** `[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026:
*"mình không khoá gì hết, đều có thể thay đổi theo từng giai đoạn làm để phù hợp với bài toán mình
đề ra"*. Nên mỗi câu được dựng thành **khe cắm có cấu hình, triển khai rỗng** (`G-11`), không phải
một mặc định bịa ra và cũng không phải một chỗ ngồi chờ. Bảng khe cắm:
[`../ux/cms-content-operations.md`](../ux/cms-content-operations.md) §11.

**Nhưng không phải thứ gì cũng đổi rẻ như nhau.** Bốn thứ đổi muộn là mất dữ liệu chứ không phải
điều chỉnh — bản đã xuất bản bất biến · media đã xuất bản bất biến · 10 kiểu chấm · tách origin
(`V-13`). → §0.4 cùng tài liệu.

### Phase 1 · Nền tảng vòng đời — ✅ giao diện xong 24/08/2026

`[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: *"giao diện chuẩn, flow đúng. tính năng chưa cần chạy thật
vì thiếu đề"* — và *"đồng bộ mã màu từ apps/web"*.

Chỉ đụng `apps/admin`. Backend chưa sửa dòng nào.

| # | Việc | Cổng đã qua |
|---|---|---|
| P1-1 | **Đồng bộ bảng màu với `apps/web`** — `palette.css` trỏ lại các token ngữ nghĩa của CMS về giá trị trong `landing.css` của app học viên | Là *remap*, không phải viết lại: `admin.css` vẫn dùng `--acc`/`--ink`/`--line` nên toàn bộ bề mặt đổi da mà không sửa nghìn dòng. Đo trên trình duyệt: `--acc` `#06803a` · `--ink` `#252525` · `--page` `#f7f9f6` |
| P1-2 | **Chỗ duy nhất không chép y nguyên**: `--green` `#10b050` đo được **2.86** — dưới ngưỡng 4.5 cả khi làm chữ lẫn khi làm nền cho chữ trắng | `landing.css` tự ghi nhận lỗi này và để nguyên vì là trang marketing. CMS là công cụ đọc tám tiếng, nên xanh giữ vai trò **nền và viền**, còn chữ dùng `#06803a` (5.05) — đúng màu nút mà `module-pages.css` của app học viên đang dùng. Không phát minh hue mới |
| P1-3 | **Bắt được một lỗi thật khi đo**: `index.html` nạp Nunito từ lâu nhưng `--font` vẫn ghi Archivo — trang tải một font và hiển thị bằng font khác | Đã trỏ `--font` về đúng stack của `landing.css`. Đo lại: `getComputedStyle(document.body).fontFamily` trả `Nunito` |
| P1-4 | **Máy trạng thái 6 trạng thái là một bảng dữ liệu**, không phải `switch` rải khắp màn | Badge, nút, câu hậu quả trong hộp xác nhận và tên hành động ghi nhật ký đều đọc từ một bảng. Thêm một chuyển trạng thái là thêm một dòng, nút tự mọc trên mọi màn |
| P1-5 | **Quyền theo phạm vi sở hữu** — `allows()` chặn chuyển trạng thái `own` với người không phải tác giả, và mở lại cho ai giữ `exam.update.any` | Có test chứng minh **nó từ chối**, không chỉ test lúc nó cho phép |
| P1-6 | Ba màn hàng chờ + một màn chi tiết dùng chung cho cả ba vai | Khác nhau giữa các vai đã được diễn đạt đúng một lần — ở chỗ bảng chuyển trạng thái trả về nút nào |
| P1-7 | **Sidebar xếp theo công việc**, không theo bảng dữ liệu; mục chưa có màn thì hiện xám và nói rõ đang chờ gì | Bản đồ CMS tự nó là thông tin. Giấu nửa chưa xây làm nửa đã xây trông như toàn bộ sản phẩm |
| P1-8 | **Trả lại bắt buộc có ghi chú** — nút xác nhận khoá cho tới khi có lý do | Tác giả nhận "trả lại" mà không có ghi chú thì phải đoán, và đoán là cách tiêu một vòng duyệt vào việc sai |
| P1-9 | **Không trạng thái nào màu đỏ.** `Trả lại` dùng amber | Đỏ dành cho thứ đã hỏng — luật L1. Đề bị trả lại là kết quả bình thường của một quy trình đang chạy, không phải hỏng |
| P1-10 | **Chế độ "Xem như vai"**, chỉ có trong bản dev | Máy chủ chưa gieo hai vai mới, nên không có cách nào đi thử màn của chúng. Gated bằng `import.meta.env.DEV` **ngay tại chỗ render** để Vite gấp hằng số lúc build — kiểm chứng bằng cách grep bundle: chuỗi `"Xem như"` **không có** trong bản production |
| P1-11 | 34 test | Trong đó ba test là **quyết định `C-16`, ranh giới PDPL và giới hạn của `content-manager` viết thành câu chạy được** — nới một preset tới mức người soạn xuất bản được thì suite đỏ |
| P1-12 | Đo lại trên trình duyệt, 4 màn + hộp thoại | **0 dưới sàn 14px · 0 `line-height` dưới 1.5 · 0 cặp dưới 4.5 · 0 uppercase · 0 cuộn ngang · 0 lỗi console.** Bắt thêm một lỗi có sẵn: `.cms-dialog h2` dùng `--lh-display` 1.2 ở cỡ 20px — dấu chồng (ế ộ ằ) chạm dòng trên. Đã sửa |

### Phase 1b · Media của đề — ✅ giao diện xong 24/08/2026

`[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: *"thiếu một phần nữa để lưu audio rồi tại vì trong đề cũng
có audio mà"*. Đúng — và khoảng trống lớn hơn một màn thiếu.

**Hiện trạng phía dưới, kiểm chứng từ mã nguồn:** cổng `IExamAssetStore` và endpoint
`GET /exams/assets/{**reference}` **đã có** (xác thực, hỗ trợ range request). Nhưng bản cài đặt duy
nhất là `FixtureAssetStore` — đọc từ thư mục `fixtures/exams/assets`, **chỉ đăng ký ở Development**,
và tự đặt tên mình là stopgap. Ghi âm Speaking của học viên nằm ở GridFS, cũng là stopgap đã ghi rõ.
**MinIO có trong `compose.yaml` nhưng không có adapter nào trong mã.** Nghĩa là **không có đường nào
để đưa một file audio vào hệ thống**.

| # | Việc | Cổng đã qua |
|---|---|---|
| P1b-1 | Màn **Kho media**: tải lên · lọc theo loại · dung lượng · thời lượng · checksum · **cột "đang dùng ở đâu"** | Kho không có cột đó là một thư mục: không ai biết gì an toàn để xoá, nên không ai xoá gì, và nó đầy lên bằng các bản gần trùng của cùng một bản thu |
| P1b-2 | **Nội dung media đã xuất bản là bất biến** — không có nút "thay tệp", chỉ có "gỡ khỏi bộ chọn" | Giữ nguyên tham chiếu mà đổi file phía sau là sửa một đề đang có người thi qua cửa sau. Số version không đổi, nhưng thí sinh nghe một đoạn khác. Khoá này giữ **cả khi version đã gỡ xuất bản** — kết quả cũ vẫn trỏ tới nó |
| P1b-3 | **Đề thiếu media thì không duyệt và không xuất bản được**, nút khoá kèm lý do nêu rõ thiếu ở đâu | Cùng cửa từ chối mà pipeline ZIP đã có ở chặng 6, đặt ở chỗ đứng khác: trước khi xuất bản thay vì giữa lúc thi. Nộp duyệt **không** chặn — bản thu còn đang cắt là chuyện bình thường |
| P1b-4 | Kiểm tệp phía trình duyệt đọc **magic bytes**, không tin đuôi tên; nêu hạn mức **trước** khi chọn tệp | Nói thẳng trên màn rằng đây là để báo sớm, **không phải hàng rào an toàn** — máy chủ kiểm lại từ đầu. Thông điệp từ chối nêu hạng mục, không nêu ngưỡng |
| P1b-5 | Tải lên tính **SHA-256** và đọc **thời lượng** thật bằng chính trình duyệt sẽ phát nó | Phần client của một lần tải lên là làm được trọn vẹn ngay bây giờ, nên đã làm trọn vẹn |
| P1b-6 | 21 test cho luật media và nút bị khoá | Gồm luật khoá-kể-cả-khi-đã-gỡ-xuất-bản, và luật "xoá được chỉ khi chưa đề nào từng tham chiếu" |
| P1b-7 | Đo lại trên trình duyệt | Bắt được **2 cặp màu dưới 4.5**: `cms-link-inline` 4.46 trên nền dải xem trước, `--muted` 4.36 trên thẻ media thiếu. Đã sửa bằng bậc màu đậm hơn, đo lại **0 vi phạm** |

**Còn thiếu ở phía máy chủ, và đây là việc thật:** adapter object storage (MinIO/S3) sau cổng
`IObjectStorage` · endpoint tải lên có kiểm magic bytes, hạn mức, dò media · `MediaAsset` như một
thực thể có checksum và danh sách nơi đang dùng · thay `FixtureAssetStore` · dời ghi âm Speaking khỏi
GridFS. `M-31` chốt trước khi làm.

---

### Phase 1c · Tối giản bộ vai — ✅ 24/08/2026

`[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: *"hiện tại đang nhiều role quá tối giản thêm đi"*.

**Năm vai vận hành xuống ba**: `exam-author` · `academic-lead` · `admin`. → `C-25`

| | |
|---|---|
| Cắt vai, **không cắt quyền** | Bộ khoá quyền giữ nguyên. Vai là **túi quyền lưu dạng dữ liệu**, nên tách lại `content-manager` hay `support` sau này là **một dòng gieo thêm**, không phải một lần triển khai |
| `content-manager` gộp vào `admin` | Bài viết · tài liệu · nghe chép **không thứ nào tồn tại trước Phase 4** — vai này đang canh một cái cửa chưa có phòng |
| `support` gộp vào `admin` | Là chỗ **duy nhất ngoài admin** giữ `learner-content.read`. Gộp lại làm quyền đọc bài luận và ghi âm học viên **hẹp đi**, đúng hướng PDPL |
| `academic-lead` **không gộp** | Nó giữ đúng lý do chủ sản phẩm đã nêu: admin đẩy đề lên web nhưng chưa chắc có kiến thức IELTS. Bỏ nó là biến bước duyệt thành thủ tục rỗng. Chưa đủ người thì cấp cả hai vai cho một tài khoản |
| Ba test mới | Chốt số vai đúng bằng ba · người soạn không có `exam.review` · trưởng chuyên môn không có `exam.publish`. Nới bộ vai tới mức mất một trong hai ranh giới thì suite đỏ |

---

**Chưa chạy thật, và nói rõ trên từng màn:** máy chủ chưa có 3 trạng thái mới, chưa có `createdBy`,
chưa có endpoint nộp/duyệt. Ba màn hàng chờ chạy trên một store trong trình duyệt, và **mỗi màn đều
có banner nói đúng điều đó**. Khi endpoint có, các màn đổi `useWorkflow()` sang API — hình dạng dữ
liệu đã đúng theo đặc tả.

### Lộ trình 6 phase — thay cho "thứ tự còn lại" bên dưới

| Phase | Nội dung | Chặn bởi |
|---|---|---|
| 1 | Nền tảng: RBAC mở rộng · quyền theo phạm vi sở hữu · metadata · vòng đời 6 trạng thái · nhật ký | — |
| 2 | Đường nạp nội dung: JSON · ZIP 7 chặng · Media Library | ~~`Đ9`~~ — **không còn chặn**. [`cms-content-operations.md`](../ux/cms-content-operations.md) §11 kết luận `Đ9` chỉ quyết định *làm đường nào trước*, không quyết định kiến trúc: bốn nguồn nội dung đi vào cùng một cổng kiểm. Bắt đầu bằng đường JSON vì nó đúng với mọi câu trả lời. Phần media phía máy chủ vẫn chờ `M-31` |
| 3 | Trình soạn đề: 14 khuôn soạn · 4 kỹ năng · bảng kiểm · xem thử | — |
| 4 | CMS nội dung: bài viết · tài liệu · dictation | — |
| 5 | Thống kê: tỉ lệ đúng theo câu · phương án nhiễu · độ khó quan sát | Cần lượt thi thật |
| 6 | AI phân tích đề thô — vào **cùng một validator**, không có workflow riêng | API AI |

### Thứ tự còn lại

| Lát | Nội dung | Chặn bởi |
|---|---|---|
| 4 | **Nhập ZIP** — 7 chặng kiểm | ⚠️ **Không làm được ngay như dòng này từng ghi** (sửa 27/08). Đúng là lát gỡ nút *"chờ sếp đưa đề"*, nhưng ba thứ nó đứng lên trên đều chưa tồn tại trong mã: cổng **`IObjectStorage` chưa có** (MinIO nằm trong `compose.yaml`, không có adapter nào), **`fixtures/packages/` đang rỗng** trong khi [`zip-ingestion-security.md`](../security/zip-ingestion-security.md) §Testing giả định nó có gói độc hại, và `FixtureAssetStore` chỉ đăng ký ở Development. Nhập ZIP mà không có chỗ lưu audio là nửa tính năng — nên thứ tự đúng là **JSON → object storage → `MediaAsset` → ZIP**, như [`cms-content-operations.md`](../ux/cms-content-operations.md) §12 Phase 2 đã đưa ra |
| 5 | Đánh giá AI | API AI |
| 6 | Cấu hình | `B-5a`/`B-5b` chưa chốt |
| — | **AI phân tích đề thô** (`I-15a`) | API AI **và** một thiết kế riêng |

Ba màn còn ở trạng thái chờ — Đánh giá AI, Lịch sử gói, Cấu hình — **chờ thứ nằm ngoài repo**, không
phải chờ một quyết định kỹ thuật. Mỗi màn nêu đúng thứ nó chờ.

### Cần chủ sản phẩm

1. **Sếp sẽ đưa đề ở dạng gì?** Word/PDF thì phải chờ AI parsing. Dạng có cấu trúc (bảng câu hỏi +
   đáp án) thì dựng được đường nhập ngay, nhanh hơn nhiều so với chờ AI.
2. **Đăng nhập admin dùng chung tài khoản** — đã làm theo hướng này: một danh tính, một phiên,
   `apps/admin` từ chối render với tài khoản không có quyền CMS nào. Nói nếu bạn muốn tách.

---

## T4 · Stage 0 — Nền tảng triển khai — ✅ xong 20/08/2026

Chốt yêu cầu 20/08/2026 (`F-1`…`F-5`) mở khoá giai đoạn xây dựng. Master plan đầy đủ nằm ngoài repo;
phần dưới là những gì **bắt đầu được ngay** và **chắc chắn không phải làm lại**.

**Nguyên tắc chi phối:** chỉ dựng thứ không phụ thuộc vào quyết định còn mở. Chính sách chưa chốt thì
làm thành **khe cắm có cấu hình với triển khai rỗng** — không bao giờ bịa giá trị mặc định (`G-11`).

**Definition of Done**
- [ ] Monorepo pnpm workspaces · Node 24 · .NET 10 · lint · format · test runner
- [ ] `docker compose` chạy được: MongoDB **single-node replica set `rs0`** + MinIO
- [ ] Khung backend 5 project + **test kiến trúc chặn `Domain`/`Application` tham chiếu Mongo hoặc vendor** — và phải **kiểm chứng test đó fail** khi cố tình vi phạm
- [ ] `packages/design-system` sinh từ `DESIGN.md` hướng C — token màu, thang chữ sàn 14px, thang spacing 4px, ba lớp nền, hai chế độ mật độ
- [ ] CI xanh trên solution rỗng, gồm cả `check-docs.py`
- [ ] Ghi ADR cho `H-10` — `rs0` khắp nơi **và** trừ token bằng một cập nhật nguyên tử trên một document

**Không làm ở T4** (còn chặn): màn thi Reading/Listening/Writing/Speaking/Kết quả (`B-8` → T2) ·
trình soạn đề CMS (chưa chốt taxonomy, chưa có đặc tả màn) · adapter AI (chờ spike `V-11`).

---

## T2 · Dựng lại danh sách màn và luồng — 🔄 vẫn nợ, chặn Track A

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

### Giai đoạn A21 — Đăng ký xong là đăng nhập luôn — ✅ 27/08/2026

`[QUYẾT ĐỊNH]` chủ sản phẩm, 27/08/2026: *"xử lí phần register như sau tạo tài khoản với email pass
cho login như bình thường nhưng sẽ xác minh ở trang hồ sơ học sinh sau cũng được"*.

**Kiểm tra trước khi sửa, và kết quả đáng ghi lại:** phần lớn việc này **đã đúng sẵn ở backend**.
`LoginWithPassword` chưa bao giờ đọc `EmailVerified`; không có chỗ nào trong toàn bộ mã nguồn từ chối
một thao tác vì tài khoản chưa xác minh. Trang hồ sơ đã có trạng thái xác minh, nút gửi lại, và cả
đường sửa email khi chưa xác minh. Cái sai nằm ở **hai chỗ, và cả hai đều là chuyện nói dối**:

| # | Vấn đề | Đã làm gì |
|---|---|---|
| A21-1 | `POST /auth/register` trả 201 **không kèm token**, client dựng màn "Đã tạo tài khoản — kiểm tra hộp thư" chỉ có một nút quay lại tab đăng nhập | Register trả về đúng `SessionResponse` như `/login`, client gọi `adoptSession` và `RequireAnonymous` đưa vào trang chính. Học viên mới không phải gõ lại đúng thứ vừa gõ |
| A21-2 | Màn đó hứa một liên kết xác minh mà **không môi trường nào gửi** — `IVerificationMessageSender` chỉ có `LoggingVerificationMessageSender` | Cổng đổi kiểu trả về thành `MessageDelivery`. API trả `verificationEmailSent`, và trang hồ sơ hiện *"Chưa gửi được: hệ thống chưa nối dịch vụ email nào"* thay vì *"Đã gửi"* |
| A21-3 | Băng thông báo ở trang học sinh viết *"Một số tính năng sẽ mở sau khi bạn xác minh email"* | Đó là **một luật không tồn tại**. Sửa thành lời mời kèm link tới `/profile`. Luật thật thì chưa ai phát biểu → `M-45` |
| A21-4 | `/me/verify-email/resend` trả `202 Accepted` không kèm gì | Thành `200` kèm sự thật: `emailVerified` và `verificationEmailSent`. "Accepted" là lời hứa việc sẽ xảy ra, mà nó không xảy ra |

**Không tự quyết định phần chưa được hỏi.** Tài khoản chưa xác minh **bị hạn chế gì** là câu hỏi của
chủ sản phẩm — nó chạm entitlement (`T4`) và ghi công giới thiệu (`T13`) — nên **không cài hạn chế
nào**, và câu hỏi ghi thành **`M-45`** trong
[assumptions-and-open-questions.md](../requirements/assumptions-and-open-questions.md). → `G-11`

**Test:** 439 backend (`RegisterUserTests`, `ResendVerificationTests`, `RegistrationFlowTests` qua
HTTP + Mongo thật, và một test khoá *"tài khoản chưa xác minh vẫn đăng nhập bình thường"* trong
`LoginWithPasswordTests`) · 215 web (`register-signs-in.test.tsx`, và `contact-info.test.tsx` thêm
nhánh "không gửi được").

**Chưa làm, và biết rõ vì sao:** chưa chọn nhà cung cấp email — chạm `B-2`, và API vẫn **từ chối khởi
động ngoài Development** chừng nào chưa có sender thật.

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
