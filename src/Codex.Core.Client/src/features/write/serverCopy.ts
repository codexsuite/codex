// What the server is known to hold for one scene (the loaded copy, then each save's), and the save in flight.
// A mutable record kept outside React state, because a save updates it from an async callback.
export class ServerCopy {
  /** The save being sent right now, if any; block operations wait for it. */
  pending: Promise<unknown> | null = null;

  /** Records the save being sent, and clears it when that save ends. */
  track(sending: Promise<unknown>) {
    this.pending = sending;
    const clear = () => {
      if (this.pending === sending) this.pending = null;
    };
    sending.then(clear, clear);
  }

  constructor(
    private bodyText: string,
    private versionNumber: number,
  ) {}

  get body() {
    return this.bodyText;
  }

  get version() {
    return this.versionNumber;
  }

  is(body: string, version: number) {
    return this.bodyText === body && this.versionNumber === version;
  }

  set(body: string, version: number) {
    this.bodyText = body;
    this.versionNumber = version;
  }
}
