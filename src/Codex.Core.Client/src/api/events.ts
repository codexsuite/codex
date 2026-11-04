// Book events as the realtime hub will deliver them (C#: Codex.Contracts.Events; "type" is the discriminator).
// Every event may name the tab whose request caused it (the X-Codex-Client header, api/client.ts); absent when the
// write came from elsewhere (MCP, a job). C#: BookEvent.ClientId.
interface BookEventBase {
  clientId?: string | null;
}

export interface ProseSaved extends BookEventBase {
  type: 'proseSaved';
  bookId: string;
  sceneId: string;
  version: number;
  origin: string;
}

export interface StructureChanged extends BookEventBase {
  type: 'structureChanged';
  bookId: string;
  kind: string;
}

export interface JobProgress extends BookEventBase {
  type: 'jobProgress';
  bookId: string;
  jobId: string;
  kind: string;
  percent: number;
  state: string;
  message: string | null;
}

export interface BookMetaChanged extends BookEventBase {
  type: 'bookMetaChanged';
  bookId: string;
  fields: string[];
}

// #80: this user's access to the book changed. role is their effective role afterwards; null when they lost access.
// Sent to the affected user's tabs only, never to the book's group.
export interface AccessChanged extends BookEventBase {
  type: 'accessChanged';
  bookId: string;
  userId: string;
  role: 'view' | 'comment' | 'write' | null;
}

export type BookEvent =
  ProseSaved | StructureChanged | JobProgress | BookMetaChanged | AccessChanged;
