import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';

/** Saves files the API returned and turns failed downloads into readable messages. */
@Injectable({ providedIn: 'root' })
export class FileDownloadService {
  /** Saves the response body as a file, named by the server's Content-Disposition header (or <paramref name="fallbackName"/>). */
  save(response: HttpResponse<Blob>, fallbackName: string): void {
    const blob = response.body;
    if (!blob) {
      return;
    }

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = FileDownloadService.fileName(response.headers.get('Content-Disposition')) ?? fallbackName;
    link.rel = 'noopener';
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Give the browser a moment to start the download before the URL is released.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  /**
   * The file name from a Content-Disposition header, preferring the RFC 5987 form (`filename*=UTF-8''...`)
   * over the plain one. Returns null when there is none.
   */
  static fileName(header: string | null): string | null {
    if (!header) {
      return null;
    }

    const extended = /filename\*\s*=\s*([^']*)'[^']*'([^;]+)/i.exec(header);
    if (extended) {
      try {
        return decodeURIComponent(extended[2].trim());
      } catch {
        // Fall through to the plain form.
      }
    }

    const plain = /filename\s*=\s*(?:"([^"]+)"|([^;]+))/i.exec(header);
    const name = plain ? (plain[1] ?? plain[2]).trim() : null;
    return name ? name.replace(/[\\/]/g, '_') : null;
  }

  /**
   * A message for a failed download. The error body arrives as a Blob (because the request asked for a file), so it is
   * read as text and, if it is a problem document, its detail or validation messages are used.
   */
  async problemMessage(error: unknown, fallback: string): Promise<string> {
    if (!(error instanceof HttpErrorResponse)) {
      return fallback;
    }

    try {
      const body = error.error instanceof Blob ? JSON.parse(await error.error.text()) : error.error;
      if (body?.errors) {
        return (Object.values(body.errors) as string[][]).flat().join(' ');
      }
      return body?.detail ?? body?.title ?? fallback;
    } catch {
      return fallback;
    }
  }
}
