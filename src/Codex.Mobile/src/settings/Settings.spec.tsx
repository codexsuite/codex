import { act, fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { bookHeader } from '~/test-support/fakes';
import { bookSettings } from '~/test-support/fakes/settings';
import type { FakeRequest } from '~/test-support/fakeApi';
import { renderApp } from '~/test-support/renderScreen';
import { shellTestIds } from '~/ui';
import { layoutSettings, publicationSettings } from './test-support/fakes';
import { settingsTestIds as ids } from './testIds';

const BOOK = '/api/books/book-1';
const sheetId = ids.sheet;

function api(extra: Record<string, unknown> = {}) {
  return {
    [`GET ${BOOK}/publication-settings`]: publicationSettings(),
    [`GET ${BOOK}/settings/layout`]: layoutSettings(),
    [`PATCH ${BOOK}/settings`]: (request: FakeRequest) =>
      bookSettings({ id: 'book-1', ...(request.body as object) }),
    ...extra,
  };
}

async function open(extra: Record<string, unknown> = {}) {
  const app = await renderApp({ url: '/book/book-1/settings', api: api(extra) });
  await screen.findByTestId(ids.row('title'));
  return app;
}

const row = (name: string) => screen.getByTestId(ids.row(name));
const value = (name: string) => screen.getByTestId(`${ids.row(name)}-value`);
const chip = (name: string) => screen.queryByTestId(`${ids.row(name)}-chip`);
const asRole = (role: string) => ({ 'GET /api/books/book-1': bookHeader({ role }) });

describe('Settings tab', () => {
  it('Settings_WhenLoaded_ShouldShowTheGroupsInDesktopOrderWithTheirValues', async () => {
    await open();
    await screen.findByTestId(ids.row('template'));

    const headers = screen.getAllByRole('header').map((h) => h.props.children);
    expect(headers).toEqual(
      expect.arrayContaining([
        'Book',
        'Series',
        'Writing',
        'Citations & permissions',
        'Layout & print',
        'Archive & export',
      ]),
    );
    const order = [
      'Book',
      'Series',
      'Writing',
      'Citations & permissions',
      'Layout & print',
      'Archive & export',
    ];
    expect(headers.filter((h) => order.includes(h))).toEqual(order);
    expect(value('title')).toHaveTextContent('The Empty Seat');
    expect(value('author')).toHaveTextContent('JD');
    expect(value('status')).toHaveTextContent('Writing');
    expect(value('target')).toHaveTextContent('90,000');
    expect(value('series')).toHaveTextContent('Forsaken Gods');
    expect(value('number')).toHaveTextContent('Book 003');
    expect(value('citation-style')).toHaveTextContent('Chicago');
    expect(value('quote-limit')).toHaveTextContent('300 words prose');
    expect(value('publisher')).toHaveTextContent('Ashfall Press');
    expect(value('formats')).toHaveTextContent('Print, Ebook');
    expect(value('template')).toHaveTextContent('Novel 6×9');
    expect(screen.getByTestId(ids.seriesFootnote)).toHaveTextContent(/comes from Forsaken Gods/);
  });

  it('Settings_ForValuesFromTheSeries_ShouldCarryTheSeriesChip', async () => {
    await open({
      [`GET ${BOOK}/publication-settings`]: publicationSettings({
        style: { value: 'apa', inherited: false },
      }),
    });
    await screen.findByTestId(ids.row('publisher'));

    expect(chip('publisher')).toHaveTextContent('Series');
    expect(chip('quote-limit')).toHaveTextContent('Series');
    expect(chip('citation-style')).toBeNull();
  });

  it('Settings_WhenTheSettingsCannotBeLoaded_ShouldShowTheErrorAndRetry', async () => {
    let fail = true;
    await renderApp({
      url: '/book/book-1/settings',
      api: api({
        [`GET ${BOOK}/settings`]: () =>
          fail
            ? { status: 500, body: { error: 'The server is unwell.', code: 'internal_error' } }
            : bookSettings({ id: 'book-1' }),
      }),
    });
    expect(await screen.findByTestId(ids.error)).toHaveTextContent('The server is unwell.');

    fail = false;
    await fireEvent.press(screen.getByTestId(`${ids.error}-retry`));

    expect(await screen.findByTestId(ids.row('title'))).toBeOnTheScreen();
  });

  describe('Book', () => {
    it('Title_WhenEditedAndSaved_ShouldPatchOnlyTheTitleAndShowItAndCloseTheSheet', async () => {
      const app = await open();

      await fireEvent.press(row('title'));
      await fireEvent.changeText(screen.getByTestId(ids.field), 'Hollow Throne');
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() => expect(value('title')).toHaveTextContent('Hollow Throne'));
      expect(app.api.callsTo(`PATCH ${BOOK}/settings`).map((c) => c.body)).toEqual([
        { title: 'Hollow Throne' },
      ]);
      expect(screen.queryByTestId(sheetId)).toBeNull();
    });

    it('Title_WhenBlank_ShouldHoldSaveAndSayWhy', async () => {
      const app = await open();

      await fireEvent.press(row('title'));
      await fireEvent.changeText(screen.getByTestId(ids.field), '   ');

      expect(screen.getByTestId(`${ids.field}-error`)).toHaveTextContent('A book needs a title.');
      await userEvent.press(screen.getByTestId(`${sheetId}-primary`));
      expect(app.api.callsTo(`PATCH ${BOOK}/settings`)).toHaveLength(0);
    });

    it('Title_WhenTheServerRefuses_ShouldKeepTheSheetOpenWithItsMessage', async () => {
      await open({
        [`PATCH ${BOOK}/settings`]: {
          status: 400,
          body: { error: 'That title is already used in this series.', code: 'validation' },
        },
      });

      await fireEvent.press(row('title'));
      await fireEvent.changeText(screen.getByTestId(ids.field), 'Northbound');
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      expect(await screen.findByTestId(ids.sheetError)).toHaveTextContent(
        'That title is already used in this series.',
      );
      expect(screen.getByTestId(sheetId)).toBeOnTheScreen();
      expect(value('title')).toHaveTextContent('The Empty Seat');
    });

    it('Sheet_WhenCancelled_ShouldSendNothing', async () => {
      const app = await open();

      await fireEvent.press(row('author'));
      await fireEvent.changeText(screen.getByTestId(ids.field), 'Someone Else');
      await fireEvent.press(screen.getByTestId(`${sheetId}-secondary`));

      expect(screen.queryByTestId(sheetId)).toBeNull();
      expect(app.api.callsTo(`PATCH ${BOOK}/settings`)).toHaveLength(0);
    });

    it('TargetWords_WhenNotANumber_ShouldHoldSave_AndWhenBlankShouldClearIt', async () => {
      const app = await open();
      await fireEvent.press(row('target'));

      await fireEvent.changeText(screen.getByTestId(ids.field), '12k');
      expect(screen.getByTestId(`${ids.field}-error`)).toHaveTextContent('Use digits only.');

      await fireEvent.changeText(screen.getByTestId(ids.field), '');
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() =>
        expect(app.api.callsTo(`PATCH ${BOOK}/settings`).map((c) => c.body)).toEqual([
          { targetWords: 0 },
        ]),
      );
    });

    it('Status_WhenAnotherIsChosen_ShouldPatchItAndNotOfferPublished', async () => {
      const app = await open();

      await fireEvent.press(row('status'));
      await userEvent.press(screen.getByTestId(ids.choice('published')));
      expect(screen.getByTestId(ids.choice('published'))).not.toBeSelected();
      expect(screen.getByTestId(ids.choiceNote)).toHaveTextContent(/do that on desktop/);
      await fireEvent.press(screen.getByTestId(ids.choice('revising')));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() => expect(value('status')).toHaveTextContent('Revising'));
      expect(app.api.callsTo(`PATCH ${BOOK}/settings`).map((c) => c.body)).toEqual([
        { status: 'revising' },
      ]);
    });

    it('Save_WhenNothingChanged_ShouldCloseWithoutARequest', async () => {
      const app = await open({
        [`GET ${BOOK}/publication-settings`]: publicationSettings(),
      });
      await screen.findByTestId(ids.row('publisher'));

      await fireEvent.press(row('title'));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));
      await waitFor(() => expect(screen.queryByTestId(sheetId)).toBeNull());
      await fireEvent.press(row('citation-style'));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));
      await waitFor(() => expect(screen.queryByTestId(sheetId)).toBeNull());
      await fireEvent.press(row('formats'));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));
      await waitFor(() => expect(screen.queryByTestId(sheetId)).toBeNull());

      expect(app.api.calls.filter((c) => c.method !== 'GET')).toHaveLength(0);
    });

    it('Status_ForAnEditor_ShouldSayPublishedIsTheOwnersToMark', async () => {
      await open(asRole('write'));
      await waitFor(() => expect(row('archive')).toHaveTextContent(/Only JD/));

      await fireEvent.press(row('status'));

      expect(screen.getByTestId(ids.choiceNote)).toHaveTextContent(
        'Only the owner can mark a book Published.',
      );
    });

    it('Cover_ShouldBeMarkedDesktopAndNotOpen', async () => {
      await open();

      expect(chip('cover')).toHaveTextContent('Desktop');
      await userEvent.press(row('cover'));
      expect(screen.queryByTestId(sheetId)).toBeNull();
    });
  });

  describe('Series', () => {
    it('Number_ForTheOwner_ShouldPutTheChosenNumber', async () => {
      const app = await open({
        [`GET ${BOOK}/settings`]: bookSettings({
          id: 'book-1',
          seriesOrder: [
            { id: 'a', title: 'One', number: 1, status: 'published' },
            { id: 'b', title: 'Two', number: 2, status: 'published' },
            { id: 'book-1', title: 'The Empty Seat', number: 3, status: 'writing' },
          ],
        }),
        [`PUT ${BOOK}/series-number`]: {
          bookId: 'book-1',
          seriesId: 'series-1',
          number: 2,
          previousNumber: 3,
          books: [],
        },
      });

      await fireEvent.press(row('number'));
      await fireEvent.press(screen.getByTestId(ids.choice('2')));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() =>
        expect(app.api.callsTo(`PUT ${BOOK}/series-number`).map((c) => c.body)).toEqual([
          { number: 2 },
        ]),
      );
    });

    it('Series_ShouldBeMarkedDesktopBecauseMovingABookIsADesktopStep', async () => {
      await open();

      expect(chip('series')).toHaveTextContent('Desktop');
      await userEvent.press(row('series'));
      expect(screen.queryByTestId(sheetId)).toBeNull();
    });
  });

  describe('Citations & permissions', () => {
    it('CitationStyle_WhenChosen_ShouldPatchThePublicationSettings', async () => {
      const app = await open({
        [`PATCH ${BOOK}/publication-settings`]: publicationSettings({
          style: { value: 'harvard', inherited: false },
        }),
      });
      await screen.findByTestId(ids.row('citation-style'));

      await fireEvent.press(row('citation-style'));
      await fireEvent.press(screen.getByTestId(ids.choice('harvard')));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() => expect(value('citation-style')).toHaveTextContent('Harvard'));
      expect(app.api.callsTo(`PATCH ${BOOK}/publication-settings`).map((c) => c.body)).toEqual([
        { style: 'harvard' },
      ]);
      expect(chip('citation-style')).toBeNull();
    });

    it('QuoteLimit_WhenZero_ShouldHoldSave', async () => {
      await open();
      await screen.findByTestId(ids.row('quote-limit'));

      await fireEvent.press(row('quote-limit'));
      await fireEvent.changeText(screen.getByTestId(ids.field), '0');

      expect(screen.getByTestId(`${ids.field}-error`)).toBeOnTheScreen();
    });

    it('Publisher_WhenBlank_ShouldSendAnEmptyStringMeaningSelfPublished', async () => {
      const app = await open({
        [`PATCH ${BOOK}/publication-settings`]: publicationSettings({
          publisher: { value: '', inherited: false },
        }),
      });
      await screen.findByTestId(ids.row('publisher'));

      await fireEvent.press(row('publisher'));
      await fireEvent.changeText(screen.getByTestId(ids.field), '');
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() => expect(value('publisher')).toHaveTextContent('Self-published'));
      expect(app.api.callsTo(`PATCH ${BOOK}/publication-settings`).map((c) => c.body)).toEqual([
        { publisher: '' },
      ]);
    });

    it('Formats_WhenToggled_ShouldSendTheCheckedFormats', async () => {
      const app = await open({
        [`PATCH ${BOOK}/publication-settings`]: publicationSettings({
          formats: { value: ['print', 'audio'], inherited: false },
        }),
      });
      await screen.findByTestId(ids.row('formats'));

      await fireEvent.press(row('formats'));
      await fireEvent.press(screen.getByTestId(ids.choice('ebook')));
      await fireEvent.press(screen.getByTestId(ids.choice('audio')));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() =>
        expect(app.api.callsTo(`PATCH ${BOOK}/publication-settings`).map((c) => c.body)).toEqual([
          { formats: ['print', 'audio'] },
        ]),
      );
    });

    it('Territory_WhenASuggestionIsTapped_ShouldFillTheField', async () => {
      await open();
      await screen.findByTestId(ids.row('territory'));

      await fireEvent.press(row('territory'));
      await fireEvent.press(screen.getByTestId(ids.choice('North America')));

      expect(screen.getByTestId(ids.field).props.value).toBe('North America');
    });

    it('UseSeriesValue_ForAnOverriddenValue_ShouldDeleteTheOverride', async () => {
      const app = await open({
        [`GET ${BOOK}/publication-settings`]: publicationSettings({
          quoteWordLimit: { value: 150, inherited: false },
        }),
        [`DELETE ${BOOK}/publication-settings/quoteWordLimit`]: publicationSettings(),
      });
      await screen.findByTestId(ids.row('quote-limit'));

      await fireEvent.press(row('quote-limit'));
      await fireEvent.press(screen.getByTestId(`${sheetId}-secondary`));

      await waitFor(() => expect(value('quote-limit')).toHaveTextContent('300 words prose'));
      expect(app.api.callsTo(`DELETE ${BOOK}/publication-settings/quoteWordLimit`)).toHaveLength(1);
      expect(chip('quote-limit')).toHaveTextContent('Series');
    });

    it('Citations_WhenTheyCannotBeLoaded_ShouldSayAndKeepTheRestOfThePageUsable', async () => {
      await open({
        [`GET ${BOOK}/publication-settings`]: {
          status: 500,
          body: { error: 'x', code: 'internal_error' },
        },
      });

      expect(await screen.findByTestId(ids.row('citations-loading'))).toHaveTextContent(
        "Couldn't load citations and permissions.",
      );
      expect(row('title')).toBeOnTheScreen();
    });
  });

  describe('Layout & print and export', () => {
    it('Template_ShouldShowItWithADesktopMarkerAndNotOpen', async () => {
      await open();
      await screen.findByTestId(ids.row('template'));

      expect(chip('template')).toHaveTextContent('Desktop');
      await userEvent.press(row('template'));
      expect(screen.queryByTestId(sheetId)).toBeNull();
    });

    it('DuplicateAndExport_ShouldBeDesktopOnly', async () => {
      await open();

      expect(chip('duplicate')).toHaveTextContent('Desktop');
      expect(chip('export-source')).toHaveTextContent('Desktop');
    });
  });

  describe('Archive', () => {
    it('Archive_WhenConfirmed_ShouldPostArchiveAndGoBackToTheLibrary', async () => {
      const app = await open({
        [`POST ${BOOK}/archive`]: bookSettings({ id: 'book-1', archived: true }),
      });

      await fireEvent.press(row('archive'));
      expect(screen.getByTestId(`${sheetId}-title`)).toHaveTextContent('Archive The Empty Seat?');
      expect(app.api.callsTo(`POST ${BOOK}/archive`)).toHaveLength(0);
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() => expect(app.getPathname()).toBe('/'));
      expect(app.api.callsTo(`POST ${BOOK}/archive`)).toHaveLength(1);
    });

    it('Archive_WhenCancelled_ShouldChangeNothing', async () => {
      const app = await open();

      await fireEvent.press(row('archive'));
      await fireEvent.press(screen.getByTestId(`${sheetId}-secondary`));

      expect(screen.queryByTestId(sheetId)).toBeNull();
      expect(app.api.callsTo(`POST ${BOOK}/archive`)).toHaveLength(0);
      expect(app.getPathname()).toBe('/book/book-1/settings');
    });

    it('Archive_WhenTheServerRefuses_ShouldStayOnTheSheetWithTheMessage', async () => {
      const app = await open({
        [`POST ${BOOK}/archive`]: {
          status: 403,
          body: { error: 'Only the owner can archive this book.', code: 'forbidden' },
        },
      });

      await fireEvent.press(row('archive'));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      expect(await screen.findByTestId(ids.sheetError)).toHaveTextContent(
        /Only the owner can archive/,
      );
      expect(app.getPathname()).toBe('/book/book-1/settings');
    });

    it('ArchivedBook_ShouldOfferRestoreInsteadOfArchive', async () => {
      const app = await open({
        [`GET ${BOOK}/settings`]: bookSettings({ id: 'book-1', archived: true }),
        [`POST ${BOOK}/restore`]: bookSettings({ id: 'book-1' }),
      });
      await screen.findByTestId(ids.row('restore'));
      expect(screen.queryByTestId(ids.row('archive'))).toBeNull();

      await fireEvent.press(row('restore'));
      await fireEvent.press(screen.getByTestId(`${sheetId}-primary`));

      await waitFor(() => expect(app.api.callsTo(`POST ${BOOK}/restore`)).toHaveLength(1));
    });
  });

  describe('roles', () => {
    it.each(['view', 'comment'])(
      'Settings_ForA_%s_User_ShouldShowValuesWithNoEdits',
      async (role) => {
        const app = await open(asRole(role));
        await screen.findByTestId(ids.readOnlyNote);
        await screen.findByTestId(ids.row('template'));

        await userEvent.press(row('title'));
        await userEvent.press(row('citation-style'));
        await userEvent.press(row('archive'));

        expect(screen.queryByTestId(sheetId)).toBeNull();
        expect(value('title')).toHaveTextContent('The Empty Seat');
        expect(app.api.calls.filter((c) => c.method !== 'GET')).toHaveLength(0);
      },
    );

    it('Settings_ForAnEditor_ShouldEditFieldsButNotArchiveOrNumberOrPublished', async () => {
      await open(asRole('write'));
      await screen.findByTestId(ids.row('template'));
      await waitFor(() =>
        expect(row('archive')).toHaveTextContent(/Only JD, the owner, can do this/),
      );

      await userEvent.press(row('archive'));
      await userEvent.press(row('number'));
      expect(screen.queryByTestId(sheetId)).toBeNull();

      await fireEvent.press(row('title'));
      expect(screen.getByTestId(sheetId)).toBeOnTheScreen();
    });

    it('Archive_BeforeTheRoleIsKnown_ShouldNotNameAnEmptyOwner', async () => {
      await open({
        'GET /api/books/book-1': { status: 500, body: { error: 'Down.', code: 'internal_error' } },
      });

      expect(row('archive')).not.toHaveTextContent(/Only ,/);
    });

    it('Settings_BeforeTheRoleIsKnown_ShouldNotOfferEdits', async () => {
      await renderApp({
        url: '/book/book-1/settings',
        api: api({
          'GET /api/books/book-1': {
            status: 500,
            body: { error: 'Down.', code: 'internal_error' },
          },
        }),
      });
      await screen.findByTestId(ids.row('title'));

      await userEvent.press(row('title'));

      expect(screen.queryByTestId(sheetId)).toBeNull();
    });
  });

  it('Settings_ShouldKeepTheBookBarAndSelectedTab', async () => {
    await open();

    expect(screen.getByTestId(shellTestIds.bookBar)).toBeOnTheScreen();
    expect(screen.getByTestId(shellTestIds.tab('settings'))).toBeSelected();
    await act(async () => undefined);
  });
});
