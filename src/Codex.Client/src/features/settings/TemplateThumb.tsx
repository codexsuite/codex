import { useQuery } from '@tanstack/react-query';
import { layoutTemplateSampleKey, layoutTemplatesApi } from '@codex/core/api/layoutTemplates';
import { TemplatePage } from './TemplatePage';
import { settingsTestIds } from './testIds';

const WIDTH = 44;

// The picker thumbnail: the version's sample.md in its own preview.css, first page only (TemplatePage). A version
// never changes, so its sample is fetched once; while it loads, or where the server cannot render (no pandoc), a blank
// page stands in.
export function TemplateThumb({
  slug,
  versionId,
  name,
}: {
  slug: string;
  versionId: string;
  name: string;
}) {
  const sample = useQuery({
    queryKey: layoutTemplateSampleKey(versionId),
    queryFn: ({ signal }) => layoutTemplatesApi.sample(versionId, { signal }),
    staleTime: Infinity,
    retry: false,
  });
  if (!sample.data) {
    return (
      <div
        data-testid={settingsTestIds.layoutTemplateThumb(slug)}
        data-state={sample.isError ? 'error' : 'loading'}
        className="h-[57px] w-[44px] shrink-0 rounded-sm bg-white shadow-sm ring-1 ring-black/10"
      />
    );
  }
  return (
    <TemplatePage
      testId={settingsTestIds.layoutTemplateThumb(slug)}
      html={sample.data.html}
      css={sample.data.css}
      page={sample.data}
      width={WIDTH}
      title={`${name} sample page`}
    />
  );
}
