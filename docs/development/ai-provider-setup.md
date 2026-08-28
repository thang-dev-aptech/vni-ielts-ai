# Nạp base URL và API key của AI

> Tài liệu này viết bằng tiếng Việt vì đây là **quy trình thao tác cho người vận hành**, không phải tài liệu kiến trúc.

> ## Đừng dán khóa vào khung chat, và đừng tạo file trong repo
>
> `.gitignore` chặn `.env*`, một PreToolUse hook chặn ghi vào đó, và CI quét chuỗi giống credential rồi fail build. **Khóa đã dán vào chat coi như đã lộ và phải xoay lại** — không có cách nào rút lại. → `CLAUDE.md` rule 6
>
> Chỗ đúng để cất khóa đã có sẵn, và nó **là một file** — chỉ nằm ngoài thư mục dự án. Xem mục 2.

---

## 1 · Bốn giá trị, và chỉ khóa mới là bí mật

| Khóa cấu hình | Là gì | Bí mật? |
|---|---|---|
| `Ai:OpenAi:BaseUrl` | Gốc API. **Bỏ trống** nếu gọi thẳng OpenAI | Không |
| `Ai:OpenAi:ApiKey` | Khóa | **Có** |
| `Ai:OpenAi:Model` | Gọi model nào. **Không có mặc định** | Không |
| `Ai:Gemini:BaseUrl` · `ApiKey` · `Model` | Như trên | Chỉ `ApiKey` |

`Model` không có giá trị mặc định là cố ý. Một mặc định ở đây nghĩa là bài viết của học viên được gửi
tới model mà người viết file này nghĩ ra, chứ không phải model ai đó chọn. → `G-11`

Chỉ cần đặt provider nào đang dùng. Không đặt gì cũng chạy được — Reading và Listening chấm theo đáp
án và **không đi qua mô hình nào** (`A-11`), nên bản cài không có khóa vẫn là bản cài hoạt động.

**Claude API cố ý không có mặt.** Chủ sản phẩm chốt 20/08/2026: GPT và Gemini, loại Claude. Thêm nhà
cung cấp thứ ba là một quyết định, không phải một giá trị cấu hình — nên nó là thêm một thuộc tính
trong `AiOptions`, không phải truyền một cái tên vào.

### `BaseUrl` không phải nhà cung cấp thì đó là **một bên xử lý dữ liệu thứ hai**

Đây là điều quan trọng nhất trong tài liệu này.

Gọi qua reseller nghĩa là toàn bộ nội dung request — bài viết của học viên, giọng nói của học viên —
đi qua một công ty mà VNI **chưa ký hợp đồng nào**. Cộng thêm: cả OpenAI lẫn Google đều đặt ở Mỹ, nên
đây chắc chắn là **chuyển dữ liệu cá nhân qua biên giới**, thuộc phạm vi PDPL và cần hồ sơ CTIA
(`B-2`, chưa có kết luận).

Vì vậy mỗi provider có một công tắc, và **mặc định của nó là hạn chế**:

```
Ai:OpenAi:SyntheticDataOnly = true   ← mặc định, không cần đặt
```

Để `true` thì endpoint đó chỉ được nhận **dữ liệu bịa**. Muốn cho phép dữ liệu thật thì phải **gõ tay
`false`** — và chính việc phải gõ là bản ghi cho quyết định đó. Không có cảnh báo nào ở đây: nơi gọi
được kỳ vọng **từ chối**, vì một dòng cảnh báo trong log của tác vụ nền là một dòng không ai đọc.

---

## 1b · Đang dùng tạm `api.vietapi.tech` — điền đúng ba dòng này

Đã dò danh mục ngày **27/08/2026**: 30 model, tất cả nói được giao thức OpenAI.

```json
"Ai:OpenAi:BaseUrl": "https://api.vietapi.tech/v1",
"Ai:OpenAi:ApiKey":  "<khóa của bạn>",
"Ai:OpenAi:Model":   "gpt-5.5"
```

**Routing theo workload (đề xuất 27/08/2026)** — *không phải* tối ưu token: xem mục ngay dưới, chi phí thật trên đường này không quan sát được — chi tiết trong `exam/Exam1/ASSESSMENT.md` § Model routing:

| Việc | Model |
|---|---|
| Band Reading / Listening | **Không gọi model** — đáp án (`A-11`) |
| Giải thích câu sai R/L (tuỳ chọn) | Cùng tier GPT. **Không** chọn tên khác họ để tiết kiệm — xem mục dưới: mọi tên đo được đều ra cùng một backend, nên "tier rẻ" ở đây không đo được |
| Writing + Speaking | Tier GPT. Speaking đắt vì ASR + rubric, không vì tên model |

Hiện chỉ có một khóa `Ai:OpenAi:Model` — đặt `gpt-5.5`. Per-workload model là việc sau khi có port evaluator.

**Gemini để trống.** Nhà cung cấp này **không có model Gemini nào** — 0 trên 30. Quyết định 20/08 là
GPT + Gemini, nên đường này chỉ phục vụ được một nửa; nửa còn lại chờ khóa Google chính thống.

Ba điều đã kiểm chứng và đáng biết trước khi tin vào endpoint này:

| | |
|---|---|
| **`GET /v1/models` trả 200 khi không có khóa** | Danh mục của họ công khai. Không phải vấn đề của mình, nhưng đáng biết |
| **15/30 model là Claude** | `claude-opus-5`, `cc-max-claude-*`, `bedrock-claude-*`. Bị loại theo quyết định 20/08 — và giờ được **chặn ở lúc khởi động**, xem mục 5 |
| **`owned_by: custom` cho 23/30**, gồm **toàn bộ 6 model GPT** | Chính nhà cung cấp khai đây không phải định tuyến thẳng. Tên như `gpt-5.6-luna`, `ox-alpha`, `hy3` không khớp quy ước đặt tên của nhà cung cấp gốc |

### Đã gọi thật, 27/08/2026 — bằng chứng nghiêng về "mọi tên model đều ra Claude"

Gọi `chat/completions` thật với 5 tên khác họ: `gpt-5.5`, `gpt-5.5-high`, `gpt-5.6-luna`,
`deepseek-v4-flash`, `qwen-3.8-max`. Cả 5 đều trả 200 và nội dung đúng yêu cầu.

**Nhưng cả 5, không trừ cái nào, đều trả về `claude_cache_creation_5_m_tokens` và
`claude_cache_creation_1_h_tokens` trong `usage`.** Hai trường đó là của riêng Anthropic — không
thuộc chuẩn OpenAI, DeepSeek hay Qwen. Cộng thêm `prompt_tokens` cho một câu 6 chữ dao động
**2040–2700**, gợi ý có system prompt ẩn khá lớn bị chèn ở mọi request.

Đọc cùng nhau: nhiều khả năng **toàn bộ endpoint đi qua cùng một backend Claude**, bất kể tên model
gõ vào là gì — và `claude-fable-5` có mặt sẵn trong danh mục (mục 1) không phải trùng hợp.

**Vì sao hàng rào ở mục 5 (`AiProviderPolicy`) không đủ chặn trường hợp này:** nó so khớp chuỗi
`"claude"` trong tên model được yêu cầu. Nếu backend thật sự là Claude bất kể tên gõ vào, thì
`Ai:OpenAi:Model = "gpt-5.5"` **vượt qua** hàng rào trong khi vẫn phạm đúng quyết định 20/08 mà
hàng rào dựng ra để giữ. Hàng rào bảo vệ đúng tên gọi, không bảo vệ được backend thật.

`[QUYẾT ĐỊNH]` chủ sản phẩm 27/08/2026: **vẫn dùng đường này để test luồng UI/API**, chưa cần xác
định backend thật. Vẫn giữ `SyntheticDataOnly: true` — phát hiện này không đổi câu hỏi B-2, chỉ đổi
mức tin cậy vào cái tên `Ai:OpenAi:Model` đang khai. Trước khi cho dữ liệu thật đi qua, hoặc trước
khi báo band từ đường này cho học viên, cần hỏi thẳng bên bán backend thật là gì — nếu không, một
band "GPT" gắn nhãn cho việc hiệu chỉnh (`M-28`) có thể là band của một model khác hoàn toàn.

Điều cuối là điều nghiêm trọng nhất, và không phải chuyện tên gọi. Khi một band do AI chấm đến tay học
viên, câu *"model nào tạo ra điểm này"* phải trả lời được — để hiệu chỉnh (`M-28`), để tái lập kết
quả, và để có vết kiểm toán. Một nhãn của reseller không ánh xạ về model id của nhà cung cấp thì câu
đó **không có câu trả lời**. Đủ dùng để dựng và thử; **chưa đủ để dữ liệu thật đi qua**.

---

## 1c · Rubric — bốn tiêu chí đã chốt, **nguồn mô tả band thì chưa**

Chấm Writing và Speaking cần một `Rubric`, và rubric **không có mặc định**:

```json
"Assessment:Writing:Version":          "ielts-writing-2023.1",
"Assessment:Writing:DescriptorSource": "IELTS public band descriptors, May 2023",
"Assessment:Speaking:Version":          "ielts-speaking-2023.1",
"Assessment:Speaking:DescriptorSource": "IELTS public band descriptors, May 2023"
```

| Trường | Ai quyết | Vì sao không có mặc định |
|---|---|---|
| **Bộ tiêu chí** | Đã chốt — *không* phải cấu hình | Chủ sản phẩm 21/08/2026: chấm đúng cách IELTS chấm, bốn tiêu chí (`A-13b`). Key lấy thẳng từ `CriterionKeys` trong mã |
| `Version` | Người vận hành | Đóng dấu lên **mọi** bài đã chấm. Đổi mô tả band mà không đổi version thì hai bài chấm dưới hai bộ luật khác nhau trở nên không phân biệt được — và bộ hiệu chuẩn (`H-8c`) đang đo một cái đích di động |
| `DescriptorSource` | **Cần trả lời `H-8a`** | Mô tả band chính thức thuộc bản quyền chung British Council · IDP · Cambridge, **không nêu điều khoản nào cho bên thứ ba tái sử dụng**. Nhúng nguyên văn vào sản phẩm thương mại là câu hỏi pháp lý, và câu trả lời có thể khác nhau theo nơi và theo thời điểm |

**Không đặt gì thì Writing và Speaking báo `AwaitingRubric`, không phải điểm 0.** Chấm mà không có
rubric sẽ tạo ra một band không ai tái lập hay bảo vệ được — và để có nó thì đã phải gửi bài của
học viên đi rồi. → `G-11`

### Bốn kỹ năng, và bốn lý do khác nhau khi chưa có điểm

Đường ống chấm đã nối trọn cho cả bốn. Cái thiếu là nhà cung cấp, và mỗi kỹ năng thiếu một kiểu:

| Trạng thái | Nghĩa là gì | Sửa bằng cách nào |
|---|---|---|
| `Marked` | Có band, đã tính lại từ điểm từng tiêu chí | — |
| `NothingSubmitted` | Học viên bỏ trống task đó | — (không phải lỗi) |
| `AwaitingRubric` | Chưa cấu hình rubric | Mục này, và trả lời `H-8a` |
| `AwaitingEvaluator` | Chưa có adapter gọi nhà cung cấp | Chờ `B-2` — vị thế PDPL về chuyển dữ liệu qua biên giới |
| `AwaitingTranscript` | **Chỉ Speaking.** Có ghi âm, chưa có chữ | Chọn nhà cung cấp speech-to-text có **word-level timings** |
| `Rejected` | Model đã trả lời và câu trả lời **bị từ chối** — sai bộ tiêu chí, band lệch lưới nửa bậc, hoặc tiêu chí không trích được dẫn chứng | Sửa prompt hoặc đổi nhà cung cấp. Đây là tín hiệu về hệ thống, **không** phải về học viên |

Reading và Listening không xuất hiện trong bảng này: band của chúng đến từ đáp án và **không có
nhánh nào** trong bộ chấm để chúng chạm tới một model. → `A-11`

---

## 2 · Cất ở đâu: `user-secrets` — vẫn là file, chỉ khác chỗ đặt

.NET có sẵn kho bí mật cho môi trường phát triển, nằm **ngoài thư mục dự án** và chỉ nạp khi
environment là `Development`. Dự án đã bật sẵn (cùng `UserSecretsId` mà SSO đang dùng).

### Cách A — mở file ra dán, không cần gõ khóa vào terminal

```bash
cd "backend/src/Vni.Ielts.Api"
dotnet user-secrets init                      # đã init sẵn, chạy lại vô hại
open ~/.microsoft/usersecrets/7ef8200d-2eb5-4a26-974f-b9ab754ba109/secrets.json
```

File đó là JSON phẳng. Thêm vào (giữ nguyên các dòng SSO nếu đã có):

```json
{
  "Ai:OpenAi:BaseUrl": "https://api.vietapi.tech/v1",
  "Ai:OpenAi:ApiKey": "",
  "Ai:OpenAi:Model": "gpt-5.5"
}
```

Khi có khóa chính thống: **xóa dòng `BaseUrl`** (để trống = gọi thẳng nhà cung cấp), thay `ApiKey`,
và đặt lại `Model` theo tên OpenAI công bố. Ba dòng, một lần sửa.

Có sẵn bản mẫu để chép: [`secrets.json.example`](../../backend/src/Vni.Ielts.Api/secrets.json.example).

> **Vì sao cách này an toàn hơn gõ lệnh:** khóa không đi qua lịch sử shell. `dotnet user-secrets set`
> ghi nguyên văn khóa vào `~/.zsh_history`.

### Cách B — dòng lệnh, nếu không ngại lịch sử shell

```bash
cd "backend/src/Vni.Ielts.Api"
dotnet user-secrets set "Ai:OpenAi:BaseUrl" "<base url>"
dotnet user-secrets set "Ai:OpenAi:ApiKey"  "<api key>"
```

Xem lại đã đặt gì: `dotnet user-secrets list`.

### Ở máy chủ thật: biến môi trường

Dấu `:` thành `__` (hai gạch dưới):

```
Ai__OpenAi__BaseUrl
Ai__OpenAi__ApiKey
Ai__OpenAi__SyntheticDataOnly
```

Không có file nào cả — deployment cấp thẳng. Cùng cách `Jwt__SigningKey` và
`Sso__Google__ClientSecret` đang dùng.

---

## 3 · Kiểm tra đã nạp được chưa

```bash
cd "backend/src/Vni.Ielts.Api" && dotnet user-secrets list
```

Ra dòng `Ai:OpenAi:ApiKey = ...` là xong. **Đừng dán kết quả lệnh này vào chat** — nó in nguyên văn khóa.

---

## 4 · Mất khóa thì sao

| Lớp | Là gì |
|---|---|
| **1 · Trình quản lý mật khẩu** | Nơi ở chính thức. Cất ngay lúc nhà cung cấp hiện khóa lần đầu |
| **2 · `secrets.json` ở trên** | Bản đang chạy. Quyền `-rw-------`, ngoài repo nên không thể lỡ tay `git add` |
| **3 · Xoay khóa** | Mất cả hai lớp trên vẫn không chết: vào cổng của nhà cung cấp, thu hồi khóa cũ và tạo khóa mới |

Với OpenAI, khóa **chỉ hiện một lần** lúc tạo, giống hệt client secret của Google. Đóng cửa sổ rồi thì
không lấy lại được — chỉ còn cách tạo khóa mới.

---

## 5 · Claude bị chặn ở lúc khởi động, không phải bằng lời nhắc

Đặt bất kỳ `Model` nào chứa `claude` thì **API không khởi động được**, kèm câu nêu rõ lý do.

Nghe cứng, và nó cứng có chủ đích. Quyết định loại Claude dễ tuân thủ khi nó có nghĩa là "đừng đăng
ký". Nó hết dễ đúng lúc xuất hiện một reseller bán 15 model Claude cạnh 6 model GPT **trên cùng một
endpoint tương thích OpenAI** — lúc đó quyết định chỉ cách một lỗi gõ, trong một mục tên là `OpenAi`,
và không có gì báo.

Muốn đảo quyết định thì sửa `AiProviderPolicy.ExcludedModelMarkers`. Đó là thay đổi code, hiện ra
trong review — đúng trọng lượng cho một quyết định của chủ sản phẩm.

---

## 6 · Có khóa rồi thì làm được gì ngay?

**Chưa gì cả, và đó không phải lỗi cấu hình.** Hiện tại chưa có adapter nào, và **cũng chưa có port
nào** — grep toàn bộ Domain và Application không có `IWritingEvaluator`, `ISpeechRecognizer` hay
`IChat`. Cái đã có là `Domain/Assessment/` (`Rubric`, `CriterionMarking`), tức chỗ **nhận** kết quả
chấm, chứ chưa có đường đi tới mô hình.

Việc đầu tiên sau khi có khóa không phải viết adapter mà là **khai port ở Application theo ADR-0005** —
Domain và Application chỉ được nhìn thấy port, SDK của nhà cung cấp chỉ tồn tại trong Infrastructure.

Và trước khi bất kỳ tính năng AI nào lên production, `B-2` vẫn phải có kết luận.

→ [`../ai/provider-comparison.md`](../ai/provider-comparison.md) · [`../decisions/0005-ai-provider-abstraction.md`](../decisions/0005-ai-provider-abstraction.md) · [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)
