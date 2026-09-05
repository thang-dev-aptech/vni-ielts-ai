# VNI IELTS AI - Core Learner UX Flows

Tài liệu này chi tiết hóa các luồng trải nghiệm người học trọng yếu trong ứng dụng VNI IELTS AI theo các quyết định đã chốt `D-4`, `D-5`, `D-6`, `D-7`, `D-8`, `D-9`.

---

## 1. Luồng Trang Chủ Học Viên (`/students/dashboard` - `D-4`)

```mermaid
sequenceDiagram
  autonumber
  actor L as Learner
  participant Dash as Student Dashboard
  participant API as Backend API

  L->>Dash: Truy cập /students/dashboard
  Dash->>API: Lấy phiên làm dở & coaching advice
  alt Có bài thi đang làm dở (sitting.status === 'inprogress')
    Dash-->>L: Hiển thị khối "Tiếp tục bài đang làm" (Solid Green Block)
  else Không có bài dở
    Dash-->>L: Ẩn khối tiếp tục, mở đầu bằng "Bước tiếp theo"
  end
  Dash-->>L: Hiển thị "Mục tiêu và khoảng cách" (GoalCoachingPanel compact)
  Dash-->>L: Hiển thị "Hoạt động" (StatStrip + StreakPanel trung tính)
  Dash-->>L: Hiển thị "Kết quả gần đây" (Tối đa 5 bài + Link xem chi tiết tại /progress)
  Dash-->>L: Hiển thị "Tài nguyên" (1 hàng 3 liên kết: Nghe chép · Tài liệu · Bài viết)
```

---

## 2. Luồng Lựa Chọn Luyện Đề & Thi Thử (`/practice` - `D-5`)

```mermaid
graph TD
  Start[Vào trang /practice] --> EntryCheck{Bài test đầu vào?}
  EntryCheck -->|Trạng thái S4| S4Layer[Modal Entry Test: Bài test đầu vào chưa mở]
  S4Layer --> SkipEntry[Bấm nút chính: Bỏ qua, vào luyện luôn]
  SkipEntry --> Catalogue[Danh mục bài tập / Đề thi]
  
  Catalogue --> ScopeSelect[Chọn Phạm vi: Một kỹ năng | Full Test]
  
  ScopeSelect -->|Chọn Một kỹ năng| SkillFilter[SkillSelector: Lưới 2x2 chọn Reading / Listening / Writing / Speaking]
  SkillFilter --> CardSingle[Card bài thi: 2 nút Luyện đề & Thi thử]
  CardSingle -->|Bấm Luyện đề| PracticeRun[/practice/:sessionId - Đếm giờ tăng, cho phép dừng]
  CardSingle -->|Bấm Thi thử| MockRun[/exam/:sessionId - Đếm ngược máy chủ, nghiêm ngặt]
  
  ScopeSelect -->|Chọn Full Test| CardFull[Card Full Test: 1 nút duy nhất Thi thử]
  CardFull --> ReadinessModal[Full Test Readiness Dialog: Xác nhận 4 kỹ năng & thiết bị]
  ReadinessModal -->|Hủy| Catalogue
  ReadinessModal -->|Bắt đầu Full Test| FullTestRun[/exam/:sessionId - Bắt đầu Reading]
```

---

## 3. Luồng Tiến Trình Full Test & Chuyển Kỹ Năng (`D-7`)

Trình tự bắt buộc theo quyết định chủ sản phẩm: `Reading -> Listening -> Writing -> Speaking`.

```mermaid
sequenceDiagram
  autonumber
  actor L as Learner
  participant Runner as ExamRunnerPage
  participant Server as Central API

  Note over Runner: Đang làm Reading (Skill 1/4)
  L->>Runner: Nhấn "Tiếp theo: Listening"
  Runner->>L: Mở SkillAdvanceConfirmModal
  Note over L,Runner: Hiển thị: Đã trả lời 31/40 · Chưa trả lời 9 · Cảnh báo không thể quay lại
  L->>Runner: Xác nhận "Hoàn thành Reading, sang Listening"
  Runner->>Server: Gửi lệnh chốt Reading và mở Listening (kèm Idempotency Key)
  Runner-->>L: Hiển thị trạng thái "Đang chốt Reading… mở Listening" (vô hiệu hóa nút)
  Server-->>Runner: Xác nhận hoàn tất, trả về phiên Listening
  Runner-->>L: Chuyển giao diện sang ListeningWorkspace
  Note over Runner: Dải tiến trình: Reading ✓ → Listening (đang làm) → Writing → Speaking
```

---

## 4. Luồng Nộp Bài Cuối Cùng & Trả Kết Quả (`D-9`)

```mermaid
sequenceDiagram
  autonumber
  actor L as Learner
  participant Runner as ExamRunner (Speaking)
  participant API as Backend API
  participant Res as ExamResultsPage

  L->>Runner: Hoàn thành Speaking -> Nhấn "Nộp bài"
  Runner->>Runner: Mở SubmitConfirmCard
  L->>Runner: Xác nhận nộp bài toàn bộ đề thi
  Runner->>API: Gửi bản ghi & chốt bài thi
  API-->>Runner: Chấp thuận, trả mã phiên
  Runner->>Res: Chuyển hướng sang /exam/:sessionId/results
  Res->>API: Lấy kết quả chấm điểm
  alt Đơn kỹ năng (Single Skill)
    Res-->>L: Hiển thị Donut (Đúng/Sai/Chưa làm), Band (nếu có quy đổi), Điểm yếu chính, 3 nút hành động
    Note over Res: KHÔNG hiển thị Overall panel
  else Full Test chưa chấm xong đủ 4 kỹ năng
    Res-->>L: Tiêu đề "Có kết quả 2/4 kỹ năng", Bảng 4 hàng trạng thái, Overall hiện dấu em-dash (—)
  else Full Test hoàn tất trọn vẹn 4 kỹ năng
    Res-->>L: Hiển thị điểm Overall phóng to cùng kết quả chi tiết từng kỹ năng
  end
```
