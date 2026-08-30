/**
 * Every user-visible string in the learner app.
 *
 * <b>This exists from the first screen because retrofitting it is expensive.</b>
 * `M-4` — whether the interface is Vietnamese, English, or both — is still an
 * open question, and so is the language of AI feedback. Waiting for that answer
 * before building the structure would mean a full pass over every screen later;
 * `client-architecture.md` says to build it in from the start for exactly that
 * reason.
 *
 * <b>What is decided and what is not:</b> the structure is decided. Vietnamese
 * is the default because the product serves Vietnamese self-study learners, and
 * English exists so the shape is proven with two languages rather than one.
 * Neither is a commitment — `M-4` still owns that.
 *
 * No i18n library. A typed record gives compile-time safety that every language
 * defines every key, which is the property that actually matters here; a library
 * would add a runtime, a loader, and a plural-rules engine this product does not
 * yet need.
 */

export const LOCALES = ['vi', 'en'] as const;
export type Locale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: Locale = 'vi';

const vi = {
  'app.name': 'VNI IELTS AI',
  'app.tagline': 'Luyện thi IELTS với trợ giúp của AI',

  'nav.home': 'Trang chủ',
  'nav.profile': 'Hồ sơ',
  'nav.signIn': 'Đăng nhập',
  'nav.signUp': 'Đăng ký',
  'nav.signOut': 'Đăng xuất',
  'nav.skipToContent': 'Bỏ qua, tới nội dung chính',
  'pager.label': 'Phân trang',
  'pager.previous': 'Trước',
  'pager.next': 'Sau',
  'pager.page': 'Trang {number}',
  'crumbs.label': 'Đường dẫn',

  'common.loading': 'Đang tải…',
  'common.retry': 'Thử lại',
  'common.back': 'Quay lại',
  'common.save': 'Lưu',
  'common.cancel': 'Huỷ',
  'common.close': 'Đóng',
  'common.email': 'Email',
  'common.password': 'Mật khẩu',
  'common.displayName': 'Tên hiển thị',
  'common.notConnected': 'Không kết nối được tới máy chủ. Kiểm tra mạng rồi thử lại.',
  'common.unexpected': 'Có lỗi ngoài dự kiến. Vui lòng thử lại.',

  'auth.tabLogin': 'Đăng nhập',
  'auth.tabRegister': 'Đăng ký mới',
  'auth.welcomeBack': 'Chào mừng trở lại 👋',
  'auth.welcomeSub': 'Đăng nhập để tiếp tục hành trình học của bạn.',
  'auth.createTitle': 'Tạo tài khoản mới',
  'auth.createSub': 'Bắt đầu hành trình luyện thi IELTS của bạn.',
  'auth.orEmail': 'hoặc dùng email',
  'auth.fullName': 'Họ và tên',
  'auth.passwordPlaceholder': 'Nhập mật khẩu',
  'auth.showPassword': 'Hiện mật khẩu',
  'auth.hidePassword': 'Ẩn mật khẩu',
  'auth.forgot': 'Quên mật khẩu?',
  'auth.createFree': 'Tạo tài khoản miễn phí',
  'auth.signInNow': 'Đăng nhập ngay',
  'auth.google': 'Tiếp tục với Google',
  'auth.soon': 'sắp có',
  'auth.notBuilt': 'Tính năng này chưa được xây dựng.',
  'auth.rateLimited': 'Bạn thử quá nhiều lần. Vui lòng đợi {seconds} giây rồi thử lại.',
  'auth.ssoSoon': 'Đăng nhập bằng Google đang được hoàn thiện',
  'auth.terms':
    'Khi tạo tài khoản, bạn đồng ý với Điều khoản sử dụng và Chính sách bảo mật của VNI Education.',
  'auth.pwWeak': 'Còn ngắn — cần ít nhất 12 ký tự',
  'auth.pwOk': 'Được, có thể dài hơn nữa',
  'auth.pwGood': 'Mật khẩu tốt',

  'sso.busy': 'Đang hoàn tất đăng nhập…',
  'sso.denied': 'Bạn đã hủy đăng nhập bằng Google.',
  'sso.expired': 'Phiên đăng nhập đã hết hạn. Vui lòng thử lại.',
  'sso.providerFailed': 'Không kết nối được với Google. Vui lòng thử lại.',
  'sso.noEmail':
    'Google không chia sẻ địa chỉ email, nên không thể đăng nhập. Hãy dùng email và mật khẩu.',
  'sso.linkRequired':
    'Email này đã có tài khoản. Hãy đăng nhập bằng mật khẩu trước, rồi liên kết sau.',
  'sso.providerUnknown': 'Cách đăng nhập này hiện không dùng được.',
  'sso.rateLimited': 'Bạn thử quá nhiều lần. Vui lòng đợi một lát rồi thử lại.',
  'sso.missingCode': 'Liên kết đăng nhập không hợp lệ. Vui lòng thử lại từ đầu.',
  'sso.backToSignIn': 'Quay lại đăng nhập',
  'sso.starting': 'Đang chuyển tới Google…',

  'verifyAgain.send': 'Gửi email xác minh',
  'verifyAgain.sending': 'Đang gửi…',
  'verifyAgain.sent': 'Đã gửi. Kiểm tra hộp thư của bạn, kể cả mục spam.',
  'verifyAgain.retry': 'Thử lại',
  /*
   * Máy chủ trả lời rằng **không có thư nào được gửi đi**, và màn hình nói
   * đúng như vậy. Chưa có dịch vụ gửi email nào được cấu hình; liên kết xác
   * minh được ghi vào log của máy chủ. Viết "đã gửi" ở đây là đẩy người học đi
   * mở một hộp thư trống rồi kết luận sản phẩm hỏng. → `M-45`
   */
  'verifyAgain.notSent':
    'Chưa gửi được: hệ thống chưa nối dịch vụ email nào. Liên kết xác minh đang được ghi vào log của máy chủ.',
  'verifyAgain.tooOften': 'Bạn vừa yêu cầu rồi. Đợi một lát rồi thử lại.',

  // ── Mã xác minh 6 số ────────────────────────────────────────────────
  // Mỗi lời từ chối có một câu riêng, vì bước tiếp theo của học viên khác
  // nhau: sai mã thì nhìn lại thứ vừa gõ, hết hạn thì bấm gửi lại, hết lượt
  // thì phải biết vì sao mã trong tay đã ngừng hoạt động.
  'verifyCode.label': 'Mã xác minh 6 số',
  // Nói rõ đã gửi *cái gì* và sống được bao lâu. "Đã gửi" một mình để học viên
  // đi tìm một cái link không tồn tại — mail này không có link nào cả.
  'verifyCode.hint':
    'Đã gửi mã 6 số tới email của bạn. Mã có hiệu lực 10 phút — nhớ xem cả mục spam.',
  'verifyCode.submit': 'Xác minh',
  'verifyCode.checking': 'Đang kiểm tra…',
  'verifyCode.done': 'Đã xác minh email của bạn.',
  'verifyCode.incorrect': 'Mã không đúng. Hãy kiểm tra lại email và thử lần nữa.',
  'verifyCode.expired': 'Mã đã hết hạn. Hãy bấm gửi lại để nhận mã mới.',
  'verifyCode.exhausted':
    'Bạn đã nhập sai quá nhiều lần và mã này không còn dùng được. Hãy bấm gửi lại để nhận mã mới.',
  'verifyCode.resend': 'Gửi lại mã',

  'email.change': 'Đổi',
  'email.changeHint':
    'Chỉ đổi được khi email chưa xác minh. Xác minh xong là khoá lại — vì đó là đường lấy lại tài khoản của bạn.',
  'email.taken': 'Email này đã có tài khoản khác dùng.',
  'email.invalid': 'Email chưa đúng định dạng.',
  'email.locked': 'Email đã xác minh nên không đổi được nữa.',

  'phone.add': 'Thêm số điện thoại',
  'phone.change': 'Sửa',
  'phone.save': 'Lưu',
  'phone.cancel': 'Hủy',
  'phone.invalid': 'Số này chưa đúng. Ví dụ: 0912 345 678.',
  'phone.hint': 'Để trống rồi bấm Lưu nếu bạn muốn bỏ số điện thoại.',

  'password.createTitle': 'Tạo mật khẩu',
  'password.createLead':
    'Bạn đang vào bằng nút "Tiếp tục với Google". Đặt thêm mật khẩu thì lần sau bạn vào được bằng cả hai cách — vẫn là một tài khoản, không mất gì cả.',
  'password.changeTitle': 'Đổi mật khẩu',
  'password.changeLead': 'Nhập mật khẩu đang dùng, rồi đặt mật khẩu mới.',
  'password.current': 'Mật khẩu hiện tại',
  'password.next': 'Mật khẩu mới',
  'password.create': 'Tạo mật khẩu',
  'password.change': 'Đổi mật khẩu',
  'password.saving': 'Đang lưu…',
  'password.rule': 'Ít nhất 12 ký tự.',
  'password.done': 'Đã lưu mật khẩu mới.',
  'password.wrongCurrent': 'Mật khẩu hiện tại không đúng.',
  'password.tooWeak': 'Mật khẩu còn ngắn — cần ít nhất 12 ký tự.',
  'password.othersSignedOut':
    'Sau khi lưu, các thiết bị khác sẽ bị đăng xuất. Thiết bị bạn đang dùng thì không.',
  'password.forgotTitle': 'Quên mật khẩu',
  'password.forgotLead':
    'Nhập email của bạn. Nếu địa chỉ đó có tài khoản, chúng tôi sẽ gửi một liên kết đặt lại mật khẩu.',
  'password.forgotSubmit': 'Gửi liên kết',
  'password.forgotSent':
    'Nếu địa chỉ này có tài khoản, liên kết đặt lại đã được gửi. Kiểm tra hộp thư của bạn.',
  'password.resetTitle': 'Đặt mật khẩu mới',
  'password.resetLead': 'Đặt mật khẩu mới cho tài khoản của bạn.',
  'password.resetSubmit': 'Lưu mật khẩu',
  'password.resetDone': 'Xong. Bạn có thể đăng nhập bằng mật khẩu mới.',
  'password.resetInvalid': 'Liên kết này không còn hiệu lực. Hãy yêu cầu một liên kết mới.',
  'password.resetMissing': 'Liên kết thiếu mã đặt lại. Hãy mở lại từ email.',
  'password.backToSignIn': 'Về trang đăng nhập',

  'devices.lead': 'Những thiết bị đang đăng nhập vào tài khoản này.',
  'devices.loading': 'Đang tải danh sách thiết bị…',
  'devices.thisDevice': 'Thiết bị bạn đang dùng',
  'devices.lastUsed': 'Hoạt động {when}',
  'devices.signOut': 'Đăng xuất',
  'devices.signingOut': 'Đang đăng xuất…',
  'devices.signOutFailed': 'Chưa đăng xuất được thiết bị này. Vui lòng thử lại.',
  'devices.signOutOthers': 'Đăng xuất khỏi {n} thiết bị khác',
  'devices.showAll': 'Xem thêm {n} thiết bị',
  'devices.failed': 'Chưa tải được danh sách',
  'devices.failedBody': 'Kiểm tra kết nối mạng rồi mở lại trang.',

  'time.justNow': 'vừa xong',
  'time.minutes': '{n} phút trước',
  'time.hours': '{n} giờ trước',
  'time.days': '{n} ngày trước',

  'profile.email': 'Email',
  'profile.emailNone': 'Chưa có email',
  'profile.phone': 'Số điện thoại',
  'profile.phoneNone': 'Chưa thêm',
  'profile.password.googleOnly': 'Bạn đang đăng nhập bằng Google',
  'profile.password.googleOnlyBody':
    'Tài khoản của bạn không dùng mật khẩu riêng — bạn vào bằng nút "Tiếp tục với Google". Mật khẩu và bảo mật do Google quản lý, nên muốn đổi thì đổi ở tài khoản Google của bạn.',
  'profile.password.hasPassword': 'Đổi mật khẩu',
  'profile.password.hasPasswordBody':
    'Phần đổi mật khẩu đang được hoàn thiện. Trong lúc chờ, nếu bạn quên mật khẩu thì đăng nhập bằng Google với cùng địa chỉ email này cũng vào được tài khoản.',

  'account.profile': 'Hồ sơ học sinh',
  'account.studentPage': 'Trang học sinh',
  'account.signOut': 'Đăng xuất',

  'notifications.label': 'Thông báo',
  'notifications.empty': 'Chưa có thông báo nào',
  'notifications.emptyBody': 'Khi có kết quả chấm bài hoặc nhắc lịch học, thông báo sẽ hiện ở đây.',

  'progress.empty': 'Chưa có gì để xem',
  'progress.emptyBody':
    'Tiến độ được dựng từ các bài bạn đã làm. Làm bài đầu tiên xong thì phần này sẽ có nội dung.',

  'signIn.title': 'Đăng nhập',
  'signIn.submit': 'Đăng nhập',
  'signIn.busy': 'Đang đăng nhập…',
  'signIn.noAccount': 'Chưa có tài khoản?',
  'signIn.invalidWithHint':
    'Email hoặc mật khẩu không đúng. Nếu bạn từng vào bằng nút "Tiếp tục với Google", hãy dùng lại nút đó thay vì nhập mật khẩu.',
  'signIn.invalid': 'Email hoặc mật khẩu không đúng.',
  'signIn.suspended': 'Tài khoản này đã bị khoá. Liên hệ hỗ trợ nếu bạn cho rằng đây là nhầm lẫn.',

  'signUp.title': 'Tạo tài khoản',
  'signUp.submit': 'Tạo tài khoản',
  'signUp.busy': 'Đang tạo tài khoản…',
  'signUp.haveAccount': 'Đã có tài khoản?',
  'signUp.passwordHint':
    'Ít nhất 12 ký tự. Không bắt buộc chữ hoa hay ký tự đặc biệt — độ dài quan trọng hơn.',
  'signUp.emailTaken':
    'Email này đã có tài khoản rồi. Nếu trước đây bạn vào bằng nút "Tiếp tục với Google" thì hãy bấm nút đó — tài khoản kiểu này không có mật khẩu riêng.',
  'signUp.goSignIn': 'Sang trang đăng nhập',
  'signUp.emailInvalid': 'Địa chỉ email không hợp lệ.',
  'signUp.passwordWeak': 'Mật khẩu cần ít nhất 12 ký tự.',
  'signUp.nameRequired': 'Vui lòng nhập tên hiển thị.',
  'auth.emailRequired': 'Vui lòng nhập email.',
  'auth.passwordRequired': 'Vui lòng nhập mật khẩu.',
  'auth.tabsLabel': 'Đăng nhập hoặc tạo tài khoản',
  'auth.backHome': 'Về trang chủ',

  /*
   * Browser-tab titles.
   *
   * 15 of 17 `usePageTitle` call sites passed a Vietnamese string literal, so
   * switching the app to English left every tab, every history entry and every
   * bookmark in Vietnamese. Cheap to fix at fourteen screens and progressively
   * less cheap at forty. → `M-4`
   */
  'title.landing': 'Luyện thi IELTS có AI chấm',
  'title.forgotPassword': 'Quên mật khẩu',
  'title.verifyEmail': 'Xác minh email',
  'title.resetPassword': 'Đặt mật khẩu mới',
  'title.ssoCallback': 'Đang đăng nhập',
  'title.articles': 'Bài viết',
  'title.dashboard': 'Khu vực học sinh',
  'title.dictation': 'Nghe chép chính tả',
  'title.profile': 'Hồ sơ',
  'title.results': 'Kết quả',
  'title.practice': 'Luyện 4 kỹ năng',
  'title.signIn': 'Đăng nhập',
  'title.signUp': 'Tạo tài khoản',
  'title.notFound': 'Không tìm thấy trang',
  'notFound.elsewhere': 'Hoặc đi tới một trong bốn phần chính',
  'sso.title': 'Đang đăng nhập…',
  'sso.failedTitle': 'Không đăng nhập được',
  'password.requestNew': 'Yêu cầu liên kết mới',

  'verify.title': 'Xác minh email',
  'verify.busy': 'Đang xác minh…',
  'verify.success': 'Email của bạn đã được xác minh.',
  'verify.invalid':
    'Liên kết xác minh này không còn hiệu lực. Liên kết chỉ dùng được một lần và hết hạn sau 24 giờ.',
  'verify.missing': 'Liên kết thiếu mã xác minh.',
  'verify.continue': 'Tiếp tục',

  'home.greeting': 'Xin chào, {name}',
  'home.unverifiedTitle': 'Email chưa được xác minh',
  /*
   * Câu cũ là *"Một số tính năng sẽ mở sau khi bạn xác minh email"* — một luật
   * **không tồn tại**. Không có chỗ nào trong sản phẩm từ chối tài khoản chưa
   * xác minh bất cứ điều gì, và tài khoản chưa xác minh **được phép làm gì**
   * vẫn là câu hỏi của chủ sản phẩm (`M-45`). Nói trước hộ chủ sản phẩm là bịa
   * chính sách; nói với người học rằng họ đang bị chặn trong khi họ không bị
   * chặn là nói sai. → `G-11`
   */
  'home.unverifiedBody':
    'Tài khoản của bạn dùng bình thường. Khi nào tiện, bạn có thể xác minh email ở trang hồ sơ.',
  'home.unverifiedAction': 'Xác minh ở trang hồ sơ',
  'home.practiceEmpty': 'Chưa có đề thi nào',
  'home.practiceEmptyBody': 'Phần thi chưa được xây dựng. Khi có đề, các đề sẽ xuất hiện tại đây.',
  'home.historyEmpty': 'Bạn chưa làm bài nào',
  'home.historyEmptyBody': 'Kết quả các lần làm bài sẽ hiện tại đây.',

  'dict.eyebrow': 'Nghe chép chính tả',
  'dict.sentenceOf': 'Câu {index} / {total}',
  'dict.replayable': 'Nghe lại thoải mái',
  'dict.play': 'Nghe',
  'dict.stop': 'Dừng',
  'dict.speed': 'Tốc độ',
  'dict.played': 'Đã nghe {count} lần',
  'dict.typeWhatYouHear': 'Gõ lại câu bạn vừa nghe',
  'dict.check': 'Kiểm tra',
  'dict.checking': 'Đang kiểm tra…',
  'dict.next': 'Câu tiếp theo',
  'dict.previous': 'Câu trước',
  'dict.score': 'Đúng {correct}/{total} từ',
  'dict.perfect': 'Chính xác toàn bộ',
  'dict.actual': 'Câu đúng:',
  'dict.missing': 'Bạn bỏ sót từ này',
  /* Short prefixes, spoken before the word itself. The long strings above are
     tooltips; a screen reader reading "Bạn bỏ sót từ này bus" for every word
     would bury the sentence being checked. */
  'dict.markMissing': 'thiếu',
  'dict.setDone': 'Xong bộ câu này',
  'dict.setDoneBody': 'Bạn đã đi hết {total} câu. Nghe lại bất cứ câu nào, hoặc chọn một bộ khác.',
  'dict.setDoneAction': 'Chọn bộ khác',
  'dict.markExtra': 'thừa',
  'dict.markWrong': 'sai',
  'dict.shouldBe': 'đúng là',
  'dict.extra': 'Từ này không có trong câu',
  'dict.emptyTitle': 'Chưa có bộ câu nào',
  'dict.emptyBody': 'Khi có bộ câu, phần luyện nghe chép sẽ mở ở đây.',

  'exam.hubTitle': 'Luyện tập',
  'exam.hubLead': 'Chọn cách làm bài, rồi chọn đề. Kết quả được ghi vào hồ sơ của bạn.',
  'exam.modeLabel': 'Cách làm bài',
  'exam.modeSingle': 'Luyện từng kỹ năng',
  'exam.modeFull': 'Thi thử full',
  'exam.modeSingleHint':
    'Làm xong một kỹ năng là kết thúc — hệ thống không tự chuyển sang kỹ năng khác. Muốn luyện tiếp thì chọn đề mới.',
  'exam.modeFullHint':
    'Một phiên duy nhất, đi lần lượt Reading → Listening → Writing → Speaking. Hết một kỹ năng, nút “Tiếp theo” chuyển thẳng sang kỹ năng kế tiếp.',
  'exam.moduleMeta': '{count} câu · {duration}',
  'exam.start': 'Bắt đầu',
  'exam.startFull': 'Bắt đầu thi thử full',
  'exam.starting': 'Đang mở…',
  'exam.startFailed': 'Không mở được phiên thi. Thử lại giúp mình nhé.',
  'exam.notFullTest': 'Đề này chỉ có {modules} nên chưa thi full được.',
  'exam.loading': 'Đang tải…',
  'exam.loadFailed': 'Không tải được danh sách đề. Kiểm tra kết nối rồi thử lại.',
  'exam.emptyTitle': 'Chưa có đề nào',
  'exam.emptyBody': 'Kho đề đang được xây dựng. Khi có đề, các đề sẽ hiện ở đây.',

  'exam.passageLabel': 'Bài đọc',
  'exam.questionsLabel': 'Câu hỏi',
  'exam.partsLabel': 'Các phần',
  'exam.part': 'Phần {number}',
  'exam.questionNumber': 'Câu {number}',
  'exam.questionRange': 'Câu {from}–{to}',
  'exam.questionsIn': 'Câu hỏi phần {number}',
  'exam.answeredOf': 'Đã trả lời {answered}/{total}',
  'exam.answerLabel': 'Câu trả lời của bạn',
  'exam.pickAnswer': '— chọn —',
  'exam.answerBank': 'Ngân hàng đáp án',
  'exam.bankInstructions':
    'Kéo đáp án vào câu hỏi, hoặc chọn một đáp án rồi chọn ô trả lời. Có thể dùng danh sách chọn bên dưới bằng bàn phím.',
  'exam.dropAnswer': 'Thả hoặc chọn đáp án',
  'exam.assignAnswer': 'Chọn {key} cho câu này',
  'exam.usedAt': '(đã dùng ở câu {number})',
  'exam.maxWords': 'Tối đa {count} từ',
  'exam.saved': 'Đã lưu',
  'exam.saving': 'Đang gửi',
  'exam.notSentYet': 'Chưa gửi được',
  'exam.savePending': 'Đang chờ lưu',
  'exam.submitFailed': 'Không nộp được bài. Bài của bạn vẫn còn trên máy chủ — hãy thử nộp lại.',
  // Says the true thing. "Không nộp được bài" would reassure the learner about
  // the server precisely when the answer at risk is the one still only on their
  // screen.
  'exam.saveBlockedStep':
    'Câu trả lời cuối chưa lưu được, nên chưa nộp bài. Bài của bạn vẫn trên màn hình — hãy thử lại.',
  'exam.expiredUnsaved':
    'Hết giờ. Những câu bạn trả lời cuối cùng chưa gửi được — phần đã lưu trước đó vẫn được giữ.',
  'exam.saveFailed': 'Gửi thất bại',
  // Names the questions. A refusal the learner cannot locate costs them the
  // answer: the chip can only say that *a* save failed, and on a forty-question
  // paper that is not something anyone can act on.
  'exam.answersRefused': 'Máy chủ không nhận câu {questions}. Hãy sửa lại rồi thử nộp.',
  'exam.underFiveMinutes': 'còn dưới 5 phút',
  'exam.underOneMinute': 'còn dưới 1 phút',
  'exam.clockKeepsRunning': 'Đồng hồ do máy chủ giữ và không dừng khi mất mạng.',
  'exam.expired': 'Hết giờ. Phần bạn đã lưu trước hạn vẫn được giữ.',
  'exam.submit': 'Nộp bài',
  'exam.submitting': 'Đang nộp…',

  /*
   * Full Test and Single Skill say different things here, and `E-12`/`E-13`
   * are why. "Tiếp theo" nộp phần đang làm rồi mở phần kế tiếp trong CÙNG một
   * phiên; "Nộp bài" đóng cả phiên. Dùng nhầm một trong hai là mất ba kỹ năng
   * hoặc là hứa một bước không tồn tại.
   */
  'exam.next': 'Tiếp theo',
  'exam.advancing': 'Đang chuyển phần…',
  'exam.advanceFailed':
    'Không chuyển được sang kỹ năng tiếp theo. Bài của bạn vẫn còn trên máy chủ — thử lại.',
  'exam.nextNote': 'Bấm “Tiếp theo” là nộp phần {current} và mở phần {next}. Không quay lại được.',
  'exam.lastSectionNote': 'Đây là kỹ năng cuối. Nộp bài là kết thúc cả phiên thi.',
  'exam.sectionOf': 'Kỹ năng {number}/{total}',
  'exam.newTest': 'Làm đề mới',
  'exam.singleEndsHere':
    'Luyện từng kỹ năng kết thúc ở đây — không có bước chuyển sang kỹ năng khác.',
  'exam.nothingMarkedTitle': 'Chưa có kết quả nào cho buổi này',
  'exam.nothingMarkedBody':
    'Bài đã nộp và đang nằm trên máy chủ. Kỹ năng do AI chấm đang chờ xử lý hoặc chờ cấu hình chấm; bấm “Kiểm tra lại” sau ít phút.',

  'exam.gone': 'Không tìm thấy phiên thi',
  'exam.goneBody': 'Phiên thi này không còn nữa, hoặc không thuộc về tài khoản của bạn.',
  'exam.resultsRetryBody':
    'Bài của bạn vẫn nằm trên máy chủ. Kiểm tra mạng rồi tải lại trang kết quả.',

  'exam.play': 'Phát',
  'exam.audioLoading': 'Đang tải audio…',
  'exam.pause': 'Tạm dừng',
  'exam.audioOnce': 'Audio chỉ phát một lần, không tua được.',
  'exam.audioReplayable': 'Không tua được.',
  'exam.audioSeekable': 'Có thể phát lại và tua theo chính sách bài luyện.',
  'exam.audioSeek': 'Tua audio',
  'exam.audioSpent': 'Audio đã phát xong.',
  'exam.audioFailed': 'Không tải được audio. Kiểm tra kết nối rồi mở lại phần này.',
  'exam.audioRetry': 'Thử tải lại',
  'exam.audioPolicyMissing': 'Thiếu chính sách phát audio từ máy chủ. Không thể bắt đầu phần nghe.',
  'exam.imageLoading': 'Đang tải hình…',
  'exam.imageFailed': 'Không tải được hình. Kiểm tra kết nối rồi mở lại phần này.',
  'exam.imageNoDescription':
    'Hình này chưa có phần mô tả bằng chữ. Nếu bạn dùng trình đọc màn hình, hãy báo cho VNI để chúng tôi bổ sung.',
  'exam.transferNote': 'Sau khi audio kết thúc, bạn còn {minutes} phút để chép đáp án.',
  'exam.words': '{count} từ',
  'exam.minWords': 'Cần ít nhất {count} từ',
  'exam.underMinWords': 'Còn thiếu {count} từ',
  'exam.record': 'Bắt đầu ghi âm',
  'exam.prepareThenRecord': 'Bắt đầu chuẩn bị',
  /*
   * Nói trước khi bấm, không phải sau.
   *
   * Reading và Listening báo thời lượng ngay trên thẻ đề, Writing báo số từ tối
   * thiểu ngay dưới ô viết — chỉ Speaking là không báo gì cho tới khi đồng hồ
   * đã chạy. Hai số này vốn đã có sẵn trong `SpeakingPartTimingView`.
   */
  'exam.speakingBudget': 'Chuẩn bị {prep} · nói tối đa {response}',
  'exam.speakingBudgetNoPrep': 'Nói tối đa {response}, không có thời gian chuẩn bị',
  'exam.preparing': 'Đang chuẩn bị',
  'exam.recording': 'Đang ghi âm',
  'exam.stopRecording': 'Dừng',
  'exam.uploading': 'Đang gửi bản ghi…',
  'exam.uploadingPercent': 'Đang gửi bản ghi… {percent}%',
  'exam.recordingStored': 'Đã lưu bản ghi',
  'exam.uploadFailed': 'Gửi bản ghi thất bại. Bản ghi vẫn còn, thử gửi lại.',
  'exam.recordingQueued':
    'Bản ghi đang giữ trên máy — sẽ gửi khi có mạng, hoặc bấm Gửi lại.',
  'exam.micPermissionHint':
    'Trình duyệt sẽ hỏi quyền micro trước khi đồng hồ chuẩn bị hoặc ghi âm bắt đầu.',
  'exam.levelMeter': 'Mức âm thanh micro',

  /*
   * ── Luyện đề · `E-20`…`E-32` ─────────────────────────────────────────
   *
   * The mode with a stopwatch instead of a deadline. Every string here is
   * about time the learner controls, so none of them borrows the countdown's
   * vocabulary: nothing "hết giờ", nothing "còn lại".
   */
  'practice.modeBadge': 'Luyện đề',
  'practice.leave': 'Thoát',
  'practice.leaveTitle': 'Thoát khỏi bài đang làm?',
  'practice.leaveBody': 'Bài chưa được nộp. Bạn có thể quay lại phiên này để làm tiếp.',
  'practice.leaveUnsettled':
    'Một số thay đổi chưa được máy chủ xác nhận. Trạng thái trên màn hình cho biết phần nào còn đang chờ.',
  'practice.leaveConfirm': 'Thoát khỏi bài',
  'practice.runnerState': 'Trạng thái bài làm',
  'practice.readingView': 'Chọn phần hiển thị trên màn hình nhỏ',
  'practice.connectionOnline': 'Đã kết nối',
  'practice.connectionOffline': 'Mất kết nối',
  'practice.scopeInvalidTitle': 'Không thể mở đúng phần bài tập',
  'practice.scopeInvalidBody':
    'Dữ liệu phiên không khớp với phần máy chủ đã chọn. Không có câu hỏi nào được hiển thị; hãy quay lại thư viện và mở lại phiên.',
  'practice.startPractice': 'Luyện đề',
  'practice.startPracticeHint': 'Đồng hồ đếm lên, dừng được',
  'practice.clockLabel': 'Thời gian đã làm',
  'practice.pause': 'Dừng đồng hồ',
  'practice.resume': 'Chạy tiếp',
  'practice.running': 'Đồng hồ đang chạy',
  'practice.paused': 'Đồng hồ đang dừng',
  'practice.clockBusy': 'Đang đổi trạng thái đồng hồ…',
  'practice.clockFailed': 'Không đổi được trạng thái đồng hồ. Đồng hồ vẫn như cũ.',
  'practice.clockOffline':
    'Mất kết nối nên chưa dừng được đồng hồ — dừng đồng hồ là một thao tác ở máy chủ.',
  'practice.target': 'Mốc thời gian mục tiêu',
  'practice.targetOpen': 'Mốc mục tiêu',
  'practice.targetNone': 'Chưa đặt mốc',
  'practice.targetSet': 'Mục tiêu {time}',
  'practice.targetPassed': 'Đã qua mốc mục tiêu',
  'practice.targetPreset': '{count} phút',
  'practice.targetCustom': 'Tự nhập (phút)',
  'practice.targetApply': 'Đặt mốc',
  'practice.targetClear': 'Xoá mốc',
  'practice.targetFailed': 'Không đặt được mốc thời gian.',
  'practice.targetRange': 'Mốc thời gian phải từ 1 đến 360 phút.',
  'practice.sectionMap': 'Bản đồ câu hỏi theo section',
  'practice.sectionN': 'Section {number}',
  'practice.sectionCount': '{answered}/{total} câu',
  'practice.sectionProgress': 'Section {number} · {answered}/{total}',
  'practice.emptySection': 'Section {number} chưa có câu hỏi nào',
  'practice.boxAnswered': 'đã trả lời, đã lưu',
  'practice.boxUnsaved': 'đã nhập, chưa lưu xong',
  'practice.boxEmpty': 'chưa trả lời',
  'practice.prevSection': 'Section trước',
  'practice.nextSection': 'Section sau',
  'practice.confirmTitle': 'Bạn chắc chắn muốn nộp bài?',
  'practice.confirmBody': 'Sau khi nộp không thể sửa.',
  'practice.confirmUnanswered': 'Còn {count} câu chưa trả lời.',
  'practice.confirmUnansweredIn': 'Section {number}: {count} câu',
  'practice.confirmOffline':
    'Đang mất kết nối. Bài chỉ nộp được khi máy chủ nhận được — không có hàng đợi nào giữ hộ bạn.',
  'practice.fullNotSupported':
    'Phiên này là bài thi đủ bốn kỹ năng. Ở đây bạn làm và nộp được phần đang mở; chuyển sang kỹ năng kế tiếp trong chế độ luyện đề thì chưa có luật, nên chưa dựng.',
  'exam.micDenied': 'Chưa được cấp quyền micro. Cho phép micro rồi thử lại.',
  'exam.micNoDevice': 'Không tìm thấy micro nào. Cắm tai nghe hoặc micro rồi thử lại.',
  'exam.micBusy': 'Micro đang được ứng dụng khác dùng. Đóng ứng dụng đó rồi thử lại.',
  'exam.micUnsupported':
    'Trình duyệt này không ghi âm được. Hãy mở bài Speaking trên ứng dụng VNI hoặc trên Chrome/Safari bản mới.',
  'exam.micHowTo':
    'Trên máy tính: bấm biểu tượng ổ khoá cạnh địa chỉ trang rồi bật Micro. Trên iPhone: Cài đặt › Safari › Micro. Trên Android: Cài đặt › Ứng dụng › Chrome › Quyền › Micro.',
  'exam.sendAgain': 'Gửi lại bản ghi',
  'exam.recordAgain': 'Ghi lại từ đầu',
  'exam.tryAgain': 'Thử lại',
  'exam.taskLabel': 'Đề bài',
  'exam.essayLabel': 'Bài viết của bạn',
  'exam.speakingLabel': 'Phần nói',
  'exam.recordingsLabel': 'Câu trả lời của bạn',
  'exam.aiNotMarked': 'Writing và Speaking chưa được chấm xong.',

  'exam.resultsEyebrow': 'Kết quả',
  'exam.resultsLead': 'Điểm từng kỹ năng của lần làm bài này.',
  'exam.resultsExpired': 'Phiên thi hết giờ. Phần bạn đã lưu trước hạn vẫn được chấm.',
  'exam.overall': 'Điểm tổng',
  'exam.overallPending': 'Điểm tổng chỉ có khi đủ cả bốn kỹ năng.',
  'exam.rawOf': 'Đúng {raw}/{max} câu',
  'exam.notMarked': 'Chưa chấm',
  // Kept as the fallback for a sitting whose marking has no job behind it —
  // an older sitting, or one closed before the outbox existed. Everything with
  // a job says what actually happened instead. → `exam.markingWaiting`
  'exam.aiPending':
    'Writing và Speaking do AI chấm đang chờ xử lý, nên kỹ năng chưa có điểm hiện dấu gạch ngang.',
  // One sentence per state, because one sentence for every state is a lie: the
  // learner's next move is different for each of them.
  'exam.markingWaiting': 'Đang chờ chấm.',
  'exam.markingRunning': 'Đang chấm.',
  'exam.markingRetryable': 'Đang thử chấm lại.',
  'exam.markingFailed': 'Chưa chấm được. Bạn có thể kiểm tra lại sau.',
  'exam.markingAwaitingEvaluator': 'Đang chờ bộ chấm tự động sẵn sàng.',
  'exam.markingAwaitingRubric': 'Đang chờ cấu hình bộ tiêu chí chấm.',
  'exam.markingAwaitingVoiceProvider':
    'Bản ghi đã nhận. Chấm Speaking chờ nhà cung cấp giọng nói.',
  'exam.markingNothingSubmitted': 'Chưa có bài nộp để chấm.',
  'exam.markingRejected': 'Bài chấm bị từ chối khi kiểm tra an toàn.',
  'exam.checkAgain': 'Kiểm tra lại',
  'exam.reviewTitle': 'Xem lại từng câu · {skill}',
  'exam.reviewQuestion': 'Câu {number}:',
  'exam.reviewRight': 'đúng',
  'exam.reviewWrong': 'sai',
  'exam.reviewBlank': 'bỏ trống',
  'exam.reviewAnswered': 'bạn trả lời "{answer}"',
  'exam.reviewNoKey':
    'Đây là những gì bạn đã điền. Đáp án đúng không hiển thị ở đây, để đề này còn làm lại được.',
  'exam.reviewExplanationNote':
    'Giải thích chỉ mở sau khi nộp bài và không thay đổi điểm đã chấm theo đáp án.',
  'exam.explanationRequest': 'Vì sao đúng?',
  'exam.explanationRetry': 'Thử tạo lại giải thích',
  'exam.explanationLoading': 'Đang tạo giải thích…',
  'exam.explanationPending': 'Đang tạo giải thích cá nhân.',
  'exam.explanationFailed': 'Chưa tạo được giải thích. Hãy thử lại.',
  'exam.explanationCorrectAnswer': 'Đáp án đúng',
  'exam.markingReviewTitle': 'Xem nhận xét · {skill}',
  'exam.markingTask': 'Task {number}',
  'exam.markingWholeSkill': 'Toàn bộ kỹ năng',
  'exam.markingRubric': 'Bộ tiêu chí: {version}',
  'exam.markingFlags': 'Có {count} cảnh báo cần giáo viên xem lại.',
  'exam.backToPractice': '← Về danh sách đề',

  'dash.railLabel': 'Dành cho học sinh',
  'dash.openNav': 'Mở menu',
  'dash.backHome': 'Quay lại trang chủ',
  'dash.collapseRail': 'Thu gọn thanh bên',
  'dash.expandRail': 'Mở rộng thanh bên',
  'dash.nav.overview': 'Tổng quan',
  'dash.nav.practice': 'Luyện tập',
  'dash.nav.results': 'Buổi gần đây',
  'dash.nav.coming': 'Phần khác',

  'dash.eyebrow': 'Khu vực học sinh',
  'dash.lead': 'Bài đang làm dở, kết quả gần đây, và các phần luyện tập của bạn.',
  'dash.notice':
    'Kho đề đang được xây dựng nên chưa có đề nào để bắt đầu. Các lối vào bên dưới sẽ mở ngay khi có đề.',

  // ── Tổng quan: trạng thái thật của người học ──────────────────────────
  'dash.now.title': 'Đang làm dở',
  'dash.now.section': 'Đang ở phần',
  'dash.now.left': 'Còn lại',
  'dash.now.continue': 'Tiếp tục làm',
  'dash.now.over': 'Đã hết giờ',
  // Không hứa hẹn gì về việc bảo lưu: đồng hồ thi do máy chủ giữ và không dừng
  // (ADR-0007). Câu này nói đúng điều đã xảy ra, không nói điều mình mong.
  'dash.now.overBody': 'Hết thời gian cho phần này. Mở ra để nộp phần đã làm được.',
  'dash.now.open': 'Mở bài',
  'dash.now.none': 'Không có bài nào đang làm dở',
  'dash.now.noneBody': 'Chọn một kỹ năng bên dưới để bắt đầu.',
  'dash.now.browseExams': 'Xem đề luyện',
  'dash.now.tryDictation': 'Thử nghe chép chính tả',

  'dash.stat.sittings': 'Buổi đã làm',
  'dash.stat.skills': 'Kỹ năng đã thử',
  'dash.stat.latest': 'Band gần nhất',
  // Nhãn của dấu gạch. Không phải "0 điểm" — là "chưa có điểm nào".
  'dash.stat.none': 'Chưa có',

  'dash.recent.title': 'Buổi gần đây',
  'dash.progressLabel': 'Tiến độ của bạn',
  'dash.recent.view': 'Xem chi tiết',
  'dash.recent.unmarked': 'Chưa chấm',
  'dash.recent.inProgress': 'Đang làm',
  'dash.recent.overall': 'Tổng',
  'dash.recent.empty': 'Chưa có buổi nào',
  'dash.recent.emptyBody': 'Buổi thi đầu tiên của bạn sẽ hiện ở đây, kèm điểm từng kỹ năng.',
  'dash.recent.all': 'Tất cả',

  'dash.other.title': 'Phần khác',

  'dash.resume.title': 'Bài đang làm dở',
  'dash.resume.empty': 'Không có bài nào đang làm dở',
  'dash.resume.emptyBody':
    'Khi bạn rời đi giữa chừng một bài thi, bài đó sẽ hiện ở đây để vào làm tiếp. Đồng hồ bài thi do máy chủ giữ và không tạm dừng trong lúc bạn vắng mặt.',

  'dash.practice.title': 'Luyện tập',
  'dash.practice.lead':
    'Hai cách làm bài, khác nhau ở chỗ kết thúc: thi full đi hết bốn kỹ năng trong một phiên, còn luyện từng kỹ năng thì dừng lại sau kỹ năng đó.',

  'dash.full.title': 'Thi thử full 4 kỹ năng',
  'dash.full.body':
    'Một phiên duy nhất, đi lần lượt qua bốn kỹ năng. Hết một kỹ năng, nút “Tiếp theo” chuyển thẳng sang kỹ năng kế tiếp trong cùng phiên.',

  'dash.skills.title': 'Luyện từng kỹ năng',
  'dash.skills.lead':
    'Làm xong một kỹ năng là kết thúc — hệ thống không tự chuyển sang kỹ năng khác. Muốn luyện tiếp thì chọn đề mới.',
  'dash.skill.reading': 'Đọc văn bản học thuật và trả lời câu hỏi theo bài đọc.',
  'dash.skill.listening': 'Nghe audio và trả lời câu hỏi theo nội dung vừa nghe.',
  'dash.skill.writing': 'Viết bài theo đề, nhận nhận xét và điểm tham khảo từ AI.',
  'dash.skill.speaking': 'Ghi âm phần trả lời của bạn, AI nghe lại rồi nhận xét.',

  'dash.scoring.key': 'Chấm theo đáp án',
  'dash.scoring.ai': 'AI chấm · tham khảo',
  'dash.status.noExam': 'Chưa có đề',
  'dash.open': 'Vào luyện →',
  'dash.noticeSome':
    'Kho đề đang được xây dựng. Hiện đã có đề mẫu để bạn đi trọn một lượt làm bài.',
  'dash.status.soon': 'Sắp mở',

  'dash.results.title': 'Kết quả gần đây',
  'dash.results.emptyBody':
    'Sau mỗi lần nộp bài, điểm từng kỹ năng và điểm tổng sẽ hiện ở đây. Kỹ năng nào chưa chấm xong hiện dấu gạch ngang, không hiện điểm tạm.',

  'dash.more.title': 'Sắp mở',
  'dash.more.dictation': 'Nghe chép chính tả',
  'dash.more.dictationBody': 'Nghe audio, gõ lại và đối chiếu từng từ.',
  'dash.more.documents': 'Tài liệu',
  'dash.more.documentsBody': 'Đọc PDF ngay trên web hoặc tải file về.',
  'dash.more.articles': 'Bài viết',
  'dash.more.articlesBody': 'Bài hướng dẫn và mẹo luyện thi do VNI đăng.',

  'dash.ai.open': 'Hỏi đáp AI',
  'dash.ai.sub': 'Trợ lý luyện thi',
  'dash.ai.emptyTitle': 'Trợ lý chưa được kết nối',
  'dash.ai.emptyBody':
    'Hỏi đáp AI nằm trong bản phát hành đầu tiên, nhưng phạm vi được phép trả lời và mức token cho mỗi câu hỏi vẫn đang được chốt.',
  'dash.ai.inputLabel': 'Câu hỏi cho trợ lý AI',
  'dash.ai.placeholder': 'Ô nhập sẽ mở khi trợ lý được kết nối',
  'dash.ai.note': 'Ô nhập đang tắt, để không nhận câu hỏi mà chưa có gì trả lời.',

  'profile.roleStudent': 'Học sinh',
  'profile.statusActive': 'Hoạt động',
  'profile.memberHint': 'Tài khoản VNI IELTS AI',
  'profile.personalInfo': 'Thông tin cá nhân',
  'profile.userId': 'Mã người dùng',
  'profile.emailVerified': 'Trạng thái email',
  'profile.verified': 'Đã xác minh',
  'profile.unverified': 'Chưa xác minh',
  'profile.edit': 'Chỉnh sửa',
  'profile.editSoon': 'Chỉnh sửa hồ sơ sẽ mở khi có API cập nhật.',
  'profile.modules': 'Mục hồ sơ',
  'profile.tab.password': 'Bảo mật',
  'profile.tab.devices': 'Thiết bị',
  'profile.tab.progress': 'Tiến độ học tập',
  'profile.tabGroup.account': 'Tài khoản',
  'profile.tabGroup.learning': 'Học tập',
  'profile.devices.title': 'Quản lý thiết bị',
  'profile.progress.title': 'Tiến độ học tập',

  'profile.pageEyebrow': 'Hồ sơ học tập',
  'profile.pageTitle': 'Hồ sơ của bạn',
  'profile.pageLead': 'Thông tin cá nhân, mục tiêu IELTS và trạng thái học tập của bạn.',

  'goal.title': 'Mục tiêu IELTS',
  'goal.noData': 'Chưa có dữ liệu',
  'goal.current': 'Band hiện tại',
  'goal.target': 'Band mục tiêu',
  'goal.examDate': 'Ngày thi dự kiến',
  'goal.progress': 'Tiến độ đến mục tiêu',
  'goal.note':
    'Band hiện tại được tính từ các bài thi bạn đã làm. Mục tiêu và ngày thi sẽ đặt được khi phần thi mở.',
  'goal.skills': 'Bốn kỹ năng',
  'goal.skillsNote': 'Điểm từng kỹ năng hiện lên sau bài thi đầu tiên của bạn.',
  'goal.scoreNone': 'chưa có điểm',
  'profile.password.lead': 'Đổi mật khẩu cho tài khoản đăng nhập bằng email.',
  'profile.password.empty': 'Chưa hỗ trợ đổi mật khẩu tại đây',
  'profile.password.emptyBody':
    'Luồng quên / đổi mật khẩu chưa được xây. Tài khoản Google tiếp tục đăng nhập bằng Google.',
  'profile.devices.lead':
    'Danh sách thiết bị và phiên đăng nhập. Hiện chỉ hiện trình duyệt đang dùng — thu hồi phiên từ xa sẽ bổ sung sau.',
  'profile.devices.thisBrowser': 'Trình duyệt hiện tại',
  'profile.devices.thisBrowserBody': 'Bạn đang đăng nhập trên thiết bị này.',
  'profile.progress.lead':
    'Tiến độ và lịch sử luyện tập. Engine thi chưa có nên phần này còn trống có chủ đích.',

  'notFound.title': 'Không tìm thấy trang',
  'notFound.body': 'Đường dẫn bạn mở không tồn tại hoặc đã bị đổi.',
  'notFound.home': 'Về trang chủ',

  'error.boundaryTitle': 'Trang gặp sự cố',
  'error.boundaryBody':
    'Phần này của ứng dụng gặp lỗi. Bạn có thể tải lại; nếu lỗi lặp lại, vui lòng báo cho chúng tôi.',
  'error.reload': 'Tải lại trang',
} as const;

/** Every key must exist in every locale — the compiler enforces it. */
export type StringKey = keyof typeof vi;

const en: Record<StringKey, string> = {
  'app.name': 'VNI IELTS AI',
  'app.tagline': 'IELTS practice with AI assistance',

  'nav.home': 'Home',
  'nav.profile': 'Profile',
  'nav.signIn': 'Sign in',
  'nav.signUp': 'Sign up',
  'nav.signOut': 'Sign out',
  'nav.skipToContent': 'Skip to main content',
  'pager.label': 'Pagination',
  'pager.previous': 'Previous',
  'pager.next': 'Next',
  'pager.page': 'Page {number}',
  'crumbs.label': 'Breadcrumb',

  'common.loading': 'Loading…',
  'common.retry': 'Try again',
  'common.back': 'Back',
  'common.save': 'Save',
  'common.cancel': 'Cancel',
  'common.close': 'Close',
  'common.email': 'Email',
  'common.password': 'Password',
  'common.displayName': 'Display name',
  'common.notConnected': 'Could not reach the server. Check your connection and try again.',
  'common.unexpected': 'Something went wrong. Please try again.',

  'auth.tabLogin': 'Sign in',
  'auth.tabRegister': 'Create account',
  'auth.welcomeBack': 'Welcome back 👋',
  'auth.welcomeSub': 'Sign in to pick up where you left off.',
  'auth.createTitle': 'Create your account',
  'auth.createSub': 'Start your IELTS practice journey.',
  'auth.orEmail': 'or use email',
  'auth.fullName': 'Full name',
  'auth.passwordPlaceholder': 'Enter your password',
  'auth.showPassword': 'Show password',
  'auth.hidePassword': 'Hide password',
  'auth.forgot': 'Forgot password?',
  'auth.createFree': 'Create a free account',
  'auth.signInNow': 'Sign in',
  'auth.google': 'Continue with Google',
  'auth.soon': 'soon',
  'auth.notBuilt': 'This feature has not been built yet.',
  'auth.rateLimited': 'Too many attempts. Please wait {seconds} seconds and try again.',
  'auth.ssoSoon': 'Google sign-in is still being built',
  'auth.terms':
    'By creating an account you agree to VNI Education’s Terms of Use and Privacy Policy.',
  'auth.pwWeak': 'Too short — at least 12 characters',
  'auth.pwOk': 'Fine, longer is better',
  'auth.pwGood': 'Strong password',

  'sso.busy': 'Finishing sign-in…',
  'sso.denied': 'You cancelled the Google sign-in.',
  'sso.expired': 'This sign-in has expired. Please try again.',
  'sso.providerFailed': 'Could not reach Google. Please try again.',
  'sso.noEmail':
    'Google did not share an email address, so sign-in cannot continue. Use email and password instead.',
  'sso.linkRequired':
    'An account already uses this email address. Sign in with your password first, then link it.',
  'sso.providerUnknown': 'That sign-in method is not available.',
  'sso.rateLimited': 'Too many attempts. Please wait a moment and try again.',
  'sso.missingCode': 'That sign-in link is not valid. Please start again.',
  'sso.backToSignIn': 'Back to sign in',
  'sso.starting': 'Redirecting to Google…',

  'verifyAgain.send': 'Send the verification email',
  'verifyAgain.sending': 'Sending…',
  'verifyAgain.sent': 'Sent. Check your inbox, and your spam folder.',
  'verifyAgain.retry': 'Try again',
  'verifyAgain.notSent':
    'Not sent: no email provider is connected yet. The verification link is written to the server log.',
  'verifyAgain.tooOften': 'You just asked for one. Wait a moment and try again.',

  'verifyCode.label': '6-digit verification code',
  'verifyCode.hint':
    'A 6-digit code has been sent to your email. It is valid for 10 minutes — check spam too.',
  'verifyCode.submit': 'Verify',
  'verifyCode.checking': 'Checking…',
  'verifyCode.done': 'Your email is verified.',
  'verifyCode.incorrect': 'That code is not right. Check the email and try again.',
  'verifyCode.expired': 'That code has expired. Send a new one.',
  'verifyCode.exhausted': 'Too many wrong attempts, so that code no longer works. Send a new one.',
  'verifyCode.resend': 'Send a new code',

  'email.change': 'Change',
  'email.changeHint':
    'Only while it is unverified. Once verified it locks — it is how you get back into your account.',
  'email.taken': 'Another account already uses that address.',
  'email.invalid': 'That is not a valid email address.',
  'email.locked': 'A verified address cannot be changed.',

  'phone.add': 'Add a phone number',
  'phone.change': 'Change',
  'phone.save': 'Save',
  'phone.cancel': 'Cancel',
  'phone.invalid': 'That number does not look right. For example: 0912 345 678.',
  'phone.hint': 'Leave it empty and save to remove your number.',

  'password.createTitle': 'Create a password',
  'password.createLead':
    'You sign in with the "Continue with Google" button. Adding a password lets you use either way in — still one account, nothing is lost.',
  'password.changeTitle': 'Change password',
  'password.changeLead': 'Enter the password you use now, then choose a new one.',
  'password.current': 'Current password',
  'password.next': 'New password',
  'password.create': 'Create password',
  'password.change': 'Change password',
  'password.saving': 'Saving…',
  'password.rule': 'At least 12 characters.',
  'password.done': 'Your new password is saved.',
  'password.wrongCurrent': 'That is not your current password.',
  'password.tooWeak': 'Too short — at least 12 characters.',
  'password.othersSignedOut':
    'Saving signs your other devices out. The one you are using stays signed in.',
  'password.forgotTitle': 'Forgot password',
  'password.forgotLead':
    'Enter your email. If it has an account, we will send a link to set a new password.',
  'password.forgotSubmit': 'Send the link',
  'password.forgotSent':
    'If that address has an account, the reset link is on its way. Check your inbox.',
  'password.resetTitle': 'Set a new password',
  'password.resetLead': 'Choose a new password for your account.',
  'password.resetSubmit': 'Save password',
  'password.resetDone': 'Done. You can sign in with the new password.',
  'password.resetInvalid': 'This link is no longer valid. Request a new one.',
  'password.resetMissing': 'The link is missing its reset code. Open it from the email again.',
  'password.backToSignIn': 'Back to sign in',

  'devices.lead': 'Devices currently signed in to this account.',
  'devices.loading': 'Loading devices…',
  'devices.thisDevice': 'This device',
  'devices.lastUsed': 'Active {when}',
  'devices.signOut': 'Sign out',
  'devices.signingOut': 'Signing out…',
  'devices.signOutFailed': 'Could not sign that device out. Please try again.',
  'devices.signOutOthers': 'Sign out of {n} other devices',
  'devices.showAll': 'Show {n} more',
  'devices.failed': 'Could not load the list',
  'devices.failedBody': 'Check your connection and reload the page.',

  'time.justNow': 'just now',
  'time.minutes': '{n} minutes ago',
  'time.hours': '{n} hours ago',
  'time.days': '{n} days ago',

  'profile.email': 'Email',
  'profile.emailNone': 'No email',
  'profile.phone': 'Phone number',
  'profile.phoneNone': 'Not added',
  'profile.password.googleOnly': 'You sign in with Google',
  'profile.password.googleOnlyBody':
    'Your account has no separate password — you sign in with the "Continue with Google" button. Google looks after the password, so change it in your Google account.',
  'profile.password.hasPassword': 'Change password',
  'profile.password.hasPasswordBody':
    'Changing your password here is still being built. In the meantime, signing in with Google using this same address also gets you in.',

  'account.profile': 'Student profile',
  'account.studentPage': 'Student page',
  'account.signOut': 'Sign out',

  'notifications.label': 'Notifications',
  'notifications.empty': 'Nothing yet',
  'notifications.emptyBody':
    'Scoring results and study reminders will appear here once there are any.',

  'progress.empty': 'Nothing to track yet',
  'progress.emptyBody':
    'Progress is built from the tests you have taken. It fills in after your first attempt.',

  'signIn.title': 'Sign in',
  'signIn.submit': 'Sign in',
  'signIn.busy': 'Signing in…',
  'signIn.noAccount': 'No account yet?',
  'signIn.invalidWithHint':
    'Email address or password is incorrect. If you used the "Continue with Google" button before, use it again instead of a password.',
  'signIn.invalid': 'Email address or password is incorrect.',
  'signIn.suspended':
    'This account has been suspended. Contact support if you think this is a mistake.',

  'signUp.title': 'Create an account',
  'signUp.submit': 'Create account',
  'signUp.busy': 'Creating account…',
  'signUp.haveAccount': 'Already have an account?',
  'signUp.passwordHint':
    'At least 12 characters. No uppercase or symbol required — length matters more.',
  'signUp.emailTaken':
    'This address already has an account. If you used the "Continue with Google" button before, use it again — an account set up that way has no separate password.',
  'signUp.goSignIn': 'Go to sign in',
  'signUp.emailInvalid': 'That is not a valid email address.',
  'signUp.passwordWeak': 'Password must be at least 12 characters.',
  'signUp.nameRequired': 'Please enter a display name.',
  'auth.emailRequired': 'Please enter your email.',
  'auth.passwordRequired': 'Please enter your password.',
  'auth.tabsLabel': 'Sign in or create an account',
  'auth.backHome': 'Back to the home page',
  'title.landing': 'IELTS practice with AI marking',
  'title.forgotPassword': 'Forgotten password',
  'title.verifyEmail': 'Verify your email',
  'title.resetPassword': 'Set a new password',
  'title.ssoCallback': 'Signing you in',
  'title.articles': 'Articles',
  'title.dashboard': 'Student area',
  'title.dictation': 'Dictation',
  'title.profile': 'Your profile',
  'title.results': 'Results',
  'title.practice': 'Four-skill practice',
  'title.signIn': 'Sign in',
  'title.signUp': 'Create an account',
  'title.notFound': 'Page not found',
  'notFound.elsewhere': 'Or head to one of the four main sections',
  'sso.title': 'Signing you in…',
  'sso.failedTitle': 'Could not sign you in',
  'password.requestNew': 'Request a new link',

  'verify.title': 'Verify your email',
  'verify.busy': 'Verifying…',
  'verify.success': 'Your email address has been verified.',
  'verify.invalid':
    'This verification link is no longer valid. Links can be used once and expire after 24 hours.',
  'verify.missing': 'The link is missing its verification code.',
  'verify.continue': 'Continue',

  'home.greeting': 'Hello, {name}',
  'home.unverifiedTitle': 'Email not verified',
  'home.unverifiedBody':
    'Your account works as normal. You can verify your email from your profile whenever it suits you.',
  'home.unverifiedAction': 'Verify from your profile',
  'home.practiceEmpty': 'No exams yet',
  'home.practiceEmptyBody':
    'The exam section has not been built. Exams will appear here once added.',
  'home.historyEmpty': 'No attempts yet',
  'home.historyEmptyBody': 'Results from your attempts will appear here.',

  'dict.eyebrow': 'Dictation',
  'dict.sentenceOf': 'Sentence {index} of {total}',
  'dict.replayable': 'Replay as often as you like',
  'dict.play': 'Play',
  'dict.stop': 'Stop',
  'dict.speed': 'Speed',
  'dict.played': 'Played {count} times',
  'dict.typeWhatYouHear': 'Type the sentence you heard',
  'dict.check': 'Check',
  'dict.checking': 'Checking…',
  'dict.next': 'Next sentence',
  'dict.previous': 'Previous',
  'dict.score': '{correct}/{total} words correct',
  'dict.perfect': 'Every word',
  'dict.actual': 'The sentence was:',
  'dict.missing': 'You missed this word',
  'dict.markMissing': 'missing',
  'dict.setDone': 'Set complete',
  'dict.setDoneBody':
    'You worked through all {total} sentences. Replay any of them, or pick another set.',
  'dict.setDoneAction': 'Pick another set',
  'dict.markExtra': 'extra',
  'dict.markWrong': 'wrong',
  'dict.shouldBe': 'should be',
  'dict.extra': 'This word was not in the sentence',
  'dict.emptyTitle': 'No sentence sets yet',
  'dict.emptyBody': 'Dictation opens here once a set exists.',

  'exam.hubTitle': 'Practice',
  'exam.hubLead': 'Choose how you want to sit it, then choose an exam. Results go to your profile.',
  'exam.modeLabel': 'How to sit it',
  'exam.modeSingle': 'One skill',
  'exam.modeFull': 'Full test',
  'exam.modeSingleHint':
    'Finishing a skill ends the sitting — nothing advances by itself. To keep going, start a new test.',
  'exam.modeFullHint':
    'One sitting, taken in order through Reading → Listening → Writing → Speaking. When a skill ends, “Next” moves straight on to the following one.',
  'exam.moduleMeta': '{count} questions · {duration}',
  'exam.start': 'Start',
  'exam.startFull': 'Start the full test',
  'exam.starting': 'Opening…',
  'exam.startFailed': 'The sitting could not be opened. Please try again.',
  'exam.notFullTest': 'This exam only has {modules}, so it cannot be sat as a full test.',
  'exam.loading': 'Loading…',
  'exam.loadFailed': 'The exam list could not be loaded. Check your connection and try again.',
  'exam.emptyTitle': 'No exams yet',
  'exam.emptyBody': 'The exam library is still being built. Exams will appear here once added.',

  'exam.passageLabel': 'Passage',
  'exam.questionsLabel': 'Questions',
  'exam.partsLabel': 'Parts',
  'exam.part': 'Part {number}',
  'exam.questionNumber': 'Question {number}',
  'exam.questionRange': 'Questions {from}–{to}',
  'exam.questionsIn': 'Questions in part {number}',
  'exam.answeredOf': '{answered}/{total} answered',
  'exam.answerLabel': 'Your answer',
  'exam.pickAnswer': '— choose —',
  'exam.answerBank': 'Answer bank',
  'exam.bankInstructions':
    'Drag an answer to a question, or select an answer and then activate its answer target. The select below is also available from the keyboard.',
  'exam.dropAnswer': 'Drop or assign an answer',
  'exam.assignAnswer': 'Assign {key} to this question',
  'exam.usedAt': '(already at {number})',
  'exam.maxWords': 'At most {count} words',
  'exam.saved': 'Saved',
  'exam.saving': 'Sending',
  'exam.notSentYet': 'Not sent yet',
  'exam.savePending': 'Waiting to save',
  'exam.submitFailed': 'Could not submit. Your work is still on the server — try submitting again.',
  'exam.saveBlockedStep':
    'Your last answer has not been saved, so nothing was submitted. It is still on screen — try again.',
  'exam.expiredUnsaved':
    'Time is up. Your last few answers could not be sent — everything saved before that is kept.',
  'exam.saveFailed': 'Sending failed',
  'exam.answersRefused': 'The server would not take {questions}. Correct it, then submit.',
  'exam.underFiveMinutes': 'under 5 minutes left',
  'exam.underOneMinute': 'under 1 minute left',
  'exam.clockKeepsRunning': 'The clock is held by the server and does not stop if you go offline.',
  'exam.expired': 'Time is up. Everything saved before the deadline is kept.',
  'exam.submit': 'Submit',
  'exam.submitting': 'Submitting…',

  'exam.next': 'Next',
  'exam.advancing': 'Moving on…',
  'exam.advanceFailed':
    'Could not move to the next skill. Your work is still on the server — try again.',
  'exam.nextNote': '“Next” submits {current} and opens {next}. You cannot come back.',
  'exam.lastSectionNote': 'This is the last skill. Submitting ends the whole sitting.',
  'exam.sectionOf': 'Skill {number} of {total}',
  'exam.newTest': 'New test',
  'exam.singleEndsHere': 'Single-skill practice ends here — there is no next skill to move on to.',
  'exam.nothingMarkedTitle': 'No result for this sitting yet',
  'exam.nothingMarkedBody':
    'Your work was submitted and is on the server. AI-marked skills are waiting for processing or marking configuration; press “Check again” in a few minutes.',

  'exam.gone': 'No such exam session',
  'exam.goneBody': 'This sitting no longer exists, or it does not belong to your account.',
  'exam.resultsRetryBody':
    'Your work is still on the server. Check your connection and load the results again.',

  'exam.play': 'Play',
  'exam.audioLoading': 'Loading the audio…',
  'exam.pause': 'Pause',
  'exam.audioOnce': 'The audio plays once and cannot be rewound.',
  'exam.audioReplayable': 'Cannot be rewound.',
  'exam.audioSeekable': 'Replay and seeking are available for this practice.',
  'exam.audioSeek': 'Seek audio',
  'exam.audioSpent': 'The audio has finished.',
  'exam.audioFailed':
    'The audio could not be loaded. Check your connection and reopen this section.',
  'exam.audioRetry': 'Retry audio',
  'exam.audioPolicyMissing':
    'The server did not provide an audio playback policy. This Listening part cannot start.',
  'exam.imageLoading': 'Loading the image…',
  'exam.imageFailed':
    'The image could not be loaded. Check your connection and reopen this section.',
  'exam.imageNoDescription':
    'This image has no written description yet. If you use a screen reader, please tell VNI so we can add one.',
  'exam.transferNote': 'After the audio ends you have {minutes} minutes to copy your answers over.',
  'exam.words': '{count} words',
  'exam.minWords': 'At least {count} words',
  'exam.underMinWords': '{count} words short',
  'exam.record': 'Start recording',
  'exam.prepareThenRecord': 'Start preparation',
  'exam.speakingBudget': '{prep} to prepare · up to {response} speaking',
  'exam.speakingBudgetNoPrep': 'Up to {response} speaking, with no preparation time',
  'exam.preparing': 'Preparing',
  'exam.recording': 'Recording',
  'exam.stopRecording': 'Stop',
  'exam.uploading': 'Sending the recording…',
  'exam.uploadingPercent': 'Sending the recording… {percent}%',
  'exam.recordingStored': 'Recording saved',
  'exam.uploadFailed': 'The recording could not be sent. It is still here — try again.',
  'exam.recordingQueued':
    'The recording is held on this device — it will send when you are back online, or tap Send again.',
  'exam.micPermissionHint':
    'The browser will ask for microphone permission before the preparation or recording clock starts.',
  'exam.levelMeter': 'Microphone input level',

  /* ── Practice mode · `E-20`…`E-32` ──────────────────────────────────── */
  'practice.modeBadge': 'Practice',
  'practice.leave': 'Exit',
  'practice.leaveTitle': 'Exit this sitting?',
  'practice.leaveBody': 'The paper has not been submitted. You can return to this sitting later.',
  'practice.leaveUnsettled':
    'Some changes have not been confirmed by the server. The on-screen status shows what is still pending.',
  'practice.leaveConfirm': 'Exit sitting',
  'practice.runnerState': 'Sitting status',
  'practice.readingView': 'Choose the pane shown on a small screen',
  'practice.connectionOnline': 'Connected',
  'practice.connectionOffline': 'Offline',
  'practice.scopeInvalidTitle': 'The selected practice part cannot be opened',
  'practice.scopeInvalidBody':
    'The session data does not match the part selected by the server. No questions are shown; return to the library and reopen the sitting.',
  'practice.startPractice': 'Practice',
  'practice.startPracticeHint': 'Count-up clock you can stop',
  'practice.clockLabel': 'Time worked',
  'practice.pause': 'Stop the clock',
  'practice.resume': 'Start the clock',
  'practice.running': 'The clock is running',
  'practice.paused': 'The clock is stopped',
  'practice.clockBusy': 'Changing the clock…',
  'practice.clockFailed': 'The clock could not be changed. It is as it was.',
  'practice.clockOffline':
    'Offline, so the clock cannot be stopped — stopping it is a server operation.',
  'practice.target': 'Target working time',
  'practice.targetOpen': 'Target time',
  'practice.targetNone': 'No target set',
  'practice.targetSet': 'Target {time}',
  'practice.targetPassed': 'Past your target time',
  'practice.targetPreset': '{count} min',
  'practice.targetCustom': 'Type your own (minutes)',
  'practice.targetApply': 'Set target',
  'practice.targetClear': 'Clear target',
  'practice.targetFailed': 'The target time could not be set.',
  'practice.targetRange': 'A target must be between 1 and 360 minutes.',
  'practice.sectionMap': 'Question map by section',
  'practice.sectionN': 'Section {number}',
  'practice.sectionCount': '{answered}/{total} answered',
  'practice.sectionProgress': 'Section {number} · {answered}/{total}',
  'practice.emptySection': 'Section {number} has no questions',
  'practice.boxAnswered': 'answered and saved',
  'practice.boxUnsaved': 'answered, not saved yet',
  'practice.boxEmpty': 'not answered',
  'practice.prevSection': 'Previous section',
  'practice.nextSection': 'Next section',
  'practice.confirmTitle': 'Are you sure you want to submit?',
  'practice.confirmBody': 'You cannot edit the paper after you submit.',
  'practice.confirmUnanswered': '{count} questions are still unanswered.',
  'practice.confirmUnansweredIn': 'Section {number}: {count} questions',
  'practice.confirmOffline':
    'You are offline. A paper is only submitted once the server has it — nothing is queued on your behalf.',
  'practice.fullNotSupported':
    'This sitting is a full four-skill test. You can work on and submit the open section here; moving on to the next skill in practice mode has no rule yet, so it is not built.',
  'exam.micDenied': 'Microphone permission was refused. Allow it and try again.',
  'exam.micNoDevice': 'No microphone found. Plug in a headset or microphone and try again.',
  'exam.micBusy': 'Another application is using the microphone. Close it and try again.',
  'exam.micUnsupported':
    'This browser cannot record audio. Open the Speaking task in the VNI app, or in an up-to-date Chrome or Safari.',
  'exam.micHowTo':
    'On a computer: click the padlock beside the address bar and allow the microphone. On iPhone: Settings › Safari › Microphone. On Android: Settings › Apps › Chrome › Permissions › Microphone.',
  'exam.sendAgain': 'Send the recording again',
  'exam.recordAgain': 'Record again from the start',
  'exam.tryAgain': 'Try again',
  'exam.taskLabel': 'Task',
  'exam.essayLabel': 'Your answer',
  'exam.speakingLabel': 'Speaking part',
  'exam.recordingsLabel': 'Your answers',
  'exam.aiNotMarked': 'Writing and Speaking are not marked yet.',

  'exam.resultsEyebrow': 'Results',
  'exam.resultsLead': 'Per-skill bands for this sitting.',
  'exam.resultsExpired':
    'The sitting ran out of time. Everything saved before the deadline is still marked.',
  'exam.overall': 'Overall band',
  'exam.overallPending': 'An overall band needs all four skills.',
  'exam.rawOf': '{raw}/{max} correct',
  'exam.notMarked': 'Not marked',
  'exam.aiPending':
    'Writing and Speaking are AI marked and still processing, so unmarked skills show a dash.',
  'exam.markingWaiting': 'Waiting to be marked.',
  'exam.markingRunning': 'Being marked now.',
  'exam.markingRetryable': 'Trying to mark it again.',
  'exam.markingFailed': 'Marking is not ready. Check again later.',
  'exam.markingAwaitingEvaluator': 'Waiting for automated marking to be available.',
  'exam.markingAwaitingRubric': 'Waiting for the marking rubric to be configured.',
  'exam.markingAwaitingVoiceProvider':
    'Recording received. Speaking marking is waiting for the voice provider.',
  'exam.markingNothingSubmitted': 'There is no submitted answer to mark.',
  'exam.markingRejected': 'The marking was rejected by safety validation.',
  'exam.checkAgain': 'Check again',
  'exam.reviewTitle': 'Review each question · {skill}',
  'exam.reviewQuestion': 'Question {number}:',
  'exam.reviewRight': 'correct',
  'exam.reviewWrong': 'wrong',
  'exam.reviewBlank': 'left blank',
  'exam.reviewAnswered': 'you answered "{answer}"',
  'exam.reviewNoKey':
    'This is what you entered. The correct answers are not shown here, so this paper can be sat again.',
  'exam.reviewExplanationNote':
    'Explanations are available only after submit and do not change the answer-key score.',
  'exam.explanationRequest': 'Why is this correct?',
  'exam.explanationRetry': 'Try the explanation again',
  'exam.explanationLoading': 'Creating explanation…',
  'exam.explanationPending': 'Creating a personal explanation.',
  'exam.explanationFailed': 'The explanation is not ready. Try again.',
  'exam.explanationCorrectAnswer': 'Correct answer',
  'exam.markingReviewTitle': 'Review feedback · {skill}',
  'exam.markingTask': 'Task {number}',
  'exam.markingWholeSkill': 'Whole skill',
  'exam.markingRubric': 'Rubric: {version}',
  'exam.markingFlags': '{count} validation warnings need teacher review.',
  'exam.backToPractice': '← Back to the exam list',

  'dash.railLabel': 'Student area',
  'dash.openNav': 'Open the menu',
  'dash.backHome': 'Back to the home page',
  'dash.collapseRail': 'Collapse the sidebar',
  'dash.expandRail': 'Expand the sidebar',
  'dash.nav.overview': 'Overview',
  'dash.nav.practice': 'Practice',
  'dash.nav.results': 'Recent sittings',
  'dash.nav.coming': 'More',

  'dash.eyebrow': 'Student area',
  'dash.lead': 'What you have open, how you have been scoring, and where to practise next.',
  'dash.notice':
    'The exam library is still being built, so there is nothing to start yet. The entries below open as soon as exams exist.',

  'dash.now.title': 'In progress',
  'dash.now.section': 'Currently on',
  'dash.now.left': 'Time left',
  'dash.now.continue': 'Continue',
  'dash.now.over': 'Time is up',
  'dash.now.overBody': 'This section has run out of time. Open it to submit what you have.',
  'dash.now.open': 'Open',
  'dash.now.none': 'Nothing in progress',
  'dash.now.noneBody': 'Pick a skill below to start.',
  'dash.now.browseExams': 'Browse practice tests',
  'dash.now.tryDictation': 'Try dictation instead',

  'dash.stat.sittings': 'Sittings',
  'dash.stat.skills': 'Skills attempted',
  'dash.stat.latest': 'Latest band',
  'dash.stat.none': 'None yet',

  'dash.recent.title': 'Recent sittings',
  'dash.progressLabel': 'Your progress',
  'dash.recent.view': 'View',
  'dash.recent.unmarked': 'Not marked',
  'dash.recent.inProgress': 'In progress',
  'dash.recent.overall': 'Overall',
  'dash.recent.empty': 'No sittings yet',
  'dash.recent.emptyBody': 'Your first sitting will appear here with a band for each skill.',
  'dash.recent.all': 'All',

  'dash.other.title': 'More',

  'dash.resume.title': 'Unfinished attempt',
  'dash.resume.empty': 'Nothing in progress',
  'dash.resume.emptyBody':
    'If you leave an exam part-way through, it appears here so you can carry on. The exam clock is held by the server and does not pause while you are away.',

  'dash.practice.title': 'Practice',
  'dash.practice.lead':
    'Two ways to sit an exam, and they end differently: a full test runs all four skills in one session, while single-skill practice stops after that skill.',

  'dash.full.title': 'Full test — all four skills',
  'dash.full.body':
    'One session, taken in order through the four skills. When a skill ends, “Next” moves straight on to the following skill inside the same session.',

  'dash.skills.title': 'Practise one skill',
  'dash.skills.lead':
    'Finishing a skill ends the session — nothing advances by itself. To keep going, start a new test.',
  'dash.skill.reading': 'Read an academic passage and answer questions on it.',
  'dash.skill.listening': 'Listen to the audio and answer questions on what you heard.',
  'dash.skill.writing': 'Write to the prompt and get AI comments and an indicative band.',
  'dash.skill.speaking': 'Record your answers; the AI listens back and comments.',

  'dash.scoring.key': 'Marked from the answer key',
  'dash.scoring.ai': 'AI marked · indicative',
  'dash.status.noExam': 'No exam yet',
  'dash.open': 'Practise →',
  'dash.noticeSome':
    'The exam library is still being built. A sample exam is available so you can take a full run through.',
  'dash.status.soon': 'Coming',

  'dash.results.title': 'Recent results',
  'dash.results.emptyBody':
    'After each submission, the per-skill bands and the overall band appear here. A skill that has not finished marking shows a dash, never a provisional score.',

  'dash.more.title': 'Coming',
  'dash.more.dictation': 'Dictation',
  'dash.more.dictationBody': 'Listen, type it back, and compare word by word.',
  'dash.more.documents': 'Documents',
  'dash.more.documentsBody': 'Read a PDF in the browser or download it.',
  'dash.more.articles': 'Articles',
  'dash.more.articlesBody': 'Guides and practice tips published by VNI.',

  'dash.ai.open': 'Ask the AI',
  'dash.ai.sub': 'Practice assistant',
  'dash.ai.emptyTitle': 'The assistant is not connected',
  'dash.ai.emptyBody':
    'AI chat is in the first release, but its allowed scope and token cost per question are still being settled.',
  'dash.ai.inputLabel': 'Question for the AI assistant',
  'dash.ai.placeholder': 'The composer opens once the assistant is connected',
  'dash.ai.note': 'The composer is disabled so it cannot take a question nothing can answer.',

  'profile.roleStudent': 'Student',
  'profile.statusActive': 'Active',
  'profile.memberHint': 'VNI IELTS AI account',
  'profile.personalInfo': 'Personal details',
  'profile.userId': 'User id',
  'profile.emailVerified': 'Email status',
  'profile.verified': 'Verified',
  'profile.unverified': 'Not verified',
  'profile.edit': 'Edit',
  'profile.editSoon': 'Profile editing opens once an update API exists.',
  'profile.modules': 'Profile sections',
  'profile.tab.password': 'Security',
  'profile.tab.devices': 'Devices',
  'profile.tab.progress': 'Learning progress',
  'profile.tabGroup.account': 'Account',
  'profile.tabGroup.learning': 'Learning',
  'profile.devices.title': 'Devices',
  'profile.progress.title': 'Learning progress',

  'profile.pageEyebrow': 'Learning profile',
  'profile.pageTitle': 'Your profile',
  'profile.pageLead': 'Your details, your IELTS goal, and where your learning stands.',

  'goal.title': 'IELTS goal',
  'goal.noData': 'No data yet',
  'goal.current': 'Current band',
  'goal.target': 'Target band',
  'goal.examDate': 'Exam date',
  'goal.progress': 'Progress towards target',
  'goal.note':
    'Your current band is worked out from the exams you have taken. Target and exam date can be set once the exam section opens.',
  'goal.skills': 'Four skills',
  'goal.skillsNote': 'Per-skill bands appear after your first exam.',
  'goal.scoreNone': 'no band yet',
  'profile.password.lead': 'Change the password for an email-and-password account.',
  'profile.password.empty': 'Password change is not available here yet',
  'profile.password.emptyBody':
    'Forgot / change-password flows are not built. Google accounts keep signing in with Google.',
  'profile.devices.lead':
    'Signed-in devices and sessions. Today this only shows the current browser — remote revoke comes later.',
  'profile.devices.thisBrowser': 'This browser',
  'profile.devices.thisBrowserBody': 'You are signed in on this device.',
  'profile.progress.lead':
    'Progress and practice history. Empty on purpose until the exam engine exists.',

  'notFound.title': 'Page not found',
  'notFound.body': 'The address you opened does not exist, or has moved.',
  'notFound.home': 'Go to home',

  'error.boundaryTitle': 'This page hit a problem',
  'error.boundaryBody':
    'Part of the app failed to render. You can reload; if it keeps happening, please tell us.',
  'error.reload': 'Reload the page',
};

export const STRINGS: Record<Locale, Record<StringKey, string>> = { vi, en };
