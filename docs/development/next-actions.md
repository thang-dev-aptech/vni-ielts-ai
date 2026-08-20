# Next Actions — hàng đợi công việc

> Tài liệu này viết bằng tiếng Việt vì đây là **checklist vận hành cho người thực thi**, không phải tài liệu kiến trúc. Toàn bộ `docs/` còn lại là tiếng Anh.

**Cách dùng:** làm **đúng một task** mỗi lần. Xong → đối chiếu Definition of Done → báo cáo → **dừng lại**. Không tự động nhảy sang task kế tiếp.

Session mới chỉ cần đọc file này. Mọi thứ cần biết đều nằm ở đây hoặc trong link.

---

## Trạng thái

| | |
|---|---|
| Phase hiện tại | **Phase 4 — Nền tảng triển khai** (Phase 2 chốt yêu cầu đã diễn ra 20/08/2026) |
| Task đang mở | **T5 giai đoạn B · Engine thi qua API** — theo [ADR-0012](../decisions/0012-learner-first-sequencing.md) |
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
thái đổi, **đè lên** đích người dùng định vào. Ai mở link tới `/ho-so` khi chưa đăng nhập thì sau khi
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

### Giai đoạn B — Engine thi (API trước, màn hình sau)

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
| C1 | **Không có khoá theo tài khoản khi đăng nhập sai nhiều lần** | Rate limit giờ nới lỏng có chủ ý (120/phút) để không khoá nhầm cả dải NAT — nên **credential stuffing nhắm một tài khoản không bị chặn**. Cần bộ đếm sai theo tài khoản ở tầng ứng dụng, nơi biết địa chỉ email | **CAO** — `T4`/`T5` |
| C2 | Idempotency **đệm toàn bộ response trong RAM** | Không giới hạn kích thước. Response hiện nhỏ, nhưng một `ExamVersion` 40 câu thì không | TRUNG BÌNH |
| C3 | **Không có test tự động cho middleware** | Idempotency, rate limit, `X-Server-Time` mới chỉ kiểm bằng `curl` tay. Refactor sẽ không ai bắt được | **CAO** |
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
