// Holds the Markdown a scene's editor produced and sends it to the app a moment after typing stops. The app autosaves
// what it is told, so a pause is enough; `flush` sends at once (blur, or before an action that reads the saved text).

export class ChangeSender {
  private readonly pending = new Map<
    string,
    { markdown: string; timer: ReturnType<typeof setTimeout> }
  >();

  constructor(
    private readonly send: (sceneId: string, markdown: string) => void,
    private readonly delayMs: number,
  ) {}

  schedule(sceneId: string, markdown: string): void {
    const waiting = this.pending.get(sceneId);
    if (waiting) clearTimeout(waiting.timer);
    const timer = setTimeout(() => this.flush(sceneId), this.delayMs);
    this.pending.set(sceneId, { markdown, timer });
  }

  /** Sends the scene's waiting change now; nothing waiting sends nothing. */
  flush(sceneId: string): void {
    const waiting = this.pending.get(sceneId);
    if (!waiting) return;
    clearTimeout(waiting.timer);
    this.pending.delete(sceneId);
    this.send(sceneId, waiting.markdown);
  }

  /** Sends every waiting change now (the page is being hidden). */
  flushAll(): void {
    for (const sceneId of [...this.pending.keys()]) this.flush(sceneId);
  }

  /** Sends every waiting change except the `skipped` scenes', which are dropped (their text is about to be replaced). */
  flushAllExcept(skipped: ReadonlySet<string>): void {
    for (const sceneId of [...this.pending.keys()]) {
      if (skipped.has(sceneId)) this.cancel(sceneId);
      else this.flush(sceneId);
    }
  }

  /** Drops the scene's waiting change: its text was replaced by the app's, so the typed text is not the truth any more. */
  cancel(sceneId: string): void {
    const waiting = this.pending.get(sceneId);
    if (!waiting) return;
    clearTimeout(waiting.timer);
    this.pending.delete(sceneId);
  }
}
