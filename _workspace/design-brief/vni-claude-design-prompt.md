# VNI IELTS AI — Design brief cho Claude Design
# Redesign toàn bộ UI/UX web học viên + admin CMS · MVP · 06/09/2026

## 0. Vai trò và cách làm việc

Bạn là design lead cho VNI IELTS AI, nền tảng luyện thi IELTS của VNI Education (Việt Nam). Bạn nhận một sản phẩm đã có mã nguồn, đã có hệ thống token thị giác, và đã có 22 quyết định sản phẩm được chốt. Việc của bạn là thiết kế lại trải nghiệm — không phải phát minh lại thương hiệu, không phải quyết định lại sản phẩm.

Ba quy tắc làm việc:
1. Mọi chuỗi chữ trên giao diện là tiếng Việt có dấu đầy đủ. Tên kỹ năng giữ tiếng Anh: Reading, Listening, Writing, Speaking. Không bao giờ lẫn tiếng Anh vào label, nút, thông báo.
2. Khi brief này nói "không được tự ý thay đổi" (mục 13), bạn thiết kế đúng như vậy. Nếu thấy có lý do mạnh để khác đi, ghi thành một ghi chú "Đề xuất khác" bên cạnh, không đổi thẳng.
3. Khi brief này chưa đủ để quyết một chi tiết, bạn chọn phương án đơn giản nhất và ghi rõ đó là giả định. Không bịa số liệu, không bịa band, không bịa quy tắc token.

## 1. Product direction

VNI IELTS AI là nền tảng luyện và thi thử IELTS, miễn phí ở giai đoạn đầu, chạy trên web trước, sau đó lên Android/iOS.

Bốn kỹ năng, hai cách chấm:
- Reading và Listening chấm theo đáp án của đề — kết quả tức thì, không qua AI.
- Writing do AI chấm theo 4 tiêu chí (Task Response/Achievement, Coherence và Cohesion, Lexical Resource, Grammatical Range và Accuracy). Rubric là framework phục vụ AI, KHÔNG tuyên bố là chấm IELTS chính thức. Mọi con số do AI đưa ra phải mang nhãn "AI · tham khảo".
- Speaking trong MVP: học viên ghi âm được và bản ghi được lưu, nhưng CHƯA chấm. Giao diện phải nói thật điều đó.

Hai khu vực học tách bạch:
- LUYỆN TẬP: học từng kỹ năng, không áp lực, feedback chi tiết. Đồng hồ đếm lên; có công tắc "Bật đồng hồ" để chuyển sang đếm ngược nếu muốn.
- THI THỬ: mô phỏng phòng thi, nhiều kỹ năng liên tiếp theo thứ tự của đề, đồng hồ đếm ngược do máy chủ giữ, kết quả tổng hợp. MVP chạy Reading → Listening → Writing (Speaking ghi âm, chưa chấm).

Bốn tính năng hỗ trợ: Nghe chép chính tả (dictation, không tính giờ, không band), Tài liệu, Bài viết (hai thư viện độc lập, chưa gắn với đề), Tiến độ.

Token: hệ thống ghi nhận mức sử dụng nhưng KHÔNG chặn. Người mới có 10 lượt hiển thị; hết vẫn dùng được. Kiếm thêm bằng đăng nhập hằng ngày và giới thiệu qua link. Không có mua bán.

Cảm giác cần đạt: một ứng dụng học nghiêm túc nhưng thân thiện, trung thực về cái gì đã chấm và cái gì chưa, không hứa điều chưa có. Mọi màn sau đăng nhập là công cụ, không phải trang tiếp thị.

## 2. Information architecture

Hai khung, không hơn.

KHÁCH (chưa đăng nhập) — header công khai
/                    Landing. Trang duy nhất được phép có hero và prose giới thiệu
/practice            Xem được toàn bộ catalogue luyện tập; bấm Bắt đầu → mời đăng nhập
/exams               Xem được danh sách đề thi thử; bấm Thi → mời đăng nhập
/library/documents   Đọc danh sách; tải cần đăng nhập
/library/articles    Đọc được toàn bộ
/dictation           Nghe thử được một câu; làm bài cần đăng nhập
/login  /register  /forgot-password  /reset-password  /verify-email

ĐÃ ĐĂNG NHẬP — một khung duy nhất (rail trái trên desktop, drawer trên mobile)
/home                Trang chủ người học. Không phải landing
/practice            Luyện từng kỹ năng
  /practice/:skill   Danh sách bài của một kỹ năng
/exams               Thi thử
/library
  /documents
  /articles → /articles/:slug
/dictation → /dictation/:setId
/progress            Tiến độ + lịch sử toàn bộ kết quả
/results/:sessionId  Kết quả một phiên — layout thống nhất (mục 8)
/account             Hồ sơ, bảo mật, thiết bị, token

TOÀN MÀN HÌNH — không nav, cố ý
/session/:sessionId  Màn làm bài. Không sidebar, không link ra ngoài, không menu tài khoản

Quy tắc: người đã đăng nhập không bao giờ rơi ngược về khung khách. Mọi mục trong rail đều mở trang nằm TRONG rail. Một mục menu là một lời hứa — thứ chưa có thì không có trong menu.

ADMIN CMS (ứng dụng riêng, mật độ compact)
/                    Tổng quan
/exams → /exams/:id  Danh sách và chi tiết đề, kèm trạng thái duyệt
/import              Nhập đề bằng ZIP → theo dõi → preview → gửi duyệt
/review              Hàng chờ duyệt
/library/documents, /library/articles   CRUD + xuất bản
/users → /users/:id, /roles, /audit

## 3. Navigation

Header khách (desktop): logo VNI · Luyện 4 kỹ năng · Thi thử · Nghe chép chính tả · Tài liệu · Bài viết · [Đăng nhập] [Bắt đầu miễn phí]. Mobile: logo + hamburger + một nút chính.

Rail sau đăng nhập (desktop 248px, thu gọn 56px icon-only có tooltip), ba nhóm có nhãn:
  HỌC TẬP      Trang chủ · Luyện tập · Thi thử · Tiến độ
  TÀI NGUYÊN   Nghe chép · Tài liệu · Bài viết
  TÀI KHOẢN    Tài khoản (kèm số token còn lại ngay trong mục này)
Thanh trên: hamburger (mobile) · tên trang hiện tại · avatar + tên → menu: Tài khoản, Tiến độ, Đăng xuất.
Không có chuông thông báo. Không có mục Trợ lý AI. Không có mục Lộ trình.

Mobile web (dưới 900px): drawer từ trái, đóng bằng Escape/tap ngoài, khoá cuộn, trả focus. Không dùng bottom tab bar cho responsive web — bottom tab dành cho concept app ở mục 10.

Breadcrumb: chỉ trong CMS và trong /library. Không dùng breadcrumb trong khu học.

## 4. Màn hình cần thiết

HỌC VIÊN — thiết kế đủ desktop 1440 và mobile 390 cho mỗi màn
L01  Landing (khách)
L02  Đăng nhập / Đăng ký (tab), Quên mật khẩu, Đặt lại mật khẩu, Kiểm tra email sau đăng ký, Xác thực email
L03  Trang chủ người học /home
L04  Luyện tập /practice — chọn kỹ năng
L05  Luyện tập /practice/:skill — danh sách bài + công tắc đồng hồ
L06  Thi thử /exams — danh sách đề + màn chuẩn bị trước khi vào
L07  Màn làm bài /session/:id — bốn biến thể: Reading, Listening, Writing, Speaking; hai chế độ đồng hồ (đếm lên / đếm ngược); dải tiến trình khi thi thử
L08  Thẻ xác nhận "Tiếp theo: Listening" và "Nộp bài"
L09  Kết quả /results/:id — layout thống nhất, năm biến thể nội dung: Reading, Listening, Writing, Speaking (chưa chấm), Dictation
L10  Tiến độ /progress
L11  Tài khoản /account — Hồ sơ, Mật khẩu, Thiết bị, Token
L12  Nghe chép /dictation và /dictation/:setId
L13  Thư viện /library/documents, /library/articles, /library/articles/:slug
L14  Trạng thái hệ thống: trống, đang tải, lỗi mạng, mất kết nối giữa bài, hết phiên, 404

ADMIN CMS — desktop only, 1440
A01  Đăng nhập admin
A02  Tổng quan
A03  Danh sách đề + bộ lọc trạng thái (Nháp · Chờ duyệt · Đã duyệt · Đã xuất bản · Đã gỡ)
A04  Chi tiết đề — xem cấu trúc, lịch sử phiên bản, hành động theo vai
A05  Nhập đề: tải ZIP → tiến trình từng bước → danh sách phát hiện (lỗi chặn / cảnh báo) → preview đề đã phân tích → gửi duyệt
A06  Hàng chờ duyệt — người duyệt xem preview, duyệt hoặc trả về kèm lý do
A07  Preview đề — cùng khung với màn làm bài của học viên nhưng có lớp chú thích
A08  Tài liệu / Bài viết — danh sách, soạn, xuất bản
A09  Người dùng, Vai trò (ma trận quyền), Nhật ký

## 5. User flow

Luồng chính (phải mượt tuyệt đối):
Khách → Landing → Đăng ký (Google hoặc email) → màn "Kiểm tra email" → /home → Luyện tập → chọn Reading → chọn bài → [công tắc đồng hồ tắt] → Bắt đầu → làm bài, autosave → Nộp → Kết quả (điểm thô, độ chính xác, review từng câu, giải thích) → "Làm bài khác" hoặc "Về trang chủ"

Thi thử:
/exams → chọn đề → màn chuẩn bị (thứ tự kỹ năng, thời lượng từng phần, yêu cầu mic/audio, cảnh báo không quay lại được) → Bắt đầu → Reading → thẻ "Tiếp theo: Listening" → Listening → "Tiếp theo: Writing" → Writing → "Nộp bài" → Kết quả tổng hợp (Reading, Listening ngay; Writing "Đang chấm" rồi tự cập nhật; Speaking không có trong MVP)

Speaking trong Luyện tập:
chọn Speaking → cue card + thời gian chuẩn bị → ghi âm → nghe lại → nộp → Kết quả hiển thị bản ghi và trạng thái "Đã lưu, chưa chấm — Speaking AI sẽ mở sau"

Nghe chép:
/dictation → chọn bộ → nghe câu → gõ → kiểm tra → xem đối chiếu từng từ → câu tiếp → hết bộ → Kết quả (layout thống nhất)

Trở lại:
/home hiển thị "Đang dở" nếu có phiên chưa nộp → bấm → vào thẳng /session/:id đúng chỗ đã dừng

Điểm cụt cần loại bỏ: modal bài test đầu vào; mục menu không có trang; nút dẫn tới thông điệp "đang xây".

## 6. Layout từng màn hình

L01 Landing — trang DUY NHẤT có hero. Hero ngắn, chiếm dưới 70% chiều cao màn, một câu giá trị, một nút "Bắt đầu miễn phí", một nút "Xem kho đề". Bên dưới tối đa bốn khối: bốn kỹ năng và cách chấm; luyện tập vs thi thử; nghe chép và thư viện; về VNI + kênh liên hệ. Không FAQ dài. Không minh hoạ giả tương tác.

L03 Trang chủ người học — một cột trên mobile, lưới 8/4 trên desktop. Thứ tự: (1) Đang dở — chỉ hiện khi có, khối nổi nhất trang; (2) Học tiếp — một thẻ, một nút chính, nội dung từ gợi ý máy chủ, không có thì câu mặc định "Bắt đầu với Reading hoặc Listening — hai kỹ năng có kết quả ngay"; (3) Token còn lại — số + cách kiếm thêm, không phải cảnh báo; (4) Mục tiêu và khoảng cách; (5) Chuỗi ngày học — số 0 đọc trung tính, không như thất bại; (6) Kết quả gần đây — tối đa 5, link sang /progress. Không có bốn thẻ kỹ năng lặp lại.

L04/L05 Luyện tập — /practice: bốn thẻ kỹ năng lớn, lưới 2x2 trên mobile (không bao giờ dải ngang bị cắt), mỗi thẻ ghi cách chấm ("Theo đáp án" / "AI · tham khảo" / "Ghi âm, chưa chấm"). /practice/:skill: một dòng giải thích ngắn, công tắc "Bật đồng hồ" ở đầu danh sách (mặc định tắt), danh sách bài dạng hàng trên mobile và thẻ trên desktop. Mỗi bài: tên · số phần/câu · thời lượng · nguồn chấm · trạng thái (Chưa làm / Đang dở / Đã làm x lần). Một nút "Bắt đầu". Không có nút thứ hai.

L06 Thi thử — danh sách đề, mỗi đề ghi các kỹ năng có trong đề và tổng thời lượng. Màn chuẩn bị là một trang (không phải modal) với: thứ tự kỹ năng, thời lượng từng phần, yêu cầu tai nghe/mic, "Không quay lại kỹ năng đã hoàn thành", "Đồng hồ không dừng khi mất mạng", nút "Bắt đầu thi thử" và "Để sau".

L07 Màn làm bài — header một dòng 64px trên desktop: [VNI · Luyện tập] [Reading · Part 2 · tên đề] … [Tạm dừng] [Mục tiêu] [Rời khỏi] [12:34]. Mobile đúng hai dòng, mỗi dòng 44px: dòng 1 = chế độ, kỹ năng, part, đồng hồ; dòng 2 = các nút pill và chip trạng thái lưu. Chế độ đếm ngược: không có Tạm dừng, không Mục tiêu, không Rời khỏi. Đồng hồ ba mức cảnh báo bằng kích cỡ + viền + nhãn — không bao giờ đỏ, không nhấp nháy.
  Reading: desktop chia 50/50, hai pane cuộn độc lập; mobile hai tab "Bài đọc | Câu hỏi (12/40)" ghim dưới header. Thanh công cụ bài đọc: cỡ chữ, bút dạ.
  Listening: thanh audio chiếm trọn hàng, không có tua, trạng thái sẵn sàng/đang phát/đã phát/lỗi/đang kết nối lại; bảng đáp án kéo thả có lựa chọn bàn phím; câu hỏi bên dưới.
  Writing: desktop 40% đề / 60% soạn thảo; ảnh đề tối đa 40vh có "Xem lớn"; đếm từ đổi màu cảnh báo khi dưới mức tối thiểu nhưng không chặn nộp.
  Speaking: một câu mỗi lần; thẻ cue card riêng với đồng hồ chuẩn bị bên trong; máy trạng thái ghi âm: sẵn sàng → chuẩn bị → đang ghi → đang tải lên → đã lưu → lỗi; mỗi lỗi có nút thử lại hoặc hướng dẫn. Dưới cùng ghi rõ: "Bản ghi được lưu. Speaking chưa được chấm trong phiên bản này."
  Footer: bản đồ phần/câu với ba trạng thái ô (trống / đã trả lời / chưa lưu), Trước / Sau / Nộp. Mobile: gom thành một nút "Part 2 · 6/10" mở bottom sheet.
  Chip lưu: Đã lưu ✓ · Đang gửi · Chưa gửi được (offline) · Gửi thất bại. "Đã lưu" chỉ hiện sau khi máy chủ xác nhận.

L08 Thẻ xác nhận — "Hoàn thành Reading?" · "Đã trả lời 31/40 · Chưa trả lời 9 · Đã lưu ✓" · "Sau khi sang Listening, bạn không quay lại Reading được." · [Xem câu chưa trả lời] [Hoàn thành, sang Listening]. Khi đang chuyển: hai nút vô hiệu, dòng "Đang chốt Reading… mở Listening".

L09 Kết quả — xem mục 8.

L10 Tiến độ — mục tiêu band và khoảng cách; lịch hoạt động; toàn bộ lịch sử phiên dạng bảng có lọc theo kỹ năng và chế độ; mỗi hàng dẫn tới /results/:id. Phiên chưa chấm hiện dấu gạch "—", không hiện 0. Phiên bỏ dở vẫn hiện.

L11 Tài khoản — tab: Hồ sơ · Mật khẩu · Thiết bị · Token. Tab Token: số còn lại, lịch sử cộng/trừ, cách kiếm thêm (đăng nhập hằng ngày, link giới thiệu có nút sao chép). Không có nút mua.

L12 Nghe chép — danh sách bộ; màn làm: một câu, nút phát (nghe lại không giới hạn), ô gõ, nút Kiểm tra, đối chiếu từng từ với ba màu (đúng / sai / thiếu) và ký hiệu chữ đi kèm màu.

L13 Thư viện — không hero. Bộ lọc chỉ hiện khi có từ 8 mục trở lên; mục đếm 0 không hiện. Trạng thái trống là một câu và một link sang Luyện tập, không phải bộ lọc trên khoảng trắng.

L14 Trạng thái hệ thống — mất kết nối giữa bài: dải mảnh dưới header "Mất kết nối — bài của bạn được lưu trên máy này và sẽ gửi khi có mạng", không modal, không chặn gõ. Hết phiên: trang riêng giải thích và nút về kết quả. Lỗi tải trang: khối có nút "Thử lại" thật.

ADMIN A05 Nhập đề — vùng thả file ZIP với mô tả cấu trúc mong đợi (bốn thư mục reading/listening/writing/speaking, thiếu thư mục nào bỏ kỹ năng đó); sau khi tải: danh sách bước dọc có trạng thái (Kiểm an toàn → Giải nén → Trích văn bản → AI phân tích → Ghép đáp án → Kiểm tra); kết quả chia hai nhóm rõ "Lỗi chặn — phải sửa" và "Cảnh báo — có thể bỏ qua", mỗi cảnh báo có ô đánh dấu "Bỏ qua" và bắt buộc ghi lý do; nút "Gửi duyệt" chỉ sáng khi hết lỗi chặn.
ADMIN A06/A07 Duyệt — hai cột: trái là preview đề (dùng cùng khung làm bài của học viên, có lớp chú thích hiển thị đáp án và ghi chú), phải là bảng thông tin: nguồn, phiên bản, người soạn, danh sách cảnh báo đã bỏ qua và lý do, nút "Duyệt" / "Trả về" (bắt buộc lý do). Người duyệt không được là người soạn — giao diện hiện lý do nếu bị chặn.

## 7. Component chính

- Thẻ kỹ năng (bốn màu chip: Reading xanh dương, Listening tím, Writing xanh lá, Speaking cam — chỉ dùng cho chip, không cho nền)
- Thẻ bài luyện / hàng bài luyện (desktop / mobile)
- Công tắc "Bật đồng hồ" có mô tả hai dòng bên dưới
- Header và footer màn làm bài (hai biến thể: đếm lên / đếm ngược)
- Đồng hồ ba mức
- Chip trạng thái lưu (bốn trạng thái)
- Thanh audio không tua
- Ô câu hỏi cho từng dạng: chọn một, chọn nhiều, True/False/Not Given, Yes/No/Not Given, nối, điền, trả lời ngắn, gắn nhãn, viết luận, ghi âm
- Bản đồ câu hỏi (ô ba trạng thái)
- Thẻ xác nhận chuyển kỹ năng / nộp bài
- Dải tiến trình thi thử (desktop chuỗi; mobile "2/3 Listening · Tiếp: Writing")
- Khối kết quả: dải tổng quan; biểu đồ tròn đúng/sai/bỏ trống; bảng 4 tiêu chí Writing (thanh ngang, không tròn)
- Khối "AI · tham khảo" — viền đứt nét, nhãn, đọc được ở chế độ đen trắng
- Khối "Chưa chấm" cho Speaking
- Hàng review câu hỏi (trái) và bảng review (phải)
- Nhãn trạng thái chấm: Đang chờ chấm · Đang chấm · Sẽ thử lại · Chấm thất bại · Đã chấm · Không có bài để chấm
- Thẻ token (số + cách kiếm)
- Trạng thái trống / lỗi / đang tải (một bộ, dùng chung)
- CMS: bước tiến trình dọc; hàng phát hiện (chặn / cảnh báo) với ô bỏ qua + lý do; huy hiệu trạng thái đề năm mức; ma trận quyền

## 8. Result/Review layout — áp dụng cho MỌI loại bài

Cấu trúc cố định, nội dung đổi theo kỹ năng.

DẢI TỔNG QUAN (ngang, phía trên, một hàng trên desktop, cuộn ngang hoặc xếp 2 hàng trên mobile)
  Ô 1: kết quả chính của bài
  Ô 2: biểu đồ
  Ô 3+: chỉ số theo loại bài
  Cuối dải: trạng thái chấm nếu chưa xong, và các nút "Làm bài khác" / "Về trang chủ"

KHU REVIEW (hai cột trên desktop; mobile: cột trái thu thành danh sách chọn, cột phải là nội dung)
  Cột trái: đề / bài đã làm, có thể chọn từng câu hoặc task
  Cột phải: review của mục đang chọn

Theo kỹ năng:
  Reading, Listening
    Dải: điểm thô 32/40 · độ chính xác 80% · biểu đồ tròn đúng/sai/bỏ trống · band CHỈ khi đề có bảng quy đổi đã xác minh, kèm nhãn "ước tính"; không có thì hiện "—" và một dòng "Đề này chưa có bảng quy đổi band"
    Trái: đoạn văn (Reading) hoặc trình phát audio + transcript (Listening) và danh sách câu có đánh dấu ✓ ✕ ○
    Phải: câu hỏi → "Bạn trả lời" → "Đáp án đúng" (luôn hiện) → giải thích; nút "Giải thích câu 12" khi chưa có giải thích
    Bộ lọc trên cột trái: Cần xem lại (mặc định) · Sai · Chưa làm · Đúng · Tất cả
  Writing
    Dải: band bài (từ Task 1 và Task 2 theo trọng số 1:2) trong khối viền đứt "AI · tham khảo" · 4 tiêu chí dạng thanh ngang · số từ từng task
    Trái: đề Task 1 và Task 2, bài viết của học viên; chọn task
    Phải: band từng tiêu chí → nhận xét → dẫn chứng trích từ chính bài viết (được tô sáng ở cột trái khi chọn) → cách cải thiện → gợi ý luyện tiếp
    Khi đang chấm: dải hiện "Đang chấm — tự cập nhật khi xong", khu review hiện bài viết bên trái và khung chờ bên phải
  Speaking (MVP)
    Dải: trạng thái "Đã lưu bản ghi · Chưa chấm" · thời lượng ghi · số câu đã ghi
    Trái: danh sách câu / cue card, chọn để nghe lại
    Phải: trình phát bản ghi; một khối giải thích "Speaking AI sẽ mở sau. Bản ghi của bạn được giữ để chấm khi tính năng sẵn sàng."
  Dictation
    Dải: số câu đúng / tổng · tỉ lệ · biểu đồ tròn
    Trái: danh sách câu đã nghe, đánh dấu đúng/sai
    Phải: nghe lại → "Bạn gõ" → "Câu đúng" → từ sai được tô, ký hiệu kèm màu

Nguyên tắc: sau khi hoàn thành bất kỳ bài nào, học viên nhìn thấy cùng một bố cục và biết ngay phải nhìn vào đâu. Chưa chấm thì hiện "—", không hiện 0. Không có band tổng cho thi thử 3 kỹ năng trong MVP trừ khi chủ sản phẩm quyết định khác — hiện band từng kỹ năng.

## 9. Responsive web

Ba mốc: 390 (mobile), 768 (tablet), 1280+ (desktop). Thiết kế mobile trước cho mọi màn học; desktop trước cho CMS.
- Không có dải ngang bị cắt. Lưới kỹ năng 2x2 dưới 640px.
- Màn làm bài: header đúng hai dòng 44px trên mobile; footer không quá 112px; vùng câu hỏi luôn đủ chỗ cho bàn phím ảo.
- Reading mobile: hai tab, không cuộn hai pane.
- Kết quả mobile: dải tổng quan cuộn ngang hoặc gói 2 hàng; khu review chuyển thành danh sách chọn ở trên và nội dung ở dưới.
- Mục tiêu chạm tối thiểu 44px. Chữ tối thiểu 16px cho nội dung bài, 14px cho nhãn phụ.
- Không có trang nào cao quá 3 màn hình trên mobile ngoài màn làm bài và review.

## 10. Mobile experience — concept cho ứng dụng tương lai

Web làm trước. Ứng dụng Android/iOS lên chợ sau. Ở lượt này, thiết kế MỘT bộ concept ngắn (4–6 màn, 390px) để khoá hướng, đánh dấu rõ "Concept app — không dùng cho web":
- Trang chủ app: đang dở, học tiếp, chuỗi ngày, token — bottom tab: Trang chủ · Luyện · Thi thử · Tiến độ · Tôi
- Chọn kỹ năng và chọn bài
- Màn làm bài Reading (hai tab) và Speaking (ghi âm)
- Kết quả (dải + review xếp dọc)
Hướng: gần với ứng dụng học thân thiện kiểu Duolingo về NHỊP và CẢM GIÁC — nhiệm vụ ngắn, tiến độ rõ, nút to, phản hồi tức thì. KHÔNG sao chép nhận diện, linh vật, XP, huy hiệu, streak-fire hay bất kỳ tài sản độc quyền nào. Chuỗi ngày học hiện như một con số trung tính.

## 11. Visual direction — hệ thống đã có, không phát minh lại

Nguồn chân lý: token của chủ sản phẩm chốt 03/09/2026. Dùng đúng, không đổi.
  Chữ: Nunito (có subset tiếng Việt), fallback system-ui. Không Fredoka, không Outfit, không Inter.
  Thang chữ: 14 · 16 · 18 · 20 · 24 · 32 · 44 · 60. Dưới 32 line-height 1.5; từ 32 line-height 1.2; tiêu đề trang và số band = Nunito 800.
  Khoảng cách: 4 · 8 · 12 · 16 · 24 · 32 · 48 · 72.
  Bo góc: 12px nút/thẻ/pill; 8px ô nhập; tròn cho chip.
  Viền: 2px trên thứ bấm được và bề mặt điểm số; 1px cho đường phân cách.
  Bóng: MỘT bóng cứng 0 4px 0 cùng tông đậm hơn, trên nút và thẻ bấm được — đọc như độ dày. Bóng mờ chỉ cho dialog/drawer/popover. Không bóng trang trí trên thẻ tĩnh.
  Chuyển động: 180ms; tắt khi prefers-reduced-motion; KHÔNG chuyển động nào khi học viên đang gõ đáp án.
  Màu:
    --primary #06803a — nút chính, chữ trắng (tương phản 5.05:1)
    --brand-green #16ad54, --brand-orange #f48634 — chỉ làm mảng khối đặc, chữ trên đó là --ink; không dùng làm màu chữ
    --acc #2867ac — link, thông tin, chip Reading
    --ink #17161a · --ink-2 #4a4950 · --muted #6b6a71
    --page #f6f5f3 · --card #ffffff · --sunk #faf9f7 · --line #e6e4e0
    --warn #9a4e07 / nền #fdf1e3 — cảnh báo, đồng hồ mức 2–3, thiếu từ
    --ok #1e7a3c / nền #e4f4e9 — đúng, đã lưu
    --bad #b3261e / nền — chỉ cho hỏng thật (mất mạng, sai câu). KHÔNG dùng cho thời gian.
  Quy ước đọc được ở đen trắng: chấm theo đáp án = viền liền; AI = viền ĐỨT NÉT + nhãn "AI · tham khảo". Đúng/sai luôn có ký hiệu chữ đi kèm màu.
  Hai thanh ghi: TRONG màn làm bài — kiệm, không mảng màu đặc, không chữ display, không trang trí. NGOÀI màn làm bài — mảng khối đặc, viền 2px, số display trên kết quả.
  Không linh vật. Không XP. Không huy hiệu.

## 12. UI hiện tại — giữ / sửa / bỏ

GIỮ
- Nhận diện: logo VNI, Nunito, xanh lá primary, giọng văn trung thực ("luôn mang nhãn tham khảo", "chưa chấm thì hiện dấu gạch")
- Toàn bộ logic màn làm bài: autosave, chip lưu, đồng hồ máy chủ, bản đồ câu hỏi, thẻ xác nhận — chỉ đổi bố cục và chữ
- Máy trạng thái ghi âm Speaking
- Trang Tiến độ và cấu trúc Tài khoản (Mật khẩu → Thiết bị)
- Breadcrumb trong thư viện

SỬA
- Landing: rút còn một hero ngắn + bốn khối
- /practice: tách Thi thử ra /exams; bỏ hero, FAQ, prose; lưới kỹ năng 2x2 trên mobile; một nút + công tắc thay hai nút
- Kết quả: viết lại theo layout mục 8
- /documents, /articles: bỏ hero, bỏ bộ lọc khi chưa có nội dung, trạng thái trống là một câu + một link
- /register: đồng bộ tiếng Việt toàn bộ form; bỏ dòng "Google sign-in is still being built"
- Trang chủ người học: bỏ bốn thẻ kỹ năng lặp, thêm khối Token và Đang dở

BỎ
- Modal bài test đầu vào và khối "Chưa biết bắt đầu từ đâu?"
- Hero trên mọi trang sau đăng nhập
- FAQ trên /practice, /dictation, /documents
- Minh hoạ "Phòng luyện" giả tương tác trên landing
- Mục Trợ lý AI, chuông thông báo, mục Lộ trình
- Mọi thông điệp "đang xây" / "sắp có" trên giao diện học viên — thứ chưa có thì không hiện

## 13. Không được tự ý thay đổi

1. Tên và thứ tự bốn kỹ năng: Reading, Listening, Writing, Speaking. Thứ tự thi thử theo đề, mặc định Reading → Listening → Writing.
2. Hai cách chấm và cách gọi: "Theo đáp án" cho Reading/Listening; "AI · tham khảo" cho Writing; "Ghi âm, chưa chấm" cho Speaking. Không bao giờ gọi điểm AI là "điểm IELTS" hay "band chính thức".
3. Band chỉ xuất hiện dưới dạng nửa điểm 0–9 (5.0, 5.5, 6.0…). Không có 6.3. Không có band khi chưa có dữ liệu — hiện "—".
4. Band Reading/Listening chỉ hiện khi đề có bảng quy đổi đã xác minh. Không tự vẽ band vào mọi kết quả.
5. Luyện tập và Thi thử là hai khu, hai mục menu, hai URL. Không gộp.
6. Đồng hồ: không đỏ, không nhấp nháy, không âm thanh. Ba mức bằng cỡ + viền + nhãn.
7. Trong màn làm bài không có link ra ngoài, không sidebar, không menu tài khoản. Chế độ đếm ngược không có Tạm dừng.
8. Đáp án đúng chỉ hiện SAU khi nộp, và khi đó luôn hiện.
9. Token: chỉ hiển thị và ghi nhận. Không có màn "hết lượt", không có nút mua, không có giá.
10. Hệ thống thị giác mục 11: chữ, thang, màu, bóng, bo góc. Không thêm màu mới ngoài bảng. Không đổi font.
11. Ngôn ngữ giao diện: tiếng Việt có dấu. Không lẫn tiếng Anh ngoài tên kỹ năng và thuật ngữ IELTS chuẩn (Task 1, Part 2, cue card, band).
12. Không linh vật, không XP, không huy hiệu, không streak-fire.
13. Không thiết kế: AI Chat, bài test đầu vào, thanh toán, lộ trình học, thông báo, chấm Speaking. Thứ ngoài MVP không có chỗ trên giao diện.
14. Quy trình duyệt trong CMS: Soạn → người khác duyệt → admin xuất bản. Ba vai, ba nút khác nhau, không rút gọn thành một.

## 14. Bàn giao mong đợi

1. Bản đồ màn hình (sitemap) đúng mục 2, đánh số L01–L14 và A01–A09.
2. Mỗi màn: desktop 1440 và mobile 390 (CMS chỉ desktop). Riêng L07 đủ bốn biến thể kỹ năng và hai chế độ đồng hồ; L09 đủ năm biến thể nội dung.
3. Một bảng component (mục 7) với mọi trạng thái: mặc định, hover, focus, vô hiệu, đang tải, lỗi.
4. Bốn luồng chính của mục 5 dạng chuỗi màn hình có chú thích.
5. Concept app 4–6 màn, đánh dấu rõ là concept.
6. Một trang "Đề xuất khác": mọi chỗ bạn muốn làm khác brief này, kèm lý do — không sửa thẳng vào thiết kế chính.
7. Một trang "Giả định": mọi chi tiết brief chưa quyết mà bạn đã chọn.

Chữ trên thiết kế là chữ thật bằng tiếng Việt, không lorem ipsum. Số liệu mẫu phải hợp lý với IELTS (32/40, band 6.5, 267 từ) và được ghi là dữ liệu mẫu.
