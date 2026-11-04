import type { LayoutSettings } from '@codex/core/api/settings';
import { Row, SectionHeader } from '~/ui';
import { settingsTestIds } from './testIds';

/** The layout template, for reading only: template options, preflight and exports stay on desktop. */
export function LayoutGroup({ layout }: { layout: LayoutSettings }) {
  return (
    <>
      <SectionHeader title="Layout & print" />
      <Row
        testID={settingsTestIds.row('template')}
        label="Template"
        value={layout.template?.name ?? 'None'}
        detail={layout.template?.trimSize ?? undefined}
        chip="Desktop"
        disabled
      />
    </>
  );
}
