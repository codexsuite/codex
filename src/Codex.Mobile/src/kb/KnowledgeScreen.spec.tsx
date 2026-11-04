import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { bookHeader, kbCategory } from '~/test-support/fakes';
import { renderApp } from '~/test-support/renderScreen';
import { seriesKb } from './test-support/kbWorld';
import { kbTestIds } from './testIds';

const KB = 'GET /api/books/book-1/kb';
const open = () => renderApp({ url: '/book/book-1/knowledge', api: { [KB]: seriesKb } });

describe('Knowledge list', () => {
  it('Knowledge_WhenLoaded_ShouldGroupEntriesByCategoryWithTheSeriesNoteOverTheSharedOnes', async () => {
    await open();

    expect(await screen.findByTestId(kbTestIds.row('maren'))).toBeOnTheScreen();
    expect(screen.getByTestId(kbTestIds.seriesNote)).toHaveTextContent(
      'From The Ashen Crown. Shared by every book in it; edits change it everywhere.',
    );
    expect(
      within(screen.getByTestId(kbTestIds.group('rules'))).getByText('The Oath'),
    ).toBeOnTheScreen();
    expect(
      within(screen.getByTestId(kbTestIds.group('people'))).getByText('Tobin'),
    ).toBeOnTheScreen();
    expect(screen.getByTestId(kbTestIds.row('maren'))).toHaveTextContent(/Always/);
    expect(screen.getByTestId(kbTestIds.row('tobin'))).not.toHaveTextContent(/Always/);
    expect(screen.getByTestId(kbTestIds.row('oath'))).toHaveTextContent(/4$/);
    expect(screen.getByTestId(kbTestIds.row('maren'))).toHaveTextContent(/Rules from the north\./);
  });

  it('Knowledge_ShouldOfferAllEachCategoryWithItsCountAndUnusedButNoVoiceNotes', async () => {
    await open();
    await screen.findByTestId(kbTestIds.strip);

    expect(screen.getByTestId(kbTestIds.chip('all'))).toHaveTextContent('All4');
    expect(screen.getByTestId(kbTestIds.chip('people'))).toHaveTextContent('Characters2');
    expect(screen.getByTestId(kbTestIds.chip('unused'))).toHaveTextContent('Unused1');
    expect(screen.queryByText(/voice notes/i)).toBeNull();
  });

  it('Knowledge_WhenACategoryChipIsPressed_ShouldListOnlyThatCategory', async () => {
    await open();
    await fireEvent.press(await screen.findByTestId(kbTestIds.chip('places')));

    expect(screen.getByTestId(kbTestIds.row('hall'))).toBeOnTheScreen();
    expect(screen.queryByTestId(kbTestIds.row('maren'))).toBeNull();
    expect(screen.queryByTestId(kbTestIds.seriesNote)).toBeNull();
    expect(screen.getByTestId(kbTestIds.chip('places')).props.accessibilityState.selected).toBe(
      true,
    );
  });

  it('Knowledge_WhenUnusedIsPressed_ShouldListEntriesNoSceneUses', async () => {
    await open();
    await fireEvent.press(await screen.findByTestId(kbTestIds.chip('unused')));

    expect(screen.getByTestId(kbTestIds.row('tobin'))).toBeOnTheScreen();
    expect(screen.queryByTestId(kbTestIds.row('maren'))).toBeNull();
  });

  it('Knowledge_WhenSearching_ShouldMatchAliasesAndSayWhenNothingMatches', async () => {
    await open();
    await fireEvent.changeText(await screen.findByTestId(kbTestIds.search), 'queen');

    expect(screen.getByTestId(kbTestIds.row('maren'))).toBeOnTheScreen();
    expect(screen.queryByTestId(kbTestIds.row('tobin'))).toBeNull();

    await fireEvent.changeText(screen.getByTestId(kbTestIds.search), 'zzz');

    expect(screen.getByTestId(kbTestIds.noMatches)).toBeOnTheScreen();
  });

  it('Knowledge_WhenARowIsPressed_ShouldOpenTheEntryWithTheTabStillSelected', async () => {
    const app = await open();
    await fireEvent.press(await screen.findByTestId(kbTestIds.row('maren')));

    expect(await screen.findByTestId(kbTestIds.entryScreen)).toBeOnTheScreen();
    expect(app.getPathname()).toBe('/book/book-1/knowledge/maren');
  });

  it('Knowledge_WhenTheBookHasNoEntries_ShouldShowTheEmptyStateWithNewEntry', async () => {
    await renderApp({
      url: '/book/book-1/knowledge',
      api: { [KB]: { categories: [kbCategory()], entries: [] } },
    });

    expect(await screen.findByTestId(kbTestIds.empty)).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId(`${kbTestIds.empty}-action`));

    expect(screen.getByTestId(kbTestIds.newForm)).toBeOnTheScreen();
  });

  it('Knowledge_WhenLoadingFails_ShouldShowTheErrorAndRetryReloads', async () => {
    let fail = true;
    await renderApp({
      url: '/book/book-1/knowledge',
      api: {
        [KB]: () =>
          fail ? { status: 500, body: { error: 'Boom', code: 'internal_error' } } : seriesKb,
      },
    });

    expect(await screen.findByTestId(kbTestIds.error)).toHaveTextContent('Boom');

    fail = false;
    await fireEvent.press(screen.getByTestId(`${kbTestIds.error}-retry`));

    expect(await screen.findByTestId(kbTestIds.row('maren'))).toBeOnTheScreen();
  });

  it('Knowledge_WhenPulledToRefresh_ShouldReloadTheList', async () => {
    const app = await open();
    await screen.findByTestId(kbTestIds.row('maren'));
    const before = app.api.callsTo(KB).length;

    const { refreshControl } = screen.getByTestId(kbTestIds.list).props;
    await act(async () => refreshControl.props.onRefresh());

    await waitFor(() => expect(app.api.callsTo(KB).length).toBe(before + 1));
  });
});

describe('Knowledge list: new entry', () => {
  const created = { ...seriesKb.entries[1], id: 'new-1', name: 'Ilse' };

  it('NewEntry_WhenCreated_ShouldPostNameAndCategoryThenOpenTheEntry', async () => {
    const app = await renderApp({
      url: '/book/book-1/knowledge',
      api: {
        [KB]: seriesKb,
        'POST /api/books/book-1/kb/entries': { id: 'new-1', name: 'Ilse' },
        'GET /api/kb/entries/new-1': {
          ...created,
          categoryName: 'Places',
          usedIn: [],
          aliases: [],
          summary: '',
        },
      },
    });
    await fireEvent.press(await screen.findByTestId(kbTestIds.add));
    await fireEvent.changeText(screen.getByTestId(kbTestIds.newName), ' Ilse ');
    await fireEvent.press(screen.getByTestId(kbTestIds.newCategory('places')));
    await fireEvent.press(screen.getByTestId(kbTestIds.newCreate));

    await waitFor(() => expect(app.getPathname()).toBe('/book/book-1/knowledge/new-1'));
    expect(app.api.callsTo('POST /api/books/book-1/kb/entries')[0].body).toEqual({
      categoryId: 'places',
      name: 'Ilse',
    });
  });

  it('NewEntry_FromACategorysPlus_ShouldStartInThatCategory', async () => {
    const app = await open();
    await fireEvent.press(await screen.findByTestId(kbTestIds.groupAdd('places')));
    await fireEvent.changeText(screen.getByTestId(kbTestIds.newName), 'Ilse');
    app.api.respond('POST /api/books/book-1/kb/entries', { id: 'new-1', name: 'Ilse' });
    await fireEvent.press(screen.getByTestId(kbTestIds.newCreate));

    await waitFor(() =>
      expect(app.api.callsTo('POST /api/books/book-1/kb/entries')[0].body).toEqual({
        categoryId: 'places',
        name: 'Ilse',
      }),
    );
  });

  it('NewEntry_WithoutAName_ShouldSayAnEntryNeedsOneAndSendNothing', async () => {
    const app = await open();
    await fireEvent.press(await screen.findByTestId(kbTestIds.add));
    await fireEvent.press(screen.getByTestId(kbTestIds.newCreate));

    expect(screen.getByTestId(kbTestIds.newError)).toHaveTextContent('An entry needs a name.');
    expect(app.api.callsTo('POST /api/books/book-1/kb/entries')).toHaveLength(0);
  });

  it('NewEntry_WhenTheNameIsTaken_ShouldShowTheServersMessageAndOfferCreateAnyway', async () => {
    const app = await open();
    app.api.respond('POST /api/books/book-1/kb/entries', {
      status: 409,
      body: { error: 'Maren already exists.', code: 'conflict' },
    });
    await fireEvent.press(await screen.findByTestId(kbTestIds.add));
    await fireEvent.changeText(screen.getByTestId(kbTestIds.newName), 'Maren');
    await fireEvent.press(screen.getByTestId(kbTestIds.newCreate));

    expect(await screen.findByTestId(kbTestIds.newError)).toHaveTextContent(
      'Maren already exists.',
    );
    expect(screen.getByTestId(kbTestIds.newCreate)).toHaveTextContent('Create anyway');

    app.api.respond('POST /api/books/book-1/kb/entries', { id: 'new-1', name: 'Maren' });
    await fireEvent.press(screen.getByTestId(kbTestIds.newCreate));

    await waitFor(() =>
      expect(app.api.callsTo('POST /api/books/book-1/kb/entries')[1].body).toEqual({
        categoryId: 'rules',
        name: 'Maren',
        allowDuplicate: true,
      }),
    );
  });

  it('NewEntry_WhenCancelled_ShouldCloseTheForm', async () => {
    await open();
    await fireEvent.press(await screen.findByTestId(kbTestIds.add));
    await fireEvent.press(screen.getByTestId(kbTestIds.newCancel));

    expect(screen.queryByTestId(kbTestIds.newForm)).toBeNull();
  });
});

describe('Knowledge list: a reader', () => {
  const view = { 'GET /api/books/book-1': bookHeader({ id: 'book-1', role: 'view' }) };

  it('Knowledge_ForAViewUser_ShouldListEntriesWithNoPlusAnywhere', async () => {
    await renderApp({ url: '/book/book-1/knowledge', api: { [KB]: seriesKb, ...view } });

    expect(await screen.findByTestId(kbTestIds.row('maren'))).toBeOnTheScreen();
    await waitFor(() => expect(screen.queryByTestId(kbTestIds.add)).toBeNull());
    expect(screen.queryByTestId(kbTestIds.groupAdd('people'))).toBeNull();
  });

  it('Knowledge_ForAViewUserWithNoEntries_ShouldOfferNoNewEntry', async () => {
    await renderApp({
      url: '/book/book-1/knowledge',
      api: { [KB]: { categories: [kbCategory()], entries: [] }, ...view },
    });

    expect(await screen.findByTestId(kbTestIds.empty)).toBeOnTheScreen();
    expect(screen.queryByTestId(`${kbTestIds.empty}-action`)).toBeNull();
  });
});

describe('Knowledge list: empty categories', () => {
  it('Knowledge_UnderAll_ShouldKeepACategoryWithNoEntriesAndItsPlus', async () => {
    await renderApp({
      url: '/book/book-1/knowledge',
      api: {
        [KB]: {
          categories: [
            ...seriesKb.categories,
            kbCategory({ id: 'lore', name: 'Lore', position: 'd0' }),
          ],
          entries: seriesKb.entries,
        },
      },
    });

    expect(await screen.findByTestId(kbTestIds.group('lore'))).toBeOnTheScreen();
    expect(screen.getByTestId(kbTestIds.groupAdd('lore'))).toBeOnTheScreen();
  });
});
