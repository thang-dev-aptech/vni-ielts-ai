/**
 * Untrusted exam copy. React text nodes only — never HTML or Markdown.
 */
export function ContentText({
  as: Tag = 'div',
  variant,
  children,
  className,
}: {
  as?: 'div' | 'p' | 'span' | 'pre';
  variant: 'passage' | 'transcript' | 'prompt' | 'option' | 'answer';
  children: string;
  className?: string;
}) {
  const classes = ['cms-content-text', `is-${variant}`, className].filter(Boolean).join(' ');
  return <Tag className={classes}>{children}</Tag>;
}
