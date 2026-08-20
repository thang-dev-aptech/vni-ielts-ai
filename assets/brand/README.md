# Brand assets

| File | Nội dung |
|---|---|
| [`vni-education-logo.png`](vni-education-logo.png) | Logo VNI Education, 382×238, PNG có nền trong suốt |

Thư mục này nằm **ngoài `docs/ux/`** một cách có chủ ý. Nhận diện thương hiệu không đổi khi ngôn
ngữ thiết kế được làm lại, nên nó không nên bị xoá cùng lớp UX như lần 18/08/2026.

---

## Ba màu thương hiệu

| Màu | Mã dùng chính thức | Đo từ file PNG | Lệch |
|---|---|---|---|
| Xanh dương | `#2A6FB1` | `#286FB1` | 2/255 ở kênh đỏ |
| Cam | `#F48634` | `#F58634` | 1/255 ở kênh đỏ |
| Xanh lá | `#16AD54` | `#10AD54` | 6/255 ở kênh đỏ |

**Dùng cột "chính thức".** Đó là giá trị chủ sản phẩm lấy từ bản gốc trong Canva. Cột thứ hai là
màu đọc trực tiếp từ file PNG này — nó **xác nhận** cột thứ nhất, nhưng bản thân nó là một bản xuất
raster nên có sai lệch do chuyển đổi hồ sơ màu và khử răng cưa. Bản gốc thắng bản xuất.

Chênh lệch lớn nhất là 6/255 ở màu xanh lá, mắt thường không phân biệt được.

---

## Ràng buộc quan trọng: **không màu nào trong ba màu này dùng làm chữ được**

Đo trên nền sáng, ngưỡng WCAG cần đạt là **4.5**:

| Màu | Trên nền sáng | Chữ trắng trên nó | Kết luận |
|---|---|---|---|
| Xanh `#2A6FB1` | 4.96 | 5.24 | Đạt, nhưng **không có biên an toàn** — chỉ dùng mảng lớn, không dùng chữ nhỏ |
| Xanh lá `#16AD54` | **2.79** | **2.94** | **Trượt cả hai.** Chỉ tô nền, và chữ trên đó phải đen |
| Cam `#F48634` | **2.39** | **2.53** | **Trượt nặng nhất.** Chỉ tô nền, chữ trên đó phải đen |

Đây là chuyện bình thường: màu logo chọn để nhìn từ xa, màu giao diện chọn để đọc ở cỡ 16px trên
điện thoại ngoài nắng. Hai bài toán khác nhau.

**Hệ quả cho `DESIGN.md` mới:** muốn dùng ba màu này làm màu chữ thì phải **giữ nguyên góc hue và
hạ độ sáng** cho tới khi đạt ngưỡng. Đừng chọn màu mới bằng mắt — như vậy sẽ lệch khỏi nhận diện.

---

## Quy tắc dùng logo

- **Chữ `EDUCATION` màu xanh trên nền tối sẽ trượt tương phản** (3.46). Trên nền tối phải đổi chữ
  sang màu sáng; ba ô màu thì giữ nguyên vì chúng là hình khối, không phải chữ.
- Không đổi màu, không xoay, không thêm hiệu ứng, không đặt lên ảnh nền rối.
- Không đặt logo trong khung của phiên thi đang diễn ra — chỗ đó dành cho đồng hồ, trạng thái lưu
  và tên phần thi.

---

## Còn thiếu

- `[CẦN CÓ]` **File vector** (`.ai` / `.svg` / `.pdf`). Bản PNG 382×238 này quá nhỏ để in hoặc để
  hiển thị trên màn hình mật độ cao.
- `[CÂU HỎI]` Chữ `EDUCATION` trong logo nhìn đậm hơn ô `V` một chút. Đó là **màu thứ tư** hay vẫn
  là `#2A6FB1`? Hệ màu không cần giá trị này, nhưng đặc tả logo nên ghi cho đúng.
- `[CÂU HỎI]` VNI Education đã có bộ nhận diện quy định **font** chưa? Nếu có và nó bắt buộc một
  font, font đó **phải được kiểm có subset `vietnamese`** trước khi chấp nhận — `Outfit` từng
  trượt đúng bài kiểm này.
