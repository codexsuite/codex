import { getSchema } from '@tiptap/react';
import { proseExtensions, summaryExtensions } from './extensions';

export type ProseVariant = 'prose' | 'summary';

export const proseSchema = getSchema(proseExtensions());
export const summarySchema = getSchema(summaryExtensions());

export function schemaFor(variant: ProseVariant) {
  return variant === 'prose' ? proseSchema : summarySchema;
}
