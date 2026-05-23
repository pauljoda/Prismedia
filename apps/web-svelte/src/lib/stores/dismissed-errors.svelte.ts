const STORAGE_KEY = "prismedia:dismissed-error-fingerprints";

class DismissedErrorsStore {
  #list = $state<string[]>([]);

  init() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      this.#list = raw ? (JSON.parse(raw) as string[]) : [];
    } catch {
      this.#list = [];
    }
  }

  isDismissed(fp: string): boolean {
    return this.#list.includes(fp);
  }

  get count(): number {
    return this.#list.length;
  }

  dismiss(fp: string) {
    if (!this.#list.includes(fp)) {
      this.#list = [...this.#list, fp];
      this.#save();
    }
  }

  restore(fp: string) {
    this.#list = this.#list.filter((f) => f !== fp);
    this.#save();
  }

  clearAll() {
    this.#list = [];
    this.#save();
  }

  #save() {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.#list));
    } catch {}
  }
}

export const dismissedErrors = new DismissedErrorsStore();
