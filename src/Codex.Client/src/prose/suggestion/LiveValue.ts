/** The latest value of something that changes between renders but is read from long-lived editor callbacks. */
export class LiveValue<T> {
  private value: T;

  constructor(initial: T) {
    this.value = initial;
  }

  get(): T {
    return this.value;
  }

  set(next: T): void {
    this.value = next;
  }
}
