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
  'common.email': 'Email',
  'common.password': 'Mật khẩu',
  'common.displayName': 'Tên hiển thị',
  'common.notConnected': 'Không kết nối được tới máy chủ. Kiểm tra mạng rồi thử lại.',
  'common.unexpected': 'Có lỗi ngoài dự kiến. Vui lòng thử lại.',

  'signIn.title': 'Đăng nhập',
  'signIn.submit': 'Đăng nhập',
  'signIn.busy': 'Đang đăng nhập…',
  'signIn.noAccount': 'Chưa có tài khoản?',
  'signIn.invalid': 'Email hoặc mật khẩu không đúng.',
  'signIn.suspended': 'Tài khoản này đã bị khoá. Liên hệ hỗ trợ nếu bạn cho rằng đây là nhầm lẫn.',

  'signUp.title': 'Tạo tài khoản',
  'signUp.submit': 'Tạo tài khoản',
  'signUp.busy': 'Đang tạo tài khoản…',
  'signUp.haveAccount': 'Đã có tài khoản?',
  'signUp.passwordHint':
    'Ít nhất 12 ký tự. Không bắt buộc chữ hoa hay ký tự đặc biệt — độ dài quan trọng hơn.',
  'signUp.emailTaken': 'Email này đã được đăng ký.',
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
  'home.practiceTitle': 'Luyện tập',
  'home.practiceEmpty': 'Chưa có đề thi nào',
  'home.practiceEmptyBody': 'Phần thi chưa được xây dựng. Khi có đề, các đề sẽ xuất hiện tại đây.',
  'home.historyTitle': 'Lịch sử làm bài',
  'home.historyEmpty': 'Bạn chưa làm bài nào',
  'home.historyEmptyBody': 'Kết quả các lần làm bài sẽ hiện tại đây.',

  'profile.title': 'Hồ sơ',
  'profile.subtitle': 'Thông tin tài khoản của bạn',
  'profile.userId': 'Mã người dùng',
  'profile.emailVerified': 'Trạng thái email',
  'profile.verified': 'Đã xác minh',
  'profile.unverified': 'Chưa xác minh',
  'profile.permissions': 'Quyền',
  'profile.signOut': 'Đăng xuất',

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
  'common.email': 'Email',
  'common.password': 'Password',
  'common.displayName': 'Display name',
  'common.notConnected': 'Could not reach the server. Check your connection and try again.',
  'common.unexpected': 'Something went wrong. Please try again.',

  'signIn.title': 'Sign in',
  'signIn.submit': 'Sign in',
  'signIn.busy': 'Signing in…',
  'signIn.noAccount': 'No account yet?',
  'signIn.invalid': 'Email address or password is incorrect.',
  'signIn.suspended':
    'This account has been suspended. Contact support if you think this is a mistake.',

  'signUp.title': 'Create an account',
  'signUp.submit': 'Create account',
  'signUp.busy': 'Creating account…',
  'signUp.haveAccount': 'Already have an account?',
  'signUp.passwordHint':
    'At least 12 characters. No uppercase or symbol required — length matters more.',
  'signUp.emailTaken': 'That email address is already registered.',
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
  'home.practiceTitle': 'Practice',
  'home.practiceEmpty': 'No exams yet',
  'home.practiceEmptyBody':
    'The exam section has not been built. Exams will appear here once added.',
  'home.historyTitle': 'Your attempts',
  'home.historyEmpty': 'No attempts yet',
  'home.historyEmptyBody': 'Results from your attempts will appear here.',

  'profile.title': 'Profile',
  'profile.subtitle': 'Your account details',
  'profile.userId': 'User id',
  'profile.emailVerified': 'Email status',
  'profile.verified': 'Verified',
  'profile.unverified': 'Not verified',
  'profile.permissions': 'Permissions',
  'profile.signOut': 'Sign out',

  'notFound.title': 'Page not found',
  'notFound.body': 'The address you opened does not exist, or has moved.',
  'notFound.home': 'Go to home',

  'error.boundaryTitle': 'This page hit a problem',
  'error.boundaryBody':
    'Part of the app failed to render. You can reload; if it keeps happening, please tell us.',
  'error.reload': 'Reload the page',
};

export const STRINGS: Record<Locale, Record<StringKey, string>> = { vi, en };
