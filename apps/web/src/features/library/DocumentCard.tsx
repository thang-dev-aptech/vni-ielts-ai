import { formatDate } from '../../lib/dates.js';
import { Contact } from '../landing/contact.js';
import { SKILL_LABELS, TYPE_LABELS, type LibraryDocument } from './documents.js';

/**
 * One document in the library list.
 *
 * <b>Horizontal card, not a grid tile.</b> A library entry is chosen by
 * comparing title, band, format and length — columns that line up across rows
 * beat a three-column grid that truncates the metadata that decides the click.
 *
 * <b>The thumbnail is a document preview, not a photo.</b> No file has a real
 * cover yet, so every card renders a format-coloured stand-in with the same
 * aspect ratio. When the CMS ships a cover, swap the stand-in for an `<img>`
 * with the same frame — the layout does not move.
 *
 * <b>Badge colour is never the only signal.</b> "Mới", "Đã cập nhật" and
 * "Độc quyền" also appear as text in the accessible name of the card region.
 */
export function DocumentCard({ doc }: { doc: LibraryDocument }) {
  const stateBits = [
    doc.isNew ? 'Mới' : null,
    doc.isUpdated ? 'Đã cập nhật' : null,
    doc.access === 'premium' ? 'Độc quyền' : null,
  ].filter(Boolean);

  return (
    <article
      className={`res-card${doc.access === 'premium' ? ' is-premium' : ''}${doc.isFeatured ? ' is-featured' : ''}`}
      aria-label={[doc.title, ...stateBits].join(' · ')}
    >
      <div
        className={`res-thumb is-${doc.format.toLowerCase()} is-skill-${doc.skill}`}
        aria-hidden="true"
      >
        <span className="res-thumb-format">{doc.format}</span>
        <span className="res-thumb-skill">{SKILL_LABELS[doc.skill]}</span>
      </div>

      <div className="res-card-body">
        <div className="res-card-badges">
          <span className={`res-badge is-skill-${doc.skill}`}>{doc.category}</span>
          {doc.isNew && <span className="res-badge is-new">Mới</span>}
          {doc.isUpdated && <span className="res-badge is-updated">Đã cập nhật</span>}
          {doc.access === 'premium' && <span className="res-badge is-member">Độc quyền</span>}
        </div>

        <h3 className="res-card-title">{doc.title}</h3>
        <p className="res-card-desc">{doc.description}</p>

        <ul className="res-card-meta">
          {doc.targetBand !== undefined && <li>Band {doc.targetBand}</li>}
          <li>{doc.format}</li>
          <li>{TYPE_LABELS[doc.type]}</li>
          {doc.pageCount !== undefined && <li>{doc.pageCount} trang</li>}
          <li>Cập nhật {formatDate(doc.updatedAt)}</li>
        </ul>

        <div className="res-card-actions">
          {doc.access === 'premium' ? (
            <a className="btn btn-secondary btn-small" href={Contact.phoneHref}>
              Liên hệ nhận tài liệu
            </a>
          ) : doc.fileUrl ? (
            <>
              <a
                className="btn btn-primary btn-small"
                href={doc.fileUrl}
                target="_blank"
                rel="noreferrer"
              >
                Xem tài liệu
              </a>
              <a className="btn btn-secondary btn-small" href={doc.fileUrl} download>
                Tải xuống
              </a>
            </>
          ) : (
            <span className="res-pending">Sắp có — tài liệu đang được xuất bản</span>
          )}
        </div>
      </div>
    </article>
  );
}

/**
 * The highlighted pick at the top of the list.
 *
 * Renders only when the catalogue marks one. Absent on purpose when the
 * library is still thin — an empty featured slot is worse than no slot.
 */
export function FeaturedResource({ doc }: { doc: LibraryDocument }) {
  return (
    <article className="res-featured" aria-labelledby={`featured-${doc.id}`}>
      <div
        className={`res-featured-thumb is-${doc.format.toLowerCase()} is-skill-${doc.skill}`}
        aria-hidden="true"
      >
        <span className="res-thumb-format">{doc.format}</span>
      </div>
      <div className="res-featured-body">
        <p className="res-featured-eyebrow">Tài liệu nổi bật</p>
        <h3 id={`featured-${doc.id}`} className="res-featured-title">
          {doc.title}
        </h3>
        <p className="res-featured-desc">{doc.description}</p>
        <ul className="res-card-meta">
          {doc.targetBand !== undefined && <li>Band {doc.targetBand}</li>}
          <li>{doc.format}</li>
          {doc.pageCount !== undefined && <li>{doc.pageCount} trang</li>}
        </ul>
        <div className="res-card-actions">
          {doc.access === 'premium' ? (
            <a className="btn btn-secondary btn-small" href={Contact.phoneHref}>
              Liên hệ nhận tài liệu
            </a>
          ) : doc.fileUrl ? (
            <a
              className="btn btn-primary btn-small"
              href={doc.fileUrl}
              target="_blank"
              rel="noreferrer"
            >
              Xem tài liệu
            </a>
          ) : (
            <span className="res-pending">Sắp có</span>
          )}
        </div>
      </div>
    </article>
  );
}
