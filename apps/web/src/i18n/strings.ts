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

  'verifyAgain.send': 'Gửi lại email xác minh',
  'verifyAgain.sending': 'Đang gửi…',
  'verifyAgain.sent': 'Đã gửi. Kiểm tra hộp thư của bạn, kể cả mục spam.',
  'verifyAgain.tooOften': 'Bạn vừa yêu cầu rồi. Đợi một lát rồi thử lại.',

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
  'signUp.doneTitle': 'Đã tạo tài khoản',
  'signUp.doneBody':
    'Chúng tôi đã gửi liên kết xác minh tới email của bạn. Mở liên kết đó để kích hoạt tài khoản.',
  'signUp.devNotice':
    'Môi trường phát triển: chưa có dịch vụ gửi email, liên kết xác minh được ghi vào log của máy chủ.',

  'verify.title': 'Xác minh email',
  'verify.busy': 'Đang xác minh…',
  'verify.success': 'Email của bạn đã được xác minh.',
  'verify.invalid':
    'Liên kết xác minh này không còn hiệu lực. Liên kết chỉ dùng được một lần và hết hạn sau 24 giờ.',
  'verify.missing': 'Liên kết thiếu mã xác minh.',
  'verify.continue': 'Tiếp tục',

  'home.greeting': 'Xin chào, {name}',
  'home.unverifiedTitle': 'Email chưa được xác minh',
  'home.unverifiedBody':
    'Một số tính năng sẽ mở sau khi bạn xác minh email. Kiểm tra hộp thư của bạn.',
  'home.practiceEmpty': 'Chưa có đề thi nào',
  'home.practiceEmptyBody': 'Phần thi chưa được xây dựng. Khi có đề, các đề sẽ xuất hiện tại đây.',
  'home.historyEmpty': 'Bạn chưa làm bài nào',
  'home.historyEmptyBody': 'Kết quả các lần làm bài sẽ hiện tại đây.',

  'dict.eyebrow': 'Nghe chép chính tả',
  'dict.sentenceOf': 'Câu {index} / {total}',
  'dict.replayable': 'Nghe lại thoải mái',
  'dict.play': 'Nghe',
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
  'exam.questionsIn': 'Câu hỏi phần {number}',
  'exam.answeredOf': 'Đã trả lời {answered}/{total}',
  'exam.answerLabel': 'Câu trả lời của bạn',
  'exam.maxWords': 'Tối đa {count} từ',
  'exam.saved': 'Đã lưu',
  'exam.saving': 'Đang gửi',
  'exam.notSentYet': 'Chưa gửi được',
  'exam.saveFailed': 'Gửi thất bại',
  'exam.underFiveMinutes': 'còn dưới 5 phút',
  'exam.underOneMinute': 'còn dưới 1 phút',
  'exam.clockKeepsRunning': 'Đồng hồ do máy chủ giữ và không dừng khi mất mạng.',
  'exam.expired': 'Hết giờ. Phần bạn đã lưu trước hạn vẫn được giữ.',
  'exam.submit': 'Nộp bài',
  'exam.submitting': 'Đang nộp…',
  'exam.gone': 'Không tìm thấy phiên thi',
  'exam.goneBody': 'Phiên thi này không còn nữa, hoặc không thuộc về tài khoản của bạn.',

  'exam.play': 'Phát',
  'exam.pause': 'Tạm dừng',
  'exam.audioOnce': 'Audio chỉ phát một lần, không tua được.',
  'exam.audioReplayable': 'Không tua được.',
  'exam.audioSpent': 'Audio đã phát xong.',
  'exam.audioFailed': 'Không tải được audio. Kiểm tra kết nối rồi mở lại phần này.',
  'exam.transferNote': 'Sau khi audio kết thúc, bạn còn {minutes} phút để chép đáp án.',
  'exam.words': '{count} từ',
  'exam.minWords': 'Cần ít nhất {count} từ',
  'exam.underMinWords': 'Còn thiếu {count} từ',
  'exam.record': 'Bắt đầu ghi âm',
  'exam.prepareThenRecord': 'Bắt đầu chuẩn bị',
  'exam.preparing': 'Đang chuẩn bị',
  'exam.recording': 'Đang ghi âm',
  'exam.stopRecording': 'Dừng',
  'exam.uploading': 'Đang gửi bản ghi…',
  'exam.recordingStored': 'Đã lưu bản ghi',
  'exam.uploadFailed': 'Gửi bản ghi thất bại. Bản ghi vẫn còn, thử gửi lại.',
  'exam.micDenied': 'Chưa được cấp quyền micro. Cho phép micro rồi thử lại.',
  'exam.tryAgain': 'Thử lại',
  'exam.taskLabel': 'Đề bài',
  'exam.essayLabel': 'Bài viết của bạn',
  'exam.speakingLabel': 'Phần nói',
  'exam.recordingsLabel': 'Câu trả lời của bạn',
  'exam.aiNotMarked': 'Writing và Speaking chưa được chấm — chưa nối với mô hình nào.',

  'exam.resultsEyebrow': 'Kết quả',
  'exam.resultsLead': 'Điểm từng kỹ năng của lần làm bài này.',
  'exam.resultsExpired': 'Phiên thi hết giờ. Phần bạn đã lưu trước hạn vẫn được chấm.',
  'exam.overall': 'Điểm tổng',
  'exam.overallPending': 'Điểm tổng chỉ có khi đủ cả bốn kỹ năng.',
  'exam.rawOf': 'Đúng {raw}/{max} câu',
  'exam.notMarked': 'Chưa chấm',
  'exam.aiPending':
    'Writing và Speaking do AI chấm và chưa nối với mô hình nào, nên hai kỹ năng đó hiện dấu gạch ngang.',
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

  'dash.stat.sittings': 'Buổi đã làm',
  'dash.stat.skills': 'Kỹ năng đã thử',
  'dash.stat.latest': 'Band gần nhất',
  // Nhãn của dấu gạch. Không phải "0 điểm" — là "chưa có điểm nào".
  'dash.stat.none': 'Chưa có',

  'dash.recent.title': 'Buổi gần đây',
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
    'Hỏi đáp AI nằm trong bản phát hành đầu tiên, nhưng chưa nối với mô hình nào. Phạm vi được phép trả lời, nhà cung cấp và mức token cho mỗi câu hỏi vẫn đang được chốt.',
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

  'verifyAgain.send': 'Send the verification email again',
  'verifyAgain.sending': 'Sending…',
  'verifyAgain.sent': 'Sent. Check your inbox, and your spam folder.',
  'verifyAgain.tooOften': 'You just asked for one. Wait a moment and try again.',

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
  'signUp.doneTitle': 'Account created',
  'signUp.doneBody':
    'We have sent a verification link to your email address. Open it to activate your account.',
  'signUp.devNotice':
    'Development environment: no email provider is configured, so the verification link is written to the server log.',

  'verify.title': 'Verify your email',
  'verify.busy': 'Verifying…',
  'verify.success': 'Your email address has been verified.',
  'verify.invalid':
    'This verification link is no longer valid. Links can be used once and expire after 24 hours.',
  'verify.missing': 'The link is missing its verification code.',
  'verify.continue': 'Continue',

  'home.greeting': 'Hello, {name}',
  'home.unverifiedTitle': 'Email not verified',
  'home.unverifiedBody': 'Some features unlock once you verify your email. Check your inbox.',
  'home.practiceEmpty': 'No exams yet',
  'home.practiceEmptyBody':
    'The exam section has not been built. Exams will appear here once added.',
  'home.historyEmpty': 'No attempts yet',
  'home.historyEmptyBody': 'Results from your attempts will appear here.',

  'dict.eyebrow': 'Dictation',
  'dict.sentenceOf': 'Sentence {index} of {total}',
  'dict.replayable': 'Replay as often as you like',
  'dict.play': 'Play',
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
  'exam.questionsIn': 'Questions in part {number}',
  'exam.answeredOf': '{answered}/{total} answered',
  'exam.answerLabel': 'Your answer',
  'exam.maxWords': 'At most {count} words',
  'exam.saved': 'Saved',
  'exam.saving': 'Sending',
  'exam.notSentYet': 'Not sent yet',
  'exam.saveFailed': 'Sending failed',
  'exam.underFiveMinutes': 'under 5 minutes left',
  'exam.underOneMinute': 'under 1 minute left',
  'exam.clockKeepsRunning': 'The clock is held by the server and does not stop if you go offline.',
  'exam.expired': 'Time is up. Everything saved before the deadline is kept.',
  'exam.submit': 'Submit',
  'exam.submitting': 'Submitting…',
  'exam.gone': 'No such exam session',
  'exam.goneBody': 'This sitting no longer exists, or it does not belong to your account.',

  'exam.play': 'Play',
  'exam.pause': 'Pause',
  'exam.audioOnce': 'The audio plays once and cannot be rewound.',
  'exam.audioReplayable': 'Cannot be rewound.',
  'exam.audioSpent': 'The audio has finished.',
  'exam.audioFailed':
    'The audio could not be loaded. Check your connection and reopen this section.',
  'exam.transferNote': 'After the audio ends you have {minutes} minutes to copy your answers over.',
  'exam.words': '{count} words',
  'exam.minWords': 'At least {count} words',
  'exam.underMinWords': '{count} words short',
  'exam.record': 'Start recording',
  'exam.prepareThenRecord': 'Start preparation',
  'exam.preparing': 'Preparing',
  'exam.recording': 'Recording',
  'exam.stopRecording': 'Stop',
  'exam.uploading': 'Sending the recording…',
  'exam.recordingStored': 'Recording saved',
  'exam.uploadFailed': 'The recording could not be sent. It is still here — try again.',
  'exam.micDenied': 'Microphone permission was refused. Allow it and try again.',
  'exam.tryAgain': 'Try again',
  'exam.taskLabel': 'Task',
  'exam.essayLabel': 'Your answer',
  'exam.speakingLabel': 'Speaking part',
  'exam.recordingsLabel': 'Your answers',
  'exam.aiNotMarked': 'Writing and Speaking are not marked yet — no model is wired.',

  'exam.resultsEyebrow': 'Results',
  'exam.resultsLead': 'Per-skill bands for this sitting.',
  'exam.resultsExpired':
    'The sitting ran out of time. Everything saved before the deadline is still marked.',
  'exam.overall': 'Overall band',
  'exam.overallPending': 'An overall band needs all four skills.',
  'exam.rawOf': '{raw}/{max} correct',
  'exam.notMarked': 'Not marked',
  'exam.aiPending':
    'Writing and Speaking are AI marked and are not wired to a model yet, so both show a dash.',
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

  'dash.stat.sittings': 'Sittings',
  'dash.stat.skills': 'Skills attempted',
  'dash.stat.latest': 'Latest band',
  'dash.stat.none': 'None yet',

  'dash.recent.title': 'Recent sittings',
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
    'AI chat is in the first release, but it is not wired to a model yet. What it may answer, which provider serves it, and the token cost per question are all still being settled.',
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
