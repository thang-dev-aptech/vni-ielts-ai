# VNI IELTS AI - Design System & Visual Language

Tài liệu này đặc tả toàn bộ hệ thống thẩm mỹ, bảng token, quy tắc thị giác và các thành phần giao diện dùng chung theo quyết định UX đã chốt ngày 2026-09-04 (`D-10`) và định hướng chuyển giao Duolingo adaptation.

---

## 1. Nguồn Chân Lý & Bảng Token Màu (Color Tokens)

Nguồn chân lý: `packages/design-system/src/tokens.css`.

| Tên Token | Mã Hex / Giá trị | Tỷ lệ tương phản đo được | Quy tắc áp dụng |
|---|---|---|---|
| `--primary` | `#06803a` | **5.05:1** trên chữ trắng | Nút hành động chính (Filled Primary CTA). Thay thế toàn bộ hard-code `--green-btn` và `.btn-primary` cũ |
| `--brand-green` | `#16ad54` | 2.79:1 trên trắng | **Chỉ dùng làm mảng khối đặc (solid blocks)**. Chữ trên nền này bắt buộc phải là `--ink` (`#17161a`). Không dùng làm màu chữ |
| `--brand-orange`| `#f48634` | 2.39:1 trên trắng | **Chỉ dùng làm mảng khối đặc**. Chữ trên nền này bắt buộc là `--ink`. Không dùng làm màu chữ |
| `--acc` | `#2867ac` | **5.5:1** trên trắng | Liên kết, thông tin, chip môn Reading, focus ring canvas |
| `--acc-soft` | `#eef4fb` | — | Nền nhấn nhẹ |
| `--ink` | `#17161a` | 16.7:1 trên `--page` | Tiêu đề chính, văn bản độ tương phản cao |
| `--ink-2` | `#4a4950` | 8.6:1 trên trắng | Văn bản thân bài (body text) |
| `--muted` | `#6b6a71` | 5.36:1 trên trắng | Nhãn phụ, siêu dữ liệu (metadata) |
| `--page` | `#f6f5f3` | — | Màu nền tổng thể trang |
| `--card` | `#ffffff` | — | Nền thẻ, bảng câu hỏi, modal |
| `--sunk` | `#faf9f7` | — | Nền vùng lõm, khối thông tin lót |
| `--line` | `#e6e4e0` | — | Đường viền chính |
| `--line-2` | `#efedea` | — | Đường phân cách phụ |
| `--warn` | `#9a4e07` | 5.4:1 trên `--warn-soft`| Cảnh báo thông tin (đồng hồ mức 2-3, thiếu từ) |
| `--warn-soft` | `#fdf1e3` | — | Nền cảnh báo |
| `--ok` | `#1e7a3c` | 4.7:1 trên `--ok-soft` | Thành công thực sự (đáp án đúng, đã lưu) |
| `--ok-soft` | `#e4f4e9` | — | Nền thành công |
| `--bad` | `#b3261e` | 5.6:1 trên `--bad-soft`| Chỉ dùng cho lỗi hỏng thực sự (mạng đứt, sai) |
| `--bad-soft` | `#fce9e7` | — | Nền lỗi |

### Quy tắc phân biệt Chấm điểm Tất định vs AI (`L4`):
- **Điểm tất định (Reading & Listening):** Viền xám liền nét (`solid 2px var(--line)` hoặc `var(--acc)`).
- **Điểm gợi ý từ AI (Writing & Speaking):** Khung viền nét đứt (`dashed 2px var(--line)` hoặc `var(--muted)`) kèm nhãn bắt buộc **"AI · tham khảo"**.

---

## 2. Kiểu Chữ (Typography)

- **Font giao diện chính:** **Nunito** (bắt buộc subset `vietnamese`, fallback `system-ui, sans-serif`).
- **Font số (Đồng hồ, Band, Bộ đếm từ):** **JetBrains Mono** với tính chất `tabular-nums` (ngăn đồng hồ bị giật độ rộng mỗi giây).
- **Thang kích thước chữ (Type Scale):**
  - `14px` (sàn tối thiểu cho tiếng Việt), `16px` (body), `18px`, `20px`, `24px`, `32px`, `44px`, `60px` (hiển thị band).
  - Chiều cao dòng (`line-height`): tối thiểu 1.5 cho chữ dưới 32px; 1.2 cho chữ ≥ 32px để tránh cắt dấu tiếng Việt.
  - Chữ hiển thị số lớn (Display): Nunito trọng số 800 từ 32px trở lên.

---

## 3. Thang Khoảng Cách (Spacing) & Bo Góc (Radius)

- **Spacing:** Hệ 4px chuẩn hóa: `4px`, `8px`, `12px`, `16px`, `24px`, `32px`, `48px`, `72px`. Tuyệt đối không dùng các số đo lẻ như 6, 10, 11, 14, 18, 22, 28px.
- **Bo góc (Border Radius):**
  - `--r-sm: 8px`: Dành cho ô nhập liệu (inputs), ô đáp án nhỏ.
  - `--r-md: 12px`: Dành cho nút bấm, card, pill điều hướng (Chuẩn Duolingo adaptation).
  - `--r-pill: 999px`: Dành cho chip trạng thái, tag kỹ năng.
- **Độ dày viền (Border Width):**
  - `--bw-2: 2px`: Áp dụng cho bề mặt tương tác, nút bấm, card nhấn và ô điểm số.
  - `1px`: Dành cho đường kẻ phân cách nội dung.

---

## 4. Đổ Bóng (Shadows) & Chuyển Động (Motion)

- **Bóng nút bấm & Card nhấn (Thickness Shadow):**
  `box-shadow: 0 4px 0 <darker-hue>`. Tạo cảm giác phím vật lý có độ dày cơ học, bấm xuống dịch chuyển `transform: translateY(2px)` và shadow giảm còn `0 2px 0`.
- **Bóng mờ nổi (Blurred Elevation Shadow):**
  `box-shadow: 0 12px 32px rgba(0, 0, 0, 0.12)`. Chỉ được phép dùng trên Dialog, Drawer và Popover menu. Không dùng bóng mờ trang trí trên các card nội dung.
- **Chuyển động (Motion):**
  `transition: all 180ms ease-in-out`.
  - Vô hiệu hóa toàn bộ chuyển động khi người dùng bật `prefers-reduced-motion`.
  - **Tuyệt đối không có chuyển động (animation) trong lúc học viên đang nhập câu trả lời hoặc làm bài thi**.

---

## 5. Phân Định Hai Không Gian (Register Split)

1. **Bên trong phòng thi (Inside sitting):**
   Giao diện tuyệt đối tĩnh lặng, nghiêm cẩn, bình tĩnh. Không có các mảng màu đặc sặc sỡ, không có chữ Display ngoại cỡ, không có sticker hay gamification.
2. **Bên ngoài phòng thi (Outside sitting):**
   Giao diện thân thiện, năng động, card trắng viền 2px, bóng cơ học `0 4px 0`, hiển thị điểm số rõ ràng và gợi ý hành động tiếp theo.

---

## 6. Đặc tả Linh vật VNI Bot (AI Learning Companion) & Chuẩn Giao diện `/practice` (2026 Redesign)

### 6.1. Nhận diện thương hiệu Linh vật VNI Bot:
- **Tên**: VNI Bot — AI Learning Companion (Luôn đồng hành cùng bạn trên hành trình chinh phục IELTS).
- **Hình thể**: Robot bo tròn thân thiện màu trắng sữa viền xanh lá, tỷ lệ đầu/thân khoảng 120%.
- **Chi tiết nhận diện**:
  - **Màn hình khuôn mặt**: Kính đen hiển thị LED biểu cảm cảm xúc (Bình thường, Vui vẻ, Nháy mắt, Ngạc nhiên, Suy nghĩ, Phấn khích).
  - **Tai nghe & Chi tiết đỉnh đầu**: Tai nghe tròn xanh lá (`#10b050` / `#059669`) và mầm lá xanh trên đỉnh đầu tượng trưng cho sự tăng trưởng học tập.
  - **Ngực áo**: Biểu tượng 3 màu thương hiệu VNI (Cam, Vàng, Xanh lá).
- **Hệ thống tư thế (Poses)**: Chào mừng, Chỉ dẫn, Đọc sách, Suy nghĩ, Giải thích, Tương tác với laptop, Ăn mừng.
- **Tệp tài nguyên chuẩn**: Đặt tại `apps/web/public/brand/mascot/` (`vni-bot-hero.webp`, `vni-bot-assistant.webp`, `vni-bot-character-sheet.jpg`).

### 6.2. Cấu trúc chuẩn của trang `/practice` (Luyện 4 kỹ năng — Background Artwork & 4-Block Standard):
Trang được thiết kế tối ưu hóa mật độ thị giác (Visual Density) bằng hệ thống nét vẽ vector nền (Background Vector Artwork), dải màu chuyển tiếp và các nét kỹ thuật ở 2 bên lề nhằm khắc phục hoàn toàn tình trạng nền bị trắng quá trên màn hình lớn (1440px - 2048px), đồng thời giữ các thẻ nội dung gọn gàng, tinh tế:

1. **Hệ thống Nét vẽ Nền & Hiệu ứng Môi trường (Background Canvas & Vector Strokes)**:
   - **Nền chuyển sắc đa phổ (Mesh Gradient)**: Nền ngọc bích nhạt `#f6faf7` phối kết hợp 5 quầng sáng quang phổ đa điểm (`glow-top-left`, `glow-top-right`, `glow-mid-left`, `glow-mid-right`, `glow-bottom-center`) cùng lưới chấm siêu mịn 32px.
   - **Canvas nét vẽ vector uốn lượn SVG (`.prac-v2-bg-strokes-canvas`)**:
     - Các dải nét ribbon mềm mại chuyển màu gradient (`#059669` -> `#0ea5e9` -> `#6366f1`) chạy uốn lượn xuyên suốt từ đỉnh Hero, luồn qua các card kỹ năng và hội tụ về phía CTA Banner.
     - Dải nét đôi song hành kết hợp nét liền và nét đứt (`stroke-dasharray="8 8"`), tạo cảm giác đường đua bứt phá điểm số và kết nối dữ liệu AI.
   - **Nét đồ họa kỹ thuật 2 bên lề (`.prac-v2-margin-decor`)**:
     - Hiển thị trên màn hình rộng (≥ 1380px đến 2048px): Thước đo band điểm (`6.5` -> `8.5+`), tọa độ hệ thống `[ LAT 21.0285° N · VNI IELTS ]`, vòng tròn quỹ đạo công nghệ (Orbital Ring), biểu tượng sóng âm (Soundwaves) và các dấu tâm ngắm kỹ thuật (`+`).

2. **Khối 1: Hero Section & 3D Artwork**:
   - Eyebrow pill `📊 Luyện IELTS toàn diện ✨ 2026 Edition`.
   - H1 3 dòng chuẩn typographic: `Luyện 4 kỹ năng` / `IELTS hiệu quả` / `cùng AI thông minh`.
   - Nút hành động chính: `Bắt đầu luyện tập →` (Pill gradient green) + `Tìm hiểu chi tiết` (Secondary outline).
   - Dải chứng thực học viên (Social Proof): Avatar stack + `10,000+ sĩ tử đang luyện tập mỗi ngày`.
   - 3D Mascot Artwork: VNI Bot tương tác bàn học với 2 thẻ kính nổi tương tác (Floating Glass Chips):
     - `🎯 Target Band 7.5 - 8.5 IELTS`
     - `✨ Chấm AI tức thì (Phản hồi dưới 30s)` kèm chấm đèn xung nhịp (Pulse Dot).
   - Thanh 4 cam kết giá trị ngang (Bám sát đề thi thật, Phân tích bằng AI, Lộ trình cá nhân hóa, Linh hoạt mọi lúc).

3. **Khối 2: Lưới 4 Thẻ kỹ năng riêng biệt (Reading, Listening, Writing, Speaking)**:
   - Thẻ nền trắng nổi bật với viền `1.5px solid #edf2f7`, hover nhấc nổi `translateY(-6px)` viền sáng xanh ngọc.
   - Huy hiệu đặc quyền (Badge góc thẻ): `Phổ biến nhất`, `Audio chuẩn bản xứ`, `AI Chấm tức thì`, `AI Phân tích phát âm`.
   - Icon SVG bo góc chuyên biệt cho từng kỹ năng (Sách xanh, Tai nghe cam, Bút tím, Micro đỏ hồng).
   - Danh sách thẻ tag dạng bài (Topic Tags: Matching Headings, True/False/NG, Task 1, Speaking Part 1-3...).
   - Đếm số bài luyện và nút điều hướng nhanh `Vào luyện →`.

4. **Khối 3: Bộ đôi Khối 2 cột (AI Assistant & 3 Bước luyện tập)**:
   - **Cột trái**: Thẻ trợ lí AI nền gradient mint mềm, 5 tiêu chí chấm thi, chip nhận diện độ chính xác `🤖 98.5% Khớp tiêu chí chuẩn IELTS`, minh họa VNI Bot nháy mắt vẫy tay.
   - **Cột phải**: Quy trình 3 bước (1. Chọn kỹ năng ~30s -> 2. Làm bài tập 20-60m -> 3. Xem kết quả tức thì) với đường kết nối gradient dọc (`.prac-v2-step-connector`) và hộp gợi ý (Hint box).

5. **Khối 4: CTA Banner & Footer**:
   - Container bo góc `26px`, nền gradient xanh ngọc sâu `#047857` -> `#065f46`, họa tiết quầng sáng kép, 3 cam kết dạng checklist và nút `Bắt đầu luyện tập ngay →`.

---

## 7. Đặc tả Giao diện Trang Danh sách Bộ đề (`/students/practice` — 2026 Redesign)

### 7.1. Cấu trúc tổng thể & Điều hướng (App Shell Context)
- **Vị trí**: Nằm bên trong `DashboardShell` dành cho học sinh đã đăng nhập.
- **Thanh điều hướng bên (Sidebar)**: Mục `Luyện 4 kỹ năng` hiển thị trạng thái đang chọn (Active, nền xanh ngọc bo góc mềm).
- **Thanh điều hướng phân cấp (Breadcrumb)**:
  `Trang chủ` (link tới `/students/dashboard`) > `Luyện 4 kỹ năng` (link tới `/students/practice`) > `Danh sách bộ đề` (Trang hiện tại).

### 7.2. Khu vực Tiêu đề & Ô tìm kiếm (Header & Realtime Search)
- **Eyebrow**: `Luyện IELTS` — Chữ đậm màu xanh lá thương hiệu `#059669`.
- **Tiêu đề H1**: `Danh sách bộ đề` — Cỡ chữ 32px, font-weight 800, màu than đen `#0f172a`.
- **Mô tả (Lead)**: `Chọn bộ đề phù hợp với trình độ và mục tiêu của bạn. Mỗi bộ đề được thiết kế bám sát cấu trúc đề thi thật, giúp bạn luyện tập hiệu quả và tiến bộ rõ ràng.` (màu `#64748b`, max-width ~660px).
- **Ô tìm kiếm góc phải (Real-time Search Box)**:
  - Kiểu dáng: Viên thuốc (Pill shape, border-radius 9999px), nền trắng `#ffffff`, viền `1.5px solid #e2e8f0`.
  - Icon kính lúp xám `#64748b` bên trái.
  - Placeholder: `Tìm tên bộ đề, chủ đề...`
  - Hỗ trợ lọc tức thì (Real-time filtering) theo tên bộ đề, chủ đề hoặc mô tả và nút xóa nhanh `✕`.

### 7.3. Khối 1: Khám phá bộ đề (4 Thẻ Danh mục trực quan)
Bố cục lưới 4 cột ngang với 4 nhóm bộ đề được mã hóa màu sắc đồng bộ:
1. **Cambridge IELTS**:
   - Icon: Mũ cử nhân (Graduation Cap), hộp icon nền xanh dương nhạt `#e0f2fe`, icon xanh `#0284c7`.
   - Tiêu đề: `Cambridge IELTS` | Mô tả: `Bộ đề chính thức từ Cambridge`.
   - Chân thẻ: `18 bộ đề` + Nút tròn mũi tên xanh lá (`#dcfce7`, icon `#16a34a`).
2. **IELTS Practice Tests**:
   - Icon: Tập tài liệu (Document), hộp icon nền cam đào `#ffedd5`, icon cam đậm `#ea580c`.
   - Tiêu đề: `IELTS Practice Tests` | Mô tả: `Bộ đề luyện tập tổng hợp`.
   - Chân thẻ: `12 bộ đề` + Nút tròn mũi tên xanh lá.
3. **Bộ đề theo chủ đề**:
   - Icon: Tâm ngắm tiêu điểm (Target / Bullseye), hộp icon nền hồng đào `#ffe4e6`, icon đỏ hồng `#e11d48`.
   - Tiêu đề: `Bộ đề theo chủ đề` | Mô tả: `Luyện tập theo từng chủ đề phổ biến`.
   - Chân thẻ: `8 bộ đề` + Nút tròn mũi tên xanh lá.
4. **Đề thi thử**:
   - Icon: Ngôi sao (Star), hộp icon nền vàng hổ phách `#fef3c7`, icon vàng đậm `#d97706`.
   - Tiêu đề: `Đề thi thử` | Mô tả: `Mô phỏng đề thi thật với đầy đủ 4 kỹ năng`.
   - Chân thẻ: `6 bộ đề` + Nút tròn mũi tên xanh lá.
- **Tương tác**: Bấm vào bất kỳ thẻ danh mục nào sẽ tự động kích hoạt bộ lọc danh mục tương ứng ở Khối 2 và cuộn mượt (smooth scroll) xuống danh sách bộ đề.

### 7.4. Thanh phân loại theo từng kỹ năng (Skill Selector Tabs)
- Nằm ngay dưới tiêu đề hoặc tích hợp linh hoạt:
  - **Tất cả kỹ năng** (Icon Full Test)
  - **Reading (Đọc)** (Icon cuốn sách, tone xanh dương `#2563eb`)
  - **Listening (Nghe)** (Icon tai nghe, tone cam san hô `#ea580c`)
  - **Writing (Viết)** (Icon ngòi bút, tone tím violet `#9333ea`)
  - **Speaking (Nói)** (Icon micro, tone đỏ hồng `#e11d48`)
- Đồng bộ trực tiếp với tham số URL: `?skill=reading`, `?skill=listening`, `?skill=writing`, `?skill=speaking`. Khi chọn kỹ năng, thẻ bộ đề tự động lọc các đề thi tương thích và hiển thị nhãn kỹ năng đang lọc.

### 7.5. Khối 2: Tất cả bộ đề (All Sets Explorer)
- **Thanh công cụ lọc & điều khiển (Toolbar)**:
  - **Nhóm nút lọc danh mục (Pills)**: `Tất cả` (Active: xanh lá `#059669`, chữ trắng), `Cambridge`, `Luyện tập`, `Theo chủ đề`, `Đề thi thử`.
  - **Dropdown Sắp xếp**: `Sắp xếp: Mới nhất` (các tùy chọn: `Mới nhất`, `Tên A-Z`, `Nhiều đề nhất`).
  - **Chuyển đổi Chế độ xem (View Toggle)**: Nút Lưới (Grid view — Active) và Nút Danh sách (List view).
  - **Thanh trạng thái lọc kỹ năng**: Hiển thị chip kỹ năng đang chọn kèm nút xóa lọc.
- **Cấu trúc Thẻ bộ đề (Set Card Anatomy)**:
  - **Hộp icon & Tiêu đề**: Icon theo tone của danh mục + Tên bộ đề (e.g. `Cambridge IELTS 17`, `IELTS Practice Tests Vol. 1`, `Bộ đề theo chủ đề Education`, `Đề thi thử - Full Test 1`).
  - **Huy hiệu phân loại (Badge Pill)**:
    - `Chính thức`: Xanh lá mềm (`#ecfdf5`, chữ `#059669`).
    - `Luyện tập`: Xanh dương mềm (`#eff6ff`, chữ `#2563eb`).
    - `Theo chủ đề`: Cam đất mềm (`#fffbeb`, chữ `#d97706`).
    - `Thi thử`: Tím mềm (`#f5f3ff`, chữ `#7c3aed`).
  - **Đoạn mô tả**: Tóm tắt ngắn gọn nội dung và định hướng luyện tập của bộ đề.
  - **Huy hiệu kỹ năng (Skill Chips Mini)**:
    - Hiển thị các nhãn kỹ năng có trong bộ đề: `Reading`, `Listening`, `Writing`, `Speaking` với màu sắc và biểu tượng riêng biệt. Kỹ năng đang lọc được highlight viền nổi bật.
  - **Dòng thông số kỹ thuật (Meta Rows)**:
    - Icon văn bản: Hiển thị số lượng đề (e.g. `4 đề thi`, `6 đề thi`, `10 đề thi`, `1 đề thi`).
    - Icon biểu đồ cột: Thang đo trình độ (e.g. `Trình độ: Cơ bản - Trung bình`, `Trình độ: Trung bình - Nâng cao`, `Trình độ: Nâng cao`).
  - **Nút hành động chính**: Nút full-width viền sáng `Xem chi tiết →` (hoặc `Luyện [SKILL] ngay →`) điều hướng trực tiếp vào trang chi tiết bộ đề `/students/practice/sets/:setId`.
- **Tích hợp dữ liệu**:
  - Tự động đồng bộ các bộ đề thực tế được nạp từ API (`usePracticeHierarchy`) lên đầu danh sách và liên kết trực tiếp vào các phòng thi.

### 7.6. Thanh phân trang (Pagination)
- Nút lùi trang `← Trước`, các số trang `1`, `2`, `3`, `4`, `5` (trang active có nền tròn xanh lá `#059669`), nút tiến trang `Sau →`.

### 7.7. Banner kết nối Workspace luyện tập
- Callout banner đáy trang điều hướng trực tiếp vào không gian làm bài chi tiết theo từng kỹ năng hoặc thi thử tính giờ: `/students/practice/workspace`.




