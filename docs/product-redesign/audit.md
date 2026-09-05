# VNI IELTS AI - Learner Experience Baseline Audit

Tài liệu này ghi nhận kết quả đánh giá toàn diện hiện trạng ứng dụng học viên `apps/web` và các gói dùng chung (`packages/design-system`, `packages/ui`) làm cơ sở thực hiện kế hoạch tái thiết kế theo tài liệu [VNI_IELTS_AI_COMPLETE_REDESIGN_PROMPT.md](../VNI_IELTS_AI_COMPLETE_REDESIGN_PROMPT.md) và các quyết định UX đã chốt ngày 2026-09-04 (`D-1` đến `D-12`).

---

## 1. Phân loại tổng thể thành phần (Component Inventory Classification)

| Phân loại | Thành phần / File liên quan | Lý do & Căn cứ kỹ thuật |
|---|---|---|
| **Preserve** (Bảo toàn) | `features/auth/AuthContext.tsx`, `routes/RequireAuth.tsx`, `lib/session.ts`, `lib/storage.ts`, `lib/api.ts`, `features/exam/examApi.ts`, `features/exam/useAnswerSheet.ts`, `features/exam/practice-runner/usePracticeClock.ts`, `features/exam/practice-runner/sessionProjection.ts`, `features/exam/SpeakingRecorder.tsx` (state machine & upload), `features/exam/recordingDraft.ts`, `features/exam/AudioPlayer.tsx`, `features/exam/skills.ts`, `i18n/*` structure | Tầng lõi nghiệp vụ, logic quản lý phiên thi, đồng hồ máy chủ, nhật ký offline, thuật toán chống xung đột bản ghi và bẫy lỗi đã vượt qua 31 bộ kiểm thử tự động (308 tests). |
| **Improve** (Cải tiến) | `features/chrome/DashboardShell.tsx`, `features/chrome/SiteHeader.tsx`, `features/student/DashboardState.tsx`, `features/exam/practice-runner/PracticeHeader.tsx`, `features/exam/practice-runner/PracticeFooter.tsx`, `packages/design-system/src/tokens.css` | Khung điều hướng và header/footer cần điều chỉnh layout, chuẩn hóa token `--primary`, hỗ trợ thu gọn 56px icon-only, và cấu trúc 2 dòng trên mobile. |
| **Redesign** (Tái thiết kế) | `features/student/StudentDashboardPage.tsx`, `features/exam/PracticePage.tsx`, `features/exam/ExamResultsPage.tsx`, `features/profile/ProfilePage.tsx` | Trang chủ đang lặp 4 card kỹ năng "Vào luyện"; Practice cần phân định 2 chiều Scope (Một kỹ năng vs Full Test) và Experience (Luyện đề vs Thi thử); Results cần bỏ Overall ảo trên bài thi đơn kỹ năng; Profile cần đổi thành "Tài khoản & bảo mật". |
| **Consolidate** (Hợp nhất) | `apps/web/src/styles/landing.css`, `packages/design-system/src/tokens.css`, `features/student/StudentIcons.tsx` | Thu gọn các biến màu, bóng đổ, bo góc rải rác về hệ thống tokens chuẩn (`D-10`). |
| **Hide until functional** (Ẩn chờ endpoint) | `features/chrome/NotificationMenu.tsx` (chuông thông báo), `features/student/AiChatPanel.tsx` (khung chat AI) | Chưa có notification endpoint; AI Chat chưa chốt quy tắc trừ token và ngữ cảnh (`B-6a–e`) -> gán badge "Xem trước" và khóa composer minh bạch lý do. |
| **Remove** (Loại bỏ) | In-page anchors `#results`, `#coming` trên rail điều hướng, 4 card kỹ năng lặp lại với nút "Vào luyện" trên dashboard | Phá vỡ phân cấp điều hướng, gây nhiễu nhận thức của học viên (`D-1`, `D-4`). |
| **Requires Decision** (Chờ chủ sản phẩm) | Seams: `B-13` (kết hợp Practice/Mock × Full/Single), `M-39` (hiển thị đáp án sau Mock), `H-4` (bảng raw-to-band IELTS), `H-1` (Speaking 1 sitting vs 3 parts), `B-5a/b` (token pricing/balance) | Giữ nguyên seam cấu hình, không tự bịa logic hay số liệu (`D-12`). |

---

## 2. Các phát hiện kiểm toán chi tiết (Audit Findings)

### AUDIT-01: Hệ thống điều hướng sau đăng nhập không đồng nhất và thừa neo ảo
- **Severity**: HIGH
- **Route**: `/students/dashboard`, `/practice`, `/profile`
- **Viewport**: Desktop (1440x900) & Mobile (390x844)
- **Observed Evidence**: Rail trái chứa `#results` và `#coming` dẫn đến các neo trong trang thay vì trang thực tế; chuông thông báo hiển thị nhưng không có backend endpoint xử lý. Khi thu gọn rail trên desktop, nhãn bị cắt cụt; trên mobile thanh điều hướng chưa tinh giản đúng mức.
- **Related Component**: `apps/web/src/features/chrome/DashboardShell.tsx`, `SiteNav.tsx`
- **User Impact**: Học viên bị bối rối giữa trang thật và neo cuộn; tính năng thông báo tạo kỳ vọng ảo gây mất lòng tin.
- **Recommendation**: Triển khai `D-1`: rail 3 nhóm có nhãn rõ ràng (Học tập, Tài nguyên, Tài khoản); ẩn chuông thông báo; thu gọn desktop còn 56px icon-only có `aria-label` và tooltip; mobile dùng drawer chuẩn a11y.
- **Category**: Navigation & Accessibility

### AUDIT-02: Thiếu tuyến đường `/progress` thực sự
- **Severity**: MEDIUM
- **Route**: `/progress` -> bị redirect về `/profile?tab=progress`
- **Viewport**: Mọi viewport
- **Observed Evidence**: `/progress` hiện chỉ là một tab bị giấu sâu trong trang Profile; trong khi trang Profile lại mở mặc định vào phần bảo mật/đổi mật khẩu.
- **Related Component**: `apps/web/src/routes/paths.ts`, `apps/web/src/App.tsx`, `features/profile/ProfilePage.tsx`
- **User Impact**: Học viên muốn xem tiến độ học tập và band mục tiêu nhưng lại bị chuyển vào trang cài đặt mật khẩu.
- **Recommendation**: Thực hiện `D-3`: tạo trang `/progress` thực sự chứa `GoalCoachingPanel`, `StreakPanel`, lịch sử bài thi và hành động tiếp theo; đổi hướng ngược lại `/profile?tab=progress` -> `/progress`. Trang `/profile` đổi tên thành "Tài khoản & bảo mật".
- **Category**: Navigation & Information Architecture

### AUDIT-03: Trang chủ học viên lặp card kỹ năng và thiếu phân cấp hành động
- **Severity**: HIGH
- **Route**: `/students/dashboard`
- **Viewport**: 1440x900, 1024x768, 390x844
- **Observed Evidence**: Xuất hiện 4 card kỹ năng (Reading, Listening, Writing, Speaking) đều có nút "Vào luyện" chiếm trọn tiêu điểm màn hình; thiếu chỉ dẫn rõ ràng cho bài đang làm dở hoặc bước học tập tiếp theo.
- **Related Component**: `apps/web/src/features/student/StudentDashboardPage.tsx`
- **User Impact**: Trang chủ trông như danh mục tính năng thay vì một trang học tập hướng hành động; học viên không biết nên làm gì tiếp theo.
- **Recommendation**: Thực hiện `D-4`: Xóa bỏ 4 card lặp lại; sắp xếp 6 khối ưu tiên: 1. Tiếp tục bài đang làm (`InProgressPanel`) -> 2. Bước tiếp theo (`getCoachingAdvice`) -> 3. Mục tiêu và khoảng cách (`GoalCoachingPanel compact`) -> 4. Hoạt động (`StatStrip` + `StreakPanel`) -> 5. Kết quả gần đây (max 5) -> 6. Tài nguyên nhỏ gọn.
- **Category**: Presentation & UX

### AUDIT-04: Trang danh mục `/practice` chưa phân tách rõ Scope và Experience
- **Severity**: HIGH
- **Route**: `/practice`
- **Viewport**: Mọi viewport
- **Observed Evidence**: Các nút "Luyện đề" và "Thi thử" đặt cạnh nhau mà không có giải thích phân định; trên mobile danh sách kỹ năng bị cuộn ngang tràn màn hình.
- **Related Component**: `apps/web/src/features/exam/PracticePage.tsx`
- **User Impact**: Người học nhầm lẫn giữa Luyện đề tự do (được tạm dừng, đếm giờ tăng) và Thi thử (đếm ngược nghiêm ngặt, chấm AI).
- **Recommendation**: Thực hiện `D-5`: Bộ điều khiển Scope (Một kỹ năng | Full Test) + `SkillSelector` lưới 2x2 trên mobile; card bài thi thể hiện đúng thứ tự nút; bổ sung Modal chuẩn bị Full Test (Readiness Dialog); khóa Entry-test ở trạng thái S4.
- **Category**: Presentation & UX

### AUDIT-05: Header phòng thi bị vỡ và rớt dòng trên Mobile
- **Severity**: CRITICAL
- **Route**: `/exam/:sessionId`, `/practice/:sessionId`
- **Viewport**: Mobile (< 390px và 390x844)
- **Observed Evidence**: Các nút điều khiển như "Dừng đồng hồ", "Mốc mục tiêu", "Rời khỏi" bị rớt dòng thành 3-4 hàng, che khuất một phần nội dung bài làm và đồng hồ.
- **Related Component**: `apps/web/src/features/exam/practice-runner/PracticeHeader.tsx`
- **User Impact**: Gây căng thẳng, hoảng loạn cho học viên trong phòng thi, vi phạm trực tiếp nguyên tắc "Giao diện phòng thi phải bình tĩnh".
- **Recommendation**: Thực hiện `D-6`: Thiết kế header mobile đúng 2 dòng cố định (44px mỗi dòng). Dòng 1 chứa chế độ, kỹ năng và đồng hồ; Dòng 2 chứa các pill action (Tạm dừng, Mục tiêu, Rời khỏi) và trạng thái lưu `Đã lưu`. Màn hình < 360px chuyển thành icon-only kèm `aria-label`.
- **Category**: Presentation, Responsive & Accessibility

### AUDIT-06: Chuyển kỹ năng trong Full Test thiếu cảnh báo tính không thể hoàn tác
- **Severity**: HIGH
- **Route**: `/exam/:sessionId` (Full Test)
- **Viewport**: Mọi viewport
- **Observed Evidence**: Nút "Tiếp theo" không ghi rõ chuyển sang kỹ năng nào và không có modal tổng kết/cảnh báo học viên sẽ không thể quay lại phần thi đã chốt.
- **Related Component**: `apps/web/src/features/exam/practice-runner/PracticeRunnerPage.tsx`, `PracticeFooter.tsx`
- **User Impact**: Học viên có thể lỡ tay bấm chuyển kỹ năng và mất cơ hội làm tiếp các câu hỏi chưa trả lời.
- **Recommendation**: Thực hiện `D-7`: Đổi nhãn nút thành "Tiếp theo: Listening" (hoặc kỹ năng kế tiếp); mở modal xác nhận hiển thị rõ số câu đã làm, số câu chưa làm, cảnh báo không thể quay lại; trạng thái "Đang chốt [Skill]... mở [NextSkill]".
- **Category**: Business Logic Preservation & UX Safety

### AUDIT-07: Kết quả bài thi đơn kỹ năng hiển thị Overall IELTS giả định
- **Severity**: HIGH
- **Route**: `/exam/:sessionId/results`
- **Viewport**: Mọi viewport
- **Observed Evidence**: Khi làm xong 1 kỹ năng (ví dụ Reading), giao diện vẫn dành một ô to để hiển thị điểm Overall hoặc ước lượng không có cơ sở.
- **Related Component**: `apps/web/src/features/exam/ExamResultsPage.tsx`
- **User Impact**: Đưa thông tin sai lệch về điểm thi IELTS thực tế (Overall bắt buộc phải đủ cả 4 kỹ năng hợp lệ).
- **Recommendation**: Thực hiện `D-9`: Bài thi đơn kỹ năng không hiển thị khối Overall; hiển thị Donut (Đúng / Sai / Chưa làm), Band chỉ hiện khi có bảng quy đổi hợp lệ (`H-4`); cung cấp 3 nút hành động thiết thực ("Xem lại câu sai", "Làm đề khác", "Luyện dạng câu này").
- **Category**: Scoring Integrity & Presentation

### AUDIT-08: Độ tương phản màu sắc và token thương hiệu
- **Severity**: MEDIUM
- **Route**: Toàn bộ ứng dụng
- **Viewport**: Mọi viewport
- **Observed Evidence**: Nút `--green-btn` dùng chữ trắng trên nền `#10b050` (tỷ lệ tương phản chỉ đạt 2.86:1, vi phạm chuẩn WCAG 4.5:1).
- **Related Component**: `packages/design-system/src/tokens.css`, `landing.css`
- **User Impact**: Học viên có thị lực kém hoặc dùng màn hình độ sáng thấp khó đọc nhãn nút chính.
- **Recommendation**: Thực hiện `D-10`: Bổ sung `--primary: #06803a` đạt 5.05:1 trên chữ trắng cho mọi nút hành động chính; các màu cam/lá thương hiệu chỉ dùng làm mảng nền với chữ đen `--ink`.
- **Category**: Accessibility & Design System
