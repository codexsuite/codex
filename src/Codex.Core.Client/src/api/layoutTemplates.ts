import { api, type RequestOptions } from './client';
import type { LayoutState } from './layout';

// Template management (C#: TemplateEndpoints; LayoutTemplateVersionDto, LayoutTemplateSampleDto).
export interface LayoutTemplateVersion {
  templateId: string;
  versionId: string;
  slug: string;
  name: string;
  version: string;
  isUserTemplate: boolean;
  forkedFrom: string | null;
  changelog: string[];
}

/** A package's sample.md as html5 with its preview.css, and the first trim's page in inches (the picker thumbnail). */
export interface LayoutTemplateSample {
  html: string;
  css: string | null;
  trim: string;
  widthIn: number;
  heightIn: number;
  marginTopIn: number;
  marginBottomIn: number;
  marginInnerIn: number;
  marginOuterIn: number;
  columns: number;
}

const packageForm = (file: File) => {
  const form = new FormData();
  form.append('file', file);
  return form;
};

export const layoutTemplateSampleKey = (versionId: string) =>
  ['layout-template-sample', versionId] as const;

export const layoutTemplatesApi = {
  fork: (versionId: string, name?: string) =>
    api.post<LayoutTemplateVersion>(`/api/layout-template-versions/${versionId}/fork`, { name }),
  upload: (file: File) =>
    api.upload<LayoutTemplateVersion>('/api/layout-templates', packageForm(file)),
  uploadVersion: (templateId: string, file: File) =>
    api.upload<LayoutTemplateVersion>(
      `/api/layout-templates/${templateId}/versions`,
      packageForm(file),
    ),
  archive: (templateId: string) => api.post<void>(`/api/layout-templates/${templateId}/archive`),
  sample: (versionId: string, options?: RequestOptions) =>
    api.get<LayoutTemplateSample>(`/api/layout-template-versions/${versionId}/sample`, options),
  packageUrl: (versionId: string) => `/api/layout-template-versions/${versionId}/package`,
  upgrade: (bookId: string, templateVersionId?: string) =>
    api.post<LayoutState>(`/api/books/${bookId}/layout/upgrade`, { templateVersionId }),
};
