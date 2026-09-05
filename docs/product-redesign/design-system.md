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
