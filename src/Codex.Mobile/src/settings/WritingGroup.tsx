import { router } from 'expo-router';
import { styleGuideLink } from '~/links';
import { Row, SectionHeader } from '~/ui';
import type { GroupProps } from './BookGroup';
import { preview } from './fieldValues';
import { settingsTestIds } from './testIds';

/** The style guide: a long text, so the row pushes the editor. In a series book with no guide of its own it shows the series'. */
export function WritingGroup({ settings }: Pick<GroupProps, 'settings'>) {
  const own = (settings.styleGuide ?? '').trim() !== '';
  const shown = own ? settings.styleGuide : settings.seriesGuide;
  return (
    <>
      <SectionHeader title="Writing" />
      <Row
        testID={settingsTestIds.row('style-guide')}
        label="Style guide"
        chip={!own && settings.seriesGuide !== null ? 'Series' : undefined}
        value={shown === null || shown.trim() === '' ? 'Not set' : undefined}
        detail={preview(shown)}
        onPress={() => router.push(styleGuideLink(settings.id))}
      />
    </>
  );
}
