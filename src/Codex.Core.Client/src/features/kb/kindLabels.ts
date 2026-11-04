// Words for the kinds a scene can use an entry (design/README.md -> Used in tab).
const kindLabels: Record<string, string> = {
  mention: 'Mention',
  summary: 'Summary',
  linked: 'Linked',
  beat: 'Beat',
  beat_context: 'Beat context',
  timeline: 'Timeline',
  printed: 'Printed',
  text: 'Name in text',
};

export const kindLabel = (kind: string): string => kindLabels[kind] ?? kind;
