import { useRef } from 'react';
import { useParams } from 'react-router-dom';
import { useBookSettings, usePublicationSettings } from '../../api/settingsQueries';
import { SaveStatus } from '../../editing/SaveStatus';
import { ErrorState } from '../../shell/ErrorState';
import { SlotFill } from '../../shell/Slots';
import { useDeepLink } from '../../shell/useDeepLink';
import { ArchiveSection } from './ArchiveSection';
import { CitationsSection } from './CitationsSection';
import { LayoutSection } from './LayoutSection';
import { SettingsForm } from './SettingsForm';
import { ReadOnlyNote } from './ReadOnlyNote';
import { SettingsNav } from './SettingsNav';
import { SharingSection } from './SharingSection';
import { useActiveSection, useSectionTarget } from './useSectionNav';
import { settingsTestIds } from './testIds';
import { useSettingsAccess } from './useSettingsAccess';
import './settings.css';

// Book settings: a sticky section nav beside one column of cards (760px at most). Book, Series and Writing
// and Citations & permissions autosave; Layout & print is read-only; Sharing and Archive & export act at once.
// Below write access (#80) every control is disabled under a note; an editor finds the owner-only actions disabled.
export function SettingsPage() {
  const { bookId = '' } = useParams();
  const settings = useBookSettings(bookId);
  const publication = usePublicationSettings(bookId);
  const deepLink = useDeepLink();
  const section =
    deepLink.ok && deepLink.link.screen === 'settings' ? deepLink.link.section : undefined;
  const rootRef = useRef<HTMLDivElement>(null);
  const active = useActiveSection(rootRef, section);
  const access = useSettingsAccess(bookId);
  useSectionTarget(section, settings.isSuccess && (publication.isSuccess || publication.isError));

  return (
    <div
      ref={rootRef}
      data-testid={settingsTestIds.page}
      className="grid grid-cols-1 gap-3 px-4 pb-[120px] min-[860px]:grid-cols-[196px_minmax(0,760px)] min-[860px]:justify-center min-[860px]:gap-6 min-[860px]:pt-4"
    >
      <SettingsNav bookId={bookId} active={active} />
      <div className="grid min-w-0 content-start gap-4">
        {access.canEdit && (
          <SlotFill slot="saveStatus">
            <SaveStatus />
          </SlotFill>
        )}
        {settings.isError && (
          <ErrorState
            testId={settingsTestIds.loadError}
            title="The settings could not be loaded"
            detail={settings.error.message}
          />
        )}
        {settings.data && (
          // A disabled fieldset turns off every input, select and button inside it: the page as a viewer reads it.
          <fieldset
            disabled={!access.canEdit}
            className={`contents ${access.canEdit ? '' : 'settings-read-only'}`}
          >
            {access.known && !access.canEdit && (
              <ReadOnlyNote
                role={access.role}
                owner={access.ownerName}
                title={settings.data.title}
              />
            )}
            <SettingsForm key={`form-${bookId}`} settings={settings.data} />
            {publication.isError && (
              <ErrorState
                testId={settingsTestIds.publicationError}
                title="Citations & permissions could not be loaded"
                detail={publication.error.message}
              />
            )}
            {publication.data && (
              <CitationsSection
                key={`citations-${bookId}`}
                settings={publication.data}
                title={settings.data.title}
              />
            )}
            <LayoutSection bookId={bookId} />
            <SharingSection bookId={bookId} title={settings.data.title} />
            <ArchiveSection settings={settings.data} />
          </fieldset>
        )}
      </div>
    </div>
  );
}
