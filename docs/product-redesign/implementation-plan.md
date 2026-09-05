# VNI IELTS AI - Complete Learner Product Redesign Implementation Plan

Tài liệu này lưu trữ kế hoạch triển khai đầy đủ của dự án tái thiết kế ứng dụng học viên VNI IELTS AI.

## Các giai đoạn triển khai:

1. **Phase 1: Nền tảng thiết kế & Điều hướng (Foundation, Tokens & Navigation)**
   - Cập nhật Design Tokens (`packages/design-system/src/tokens.css`) với `--primary: #06803a` và đồng bộ styles.
   - Chuẩn hoá `DashboardShell.tsx` với 3 nhóm rail (`Học tập`, `Tài nguyên`, `Tài khoản`), ẩn chuông thông báo, rail thu gọn 56px icon-only, mobile drawer.
   - Tạo tuyến đường `/progress` độc lập, chuyển hướng `/profile?tab=progress` sang `/progress`, đổi `/profile` thành "Tài khoản & bảo mật".

2. **Phase 2: Trang chủ học viên (Learner Home `/students/dashboard`)**
   - Loại bỏ 4 card kỹ năng lặp lại.
   - Thiết lập 6 khối nội dung ưu tiên: Tiếp tục bài đang làm (`InProgressPanel`) -> Bước tiếp theo (`getCoachingAdvice`) -> Mục tiêu & khoảng cách -> Hoạt động -> Kết quả gần đây -> Tài nguyên.

3. **Phase 3: Danh mục Luyện đề & Thi thử (`/practice`)**
   - Bộ điều khiển phạm vi: Một kỹ năng vs Full Test.
   - Bộ chọn kỹ năng `SkillSelector` lưới 2x2 dưới 640px.
   - Phân biệt nút Luyện đề vs Thi thử.
   - Hộp thoại chuẩn bị Full Test (Readiness Dialog).
   - Khóa Entry test ở trạng thái S4.

4. **Phase 4: Vỏ thi cử & Thanh điều khiển an toàn (Exam Shell Safety)**
   - Header máy tính 1 dòng 64px, di động đúng 2 dòng 44px.
   - Trạng thái lưu câu trả lời trung thực (Đã lưu, Đang gửi, Chưa gửi được, Gửi thất bại).
   - Dải tiến trình Full Test và nút chuyển kỹ năng tường minh "Tiếp theo: [Skill]" kèm modal xác nhận.
   - Footer câu hỏi thu gọn trên mobile.

5. **Phase 5: Không gian Reading & Listening**
   - Reading desktop 50/50 độc lập cuộn; mobile tab Bài đọc / Câu hỏi.
   - Listening audio bar full-width không có thanh tua; hỗ trợ chọn đáp án bằng phím song song kéo thả.

6. **Phase 6: Không gian Writing**
   - Desktop chia đôi 40% đề / 60% soạn thảo; ảnh giới hạn 40vh có nút xem lớn.
   - Khung soạn thảo không bị che khuất; bộ đếm từ màu `--warn` không chặn nộp bài.

7. **Phase 7: Không gian Speaking**
   - State machine ghi âm hiển thị rõ ràng từng câu hỏi.
   - Part 2 cue card có đồng hồ chuẩn bị 1 phút độc lập.

8. **Phase 8: Trải nghiệm Kết quả & Phản hồi AI**
   - Bài đơn kỹ năng không hiển thị Overall giả định; donut Đúng/Sai/Chưa làm.
   - Full Test từng phần hiển thị bảng 4 hàng trạng thái rõ ràng.
   - Review câu hỏi có bộ lọc và giải thích theo ngữ cảnh.
   - Phản hồi AI có cấu trúc trong khung viền nét đứt "AI · tham khảo".

9. **Phase 9: Các Module Phụ & Kiểm Thử**
   - Tinh giản Dictation, Documents, Articles.
   - Hoàn thiện báo cáo nghiệm thu và kiểm thử tự động toàn diện.
