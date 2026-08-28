---
description: Execute the Foundation Ready infrastructure todo through every tested phase
argument-hint: "[resume | phase/item ID, e.g. F1 or F1.3]"
---

Hoàn thiện toàn bộ hàng đợi Foundation Ready của project. `$ARGUMENTS` chỉ là điểm bắt đầu gợi ý;
trạng thái thật luôn lấy từ checklist trong repository.

## Source of truth

Đọc đầy đủ, theo thứ tự:

1. `CLAUDE.md`
2. `docs/README.md`
3. `docs/development/infrastructure-foundation-todolist.md`
4. `docs/development/infrastructure-foundation-report.md`
5. Tài liệu kiến trúc/bảo mật/ADR được item hiện tại trỏ tới

`docs/development/infrastructure-gate.md` và
`docs/development/infrastructure-completion-report.md` là bằng chứng lịch sử, không được dùng để
kết luận current code đã pass.

## Nhiệm vụ

Chạy liên tục `F0 → F5` cho tới khi toàn bộ todo Foundation Ready hoàn thành. Sau mỗi phase phải test
kỹ, cập nhật báo cáo, đánh dấu checkbox rồi tự tiếp tục phase kế tiếp. Không dừng sau một item hay
một phase chỉ để chờ người dùng nói “tiếp tục”.

## Vòng lặp bắt buộc

1. Chạy `git status --short`, đọc code/test/config liên quan và xác lập baseline của item đầu tiên
   chưa hoàn thành. Bảo toàn mọi thay đổi có sẵn không thuộc nhiệm vụ.
2. Cập nhật duy nhất dòng `Đang thực hiện` trong checklist thành ID item hiện tại. Không đánh dấu
   checkbox trước.
3. Chẩn đoán nguyên nhân từ code và tái hiện lỗi. Không sửa theo phỏng đoán.
4. Thực hiện thay đổi nhỏ nhất đóng được invariant của item; thêm regression test ở layer thấp nhất
   hợp lý và boundary/integration test nếu lỗi đi qua boundary.
5. Chạy targeted test. Sau đó tạo **negative proof** bằng regression test/fault injection chứng minh
   gate bắt được lỗi cũ. Không phá/revert worktree chỉ để minh họa test đỏ.
6. Chạy các suite liên quan và kiểm tra `git diff --check`; không được biến failure thành skip, nới
   timeout tùy tiện, giảm assertion hoặc xóa test.
7. Ghi bằng chứng item vào đúng phase trong
   `docs/development/infrastructure-foundation-report.md`, rồi mới đổi checkbox item thành `[x]`.
8. Khi tất cả item của phase đóng, chạy nguyên **phase gate** được ghi trong checklist.
9. Nếu phase gate xanh:
   - hoàn tất báo cáo phase: kết quả, thay đổi, command + exit code + test counts, negative proof,
     artifact, rủi ro và git state;
   - đánh dấu checkbox phase trong master checklist;
   - in một báo cáo ngắn cho người dùng;
   - cập nhật `Đang thực hiện` sang item đầu của phase tiếp theo và tiếp tục ngay.
10. Sau F5, chạy lại final gate từ clean state có thể tái tạo, hoàn tất báo cáo tổng, đổi
    `Foundation Ready` thành `đã đạt`, đặt `Đang thực hiện: hoàn tất` và báo cáo tổng thể cho người
    dùng.

## Luật chất lượng

- “Xanh” không đủ: item phải có regression/fault evidence phù hợp với lỗi cần ngăn.
- Lấy số test từ output vừa chạy; không sao chép số liệu lịch sử.
- Health check phải kiểm dependency thật và phân biệt object/bucket không tồn tại với auth/service
  failure.
- Integration test dùng MongoDB replica set thật với `VNI_REQUIRE_MONGO=1`; CI không được skip.
- Không để Mongo/BSON type rò vào Domain/Application và không xây PostgreSQL adapter trong hàng đợi
  này.
- Không chọn cloud, Kubernetes hay observability SaaS thay chủ dự án. Dùng OCI, S3-compatible và
  OTLP contracts trung lập nhà cung cấp.
- Không dùng credential thật. Test external integration bằng local service, fake adapter hoặc test
  collector.
- Không ghi log token, password, API key, nội dung bài làm, audio hay PII không cần thiết.
- Không overclaim Production Ready. Lúc kết thúc chỉ được kết luận Foundation Ready; production
  provisioning nằm ở §10 của checklist.

## Khi gặp blocker

Tự xử lý mọi blocker kỹ thuật an toàn trong phạm vi. Với external account, credential hoặc quyết
định vendor còn thiếu, hoàn thiện configured seam, validation, local contract test và ghi phần còn
lại vào backlog Production Ready. Nếu một acceptance criterion của Foundation thật sự không thể đạt:

1. ghi command/error và các hướng đã thử vào report;
2. giữ checkbox `[ ]`;
3. tiếp tục các item độc lập còn lại nếu an toàn;
4. chỉ dừng khi không còn tiến triển có ý nghĩa, rồi báo chính xác blocker — không tuyên bố hoàn tất.

## Ranh giới an toàn

- Không commit, push, tạo PR hoặc deploy ra môi trường ngoài nếu người dùng chưa yêu cầu riêng.
- Không xóa volume/database, reset git, ghi đè thay đổi người dùng hoặc chạy migration phá hủy.
- Không sửa tính năng sản phẩm ngoài phần tối thiểu cần để đóng infrastructure invariant.
- Nếu phát hiện lỗi chức năng ngoài phạm vi, ghi lại thành todo/risk có bằng chứng; không lén mở rộng
  scope.

