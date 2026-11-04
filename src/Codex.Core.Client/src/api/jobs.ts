import { api, type RequestOptions } from './client';

// C#: Codex.Contracts.Jobs. state is queued, running, done or failed; percent is 0 to 100.
export interface Job {
  id: string;
  bookId: string;
  kind: string;
  state: string;
  percent: number;
  message: string | null;
  resultUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

export const jobKey = (jobId: string) => ['job', jobId] as const;

export const getJob = (jobId: string, options?: RequestOptions) =>
  api.get<Job>(`/api/jobs/${jobId}`, options);
