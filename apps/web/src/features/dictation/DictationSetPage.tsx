import { Link, useParams } from 'react-router-dom';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { DictationPractice } from './DictationPractice.js';
import '../../styles/landing.css';
import '../../styles/module-pages.css';
import '../../styles/dictation-page.css';

/**
 * One dictation set — the exercise.
 *
 * <b>Split from the library on 24/08.</b> `/dictation` rendered the exercise
 * inline against `sets[0]`, which is fine with one set in the fixture and
 * wrong the moment there are two: nothing to link to, nothing to bookmark, and
 * no answer to "which one am I doing".
 *
 * <b>Nothing but the exercise.</b> No education sections, no FAQ, no call to
 * action. Someone on this page has already chosen; the argument for dictation
 * belongs on the page they chose from. The only way out is the breadcrumb and
 * the link under the exercise, which is deliberate — this is a working
 * surface, not a place to browse from.
 *
 * <b>The title comes from the set, and the set arrives inside
 * `DictationPractice`.</b> So the tab is named "Nghe chép chính tả" until the
 * fetch lands rather than flashing a set title that might 404. A page title
 * that lies for 200ms is worse than one that is merely general.
 */
export function DictationSetPage() {
  const { setId } = useParams<{ setId: string }>();
  usePageTitle('Nghe chép chính tả');

  // The router only matches this route with a `setId`, so this is a type
  // narrowing rather than a real branch — but an empty string would fetch
  // `/api/v1/dictation/` and get the list back, which is a confusing way to
  // fail.
  if (setId === undefined || setId === '') return <Navigate />;

  return (
    <div className="dict-page">
      <Breadcrumb
        trail={[
          { label: 'Trang chủ', to: Paths.home },
          { label: 'Nghe chép chính tả', to: Paths.dictation },
          { label: 'Bài luyện' },
        ]}
      />

      <section className="section dict-set-body">
        <div className="container dict-set-wrap">
          <DictationPractice setId={setId} />

          <p className="dict-set-back">
            <Link to={Paths.dictation}>← Chọn bài nghe khác</Link>
          </p>
        </div>
      </section>
    </div>
  );
}

/** A missing id is a stale link, not an error. */
function Navigate() {
  return (
    <div className="dict-page">
      <section className="section">
        <div className="container dict-set-wrap">
          <div className="dict-gate">
            <h3>Không tìm thấy bài nghe</h3>
            <p>Đường dẫn này không chỉ tới bộ câu nào. Quay lại kho để chọn một bài.</p>
            <div className="dict-gate-actions">
              <Link className="btn btn-primary" to={Paths.dictation}>
                Về kho bài nghe
              </Link>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}
