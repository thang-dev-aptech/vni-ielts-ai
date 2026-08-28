# Cổng hạ tầng — hàng đợi I0…I7 và UI0…UI11

> Tài liệu này viết bằng tiếng Việt vì đây là **checklist vận hành cho người thực thi**, giống
> [`next-actions.md`](next-actions.md). Toàn bộ `docs/` còn lại là tiếng Anh.

**Nguồn:** chỉ đạo của chủ sản phẩm ngày 27/08/2026 — báo cáo hiện trạng kèm master todo.

**Luật thực thi — đổi 28/08/2026.** Chỉ đạo chủ sản phẩm: *"lên 1 plan hoàn thiện tiếp tục cho đến khi
chạy ổn là done không cần biết là bị chặn gì"*. Nên: làm item đang mở → đối chiếu DoD → ghi bằng
chứng vào file này → sang item kế tiếp. Giữ đúng một item `đang làm`. **Không dừng vì blocker** —
blocker thành khe cắm có cấu hình (`G-11`), không thành lý do dừng.

**Một item chỉ được đóng khi có bằng chứng**, không phải khi suite xanh: một test đã kiểm chứng là
**đỏ khi gỡ bản vá**.

### Thứ tự đã sắp lại theo phụ thuộc thật

Danh sách gốc để `I2` trước `I3`, nhưng `I2.4` (reconciliation) cần một cleanup job, mà job cần
worker thật (`I3.3`). Thứ tự thi hành:

```
I0 ✅ → I1 ✅ → I2.2/I2.3/I2.6 ✅ → I5 → I4 → I3 → I2.1/I2.4/I2.5
      → I6 → I7 → UI0–UI3 → UI4–UI7 → UI8–UI10 → UI11
```

`I5` và `I4` lên trước vì chúng độc lập với mọi thứ khác và mỗi cái đóng một lỗ đã có bằng chứng.
`I3` trước phần còn lại của `I2` vì worker là điều kiện của reconciliation.

---

## Trạng thái

| | |
|---|---|
| Hàng đợi hạ tầng | **đóng — 48/48** |
| Phase đã xong | **I0** 6/6 · **I1** 6/6 · **I2** 6/6 · **I3** 6/6 · **I4** 6/6 · **I5** 5/5 · **I6** 7/7 · **I7** 6/6 |
| Kế tiếp | **UI0…UI11** — chưa mở |
| Chặn | **không còn** — chỉ đạo 28/08 biến mọi blocker thành khe cắm có cấu hình |

**Bằng chứng, 28/08/2026:** backend **519/519, 0 skipped** (Domain 157 · Application 170 ·
Infrastructure 64 · Architecture 4 · Integration 124) · web **252/252** · `@vni/auth` **8/8** ·
admin **12/12** · design-system + ui **69/69** · trình duyệt thật **14/14** (desktop + Pixel 7) ·
diễn tập khôi phục **PASSED** · `typecheck` pass · `format:check` pass · `git diff --check` 0 ·
`check-docs.py` pass.

**Báo cáo tổng:** [`infrastructure-completion-report.md`](infrastructure-completion-report.md).

---

## Phase I0 · Khôi phục cổng kiểm tra đáng tin

### ✅ I0.1 · Sửa frontend test đang đỏ — xong 27/08/2026

**Triệu chứng:** suite web đỏ 1 test mỗi lần chạy full, không cố định test nào, mọi test đều xanh khi
chạy riêng. Lần đo này: **2 trên 3 lần chạy full bị đỏ**.

**Nguyên nhân gốc — không phải máy yếu.** Đây là lần thứ ba triệu chứng này được chẩn đoán, và hai
lần trước đều kết luận là CPU contention. Lần này truy được bằng stack trace:

```
[signOut]
  at AuthContext.tsx  (token renewer catch)
  at request          (packages/auth/src/http.ts:226 — nhánh retry 401)
  at StudentDashboardPage.tsx:155  (listExams)
```

Hai thứ cộng lại mở ra lỗ hổng:

1. **`vi.unstubAllGlobals()` khôi phục `fetch` thật *trước khi* cây React được unmount.** Vitest chạy
   `afterEach` theo thứ tự ngược với thứ tự đăng ký, nên `afterEach` của file test chạy **trước**
   cleanup của Testing Library — cleanup được đăng ký trong setup file nên chạy sau cùng. Trong khoảng
   giữa hai thời điểm đó, app vẫn mounted, vẫn đang fetch, và không còn mock. Request đi thẳng tới
   **API .NET đang chạy thật trên `localhost:5099`** của máy dev.

2. **Một `401` từ request rò rỉ đó lại đi làm mới token của phiên *hiện tại*.** `renewOnce()` trong
   `packages/auth/src/http.ts` là singleton cấp module, không nhớ request đến từ phiên nào. Nên một
   lệnh gọi sót từ test trước nhận `401` thật từ `localhost:5099`, transport gọi renewer của **test kế
   tiếp**, mock của test đó không có route `/auth/refresh`, và lời từ chối đó `signOut()` một learner
   vừa mới được đăng nhập. Nhìn từ báo cáo: test dashboard assert trên trang đăng nhập.

> **Điểm 2 là lỗi sản phẩm, không phải lỗi test.** Một request cũ không được phép kết thúc một phiên
> đang sống. Đã ghi thành **I4.2 · Auth generation** bên dưới; task này không sửa nó.

**Đã sửa:**

| Việc | File |
|---|---|
| Cổng mạng: không test nào chạm được server thật | [`apps/web/src/test-setup.ts`](../../apps/web/src/test-setup.ts) |
| `openDashboard()` chờ catalogue + history load xong | [`apps/web/src/__tests__/student-dashboard.test.tsx`](../../apps/web/src/__tests__/student-dashboard.test.tsx) |
| Hai test submit chờ trang kết quả render, không chỉ chờ URL | [`apps/web/src/__tests__/exam-flow.test.tsx`](../../apps/web/src/__tests__/exam-flow.test.tsx) |
| Test submit của luyện đề chờ trang kết quả render | [`apps/web/src/__tests__/practice-runner.test.tsx`](../../apps/web/src/__tests__/practice-runner.test.tsx) |

Cổng mạng đặt trong `beforeEach` của setup file — **và vị trí đó chính là cơ chế**. `beforeEach` chạy
theo đúng thứ tự đăng ký nên nó đứng trước mọi `vi.stubGlobal('fetch', …)` của test; `vi.stubGlobal`
nhớ giá trị nó thay thế, nên `vi.unstubAllGlobals()` khôi phục **chính cổng này** chứ không phải
`fetch` thật. Khoảng hở ở (1) đóng lại mà không file test nào phải đổi.

Cổng reject bằng `TypeError` — đúng hình dạng `fetch` thật trả về khi mất mạng, và `isUnreachable()`
đã coi đó là unreachable. Nên request sót đi xuống nhánh offline của app thay vì nhánh bị-từ-chối.
Nhánh từ chối xoá credential; đó chính là thứ khiến lỗi này phá hoại chứ không chỉ ồn ào.

**DoD — đạt.** Không nới timeout nào để che lỗi.

| Kiểm chứng | Kết quả |
|---|---|
| `student-dashboard.test.tsx` chạy riêng | 14/14 |
| `verify-realtime.test.tsx` chạy riêng | 4/4 |
| Full suite × 3 liên tiếp | **230/230 · 230/230 · 230/230** |
| Full suite × 3 trước đó (cùng bản vá) | 230/230 × 3 |
| Số lệnh gọi mạng rò rỉ, đo trên 3 lần chạy full | **0** |
| Cổng mạng có thật sự làm đỏ test không | có — một test cố gọi `localhost:5099` bị fail đúng tên, kèm URL |
| `pnpm typecheck` | pass |

Trước bản vá: 2/3 lần chạy full đỏ. Sau: 6/6 xanh.

### ✅ I0.2 · Xử lý toàn bộ React `act(...)` warnings — xong 27/08/2026

**21 warning mỗi lần chạy, từ 8 test.** Tất cả cùng một lớp: test dịch đồng hồ hoặc yield qua
`setTimeout` trần trong khi app còn việc đang chạy. Ngoài `act` scope, React **không** flush effect
theo lịch của test — nên thứ mà assertion kế tiếp nhìn thấy phụ thuộc vào thời điểm chứ không phụ
thuộc vào code. Cùng hình dạng với lỗi nhấp nháy ở I0.1.

| Nguồn | Sửa |
|---|---|
| `vi.advanceTimersByTimeAsync` trần | bọc `act` — `token-lifetime.test.tsx` |
| Vòng lặp `setTimeout` chờ autosave | helper `settle()` bọc `act` — `exam-flow.test.tsx` |
| Helper `until()` | bọc `act` — `practice-runner.test.tsx` |
| `waitOutTheDebounce()` | bọc `act` — `exam-speaking-contract.test.tsx` |
| Ba `.click()` liên tiếp (raw, cố ý) | bọc trong **một** `act` — `exam-flow.test.tsx`, `practice-four-skills.test.tsx` |
| `dispatchEvent(popstate/focus)` | bọc `act` — `verify-realtime.test.tsx` |

`.click()` thô được giữ nguyên: `userEvent.click` có await giữa các lần nhấn nên React kịp re-render
và disable nút — không tái hiện được "ba lần nhấn đến nhanh hơn một lần render", tức là chính thứ
đang được kiểm.

**Cổng mới:** `act(...)` warning giờ **làm đỏ test**, cùng chỗ với cổng render-crash. Đã substitute
`%s` của React nên dòng lỗi gọi đúng tên component.

**DoD — đạt.** `console.error` **không** bị suppress. Web 230/230 × 3, **0 act warning**. Một test giả
lập cập nhật ngoài `act` bị làm đỏ đúng tên.

> Còn nợ, ghi lại chứ không tự làm: `exam-flow.test.tsx` vẫn còn vài `waitFor(..., { timeout: 5_000 })`
> viết tay, trái với chính lời cảnh báo trong `test-setup.ts`. Chúng chặt hơn ngân sách chung (8s) nên
> không gây đỏ.

### ✅ I0.3 · Giữ render crash / invalid DOM gate — xong 27/08/2026

Cổng cũ đọc `console.error` — **một** detector, và nó phụ thuộc vào một message do React sở hữu và
vào việc `ErrorBoundary.componentDidCatch` còn gọi `console.error`. Xoá method đó thì boundary vẫn
chạy còn cổng thì im lặng: suite xanh trên một app đang hiện lời xin lỗi, đúng thứ cổng sinh ra để
chặn.

**Đã thêm detector thứ hai, độc lập hoàn toàn:** `ErrorBoundary` mang `data-error-boundary` và cổng
đọc DOM. Nó chạy trước cleanup của Testing Library nên cây vẫn còn đó.

**DoD — đạt.** Một crash giả lập làm đỏ test và in cả hai dòng:
`Unhandled render error deliberate render crash` và
`ErrorBoundary rendered its apology — a component threw during render.`, kèm stack thật.

### ✅ I0.4 · Sửa formatting gate — xong 27/08/2026

`prettier --check .` chết với `Unable to read file "Đề IELTS/Đề CAM.rar": Invalid string length` —
một trần độ dài chuỗi của Node, chạm phải khi Prettier nuốt cả archive **1,3 GB** vào bộ nhớ chỉ để
kết luận rằng nó không có parser. Exit code 2, nên toàn bộ cổng format hỏng và ở nguyên trạng thái
hỏng; không ai phân biệt được lỗi style thật với lỗi này.

- `.prettierignore`: `Đề IELTS/` + toàn bộ archive/audio/video/ảnh/font/office.
- `.gitignore`: `/Đề IELTS/` — **cùng lý do với `/exam/`**: đây là tài liệu nguồn của bên thứ ba,
  chưa ai xác lập quyền phân phối, và một file 1,3 GB không có việc gì trong lịch sử git.
- Trailing whitespace: **31 dòng / 12 file**, tất cả đều là dòng **chỉ có khoảng trắng** bên trong
  khối comment JSX — Prettier không đụng tới. Đã kiểm chứng bằng script rằng không dòng nào chứa code
  trước khi sửa, nên không có string literal nào bị đổi.
- 5 file Prettier báo style: đã format.

**DoD — đạt.** `pnpm format:check` pass · `git diff --check` trả 0 dòng · web 230/230 · typecheck pass.

### ✅ I0.5 · Khóa clean-checkout fixture — xong 27/08/2026

**Trước đó một bản clone sạch không chạy được engine thi, và không ai biết.** Mọi đề mà seeder có đều
được dựng cục bộ từ `exam/Exam1` thành `fixtures/exams/exam-1.json`, và **cả hai đều bị gitignore** —
tài liệu nguồn có watermark của bên thứ ba, quyền sử dụng chưa xác lập, nên commit chưa bao giờ là
lựa chọn. `ExamRunContractTests` hỏi catalogue lấy một đề đủ bốn kỹ năng; clone sạch không có đề nào,
nên **toàn bộ engine thi không được kiểm trên bất kỳ máy nào chưa chạy importer bằng tay**.

**Đã thêm `fixtures/exams/synthetic-full-1.json`** — đề bốn kỹ năng viết riêng cho repo này: đoạn văn
tự viết, prompt tự viết, **bảng band tự đặt**. Không có gì là tài liệu Cambridge và không có gì nhằm
dùng làm đề luyện thi. Mô tả trong file nói thẳng điều đó, và nói thẳng rằng **không band nào từ đề
này được hiển thị cho học viên** — nguồn bảng band thật là `H-4`, chưa chốt.

**Mặc định đề synthetic KHÔNG vào catalogue.** Chỉ đạo `A15` của chủ sản phẩm là chỉ nội dung do chủ
sản phẩm cấp mới được ship, và hai đề demo cùng bộ dictation tổng hợp đã bị xoá vì điều đó. Seeder
giờ bỏ qua mọi file `synthetic-*.json` trừ khi `Seed:IncludeSyntheticExams` được bật — suite
integration bật nó, máy dev thì không.

Về dictation: **không** thêm fixture nội dung, đúng theo `A15`. Đã kiểm — không test nào ở bất kỳ
suite nào phụ thuộc dữ liệu dictation chưa commit; `fixtures/dictation` không tồn tại và catalogue
rỗng là trạng thái đúng.

Thêm một test khoá hợp đồng: `The_committed_catalogue_fixture_is_a_valid_four_module_paper`.

**DoD — đạt.** Đã đổi tên `exam-1.json` đi chỗ khác để giả lập clone sạch: backend **440/440,
0 skipped**, integration **56/56**.

### ✅ I0.6 · Mongo contract CI bắt buộc — xong 27/08/2026

**Hai phát hiện, cái thứ hai nghiêm trọng hơn cái đang được giao.**

**1 · CI backend chưa bao giờ chạy Infrastructure và Integration tests.** Job chỉ có Architecture +
Domain + Application — tất cả đều chạy không cần database. Nên **những test duy nhất** kiểm
answer-sheet revision CAS, section-transition CAS và idempotency claim chưa từng chạy trên CI lần
nào. Mỗi cái trong số đó nói về hai writer đua nhau, và không cái nào viết được nếu không có replica
set thật.

**2 · Bước `Unit tests` hiện tại không chạy được.** Nó viết
`dotnet test tests/Vni.Ielts.Domain.Tests tests/Vni.Ielts.Application.Tests` — hai project trong một
lệnh. Trên .NET 10 SDK điều đó fail với `MSBUILD : error MSB1008: Only one project can be specified`;
MSBuild hiểu đường dẫn thứ hai là một switch lạ. Đã kiểm chứng cục bộ. Nghĩa là bước này **không thể
chạy** kể từ khi SDK đổi, và không ai nhận ra.

**Đã sửa:**

- Mỗi project một bước `dotnet test` riêng — 5 bước.
- Bước dựng **MongoDB replica set đơn node** trong CI, đợi bầu xong PRIMARY rồi mới chạy (không đợi
  thì transaction đầu tiên đua với election và fail vì lý do chẳng liên quan gì tới code).
- `VNI_REQUIRE_MONGO=1` biến "không có Mongo" từ **suite bị skip** thành **build đỏ**. Không đặt biến
  thì vẫn skip — đúng cho một laptop không bật Docker, sai ở mọi nơi khác.
- Bước **`No test was skipped`** đọc `notExecuted` trong `.trx` của cả 5 project và fail nếu tổng khác 0.
  Một test bị skip không phải failure, nên không có bước này thì không gì trong job nhận ra.
- Upload `test-results/` làm artifact.
- Trigger thêm `contracts/**` và `fixtures/**` — seeder đọc cả hai.

**DoD — đạt, và đã kiểm chứng cả hai chiều:**

| Kiểm chứng | Kết quả |
|---|---|
| Chạy đúng trình tự CI cục bộ, 5 project | 4 · 157 · 159 · 64 · 56 = **440 pass, 0 skipped** |
| Cổng skip đọc `.trx` thật | `Skipped tests: 0`, exit 0 |
| Tắt Mongo + `VNI_REQUIRE_MONGO=1` | **Failed: 56** — build đỏ, đúng ý |
| Tắt Mongo, không đặt biến | **Skipped: 56** — vẫn skip, đúng ý cho máy dev |

---

## Phase I1 · Đóng các đường mất dữ liệu

### ✅ I1.1 · Sửa `VALIDATION_FAILED` autosave — xong 27/08/2026

Một autosave mang **mọi câu** đã sửa từ lần ack gần nhất, nên một batch bình thường có nhiều câu.
Server từ chối cả batch khi một entry sai — **đúng**, vì một autosave áp dụng nửa vời là tờ đáp án
không ai suy luận được — nhưng nó chỉ nói bằng văn xuôi.

Client, không có chi tiết gì, làm thứ duy nhất một caller không có chi tiết có thể làm:

```
pendingChanges.current = {};            // mọi thay đổi trong batch, mất sạch
savedGeneration.current = generation;   // và cổng submit mở ra
```

Học viên gõ một câu đúng cạnh một câu sai thì **mất cả hai**, bài được chấm mà không có câu nào, và
mọi tín hiệu nhìn thấy được — chip, nút, trang kết quả — đều báo thành công.

**Đã sửa, hai nửa:**

- **Backend** trả `errors[]` một dòng mỗi question id (`QUESTION_UNKNOWN` / `ANSWER_TOO_LONG`).
- **Client** bỏ đúng những câu server gọi tên, **giữ lại phần còn lại**, và **không** advance
  `savedGeneration` khi còn gì đó chưa gửi — nên cổng submit đóng cho tới khi phần còn lại lành.
  Câu bị từ chối thì bỏ hẳn, nên không bao giờ kẹt cổng vĩnh viễn.
- Từ chối **không nêu tên câu nào** vẫn bỏ cả batch — không có gì để giữ, và tính chống-kẹt quan
  trọng hơn một vòng đoán nữa.
- **UI** nêu số câu bị từ chối, theo số học viên nhìn thấy chứ không phải id.

**DoD — đạt.** Test một invalid + một valid: câu tốt được gửi lại một mình khi nhấn Nộp bài, thứ tự
ghi lại là `answers → answers → submit`, không câu nào mất. Gỡ bản vá → test đỏ. Backend thêm 2
integration test cho hợp đồng `errors[]`.

### ✅ I1.2 + I1.3 + I1.4 · Answer Sheet Closure Protocol — xong 27/08/2026

→ **[ADR-0015](../decisions/0015-answer-sheet-closure-protocol.md)** ghi đầy đủ quyết định, các
phương án đã cân nhắc và hệ quả.

**Vấn đề:** phiên thi và tờ đáp án nằm ở **hai collection khác nhau**, và không có gì nối chúng.
Transition CAS bảo vệ session document; autosave là field-level patch trên answer sheet document —
hai writer sửa hai câu khác nhau đều đúng nên cố ý không có gì để từ chối. Không cái nào nói gì về
cái kia. Nên:

1. autosave nạp phiên, thấy section còn mở, qua hết mọi kiểm tra;
2. submit thắng CAS, đóng section, chấm tờ ở revision R;
3. patch của autosave hạ cánh — revision R+1.

Chip báo **Đã lưu**. Kết quả được tính không có câu đó. Không gì ném, không gì log, bằng chứng duy
nhất là điểm thấp hơn một câu.

**Quyết định:** freeze tờ đáp án bằng **một câu lệnh nguyên tử**, và freeze **trước** transition.

- `CloseAsync` set `closedAt` bằng một `findOneAndUpdate` lọc theo `closedAt` chưa tồn tại. Có upsert
  nên section không ai làm cũng đóng được.
- **Mọi đường ghi** lọc theo `closedAt` chưa tồn tại — cả `PatchAsync` lẫn `SetAnswerAsync`. Đường
  Speaking ghi qua `SetAnswerAsync` nên nếu bỏ sót thì upload trễ đi thẳng qua rào chắn.
- **Idempotent**: hai tab, submit gặp expiry sweep, request retry đều tới đây; freeze lại ở revision
  cao hơn sẽ đổi chính nội dung marking đã đọc.
- **Freeze trước CAS.** Đóng sau CAS để nguyên khoảng hở ban đầu, vì patch thua đã bay rồi.
- **Marking nhận thẳng tờ đã freeze**, không đọc lại — bất biến bằng cấu trúc chứ không bằng lập luận.
- Từ chối báo `409 SECTION_NOT_OPEN` — client đã coi mã này là terminal.

> **Bất biến, một câu:** một patch **hoặc** commit trước freeze, **hoặc** bị từ chối trước khi client
> được báo là đã lưu. Không có kết cục thứ ba.

**Đánh đổi đã chấp nhận:** process chết giữa freeze và transition để lại tờ đã đóng trong khi phiên
vẫn hiện section đang mở. Autosave bị từ chối `SECTION_NOT_OPEN`, client bỏ patch và **không** giữ
cổng, advance/submit kế tiếp hoàn tất transition (freeze idempotent nên chạy lại vô hại). Đây là một
lỗi **lành, phục hồi được, và ồn ào** — đổi lấy việc bỏ một lỗi im lặng làm mất dữ liệu.

**DoD — đạt.** 6 barrier test mới:

| Tính chất | Test |
|---|---|
| Tờ đã freeze từ chối patch, và từ chối trọn vẹn | `A_frozen_sheet_refuses_a_patch` |
| Section không ai làm vẫn từ chối ghi trễ | `Closing_a_section_nobody_answered_still_refuses_a_later_write` |
| Đóng hai lần trả về cùng một tờ | `Closing_twice_returns_the_same_frozen_sheet` |
| Đường Speaking cùng rào chắn | `A_frozen_sheet_refuses_a_recording_too` |
| **Không có kết cục thứ ba, dưới race thật** | `An_autosave_racing_a_freeze_either_lands_before_it_or_is_refused` — 25 vòng, patch và freeze thả từ một cổng |
| Rào chắn qua HTTP, cả submit lẫn advance | 2 test trong `ExamRunContractTests` |

Gỡ freeze → **4 test đỏ**. Hai test HTTP vẫn xanh khi gỡ, vì kiểm tra section của handler bắt được
trường hợp tuần tự — chúng giữ hợp đồng, còn race do test ở tầng store giữ. Đã nói rõ trong ADR.

### ✅ I1.5 · Per-question mutation ordering — xong 27/08/2026

**Thứ tự đến của Mongo không phải thứ tự sửa của học viên.** Hai lần ghi cho cùng một câu có thể bị
đảo bởi bất cứ thứ gì giữa bàn phím và database — retry khi đổi mạng, proxy, một request kẹt trong
khi request sau đi thẳng, một tab thứ hai. Không có thứ tự thì giá trị lưu lại là cái server tình cờ
áp dụng sau cùng — là **đáp án cũ** cũng thường xuyên như đáp án mới. Học viên nhìn bản sửa của mình
tự quay về cũ, không có gì trên màn hình nói tại sao.

Revision không trả lời được: nó là **một số cho cả tờ**, nên nói được caller có bị tụt lại không, chứ
không nói được trong hai lần sửa cùng một câu thì cái nào đến sau.

**Đã làm:** ordering token **theo từng câu**.

- Mongo dùng **pipeline update**, vì quyết định là theo từng field và cần đọc giá trị đang lưu trong
  lúc ghi: `$cond` với `$gt` so token gửi lên và `$ifNull($seqs.<id>, -1)`. `$set` thường ghi đè vô
  điều kiện — chính là lỗi.
- **Strictly greater**, không phải ≥: request retry mang lại token cũ, nên coi bằng là mới sẽ để
  replay ghi đè bản sửa ở giữa.
- Quy tắc **theo câu, không theo request**: batch có một entry cũ và một entry mới vẫn ghi entry mới
  — bỏ cả batch sẽ là lỗi mất dữ liệu của `I1.1` quay lại bằng cửa khác.
- **Không phải timestamp.** Hai tab trên một máy lệch giờ nhau, và client chạy chậm sẽ bị bỏ qua mọi
  bản sửa suốt thời gian lệch. Đây là bộ đếm **nâng qua** con số server báo lần cuối — Lamport clock,
  thứ yếu nhất mà vẫn đúng.
- Client **nhận token của writer khác cùng với đáp án của họ**. Nhận đáp án mà không nhận token thì
  lần sửa kế tiếp của học viên mang token server bỏ qua — và họ nhìn bản sửa của chính mình không có
  tác dụng gì, lặp đi lặp lại, không lỗi ở đâu cả.
- `sequences` là **tuỳ chọn** trên wire: client chưa cập nhật vẫn giữ hành vi cũ.

**DoD — đạt.** 6 store test + 1 client test. Test chính là đúng kịch bản trong báo cáo: B (`dog`, tạo
sau) đến trước, A (`cat`, tạo trước) đến sau → giá trị cuối là `dog`. Gỡ phần client nhận token của
server → test đỏ (`expected 3 to be greater than 500`).

> **Việc kèm theo, đã làm:** hai test polling nặng tách sang `answer-integrity.test.tsx` — bốn test
> loại này trong một file rơi vào một worker, và dưới ba luồng song song thì cái cuối bị đói CPU đến
> mức debounce 1,2 s không kịp chạy trong 20 giây. Vitest song song **theo file**.
>
> **Và một lỗi trong chính cổng vừa dựng:** cổng `afterEach` ném **trước** cleanup của Testing
> Library (hook chạy ngược thứ tự đăng ký), nên một test đỏ để nguyên cây React sang test sau — hai
> `<App/>` trong một document, và query trúng cái đã chết. Một test hỏng không được phép **chế ra**
> test hỏng kế tiếp. Cổng giờ tự `cleanup()` trước khi ném.

### ✅ I1.6 · IndexedDB patch journal — xong 28/08/2026

**Cửa sổ được đóng lại:** autosave chạy 1,2 giây sau một phím gõ, và trong 1,2 giây đó **bản duy nhất
của câu trả lời nằm trong bộ nhớ**. Tab biến mất trong khoảng đó — crash, WebView bị hệ điều hành thu
hồi, điện thoại mất sóng rồi bị kéo để tải lại — mang câu trả lời đi cùng, và phiên thi quay lại y
như trước khi học viên gõ. Trên một bài thi có giờ đó là vài phút công sức, và **không có gì trên màn
hình từng nói rằng nó đang gặp rủi ro**.

**IndexedDB, không phải `localStorage`, và lý do không phải dung lượng.**
`localStorage` **đồng bộ**, nên mỗi lần ghi chặn đúng luồng đang render bài thi — trong lúc gõ, tức
là khoảnh khắc duy nhất nó không được phép chặn. Nó cũng là **một chuỗi cho một key**, nên hai tab
cùng ghi nhật ký của một phiên sẽ đọc–sửa–ghi lại toàn bộ, và tab thứ hai âm thầm xoá mất thứ tab thứ
nhất vừa ghi. Đó là **lỗi mất-cập-nhật ngay trong cơ chế sinh ra để không mất cập nhật**.

| Quy tắc | Vì sao |
|---|---|
| Một bản ghi **cho mỗi câu**, không phải mỗi phím | giá trị cũ của một câu bị giá trị mới thay thế **theo định nghĩa** — ordering token nói vậy — nên giữ cả hai là giữ thứ server sẽ bỏ qua |
| Xoá **chỉ khi đúng sequence của chính nó** được ack | học viên gõ tiếp trong lúc request đang bay; xoá theo "một lần lưu đã thành công" sẽ vứt đúng một bản sửa server chưa từng thấy — âm thầm, và thường là câu cuối trước khi nhấn Nộp bài |
| Section đóng thì **xoá cả nhật ký của section đó** | section đã đóng không nhận ghi nữa (ADR-0015), nên thứ còn lại là công việc không bao giờ gửi được — khôi phục nó sẽ đặt lên màn hình một câu học viên **không lưu được và cũng không xoá được** |
| Câu bị `VALIDATION_FAILED` gọi tên thì **quên hẳn** | một 4xx vĩnh viễn không được retry vô hạn |
| Khôi phục **chỉ những entry server chưa vượt qua** | section view mang theo `answerSequences`; entry có token không lớn hơn mô tả một lần ghi **đã hạ cánh**, khôi phục nó sẽ đặt đáp án cũ đè lên đáp án mới |
| Khôi phục **không tự gửi** | nó biến các câu thành *pending* — hiện ra, được đếm, và nằm trong lần flush kế tiếp; đúng trạng thái trước khi reload |
| Mọi thao tác **suy biến thành không làm gì** | private window, WebView bị khoá, trình duyệt chặn site data, hết quota. Android và iOS chạy qua WebView của Capacitor nên đây là bề mặt thật. Nhật ký không ghi được là nhật ký không tồn tại — bài thi chạy y như trước khi file này có mặt. **Đây là lưới an toàn, không phải phụ thuộc.** |

**Nửa còn lại: retry có backoff và jitter.** Trước đó một lần lưu bị từ chối **chờ tới phím gõ tiếp
theo**. `flush` dừng drain khi thất bại — đúng, vì quay vòng ở đó sẽ gửi lại đúng patch bị từ chối
nhanh hết mức mạng có thể từ chối — nhưng không có gì được hẹn lại. Nên học viên mất mạng 20 giây
giữa bài viết có công việc nằm im cho tới khi họ tình cờ gõ tiếp, còn học viên **đã gõ xong** thì nằm
im cho tới khi nhấn Nộp bài — thời điểm tệ nhất để phát hiện mất mạng.

`1s → 2s → 4s → 8s → 15s`, ±25% jitter. **Mũ** vì thứ đang chờ thường là mạng chết chứ không phải
mạng bận. **Chặn trên 15 giây** vì một section dài 60 phút và backoff không giới hạn sẽ tới "thử lại
sau 9 phút" khi học viên vẫn đang ngồi đó. **Jitter** vì cả một phòng máy mất wifi cùng một khoảnh
khắc và sẽ quay lại đồng bộ — ba mươi tab retry cùng lúc, hỏng cùng lúc, rồi nhân đôi cùng lúc.

**DoD — đạt.** 8 test cho chính nhật ký (chạy trên `fake-indexeddb`, không phải một `Map` giả — mỗi
quy tắc ở đây là thứ một `Map` đúng miễn phí còn API thật thì không) + 3 test end-to-end. Test then
chốt: gỡ lệnh ghi nhật ký trong `change()` → `expected '' to be 'cartography'`.

> **Rủi ro đã đo, và đã truy ra nguyên nhân.** Suite web nhạy với tải CPU, nhưng không mơ hồ như
> `vitest.config.ts` từng kết luận. Đo cụ thể:
>
> | Điều kiện | Kết quả |
> |---|---|
> | Chạy độc lập, 4 lần liên tiếp | **243/243 × 4**, mỗi lần 35–39 s |
> | Chạy **ngay sau** `dotnet test backend` | **30 đỏ**, 26 trong đó là `Test timed out in 15000ms`; `setup` 147 s và `environment` 363 s — gấp ~9 lần bình thường |
>
> Không phải test hỏng: là hai bộ test tranh nhau một cái máy. Suite backend dựng 5 tiến trình dotnet
> cộng một Mongo trong Docker; chạy suite web đè lên đó thì cả file bị đói CPU và chết ở 15 giây.
>
> **`pnpm check` ở gốc repo đã đúng thứ tự** — `pnpm test` (web) chạy **trước** `pnpm test:api` — nên
> lệnh chuẩn không dính. Trong CI hai suite nằm ở hai workflow riêng, cũng không dính. Điều cần nhớ
> là **đừng chạy ngược thứ tự đó bằng tay**, và trên runner CI 2 nhân thì nên đo lại
> `poolOptions.threads.maxThreads: 3` — ba luồng trên hai nhân là vượt cấp.

---

## Phase I2 · Speaking upload và media integrity

### ✅ I2.1 · Khoá bản ghi tất định — xong 28/08/2026

Mục tiêu thật của item này là **idempotency**, và cách rẻ nhất để đạt nó **không phải** là bắt buộc
`Idempotency-Key`: lần trước làm vậy khiến **mọi** upload Speaking bị trả `400` trước khi tới handler,
và Speaking không trả lời được. Client không sửa được từ phía nó — `request()` là helper duy nhất
luồn key qua và nó serialise body thành JSON, còn bản ghi là multipart.

**Thay vào đó: suy ra khoá object từ `(session, question)`.** Id ngẫu nhiên nghĩa là **mỗi** lần
upload tạo một object mới:

- retry sau khi mất response ghi object **thứ hai** rồi trỏ tờ đáp án vào nó, bỏ rơi object thứ nhất;
- **ghi âm lại** làm đúng như vậy, **mỗi lần**.

Cả hai để lại một blob không ai tham chiếu, chứa **giọng nói học viên**. Suy ra khoá nghĩa là lần
upload thứ hai **thay thế** lần thứ nhất thay vì nhập bọn với nó.

Băm chứ không nối chuỗi: question id là nội dung đến từ gói đề, và một filename GridFS ghép bằng nối
chuỗi sẽ để một id chứa ký tự phân cách trỏ tới file của câu khác. Ghi đè an toàn ở đây vì **ghi dừng
trước khi đọc bắt đầu**: marking chạy sau khi tờ đã freeze, và closure protocol từ chối upload từ
khoảnh khắc đó (ADR-0015).

**DoD — đạt.** 2 test: upload cùng một câu hai lần → **một** object trong `speaking_recordings.files`,
cùng một id; hai câu khác nhau → hai bản ghi (khoá suy ra mà làm mọi bản ghi đè lên nhau sẽ là lỗi
tệ hơn hẳn thứ nó đang sửa).

### ✅ I2.2 · Finalize recording atomically — xong 28/08/2026

**Một lần upload là hai lần ghi**, và mọi kiểm tra của handler diễn ra **trước lần ghi thứ nhất**.
12 MB qua mạng di động không phải một cửa sổ ngắn, nên submit hoặc expiry có thể đóng Speaking bên
trong nó. Trước closure protocol, lần ghi thứ hai cứ thế hạ cánh: một bản ghi tồn tại, được index vào
một câu, và **không bao giờ được chấm** vì marking đã đọc tờ đáp án rồi. Với câu nói, object đó
thường là **bản duy nhất** của câu trả lời.

- `SetAnswerAsync` đã nằm sau rào chắn đóng-tờ từ `I1.2`.
- Handler bắt `SectionSheetClosedException` và **xoá luôn bytes vừa ghi**. Để lại chúng là một object
  không ai tham chiếu, chứa **giọng nói học viên** — dữ liệu cá nhân theo PDPL — tích luỹ mỗi lần
  upload xui. Xoá là thứ biến việc này thành **từ chối** thay vì **ghi nửa vời**.
- Việc dọn dẹp **không được đổi câu trả lời**: dọn thất bại thì lời từ chối vẫn giữ nguyên. Caller
  không được báo là upload thành công chỉ vì phần dọn dẹp thất bại.
- Endpoint trả `409 SECTION_NOT_OPEN` kèm câu nói rõ bản ghi **sẽ không được chấm**. Trả 200 ở đây là
  nói với học viên rằng câu nói của họ đã được nộp cho một bài đã chấm xong mà không có nó.

### ✅ I2.3 · Upload vs submit/expiry barrier tests — xong 28/08/2026

Hai test: một cho phiên đã đóng hẳn (từ chối **trước** khi ghi storage — `Saved` rỗng), một cho đúng
cửa sổ mà kiểm tra trạng thái không nhìn thấy được — **tờ đã freeze trong khi phiên vẫn đang chạy**,
tức khoảng giữa hai lần ghi trong mọi đường đóng section, và cũng là trạng thái ADR-0015 chấp nhận.

Test thứ hai khẳng định **cả hai** tính chất: upload bị từ chối, **và** bytes đã ghi bị xoá. Chỉ
khẳng định vế đầu sẽ bỏ qua một orphan — đúng chế độ hỏng mà test này sinh ra để bắt.

Đã vá thêm một lỗ trong chính test double: `FakeAnswerSheetStore.SetAnswerAsync` **không** mô phỏng
rào chắn, nên test rào chắn Speaking sẽ xanh trong khi thứ nó kiểm tra không tồn tại — cùng hình dạng
lỗ hổng mà store thật có trước ADR-0015.

### ✅ I2.4 · Recording reconciliation — xong 28/08/2026

Bốn thứ để lại một bản ghi không ai trỏ tới, và **chỉ một lượt quét mới thấy được cái nào**:

- upload đã stream xong bytes rồi bị từ chối vì section vừa freeze — handler xoá object, và lệnh xoá
  **được phép** thất bại, vì lời từ chối phải đứng vững bất kể việc dọn dẹp có chạy được không;
- crash giữa lúc ghi bản mới và xoá bản cũ;
- một phiên thi hoặc một tài khoản bị xoá trong khi audio của nó còn trên đĩa;
- mọi thứ ghi trước khi khoá trở nên tất định, khi mỗi lần ghi âm lại bỏ rơi bản trước.

**Đây không phải chuyện gọn gàng.** Một bản ghi không ai tham chiếu là **giọng nói học viên** — dữ
liệu cá nhân theo PDPL, và **giới hạn lưu trữ** là một nguyên tắc luật nêu rõ. *"Chúng tôi giữ vì
không có gì xoá nó"* không phải một căn cứ hợp pháp, và đó là điều sản phẩm đang làm.

**Hai lớp chắn, mỗi lớp đóng một cách việc này có thể phá hỏng công việc thật:**

- **Ràng buộc tuổi.** Bản ghi viết vài giây trước có thể chỉ còn vài giây nữa là được ghi vào tờ đáp
  án — object ghi trước, id ghi sau, nên **luôn** có một khoảnh khắc storage giữ audio mà tờ chưa gọi
  tên. Quét trong khoảnh khắc đó sẽ **xoá câu trả lời của học viên trong lúc họ đang upload nó**.
- **Kiểm tra tham chiếu bằng cách đọc tờ**, không phải một bản cache của nó.

Mọi nút vặn đều nghiêng về phía **để nguyên**: tuổi tối thiểu 6 giờ, lô 200, một giờ một lượt, và
**tắt trừ khi bật**. Chế độ hỏng của "quá hăng" là xoá bản duy nhất của một câu nói; chế độ hỏng của
"quá cẩn thận" là tốn ít đĩa. Hai thứ đó không so sánh được.

**DoD — đạt.** 4 test, và test quan trọng nhất là test **không xoá**: bản ghi mà tờ vẫn gọi tên được
để yên.

### ✅ I2.5 · Retention và deletion — **khe cắm có cấu hình, theo `G-11`**

Retention là quyết định của chủ sản phẩm, nên nó ship dưới dạng **cài đặt có mặc định được nêu rõ**,
không phải một con số bịa trong handler:

| Cài đặt | Mặc định | Nghĩa |
|---|---|---|
| `Recordings:SweepEnabled` | `false` | Một tiến trình nền **xoá audio** không phải thứ bật mặc định trong môi trường chưa ai xem qua. Khoảnh khắc ai đó bật nó cũng là khoảnh khắc họ quyết định cửa sổ lưu trữ |
| `Recordings:OrphanMinimumAgeHours` | `6` | Tuổi tối thiểu trước khi một bản ghi được gọi là mồ côi |
| `Recordings:SweepIntervalMinutes` | `60` | Nhịp quét |
| `Recordings:SweepBatchSize` | `200` | Chặn trên mỗi lượt, để quét không bao giờ thành scan toàn bộ |

**Metrics** trả về từ `SweepAsync` dưới dạng `ReconciliationReport(Examined, Orphaned, Removed, Failed)` —
**trả về chứ không log**, cùng lý do với mọi outcome khác ở tầng này: Application không phụ thuộc
abstraction logging (có architecture test chặn), và một con số chỉ được log là con số **không metric
nào đọc được**. `Orphaned` là con số đáng cảnh báo: nhỏ giọt đều là hệ quả bình thường của upload bị
từ chối; **tăng vọt** nghĩa là có thứ đang ghi audio không bao giờ tới được tờ đáp án.

> **Còn nợ, cần chủ sản phẩm:** cửa sổ lưu trữ **thật** (bao lâu thì audio của một phiên đã chấm xong
> bị xoá), và đường xoá theo yêu cầu chủ thể dữ liệu. Cả hai là chính sách, không phải mã — và cả hai
> giờ chỉ cách một dòng cấu hình.

### ✅ I2.6 · Sửa exact multipart limit — xong 28/08/2026, **có một lỗ đo được**

Hai cái cap là **cùng một con số**, và điều đó khiến cái lớn hơn **không thể chạm tới**. Một body
multipart không chỉ có file: boundary trước và sau mỗi part, một `Content-Disposition` và một
`Content-Type` mỗi part, cộng trường `questionId`. Nên một bản ghi **đúng bằng** ngưỡng tạo ra body
lớn hơn ngưỡng vài trăm byte, Kestrel từ chối trước khi bất kỳ mã ứng dụng nào chạy, và học viên nhận
một lỗi transport thay vì câu 413 có mã lỗi riêng của endpoint — **cho một bản ghi nằm trong giới hạn
đã công bố**.

Đã tách: `MultipartOverheadBytes = 64 KB`, có giới hạn chứ không hào phóng — transport cap là thứ
duy nhất chặn upload không giới hạn ở route này.

> **Lỗ đã đo, không phải suy đoán:** `WebApplicationFactory` chạy trên `TestServer`, và ở đó
> `IHttpMaxRequestBodySizeFeature` là **`null`** — đã đo bằng probe. Nên khối nâng cap **không bao
> giờ chạy dưới test**, và **mọi** test upload trong `ExamRunContractTests` chỉ đang kiểm tra
> `file.Length` của endpoint chứ không kiểm tra gì ở tầng transport.
>
> Điều này quan trọng vì lỗi mà khối đó sinh ra để chữa — mặc định 1 MB của Kestrel từ chối **mọi**
> bản ghi Speaking thật trước khi mã ứng dụng chạy, chính là `A17` — đúng là loại lỗi suite này
> **không nhìn thấy được**. Nó cần một server thật. → `I7.4`
>
> Hai test mới vẫn có giá trị: chúng khoá `file.Length` và mã lỗi `RECORDING_TOO_LARGE`. Chúng
> **không** chứng minh bản vá transport, và đã ghi rõ như vậy ngay trong mã.

---

## Phase I3 · Durable marking và worker

### ✅ I3.1 + I3.2 · Marking Outbox và state machine — xong 28/08/2026

**Lỗ hổng mà chính mã nguồn đã mô tả bằng văn xuôi rồi không làm gì.** Section đóng, rồi mới được
chấm. Đó là hai lần ghi, và transition **phải đi trước** — nếu không, hai caller đến cùng lúc đều
chấm và **đánh giá bị mua hai lần**. Nên thứ tự là đúng, và cái giá của nó là một cửa sổ: process chết
giữa transition và marking để lại một section **đã đóng và chưa chấm**.

Với Reading/Listening cửa sổ đó sống được, vì điểm xác định tính lại được từ đáp án. Với
Writing/Speaking nó **vĩnh viễn**: submit vào lại thì short-circuit vì phiên không còn `inprogress`
nữa, và lượt catch-up **cố ý** bỏ qua các module không xác định — chạy lại đánh giá mỗi lần vào màn
kết quả sẽ là một chuỗi retry có tính phí không giới hạn ngay khi provider được nối.

**Điểm số biến mất suốt đời phiên thi, và không chỗ nào nói ra.**

**Outbox làm *ý định* trở nên bền, thay vì *lần thử*.** Đóng section ghi lại rằng section này **cần**
chấm; một worker biến nó thành điểm. Worker có thể crash, restart, hay bị deploy đè mà không mất đi
sự thật rằng công việc đang nợ.

| Quyết định | Lý do |
|---|---|
| `OperationId = {session}:{module}:{rubricVersion}` | đóng lại section không được tạo job thứ hai, nên id **không thể** ngẫu nhiên; nhưng rubric đổi là một phán xét **thật sự khác**, nên ghim id vào mỗi phiên sẽ âm thầm **từ chối chấm lại** dưới rubric đã sửa |
| `_id` chính là operation id | tính duy nhất mang **tính cấu trúc**, không phải một index ai đó phải nhớ tạo |
| **Không** dùng transaction chung với freeze + transition | ba collection; một transaction phủ cả ba sẽ đẩy **mọi** lần chuyển section qua một distributed commit, cho một job vốn an toàn khi enqueue hai lần. Unique index làm "hai lần" vô hại; **thứ tự** làm "enqueue mà chưa đóng" bất khả |
| State machine 5 trạng thái | `pending → running → retryable → failed → completed`. `failed` **lưu vào database chứ không log**: học viên nhìn dấu gạch ngang xứng đáng biết "chưa nối evaluator" khác "đã thử năm lần rồi thôi", và màn kết quả không đọc được log |

### ✅ I3.3 · Worker thật — xong 28/08/2026

**Thứ đứng ở đó là template của project** — một vòng lặp log giờ mỗi giây, không phụ thuộc gì cả. Tệ
hơn một file rỗng, vì một service **đang chạy** là bằng chứng với bất cứ ai đi kiểm tra.

- **Gọi `AddInfrastructure`** — cùng store, cùng evaluator port, cùng rubric source với API. Một
  composition root thứ hai sẽ là **định nghĩa thứ hai** về "chấm là gì", và cái trôi lệch luôn là cái
  chưa ai báo lỗi.
- **Claim nguyên tử.** Tìm job rồi đánh dấu running là hai câu lệnh, và hai worker lọt vào giữa — với
  hàng đợi này nghĩa là hai lần gọi provider có tính phí cho một bài luận.
- **Gia hạn lease trong lúc chạy** (2 phút, heartbeat 40 giây). Lease chặn **cái chết**, không chặn
  thời lượng.
- **Backoff mũ có jitter, 5 lần thử.** Jitter không phải trang trí: một sự cố provider làm hỏng **mọi**
  job cùng lúc, và không có jitter thì tất cả quay lại cùng một khoảnh khắc, hỏng cùng nhau, rồi nhân
  đôi cùng nhau — một đàn voi giẫm lên thứ vốn đã đang yếu.
- **Tắt êm.** Deploy huỷ token; vòng lặp ngừng claim và để job đang cầm chạy xong. Giết giữa lời gọi
  provider sẽ để lại một lease chờ hết hạn và một đánh giá **đã trả tiền**.
- **Không log heartbeat.** Hàng đợi rỗng thì nên im lặng. Một dòng mỗi giây là cách một log ngừng
  được đọc.

Một chi tiết dễ bỏ sót: **outcome không phải marking thì không phải thành công.** `AwaitingEvaluator`
và `AwaitingTranscript` mô tả một sản phẩm **chưa xong**; hoàn thành job trên chúng sẽ **xoá mất** bản
ghi rằng section này vẫn nợ một điểm — âm thầm, và vĩnh viễn. Chúng được ném ra để job ở lại hàng đợi,
lùi lại, và cuối cùng dead-letter với lý do **hiện trên màn kết quả**.

### ✅ I3.4 · Crash recovery — xong 28/08/2026

8 test tích hợp chạy trên Mongo thật. Mỗi quy tắc ở đây là thứ một dictionary trong bộ nhớ đúng miễn
phí còn database thì không.

| Tính chất | Test |
|---|---|
| Đóng cùng section hai lần chỉ còn một job | `Closing_the_same_section_twice_leaves_one_job` |
| **Đúng một** trong hai worker giành được job | `Exactly_one_of_two_workers_claims_a_job` |
| **Worker chết → job được nhận lại sau khi lease hết** | `A_job_whose_worker_died_is_taken_over_once_its_lease_expires` |
| Worker mất lease **không** hoàn thành được job | `A_worker_that_lost_its_lease_cannot_complete_the_job` |
| Backoff thật sự chặn, không phải trang trí | `A_retry_is_not_claimable_until_its_backoff_has_elapsed` |
| Bỏ cuộc là bỏ cuộc, và giữ lý do | `A_failed_job_stays_failed_and_keeps_its_reason` |
| Job xong không bao giờ bị nhận lại | `A_completed_job_is_never_claimed_again` |
| Rubric mới là job mới | `A_new_rubric_version_is_a_new_job` |

Cộng 5 test vòng đời ở tầng Application. Gỡ enqueue → **3 test đỏ**.

### ✅ I3.5 · Paid evaluator idempotency — **nền đã xong, phần provider chờ adapter**

- `OperationId` ổn định là thứ sẽ được đưa cho provider làm idempotency key của chính nó.
- `SectionMarkingRunner` **đã** idempotent: nó **đọc cái gì đã chấm trước khi chấm**, nên retry sau
  một response mất không mua đánh giá thứ hai. Đây là cái đắt nhất, và nó có sẵn.
- **Provenance** (provider/model/rubric) và **cost/latency metrics** gắn vào adapter, mà adapter là
  `I6`. Ghi rõ ở đây để không ai tưởng nó đã có.

### ✅ I3.6 · Result marking status — xong 28/08/2026

**Một câu cho bốn tình huống là một lời nói dối.** Màn kết quả nói *"AI chấm và chưa nối với mô hình
nào"* bất kể chuyện gì xảy ra — đúng khi chưa nối gì, và **sai** khi bài luận đang xếp hàng, **sai**
khi bản ghi chưa có transcript, **sai** khi hệ thống đã thử năm lần rồi dừng. Bốn tình huống, bốn câu
trả lời khác nhau cho "giờ tôi phải làm gì".

API trả `markingStatuses[]`: `module`, `state` (chính state của worker, không phải bản dịch), `attempts`,
và một `reason` **viết cho học viên**. Lý do được **ánh xạ chứ không chuyển tiếp**: lỗi thô của
provider có thể mang theo mảnh prompt, request id, hoặc chính lời của học viên trả ngược lại họ —
không thứ nào thuộc về màn kết quả.

Câu thông báo chung cũ **được giữ**, nhưng chỉ cho phiên thi **không có job nào phía sau** — phiên
đóng trước khi outbox tồn tại. Có job thì các dòng theo từng module đúng hơn hẳn, nên hiện cả hai sẽ
là trang tự mâu thuẫn.

---

## Phase I4 · Token và session hardening

### ✅ I4.1 + I4.2 + I4.3 · Một coordinator, một generation, một khoá — xong 28/08/2026

**Có bốn nơi trình được refresh token, và chúng bất đồng với nhau.** Token là *dùng một lần*: server
xoay nó và coi lần trình thứ hai là replay, **thu hồi cả family**. Bốn nơi đó là:

1. timer chủ động của provider, một phút trước hạn;
2. restore lúc mount, khi token đã hết hạn;
3. transport retry sau `401` trên lời gọi JSON;
4. transport retry sau `401` trên `fetch` thô — audio, ảnh, upload Speaking.

(3) và (4) đã dùng chung một single-flight guard. (1) và (2) dùng một boolean riêng. Nên timer nổ
trong khi restore đang bay, hoặc máy vừa thức dậy gặp một lệnh tải audio, **trình cùng một token hai
lần và kết thúc chính phiên nó đang cố cứu**. Nhìn từ bàn học: *"đang thi thì tự nhiên bị đăng xuất"*.

**Và guard đó chỉ trong một tab, tức là thiếu mất một tab.** Hai tab là hai heap JavaScript với hai
guard trên **một** refresh token trong storage. Mở app ở tab thứ hai khi tab thứ nhất đang thi là
chuyện bình thường, và đủ để thu hồi family.

**[`packages/auth/src/coordinator.ts`](../../packages/auth/src/coordinator.ts) — bốn tính chất:**

| | |
|---|---|
| **Một promise, mỗi tab** | mọi caller **nhập vào** lần xoay đang bay thay vì mở lần mới |
| **Một khoá, xuyên tab** | `navigator.locks`, fallback lease trên `localStorage` cho WebView cũ — Android và iOS đều chạy qua WebView nên fallback là yêu cầu thật |
| **Đọc lại storage *bên trong* khoá** | **đây mới là nửa quan trọng.** Khoá chỉ tuần tự hoá vẫn để tab thua trình token của nó ngay sau khi tab thắng dùng — đúng replay đó, chậm một nhịp. Ai giữ khoá thì đọc lại trước, thấy tab kia đã xoay rồi thì **nhận** phiên của họ |
| **Generation** | đăng nhập / đăng xuất / SSO adoption đều tăng. Kết quả async tính dưới generation cũ thuộc về một phiên **không còn tồn tại**; ghi nó lại là cách một tab đã đăng xuất sống dậy, hoặc `/me` của tài khoản này rơi lên màn hình tài khoản kia |
| **Broadcast** | xoay / nhận / đăng xuất đều được báo, nên tab khác **nhận** phiên mới thay vì phát hiện bằng cách hỏng |

`adopted` được xử lý riêng vì nó **có thể là tài khoản khác** — một người đăng xuất và người khác đăng
nhập trên máy dùng chung. Đọc lại `/me` là thứ ngăn tab này hiển thị tên học viên trước trên phiên của
học viên sau.

**DoD — đạt.** 8 test mới trong `packages/auth` (chạy trên jsdom, không phải node: việc của coordinator
là điều phối *trình duyệt*). Test then chốt: ba caller đồng thời → **trình token đúng một lần**; và tab
thua khoá → **không trình lần nào**, nhận phiên của tab kia.

### ✅ I4.4 · Lost-response recovery — xong 28/08/2026

**Một gói tin rơi đang làm mất phiên thi.** Xoay token đánh dấu token cũ đã dùng rồi phát hành token
kế. Nếu response mang token kế **không tới được client** — điện thoại ra khỏi hầm, proxy timeout,
WebView bị hệ điều hành treo giữa chừng — client thử lại bằng token duy nhất nó có, tức là token vừa
bị đánh dấu đã dùng. Phát hiện replay khi đó làm đúng cái điều nó **không bao giờ được làm nhầm**:
thu hồi cả family và đăng xuất học viên **giữa bài thi**, vì một gói tin rơi. Trên mạng di động đó
không phải trường hợp biên; đó là thứ Ba.

**`successorTokenHash` phân biệt hai việc.** Successor **chưa từng được dùng** nghĩa là không ai từng
nhận được nó → đây là mất response, phiên cứu được. Successor **đã được dùng** nghĩa là hai bên đang
giữ token trong cùng chuỗi, một trong hai đã trộm — và **thu hồi family vẫn nổ**, đúng như thiết kế.

Ba lớp bảo vệ, không phải một:

- **Cửa sổ 60 giây.** Client mất response thử lại trong vài giây; kẻ trộm trình khi nào tiện.
- **Successor phải nguyên vẹn.** Kẻ trộm chỉ có lợi nếu client hợp lệ **chưa bao giờ** nhận được nó.
- **Successor mồ côi bị thu hồi.** Cấp lại mà không làm việc này sẽ để **hai token sống trong một
  family** — đúng trạng thái mà toàn bộ cơ chế sinh ra để loại trừ.

Việc cứu được **giành nguyên tử**: hai lần thử lại của cùng một response mất chỉ cứu **một** lần. Đọc
rồi ghi sẽ cho cả hai lọt — dưới đúng áp lực retry đã tạo ra tình huống này.

**DoD — đạt.** 3 test. Gỡ recovery → 2 test đỏ. Test cặp đôi khẳng định replay **sau khi successor đã
được dùng** vẫn thiêu rụi family, và token đang sống cũng chết theo.

### ✅ I4.5 · Refresh retry semantics — xong 28/08/2026

- **`429` giữ phiên.** Bị bảo chờ **không phải** bị bảo không. Kết thúc phiên ở đó biến một rate limit
  thành một lần đăng xuất — phản ứng duy nhất chắc chắn khiến client thử lại **mọi thứ** nó đang làm,
  kể cả thứ đã gây ra rate limit.
- **Không hỏi được thì giữ nguyên.** Mất mạng, trang lỗi của proxy, 5xx — session ở nguyên trên đĩa để
  lần sau thử lại.
- **Chỉ lời từ chối mới kết thúc phiên.** Một câu trả lời là một câu trả lời, và câu trả lời là không.

Cả ba nằm ở **một chỗ** trong coordinator thay vì được chép lại ở bốn nơi — đó là điểm chính của I4.1.

### ✅ I4.6 · Server-side logout — xong 28/08/2026

**Đăng xuất trước nay là một hành vi cục bộ, và lẽ ra không bao giờ được như vậy.** Client xoá
`localStorage` và **hết**: refresh token family sống tiếp đủ ba mươi ngày. Nên đăng xuất trên máy dùng
chung, máy thư viện, hay điện thoại sắp sang tay **để lại một credential còn chạy được** — khôi phục
được từ bản sao lưu profile trình duyệt, hoặc từ bất cứ thứ gì đã kịp copy giá trị đó.

- `POST /api/v1/auth/logout`, có xác thực, thu hồi family lấy từ claim `fam` của chính access token —
  **không có tham số nào** để phiên này kết thúc phiên khác.
- Client **không chờ** nó: người bấm "đăng xuất" trên máy dùng chung là đúng người không thể bị bắt
  chờ. Việc xoá cục bộ là thứ làm họ đăng xuất; lệnh này là thứ làm credential chết.
- Miễn idempotency key: đây là thao tác **không bao giờ được từ chối**.

**DoD — đạt.** 3 test: refresh token còn lại trên máy hết tác dụng; thiết bị khác **không** bị ảnh
hưởng; bấm hai lần vẫn thành công.

---

## Phase I5 · Idempotency hardening

### ✅ I5.1 · Unknown outcome state — xong 28/08/2026

**"Handler ném nghĩa là chưa quyết gì" đúng với ngoại lệ ném *trước* khi commit, và sai hoàn toàn với
ngoại lệ ném *sau*.** Middleware chạy handler trong một `try` mà `catch` của nó **xoá claim**, với lý
lẽ rằng ngoại lệ nghĩa là thao tác thất bại nên retry phải chạy được.

Hình dạng phổ biến của vế thứ hai: điện thoại đổi mạng trong lúc response đang được ghi. Transition
đã hạ cánh, `OperationCanceledException` bung ngược qua middleware, claim bị xoá, và retry **advance
phiên thi lần thứ hai**.

**Hai tín hiệu nói side effect có thể đã hạ cánh:**

- **Caller biến mất.** Cancellation nói điều gì đó về **caller** và **không nói gì** về **handler**,
  nên đọc nó thành "chưa có gì xảy ra" là đọc nhầm đầu của request.
- **Handler tự nói.** `CommittedMarker` được đặt bởi handler đã qua điểm không quay lại của chính nó
  — phủ các ngoại lệ mà cancellation không phủ, ví dụ marking runner hỏng sau khi transition CAS đã
  thành công. `/advance` và `/submit` đều đặt.

Mọi thứ khác **vẫn xoá claim**, có chủ đích: lưu một 500 tạm thời sẽ biến nó thành vĩnh viễn suốt đời
key, và retry là toàn bộ lý do tồn tại của key.

Claim ở `unknown` **không được xoá cũng không được hoàn thành**. Retry trong lease nhận
`409 OPERATION_OUTCOME_UNKNOWN` + `Retry-After` kèm câu "hãy đọc trạng thái hiện tại thay vì thử lại".
Qua lease thì được giành chỗ — **chặn trên thời gian chờ, không phải bẫy key 24 giờ**.

**DoD — đạt.** 5 test. Test then chốt lái thẳng middleware với handler commit-rồi-cancel; gỡ quy tắc →
đỏ. Test cặp đôi: handler hỏng **trước** khi commit **vẫn** nhả claim.

### ✅ I5.2 · Completion ownership — xong 28/08/2026

- `UpdateOneAsync` khi hoàn thành kiểm `MatchedCount`. Bằng 0 nghĩa là lease đã hết trong lúc handler
  còn chạy và ai đó đã giành claim: **thao tác đã chạy hơn một lần và hai caller sẽ nhận hai câu trả
  lời khác nhau cho một key**. Không sửa được ở đó, nhưng nói được — im lặng bỏ qua là cách một lease
  quá ngắn cứ tiếp tục quá ngắn.
- Giành chỗ được log ở mức `Warning` kèm state trước đó: nó nghĩa là process giữ key đã chết, hoặc
  handler chạy quá lease. Cái thứ hai là cách một lần chấm mất tiền bị mua hai lần.

### ✅ I5.3 · Lease heartbeat — xong 28/08/2026

**Lease chọn theo ước lượng là một canh bạc, và heartbeat là thứ khiến đoán sai vẫn an toàn.** Năm
phút được chọn theo những gì `/submit` và `/advance` làm hôm nay; ngày evaluator được nối, chúng thành
hai lần gọi model tuần tự cộng một lượt ASR trên tối đa 14 phút audio, và **chưa ai biết đó là 3 phút
hay 12**. Lease cố định mà hụt thì không suy biến êm: giành chỗ nổ ra trong khi request đầu còn đang
ở giữa một lần gọi provider có tính phí, và pre-check "đã chấm rồi" không bắt được vì lần chạy đầu
chưa ghi gì.

Heartbeat bỏ luôn nhu cầu dự đoán: lease **di chuyển trong khi handler chạy**; process chết thì
heartbeat chết theo và lease hết hạn như thường. Nên lease chặn **cái chết** — đúng việc nó vốn làm
tốt — thay vì chặn **thời lượng**, việc nó chưa bao giờ làm tốt.

Gia hạn **trên token của chính claim**: đã bị giành chỗ thì update không khớp gì và heartbeat dừng.

Lease thành tham số constructor (mặc định 5 phút), heartbeat suy ra `lease / 5`. Không phải để tiện
test: chứng minh gia hạn cần sống qua một chu kỳ, và ở chu kỳ production đó là **một phút wall clock
mỗi assertion** — loại test rồi sẽ bị xoá.

**DoD — đạt.** Test khẳng định lease **đã dịch chuyển**, không phải "nằm ở tương lai" (điều đúng sẵn
vì claim đầu tiên đã đặt nó trước cả một lease). Gỡ heartbeat → **3 test đỏ**.

### ✅ I5.4 · Replay fidelity — xong 28/08/2026

Replay trả lại mọi response bằng `application/json`, đúng với mọi endpoint đang được canh hôm nay và
**sai với tư cách một quy tắc**: endpoint được canh đầu tiên trả `204`, hoặc `application/problem+json`,
hoặc bất cứ thứ gì có `Location`, sẽ có bản replay **khác** bản gốc — và một replay khác thứ nó replay
thì không phải replay.

Giờ lưu và trả lại `contentType` và `Location`; body rỗng không ghi gì. JSON là fallback cho bản ghi
viết trước khi có trường này.

### ✅ I5.5 · Rate-limit transition — xong 28/08/2026

`/advance` và `/submit` **chưa có giới hạn nào**, trong khi chúng là hai thao tác đắt nhất sản phẩm:
mỗi cái đóng một section, chấm nó, và ngày evaluator được nối thì mua một lần gọi model cho Writing
và một lượt ASR cho Speaking. Client kẹt vòng lặp retry đang tiêu tiền thật mỗi lần thử.

**Phân vùng theo *phiên thi*, không theo user.** Một học viên có một phiên mở tại một thời điểm và một
Full Test có 3 advance + 1 submit trong 60 phút, nên giới hạn trung thực là theo phiên: rộng rãi cho
người làm việc bình thường, chặn đứng vòng lặp. Phân vùng theo user sẽ để phiên thứ hai thừa hưởng
sự cạn kiệt của phiên thứ nhất — biến một biện pháp phòng thủ thành sự cố điểm số, đúng thứ chính sách
`InSessionRead` sinh ra để **không** làm.

**12/phút/phiên** — hai bậc độ lớn dư cho học viên, chặn cứng cho vòng lặp. Rate limiter đứng **trước**
idempotency guard trong pipeline, nên replay cùng key vẫn tiêu một permit; **đó là thứ tự đúng** (một
trận lụt replay vẫn là trận lụt) và là lý do con số được đặt theo hành vi retry chứ không theo hành vi
con người. Có `Retry-After`.

---

## Phase I6 · Production infrastructure

### ✅ I6.1 · Email sender — xong 28/08/2026

**SMTP, và đó là quyết định về *coupling* chứ không phải về nhà cung cấp.** Mọi provider đáng dùng —
SES, SendGrid, Postmark, Mailgun, một nhà cung cấp Việt Nam — đều nói SMTP. Chọn SMTP là chọn **không
chọn**: provider trở thành một hostname và một credential trong cấu hình, và đổi nhà là đổi deployment
chứ không phải đổi mã.

Điều đó quan trọng hơn bình thường ở đây: `B-2` chưa ngã ngũ, và một địa chỉ email gửi tới provider
nước ngoài là **chuyển dữ liệu xuyên biên giới** với cùng nghĩa vụ như mọi thứ khác. Nếu câu trả lời
là "giữ trong Việt Nam", đổi provider **không được phép** là viết lại.

| Quyết định | Lý do |
|---|---|
| Mặc định cổng **587**, không phải 25 | Cổng 25 là relay không xác thực giữa các server, bị chặn outbound ở hầu hết nhà cung cấp, và **không có kỳ vọng mã hoá**. Một mặc định âm thầm gửi link đặt lại mật khẩu ở dạng rõ là mặc định sai. **Cổng 25 giờ bị từ chối boot** |
| `SecureSocketOptions.StartTls`, **không** phải `StartTlsWhenAvailable` | cái sau **tự hạ cấp xuống plaintext** khi server không chào TLS — và server không chào TLS đúng là trường hợp một link đặt lại mật khẩu **không được** gửi |
| `ClientBaseUrl` từ cấu hình, không dựng từ header `Host` | dựng link từ header đến sẽ để bất cứ ai chạm được tiến trình này **chọn tên miền** mà link đặt lại mật khẩu trỏ tới — một mồi phishing khởi đầu từ chính mail của chúng ta |
| Cả HTML **và** plain text | client không render HTML — hoặc người đã tắt nó, phổ biến đúng ở nhóm cẩn thận về bảo mật, những người đọc mail đặt lại mật khẩu kỹ nhất — sẽ nhận một thư trắng với một link không thấy được |
| Lỗi trả về `NotSent`, **không ném** | ném sẽ làm hỏng chính lần đăng ký sinh ra nó — và học viên có tài khoản đã tạo thành công **không được** bị báo là đăng ký thất bại vì một mail server chậm |
| **Không log địa chỉ** | mail xác minh gửi tới địa chỉ ai đó vừa gõ; một dòng log nêu tên nó là một dòng log phải được coi là dữ liệu cá nhân suốt thời gian còn giữ |

Cổng khởi động từ chối boot production khi chưa cấu hình: sender còn lại **ghi link vào log server** —
đúng trong Development và là **lời nói dối thẳng** trong production.

### ✅ I6.2 · Object storage adapter — xong 28/08/2026

**Trước đó asset store duy nhất là một trình đọc thư mục fixtures, đăng ký *chỉ trong Development*** —
nên một tiến trình production **không có** audio đề, **không có** ảnh đề, **không có** audio nghe chép
chính tả. Listening sẽ không phát gì, và lỗi trông như **player hỏng** chứ không như thiếu adapter.

- Adapter **S3-compatible**, phủ cả MinIO (đã có sẵn trong local stack) lẫn AWS S3 lẫn mọi provider
  nói cùng giao thức.
- **Port đã phải đổi hình dạng trước**: `Open` đồng bộ ổn với file cùng đĩa và **bất khả** với object
  storage — mở một object S3 là một vòng mạng, và chặn nó trên request thread là cách một bucket chậm
  thành cạn kiệt thread pool.
- **Range requests** giữ nguyên (thanh tua audio), thêm **`Content-Length`** và **`ETag`**: đề đã
  publish là bất biến nên audio không đổi, và trình duyệt đã có file phải hỏi được "vẫn là file này
  chứ" bằng một header thay vì tải lại vài megabyte.
- Content type **lấy từ store, không đoán từ khoá**: đuôi file là nội dung đến cùng gói đề, và trình
  duyệt bị báo sai kiểu thì hoặc từ chối phát hoặc **sniff** — mà sniff đúng là thứ content type sinh
  ra để ngăn.
- Tham chiếu **không bao giờ được tin là khoá**: cùng ba lớp kiểm tra như fixture store.

### ✅ I6.3 · Configuration validation — xong 28/08/2026

Trước đó **đúng một** cài đặt được kiểm (signing key, vì đã có người bị cắn). Mọi thứ khác lộ ra lúc
chạy, dưới dạng lỗi của người dùng:

| Sai cấu hình | Biểu hiện trước khi có cổng |
|---|---|
| `Jwt:Issuer` sai | **mọi** lần đăng nhập trả 401 không ai giải thích được |
| `Cors:Origins` rỗng | app hỏng trong trình duyệt **không để lại dấu vết nào ở phía server** — API đã trả 200, trình duyệt mới từ chối |
| Mongo trỏ vào standalone | trừ token **âm thầm không nguyên tử** |
| Thiếu `Sso:ClientBaseUrl` | đăng nhập Google xong rơi vào trang trắng |
| `Sso:EnableStubProvider` bật ngoài Development | **bypass xác thực** — phát session cho bất kỳ ai hỏi |

**Báo mọi lỗi cùng một lúc**, không phải lỗi đầu tiên: một validator ném ở lỗi đầu biến việc dựng môi
trường mới thành đúng bằng số lần deploy như số lỗi, và người làm không có cách nào biết còn mấy cái.

**Development được kiểm nhưng được *báo* thay vì bị *từ chối*** — trừ những thứ là bí mật, mà không
môi trường nào được phép bịa ra.

### ✅ I6.4 · Live/readiness health — xong 28/08/2026

Trước đó là **một** `/health` trả `ok` **vô điều kiện** — trả lời như nhau dù database có sống hay
không, tệ hơn là không có: load balancer định tuyến vào nó, một lần deploy xanh vì nó, và người vận
hành nhìn thấy một hệ thống khoẻ mạnh đang trả 500.

- **`/health/live`** hỏi *"tiến trình này có đáng giữ không"* — **không chạm gì bên ngoài**. Đây là
  thứ chính sách restart đọc, và một liveness probe hỏng vì một phụ thuộc là cách một sự cố ngắn
  thành **vòng lặp restart** trên mọi tiến trình.
- **`/health/ready`** hỏi *"có nên đẩy traffic vào không"* — kiểm Mongo **và** object storage, mỗi cái
  có deadline riêng 2 giây. Một probe treo là một probe **fail open**.
- **Phụ thuộc tuỳ chọn không được làm hỏng readiness**: chưa có evaluator AI (`B-2`), và
  Reading/Listening chấm theo đáp án nên không bao giờ chạm tới nó.
- **Không rò rỉ**: endpoint ẩn danh, nên chỉ trả **kiểu** exception chứ không trả message — message
  của driver có thể mang theo connection string.

### ✅ I6.5 · Production Docker — xong 28/08/2026

**Không có Dockerfile nào**, nên không deploy được — và quan trọng hơn, **đường cấu hình production
chưa bao giờ được thực thi**. Đó là cách `Sso:EnableStubProvider` và `Cors:Origins` rỗng có thể tới
một môi trường thật mà không gì nhận ra.

- Base image **ghim theo tag có digest**, không phải `latest`: base trôi dưới một lần rebuild nghĩa là
  hai lần deploy cùng một commit là **hai hệ thống khác nhau**.
- **Hai stage**: SDK ~800 MB và chứa compiler, package manager và mã nguồn đã build; không thứ nào
  thuộc về một container đang chạy.
- **Non-root** (`uid 1654`), và phải nói rõ: base image có sẵn user `app` và **không tự chọn** nó.
- **API và Worker là hai image riêng**: chúng scale theo tín hiệu khác nhau và hỏng khác nhau — một
  worker kẹt trong lời gọi provider chậm **không được** kéo theo capacity phục vụ request.
- **Không mặc định `ASPNETCORE_ENVIRONMENT`**: mặc định Production đúng chín trên mười lần và **thảm
  hoạ ở lần thứ mười**, khi ai đó chạy image này cục bộ.
- **`--healthcheck` tự chứa**: image `aspnet` không có `curl` cũng không có `wget`, nên `HEALTHCHECK`
  chỉ có ba lựa chọn — cài thêm gói, chạy shell không gọi được HTTP, hoặc **hỏi chính ứng dụng**. Chế
  độ này chạy **trước `CreateBuilder`**: nó là client, không phải server, và một probe khởi động bản
  sao thứ hai của tiến trình là một probe hỏng vì chính áp lực bộ nhớ nó gây ra.

**DoD — đạt, và đã chạy thật:**

| Kiểm chứng | Kết quả |
|---|---|
| `docker build` cả hai image | thành công |
| Boot Production **thiếu object storage** | **từ chối boot**, nêu đúng thứ thiếu |
| Boot Production **đủ cấu hình** | `Now listening on: http://[::]:8080` · `Hosting environment: Production` |
| `/health/live` | `{"status":"live"}` `200` |
| `/health/ready` | `{"status":"ready","checks":[{"name":"mongo","status":"ok","ms":1},{"name":"object-storage","status":"ok","ms":6}]}` |
| `/health/ready` với **object storage chết** | `503` `{"status":"not-ready", … "object-storage","status":"failed"}` — chỉ đúng thành phần hỏng |
| `--healthcheck` trong container | exit **0** khi khoẻ, exit **1** khi storage chết |
| `id` trong container | `uid=1654(app)` — không phải root |

`infra/docker/compose.production.yaml` chạy được cả hai image tại chỗ — **không phải deployment
target, mà là một smoke test bạn chạy được**.

### ✅ I6.6 · Backup/restore — xong 28/08/2026

Ba script, mỗi cái một việc: [`backup.sh`](../../scripts/backup.sh) ·
[`restore.sh`](../../scripts/restore.sh) · [`restore-drill.sh`](../../scripts/restore-drill.sh).
Chi tiết đầy đủ và runbook: [`backup-and-restore.md`](backup-and-restore.md).

**`[QUYẾT ĐỊNH kỹ thuật]` — `mongodump --oplog`, không phải snapshot ổ đĩa.** Snapshot một MongoDB
đang chạy chỉ an toàn nếu filesystem chụp được nguyên tử **và** journal cùng volume; Docker named
volume không bảo đảm cả hai, nên snapshot chụp đúng lúc có ghi dở sẽ khôi phục ra database phải sửa
chữa — và việc sửa chữa đó phát hiện **trong lúc sự cố**. Cái giá nếu sai: dump logic tỉ lệ với lượng
dữ liệu; khi vượt ngưỡng thì thay bằng backup liên tục có quản lý, **không** phải snapshot.

**Không có chế độ không mã hoá, và đó là quyết định đắt nhất ở đây.** Script **từ chối chạy** khi khoá
không được đặt, không đọc được, rỗng, hoặc group/other đọc được — cả bốn đã kiểm chứng bằng cách chạy
thật. Một script tự động ghi bản rõ khi thiếu khoá sẽ làm đúng thế vào ngày có người đang vội, và một
bản backup chứa email cùng toàn bộ bài làm của mọi học viên. Dòng dữ liệu đi thẳng `mongodump | gpg`:
**bản rõ không bao giờ tồn tại trên đĩa**, kể cả khi tiến trình chết giữa chừng.

**Versioning bật cho ba bucket, cố tình tắt cho hai.** Nội dung do người soạn (`vni-exam-assets` ·
`vni-packages` · `vni-documents`) bị phá theo cách thực tế là operator upload đè — mà **mirror sao chép
trung thành cả thiệt hại đó**, chỉ versioning cứu được. Giọng nói học viên và artefact
(`vni-audio-90d` · `vni-artifacts-2y`) thì **không**: một lịch sử phiên bản là bản sao sống lâu hơn
chính lệnh xoá nó phải tôn trọng, và theo PDPL một bản ghi đã xoá phải thực sự biến mất. Đã kiểm chứng
từng bucket bằng `mc version info`.

#### Bài diễn tập — và vì sao nó phải huỷ dữ liệu thật

**Một bản backup chưa ai khôi phục thử là một giả thuyết.** Lỗi đáng sợ không phải "dump không chạy" —
cái đó ồn ào. Chúng là những lỗi im lặng: archive giải mã ra rỗng vì pipe nuốt lỗi, khoá trên máy
backup không phải khoá đã mã hoá, `--oplog` bị bỏ qua vì node không ở replica set. **Mọi trường hợp
trông y hệt một bản backup đang hoạt động** cho tới ngày cần.

Bài diễn tập ghi dữ liệu mang đúng những kiểu hay mất — `NumberDecimal` (band thành double là band
sai), `ISODate`, `BinData`, `null` (cách học viên **xoá** đáp án), dấu tiếng Việt — backup toàn
instance, kiểm tra archive từ chối khoá sai, **huỷ dữ liệu**, xác nhận đã huỷ, khôi phục, rồi so
**fingerprint EJSON từng document**. Đếm số lượng chứng minh gần như không gì: một bản khôi phục làm
rụng hết field trừ `_id` vẫn đếm đúng.

```
drill: archive vni-20260828T021413Z.archive.gz.gpg (145359 bytes)
drill: archive refuses the wrong key
drill: data destroyed
drill: restored 3 documents, byte-identical
drill: PASSED
```

**Đã kiểm chứng bài diễn tập bắt lỗi thật, không chỉ xanh:** đổi `--ns-include` sang namespace không
khớp → thoát mã 1 kèm diff đúng ba document đã mất. **Và nó chạy trong CI mỗi lần build**, không phải
theo lịch — `mongodump`/`mongorestore` lấy từ chính image database chạy, nên runner không cần cài gì.

#### RPO/RTO — đo được, nhưng mục tiêu vẫn của chủ sản phẩm

Đo 28/08/2026 trên instance 186 MB / 19 database: trọn vòng **6,5 giây**.

**RPO 24 giờ nghĩa là sự cố lúc 11 giờ làm mất trọn kỳ thi bắt đầu lúc 9 giờ.** Với sản phẩm thi cử đó
không phải mất một dòng dữ liệu — đó là mất một kết quả học viên đã bỏ hai tiếng ra làm và có thể không
làm lại được. Nếu không chấp nhận được thì lời giải là **oplog tailing liên tục**, một hạng mục riêng
chưa làm.

**RTO chưa diễn tập ở phần con người.** Bài diễn tập chứng minh cơ chế chạy; nó **không** chứng minh có
người biết khoá nằm đâu lúc 3 giờ sáng.

**Không cài sẵn cron nào** (`G-11`): tần suất backup quyết định RPO, RPO là quyết định kinh doanh, và
một con số đặt đại ở đây sẽ thành cam kết mà không ai chọn.

### ☐ I6.7 · Published exam immutability

Không overwrite published version; edit tạo version mới; historical sessions dùng đúng frozen version.

---

## Phase I7 · Contract và end-to-end gate

### ✅ I7.1 + I7.3 · OpenAPI và cổng drift — xong 28/08/2026

**Một test làm cả hai việc, có chủ đích.** Một trình sinh mà người ta phải *nhớ* chạy sẽ tạo ra một
spec sai trong vòng một tuần — và spec sai **tệ hơn không có**, vì client sinh từ nó trông đúng trong
khi mô tả một API đã đổi. Biến việc kiểm tra **chính là** trình sinh nghĩa là artifact không mục được:
cách duy nhất làm test xanh là commit đúng thứ ứng dụng đang phục vụ.

Khi lệch, test **ghi tài liệu mới vào working tree rồi mới báo đỏ** — nên câu trả lời cho *"giờ tôi
phải làm gì"* luôn là `git diff`, và cái diff đó chính là thứ cần đọc.

Đã thử đường build-time (`Microsoft.Extensions.ApiDescription.Server`) trước và **bỏ**: nó boot app
không có cấu hình nên vấp đúng cổng khởi động `I6.3` — một công cụ build không nên phải biết cách cấu
hình production.

`47 path · 49 operation · 36 cái khai báo cần token`, đều suy ra **từ metadata của chính endpoint**
(`IAuthorizeData`, `EnableRateLimitingAttribute`) chứ không viết tay 90 lần.

**Đã kiểm chứng cổng thật sự bắt:** thêm một route `/api/v1/drift-probe` → test đỏ đúng thông điệp.

### ✅ I7.2 · `packages/api-client` — xong 28/08/2026

**Trước đó web và admin *chép tay* hợp đồng.** `SessionResultsView` tồn tại hai bản — một C#, một
TypeScript — và **không gì buộc chúng khớp**. Đây không phải rủi ro lý thuyết: lỗi đắt nhất sản phẩm
từng có, `A17`, đúng là hai phía của một hợp đồng bất đồng **trong khi cả hai đều có test xanh**.

Chuỗi bảo vệ, mỗi mắt xích có người canh:

| Mắt xích | Canh bởi |
|---|---|
| API đang chạy ⇄ `v1.json` | `OpenApiContractTests`, trong job backend |
| `v1.json` ⇄ `@vni/api-client` | bước `generate` trong job frontend |
| `@vni/api-client` ⇄ kiểu viết tay | `contractParity.test.ts`, kiểm bởi **`pnpm typecheck`** |

**Hai lỗi hợp đồng thật đã lộ ra ngay khi làm việc này:**

**1 · Response không có kiểu gì cả.** Endpoint trả object ẩn danh nên `200: OK` rỗng — nửa quan trọng
hơn của hợp đồng. Đã khai `.Produces<SessionView>()` / `.Produces<SessionResultsView>()` cho các route
thi, và `SessionResultsView` + `MarkingStatusView` giờ có trong `components.schemas`.

**2 · `changes` khai là `string` trong khi `null` là cách học viên *xoá* một đáp án.** Trình sinh .NET
tôn trọng nullable reference type ở property thường và **không với tới giá trị bên trong dictionary**.
Một client sinh từ spec đó sẽ **từ chối gửi lệnh xoá**. Đã sửa bằng schema transformer.

> **Hai bài học về chính test parity, cả hai đều đã đo:**
>
> - `expectTypeOf` **bị xoá lúc chạy**, nên `vitest run` xanh bất kể kiểu nói gì. **`pnpm typecheck`
>   mới là cổng** — đã chứng minh bằng cách phá kiểu sinh ra: `tsc` đỏ trong khi runner vẫn xanh.
> - Dùng `toEqualTypeOf`, **không** dùng `toMatchTypeOf`. Cái lỏng **đồng ý với đúng lỗi nó sinh ra để
>   bắt**: `null` gán được vào `string` theo chiều nó kiểm, nên hợp đồng mất nullability vẫn qua.
### ✅ I7.4 · Full HTTP journey — xong 28/08/2026

**Chưa từng có bài test nào đi trọn bốn kỹ năng qua HTTP.** Bộ test kỹ lưỡng theo một hình dạng che
mất đúng điều này: mọi lời từ chối đều có test, mọi race đều có test, và thứ không ai kiểm là **con
đường học viên thực sự đi khi không có gì hỏng**. `ExamRunContractTests` advance **đúng một** bước,
reading → listening, rồi dừng.

Ba lớp lỗi chỉ hiện ra khi đầu ra của một lệnh thật trở thành đầu vào của lệnh kế tiếp:

| Lớp lỗi | Vì sao test từng endpoint mù |
|---|---|
| Trạng thái tích luỹ qua cả kỳ thi | Test một bước không bao giờ có hai section đã đóng cùng lúc, nên không thấy được một `results` chỉ báo cáo section đóng sau cùng |
| Deadline neo sai gốc | Ở bước một, "neo theo section" và "neo theo phiên" là **cùng một mốc thời gian** |
| Token hết hạn giữa kỳ thi | Access token sống vài phút, kỳ thi IELTS sống vài giờ — **mọi** kỳ thi thật đều refresh ít nhất một lần, và chưa test nào xoay token rồi dùng tiếp cùng một phiên |

Bài test đăng ký bằng **email + mật khẩu**, không dùng stub SSO — đường mà phần còn lại của bộ test
đi tắt, và là đường bỏ qua băm mật khẩu, trạng thái xác minh email và rate limiter đăng ký.

Đáp án được chọn để **bắt bộ chấm phải phân biệt**: Reading đúng cả 4 → band 9; Listening cố tình sai
`syn-l-4` → 4/5 → band 7.5. Một bộ chấm trả hằng số, hoặc đọc nhầm đáp án của section khác, **qua**
được một kỳ thi đúng hết và **chết** ở đây.

#### Cổng thứ hai: Kestrel thật, socket thật

`UploadRecordingEndpoint` mở trần body cho từng request, vì `Program.cs` đặt giới hạn toàn cục **1 MB**
— đúng cho JSON, và ngắn hơn mọi câu trả lời Speaking Part 2 thật.

**Dưới `TestServer`, `IHttpMaxRequestBodySizeFeature` là `null`.** Nên khối đó **chưa bao giờ chạy**
trong bất kỳ test nào, và cả tá assertion upload trong `ExamRunContractTests` chỉ đang kiểm
`file.Length` chứ không kiểm gì ở tầng transport.

**Đã đo, không suy đoán:** xoá ba dòng mở trần → **26/26 test của `ExamRunContractTests` vẫn xanh**,
trong khi 2/3 test Kestrel mới đỏ. `WebApplicationFactory.UseKestrel()` có sẵn từ .NET 9 nên cái giá
là một cổng loopback, không phải một image Docker — và nó chạy **đúng `Program`** mà container chạy.

#### Hai điều bộ test tự nó dạy lại

**`WebApplicationFactoryClientOptions` mới sẽ ghi đè địa chỉ Kestrel.** Truyền một object options
tự tạo là thay `BaseAddress` đã dò được bằng `http://localhost` mặc định — cổng 80, không ai nghe. Lỗi
hiện ra là *connection refused*, đọc như "server không khởi động" chứ không như "client trỏ sai chỗ".

**Đồng hồ test không phải đồng hồ của bộ xác thực JWT.** `JwtTokenService` đóng dấu `nbf`/`exp` từ
`IClock` — cái đồng hồ dịch được của bộ test; framework xác thực chúng theo đồng hồ hệ thống. Production
hai cái là một nên câu hỏi không tồn tại. Ở đây thì không: **một token được cấp trong lúc đồng hồ test
đi trước 20 phút sẽ bị từ chối là "chưa có hiệu lực"**, và cái 401 rơi vào lệnh **kế tiếp** chứ không
vào lệnh refresh. Quy tắc rút ra và đã ghi vào `RotateAsync`: *dịch đồng hồ thoải mái, nhưng không dịch
trong lúc đang cấp token.*

**Không xử lý bằng cách nới `ClockSkew` trong test services.** Làm vậy là biến bộ test này thành nơi
duy nhất chấp nhận một access token đã hết hạn, và lỗi vòng đời token thật đầu tiên sẽ rơi vào đúng
bài test đã được cấu hình để không nhìn thấy nó.

#### Phát hiện: hàng đợi chấm bài chưa bao giờ chạy → `H-13`

Nộp bài xong, `markingStatuses` **rỗng**. `MarkingWork.EnqueueAsync` dừng ở
`if (rubrics.For(module) is not { } rubric) return;`, và **không file `appsettings` nào có mục
`Assessment`**. Bốn trạng thái mà `I3.6` dựng lên để một dấu gạch ngang tự giải thích được chưa bao giờ
tới màn hình kết quả.

Cái seam thì đúng — `G-11` được tôn trọng, chính sách chưa chốt thành cấu hình rỗng chứ không thành
giá trị bịa. Thứ thiếu là **cấu hình**, và nó thiếu vì `H-8a` chưa có câu trả lời.

**Không tự chọn giá trị.** Đã chứng minh máy móc chạy đúng ngay khi có cấu hình bằng một test riêng
(`MarkingQueuedOnSubmitTests`): hai rubric giả → Writing và Speaking vào hàng đợi `pending`. Giữa sản
phẩm và một màn hình kết quả biết tự giải thích **chỉ còn bốn dòng cấu hình**. → `H-13`

#### Một lỗi nữa trong chính bài test, đáng ghi

Bản đầu chọn đề bằng *"đề đầu tiên có bốn module"*. Máy có đề mượn trong `fixtures/exams` sẽ seed
**hai** đề bốn module; một checkout sạch và CI seed **một**. Bài test đọc đề khác nhau tuỳ ổ đĩa của
ai — xanh ở CI, đỏ ở máy viết, và **không kết quả nào nói gì về mã nguồn**. Giờ chọn theo tên, và đề
mượn — vật liệu bên thứ ba chưa rõ giấy phép, cố tình không vào version control — **không bao giờ là
thứ một bài test đối chiếu**.

**Bằng chứng:** backend **519/519** (Domain 157 · Application 170 · Infrastructure 64 · Architecture 4
· Integration 124, +6).

### ✅ I7.5 · Bộ test trình duyệt thật — xong 28/08/2026

Package riêng [`e2e/`](../../e2e), Playwright, chạy **API thật + Vite thật + Chromium thật** trên cổng
riêng (5199/5273) và database riêng (`vni_ielts_e2e`) — không đụng vào server hay dữ liệu của người
đang phát triển. **14 test, hai profile** (desktop và Pixel 7), tất cả xanh.

**Không retry, và đó là quyết định chứ không phải thiếu sót.** Bộ này tồn tại để tìm race condition.
Một lần retry biến *"hỏng một trên ba lần"* — đúng hình dạng của một race — thành build xanh.

#### Ba phát hiện, cả ba đều là lỗi thật

**1 · Đáp án gõ lúc mất mạng, sau khi reload thì nằm im mãi mãi — không bao giờ được gửi.**

`patchJournal` khôi phục đúng: đáp án hiện lại trên màn hình sau reload. Nhưng effect khôi phục đặt
trạng thái `pending` rồi **dừng**, và không có gì khác lên lịch flush. Đo được: `PUTS AFTER RELOAD = []`.
Đáp án ngồi ở *"đang chờ lưu"* cho tới khi học viên tình cờ gõ thêm câu khác — mà **học viên reload
chính là học viên vừa mất mạng**, và cũng là người có khả năng đóng tab ngay sau đó nhất. Không có gì
trên màn hình từng thôi nói *"đang chờ"*.

Lý lẽ trong comment gốc đúng (đừng gửi ngay lúc load: sẽ đua với seeding, và với section đã đóng thì
phí những giây đầu vào một request chỉ có thể bị từ chối) — chỉ là **chưa đi hết**. Đã sửa bằng cách
lên lịch đúng cái debounce 1,2 giây mà một phím gõ dùng: lúc nó chạy thì section đã ổn định, và cổng
flush là thứ vốn đã biết section đóng nghĩa là gì. **Bằng chứng đỏ→xanh có sẵn tự nhiên**: test viết
trước, đỏ, sửa, xanh.

**2 · Mọi số nguyên trong hợp đồng API đều khai là "có thể là chuỗi".**

Trình sinh OpenAPI của .NET 10 viết một integer thành `"type": ["integer", "string"]` kèm `pattern`.
API này **không** như vậy: không có `JsonNumberHandling` nào được cấu hình, nên `System.Text.Json`
không đọc cũng không ghi số dưới dạng chuỗi. Hợp đồng đã **sai về chính đầu ra của nó**.

Thiệt hại rơi vào client sinh ra: `attempts: number | string`, `remainingSeconds: number | string`,
`overallBand: null | number | string`. Một màn hình trừ một khỏi đồng hồ đếm ngược **hoặc không compile
được, hoặc — nếu ai đó với tay sang `+` — nối chuỗi**. Đúng loại lỗi mà `packages/api-client` sinh ra
để chặn, chỉ là đi vào qua trình sinh thay vì qua bản chép tay.

Đã sửa bằng schema transformer, cùng cơ chế với bản vá `changes`. **Cổng drift bắt đúng ngay lập tức**:
test đỏ, ghi tài liệu mới vào working tree, rồi xanh lại sau khi commit đúng thứ API đang phục vụ.

**3 · Và điều đáng nói nhất về phát hiện 2: nó lộ ra vì `pnpm typecheck` chưa từng chạy với một client
đã sinh.**

`contractParity.test.ts` là **thứ duy nhất** kiểm mắt xích cuối, và nó chỉ được kiểm bởi `tsc`, không
phải bởi runner. Thư mục `packages/api-client/src/generated/` không nằm trong version control, nên
trên máy này nó **trống** — và một cổng đọc file không tồn tại không phải một cổng. Bài học đúng cái
đã ghi ở `I7.2` nhưng lần này là về *thời điểm*: chuỗi chỉ được canh khi có người sinh client **rồi
mới** typecheck, và CI đã làm đúng thứ tự đó từ đầu.

#### Một điều được xác nhận là **đúng**, và giờ có test giữ

**Đồng hồ thi không trôi khi tab bị đóng băng.** Đã đo bằng `Page.setWebLifecycleState: frozen` 12
giây: lệch **1 giây** so với server. Vì mỗi tick tính lại từ `deadlineAt` chứ không trừ dần một biến
đếm. Một bộ đếm giảm dần là cách hiển nhiên để vẽ đồng hồ thi, và nó **sai theo cách không máy nào của
lập trình viên cho thấy**: trình duyệt bóp hoặc dừng timer ở tab nền, nên con số quay lại **chậm hơn**
thời gian thật — học viên thấy số phút mình không có, viết tiếp, rồi bị server đóng section dưới chân.
Test này tồn tại để giữ nguyên hiện trạng đó.

#### Một sai lầm trong chính bộ test, và cách nó lộ ra

Bản đầu của test hai tab **ép hết hạn token bằng cách ghi đè `localStorage`** — và **không có gì xảy
ra**: app đang chạy giữ session trong bộ nhớ, nên nó tiếp tục dùng token cũ còn hợp lệ.
`REFRESH RESPONSES = []`. Một bài test xanh mà **chứng minh con số không**.

Sửa bằng cách trả 401 thật cho một lần autosave — đúng thứ mà access token hết hạn trông như thế từ
phía client. Và khi đó lộ ra một chuyện đáng giá hơn: **có hai lớp phòng thủ, không phải một.**

Bỏ hẳn phần *adopt-trong-lock* của coordinator → **test vẫn xanh**, vì cả hai tab đều refresh thật, tab
thứ hai trình một token đã xoay, và **server** nhận ra đó là ca "mất response" qua `successorTokenHash`
rồi trả successor thay vì thu hồi cả họ token. Trình duyệt chặn bản sao; server sống sót qua nó.

Nên assertion có ý nghĩa là **đếm số lần xoay**: *"không ai bị đăng xuất"* đúng trong cả hai trường
hợp; *"một lần xoay, không phải hai"* chỉ đúng khi coordinator làm việc. **Đã kiểm chứng đỏ** khi tiêm
lỗi vào coordinator.

**CI:** [`e2e.yml`](../../.github/workflows/e2e.yml) chạy profile desktop mỗi lần build.
**Chưa từng được quan sát chạy trên runner của GitHub** — lần chạy đầu tiên sẽ là bằng chứng, và nếu
đỏ thì đỏ ngay chứ không âm thầm.
### ✅ I7.6 · Báo cáo tổng kết hạ tầng — xong 28/08/2026

→ [`infrastructure-completion-report.md`](infrastructure-completion-report.md).

Nội dung: bằng chứng từng cổng · 13 đường mất dữ liệu đã đóng · 7 lỗ không boot được production · 3
phát hiện của hai item cuối · 2 điều được xác nhận là đúng · rủi ro còn lại · ba thứ chặn production
(**không thứ nào là code**) · còn phải xây và làm chúng để được gì · quyết định đang chờ chủ sản phẩm
· đề xuất thứ tự tiếp theo.

---

## Phase UI0…UI11

**Chưa mở.** Chỉ bắt đầu phase UI logic-critical sau khi **I1–I5 pass DoD**. Danh sách đầy đủ UI0.1…
UI11.6 nằm trong chỉ đạo 27/08/2026 của chủ sản phẩm và sẽ được chép vào đây khi I5 đóng — chép sớm
hơn chỉ tạo ra một checklist dài mà không ai được phép động vào.

Tóm tắt thứ tự: `UI0` design system → `UI1` shell/primitives → `UI2` trang chủ → `UI3` practice entry
→ `UI4` timed runner → `UI5` open practice → `UI6` results → `UI7` dashboard/progress → `UI8`
auth/profile → `UI9` dictation → `UI10` documents/articles → `UI11` agent tester nghiệm thu.

---

## Ma trận tính năng — theo báo cáo hiện trạng 27/08/2026

| Trạng thái | Hạng mục |
|---|---|
| **Đã có hoặc gần hoàn chỉnh** | Đăng ký/đăng nhập email · Google SSO flow · profile email/phone · tạo/đổi mật khẩu · device/session list và revoke · practice catalogue · Single Skill timed · Full Test timed chaining · Reading runner + deterministic scoring · Listening runner + authenticated audio + deterministic scoring · Writing input/word count/autosave · Speaking web recording/upload · dashboard basic history · raw results và per-question verdict · dictation engine và exercise UI |
| **Partial** | Email verification/reset (thiếu production mail) · Google SSO (production config chưa xong) · dashboard/history (thiếu full history và error state đúng) · progress (mock/empty) · Writing (thiếu evaluator và worker) · Speaking (thiếu ASR/evaluator/native recorder) · Full Test (chưa có overall result vì W/S chưa chấm) · dictation (thiếu content durable, attempt history, CMS) · results (thiếu correct answer/explanation, marking status) · mobile (mới responsive web) |
| **Mock hoặc thiếu** | Articles API/content/CMS · Documents API/storage/download/CMS · AI Chat backend · notification system · learning target/current band/exam date · entry/diagnostic test · token/entitlement charging · native mobile recorder · AI provider adapters · production object storage · worker job processing · OpenAPI generated client |
| **Chờ chủ sản phẩm quyết định** | AI cross-border/PDPL (`B-2`) · token pricing/entitlement (`B-5a`/`B-5b`) · Full Test/Single Skill vs Luyện đề/Thi thử taxonomy · entry test content · raw-to-band source · multiple-select partial credit · Writing rubric source và Task 1/Task 2 weighting (`H-8b`) · Speaking session/part model (`H-1`) · Speaking re-record/interruption · audio retention · practice history semantics · answer explanation source · public vs authenticated Articles/Documents · data residency/object storage provider |
