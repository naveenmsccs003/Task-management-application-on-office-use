/** Two-letter initials for avatars. */
export function initials(name: string | null | undefined): string {
  const parts = (name ?? '').split(/[\s._-]+/).filter(Boolean);
  return parts.slice(0, 2).map((p) => p[0].toUpperCase()).join('') || '?';
}

/** Triggers a browser download for a blob. */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
