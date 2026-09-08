import type { MediaAsset } from '../../lib/media.js';

export function MediaRefField({
  label,
  value,
  kind,
  media,
  onChange,
}: {
  label: string;
  value: string | undefined;
  kind: 'audio' | 'image';
  media: MediaAsset[];
  onChange: (ref: string | undefined) => void;
}) {
  const choices = media.filter((asset) => asset.kind === kind && !asset.retired);

  return (
    <label className="cms-field">
      <span>{label}</span>
      <select
        value={value ?? ''}
        onChange={(event) => {
          const next = event.target.value;
          onChange(next === '' ? undefined : next);
        }}
      >
        <option value="">Không chọn</option>
        {choices.map((asset) => (
          <option key={asset.mediaId} value={`media/${asset.mediaId}`}>
            {asset.fileName}
          </option>
        ))}
      </select>
      <span className="cms-muted">Tham chiếu `media/&lt;id&gt;` — không dùng tên file làm khoá.</span>
    </label>
  );
}
