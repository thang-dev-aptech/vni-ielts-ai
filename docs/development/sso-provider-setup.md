# Đăng ký ứng dụng SSO và nạp khóa

> Tài liệu này viết bằng tiếng Việt vì đây là **quy trình thao tác cho người vận hành**, không phải tài liệu kiến trúc.

**Bạn cần làm phần này khi nào?** Khi muốn đăng nhập Google **thật**. Toàn bộ luồng đã chạy được ở môi trường Development bằng provider giả (`Sso:EnableStubProvider`), nên frontend không phải chờ.

> **Không dán khóa vào bất kỳ file nào trong repo, và không dán vào khung chat.** `.gitignore` chặn `.env*`, một PreToolUse hook chặn ghi vào đó, và CI quét chuỗi giống credential rồi fail build. Khóa dán vào chat coi như đã lộ và phải xoay lại. → `CLAUDE.md` rule 6

---

## 1 · Tạo OAuth client trên Google

Google Cloud Console → [console.cloud.google.com](https://console.cloud.google.com/).

1. **Tạo project** (hoặc chọn project sẵn có của VNI).
2. **APIs & Services → OAuth consent screen**
   - User type: **External**
   - App name, support email, logo — đây là thứ người học nhìn thấy trên màn hình đồng ý, nên dùng đúng tên và logo VNI
   - Scopes: chỉ **`openid`**, **`.../auth/userinfo.email`**, **`.../auth/userinfo.profile`**. Không thêm gì nữa — mỗi scope thừa là một dòng người học phải đọc và một mảng dữ liệu cá nhân đi qua biên giới (`B-2`)
   - Authorised domains: tên miền thật của sản phẩm
   - Trong lúc còn ở chế độ **Testing**, chỉ những địa chỉ trong danh sách *Test users* đăng nhập được. Đủ dùng để kiểm thử; muốn mở cho người dùng thật thì bấm **Publish app**
3. **APIs & Services → Credentials → Create credentials → OAuth client ID**
   - Application type: **Web application**
   - Authorised redirect URIs: xem bảng mục 2 bên dưới
4. Google trả về **Client ID** và **Client secret**.

> **Client secret chỉ hiện đúng một lần, ngay lúc tạo.** Nguyên văn Google:
> *"Your application's client secret will only be shown after you create the client. You won't be
> able to view or download the client secret again."* Sau đó Console chỉ hiện **4 ký tự cuối** để
> bạn nhận diện.
>
> **Lỡ đóng cửa sổ rồi thì không lấy lại được.** Không phải tìm sai chỗ — nó thật sự không còn ở đâu
> cả. Cách duy nhất là **xoay khóa**: vào đúng client đó, thêm một secret mới (*rotate / add secret*),
> Google hiện cái mới một lần nữa. Secret cũ vẫn sống một thời gian nên không gãy gì đang chạy.
> → [Google Cloud support](https://support.google.com/cloud/answer/6158849)

5. Trong lúc app còn ở trạng thái **Testing**, thêm chính địa chỉ Gmail của bạn vào *Test users*.
   Không có bước này thì Google từ chối đăng nhập, và thông báo lỗi không nói rõ vì sao.

Tài liệu gốc: [OAuth 2.0 for Web Server Applications](https://developers.google.com/identity/protocols/oauth2/web-server) · [OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect).

---

## 2 · Redirect URI phải khai đúng từng ký tự

Google so **chuỗi**, không so ý nghĩa. Thừa một dấu `/` là hỏng, `http` với `https` là hai giá trị khác nhau.

| Môi trường | Redirect URI khai với Google |
|---|---|
| Local | `http://localhost:5099/api/v1/auth/sso/google/callback` |
| Staging | `https://<host-api-staging>/api/v1/auth/sso/google/callback` |
| Production | `https://<host-api>/api/v1/auth/sso/google/callback` |

**Đây là địa chỉ của API, không phải của web.** Provider trả trình duyệt về backend; backend mới chuyển tiếp về web kèm mã trao tay. → [ADR-0014](../decisions/0014-backend-mediated-oidc-handoff-code.md)

> **Không cần domain và không cần deploy để chạy thử.** Google miễn trừ `localhost` khỏi yêu cầu
> HTTPS, đúng để phục vụ việc này: *"Redirect URIs must use the HTTPS scheme, not plain HTTP.
> **Localhost URIs (including localhost IP address URIs) are exempt from this rule.**"*
> → [OAuth 2.0 for Web Server Applications](https://developers.google.com/identity/protocols/oauth2/web-server)
>
> Chỉ cần khai dòng `localhost` ở bảng trên là đăng nhập Google thật chạy được ngay trên máy bạn.
> Domain chỉ cần khi lên staging/production.

Khai cả ba, hoặc tạo ba OAuth client riêng cho ba môi trường. Cách sau sạch hơn: lộ khóa staging không ảnh hưởng production.

Khi làm app di động (Capacitor), sẽ cần thêm một redirect URI dạng custom scheme. Chưa cần bây giờ.

---

## 3 · Nạp vào cấu hình

Bốn biến môi trường. Hai dấu gạch dưới `__` là cách .NET biểu diễn cấp lồng nhau trong tên biến.

```
Sso__Google__ClientId=<client id>
Sso__Google__ClientSecret=<client secret>
Sso__Google__RedirectUri=https://<host-api>/api/v1/auth/sso/google/callback
Sso__ClientBaseUrl=https://<host-web>
```

Kèm theo, tắt provider giả — nó là đường đăng nhập không cần xác thực:

```
Sso__EnableStubProvider=false
```

API **từ chối khởi động** nếu `EnableStubProvider` bật ngoài Development. Đó là chủ ý: một bypass xác thực chạy êm ru còn tệ hơn một lần deploy fail.

Nạp ở đâu tùy nơi chạy — biến môi trường của container, secret của orchestrator, hoặc file `.env`
**không** nằm trong repo. Không có file mẫu nào trong repo để tránh ai đó điền thật vào rồi commit.

### Ở máy cá nhân: dùng `user-secrets`, đừng tạo file

.NET có sẵn kho bí mật cho môi trường phát triển, nằm **ngoài thư mục dự án** (`~/.microsoft/usersecrets/`)
và chỉ được nạp khi environment là `Development`. Dự án đã bật sẵn, chạy ba lệnh là xong:

```bash
cd backend/src/Vni.Ielts.Api
dotnet user-secrets set "Sso:Google:ClientId"     "<client id>"
dotnet user-secrets set "Sso:Google:ClientSecret" "<client secret>"
```

Không tạo file nào trong repo, không có gì để lỡ tay commit, và không phải nhớ export biến môi trường
mỗi lần mở terminal mới. Xem lại đã đặt gì: `dotnet user-secrets list`.

> **`RedirectUri` thì không cần đặt** — `appsettings.Development.json` đã ghi sẵn
> `http://localhost:5099/api/v1/auth/sso/google/callback`. Nó không phải bí mật.

#### "Nhỡ quên khóa thì sao?" — nó vẫn là một file, mở ra xem được

`user-secrets` **không** giấu giá trị đi. Nó là một file JSON bình thường:

```
~/.microsoft/usersecrets/7ef8200d-2eb5-4a26-974f-b9ab754ba109/secrets.json
```

Mở bằng editor được, hoặc đọc bằng `dotnet user-secrets list`. Khác biệt duy nhất so với file `.env`
trong repo là **chỗ đặt**: ngoài thư mục dự án, quyền `-rw-------`, nên không thể lỡ tay `git add`.

Ba lớp phòng quên, xếp theo thứ tự nên tin cậy:

| Lớp | Là gì |
|---|---|
| **1 · Trình quản lý mật khẩu** | Nơi ở chính thức của khóa. Lúc tạo client, Google cho tải file `client_secret_*.json` — cất bản đó vào đây ngay |
| **2 · `secrets.json` ở trên** | Bản đang dùng để chạy. Sao lưu cùng chỗ với lớp 1 nếu muốn |
| **3 · Xoay khóa** | Mất cả hai lớp trên vẫn **không chết**: vào Google Console, thêm secret mới cho đúng client đó. Client ID giữ nguyên, chỉ secret đổi |

Điều đáng nhớ nhất: **mất client secret là chuyện khôi phục được**, không phải mất vĩnh viễn. Cái thật
sự không nên xảy ra là secret **lọt vào git** — chuyện đó thì không rút lại được, vì lịch sử git còn
mãi và phải xoay khóa cộng với viết lại lịch sử.

### Provider giả tự nhường chỗ

`Sso:EnableStubProvider` vẫn để `true` trong `appsettings.Development.json`, và **không cần tắt**:
khi `Sso:Google` có đủ client id lẫn secret thì khóa thật thắng, provider giả tự lui. Lúc khởi động
API in ra một dòng nói rõ đang dùng cái nào — nếu vẫn thấy cảnh báo *"Google sign-in is faked"* thì
nghĩa là secret chưa vào tới nơi, không phải Google từ chối.

### Kiểm tra đã đúng chưa

```bash
curl -s http://localhost:5099/api/v1/auth/sso/providers
```

Trả về `{"providers":[{"key":"google",...}]}` là đã nhận cấu hình. Danh sách rỗng nghĩa là thiếu một trong ba giá trị của Google — API cố tình giấu provider cấu hình dở thay vì hiện ra rồi lỗi khi bấm.

---

## 4 · Facebook và Microsoft — hoãn

**`[QUYẾT ĐỊNH]` Chủ sản phẩm, 21/08/2026:** *"trước mắt chỉ làm cho google thôi mấy phần khác bỏ
hoàn thiện mượt app rồi bổ sung thêm"* → `AU-8`.

Google là provider duy nhất trong phạm vi. Hai nút còn lại trên bản thiết kế vẫn để tắt.

Phần dưới là **kết quả nghiên cứu đã kiểm chứng ngày 21/08**, giữ lại để lần sau khỏi tra lại từ đầu.

### Facebook — bốn thứ khiến nó không phải "thêm một dòng config"

| Phát hiện | Nguồn | Hệ quả |
|---|---|---|
| **Không có ID token.** Đăng nhập web của Facebook là OAuth 2.0 thuần, lấy hồ sơ bằng một lời gọi Graph riêng | [Manually Build a Login Flow](https://developers.facebook.com/docs/facebook-login/guides/advanced/manual-flow) | Không dùng lại được `OpenIdConnectIdentityProvider`. Cần adapter riêng |
| **Không hỗ trợ PKCE.** Tài liệu luồng thủ công không có `code_challenge`/`code_verifier` | cùng nguồn trên, kiểm 21/08/2026 | Bảo vệ chỉ còn dựa vào client secret + redirect URI khớp tuyệt đối + `state` dùng một lần. Chấp nhận được vì backend là confidential client, nhưng phải ghi rõ chứ không im lặng |
| **Phiên bản hiện hành là `v25.0`**, không phải v21 | cùng nguồn trên | `https://www.facebook.com/v25.0/dialog/oauth` và `GET https://graph.facebook.com/v25.0/oauth/access_token` |
| Lời gọi Graph nên kèm **`appsecret_proof`** — HMAC-SHA256 của access token, khóa là app secret | [Securing Graph API Requests](https://developers.facebook.com/docs/graph-api/securing-requests) | Tùy chọn theo mặc định, nhưng bật *Require App Secret* thì bắt buộc |

**Và một quyết định nghiệp vụ chưa đóng.** Facebook **không** khẳng định email đã xác minh, nên luật
gộp im lặng của [ADR-0013](../decisions/0013-one-email-one-account-silent-linking.md) không áp được.
Chủ sản phẩm ngày 21/08 nói rõ hướng muốn đi: *"đăng nhập tài khoản fb là người ta chỉ login vào
facebook rồi nhả ra access token"* — tức Facebook đứng riêng, không so email với ai.

> **Hướng đó kéo theo một thay đổi thật ở tầng dữ liệu:** không hỏi email thì tài khoản tạo bằng
> Facebook **không có email**, mà hiện `User.Email` là bắt buộc và là khóa duy nhất. Việc đã thử và
> **đã hoàn nguyên** ngày 21/08 theo chỉ đạo hoãn — `AU-5`, không dựng sẵn cấu trúc cho tính năng chưa
> làm. Khi nào mở lại Facebook thì đây là hạng mục lớn nhất, không phải adapter.
>
> Kèm theo là một câu hỏi cho chủ sản phẩm: tài khoản không có email thì **không nhận được thông báo,
> không xác minh được, và mất tài khoản Facebook là mất luôn tài khoản VNI**. Ngoài ra `T4` gắn việc
> cộng quyền lợi vào email đã xác minh — tài khoản Facebook sẽ không bao giờ qua được cổng đó.

### Microsoft

Không nằm trong requirement nào. Về kỹ thuật đây là chỗ **dễ nhất trong ba cái**: Entra ID là OpenID
Connect chuẩn, dùng lại nguyên `OpenIdConnectIdentityProvider`, chỉ khác một URL discovery và một
dòng cấu hình.

---

## 5 · Nghĩa vụ PDPL

Đăng nhập Google gửi email và địa chỉ IP của người học sang Google. **Đó là chuyển dữ liệu cá nhân xuyên biên giới**, kể cả khi nó không phải dữ liệu bài thi và do chính người dùng khởi xướng. Nó thuộc hồ sơ CTIA của `B-2`, không được mặc định coi là ngoài phạm vi. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)
