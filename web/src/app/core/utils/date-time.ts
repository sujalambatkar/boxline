/** Formats a date for `<input type="datetime-local">`: local wall-clock time as yyyy-MM-ddTHH:mm. */
export function toLocalInputValue(date: Date): string {
  const pad = (n: number): string => String(n).padStart(2, '0');
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

/** Converts a `datetime-local` value (the user's local time) to an ISO 8601 UTC string. */
export function localInputToIso(value: string): string {
  return new Date(value).toISOString();
}
