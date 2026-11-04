import { Redirect } from 'expo-router';
import { planLink } from '~/links';
import { useBookId } from '~/book/useBookId';

// /book/<id> on its own (a deep link) opens the book on Plan.
export default function BookIndex() {
  return <Redirect href={planLink(useBookId())} />;
}
