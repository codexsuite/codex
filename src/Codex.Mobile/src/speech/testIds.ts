// The Speech models screen's test ids (prefix mobile-speech-).
const screen = 'mobile-speech';
const model = (id: string) => `${screen}-model-${id}`;

export const speechTestIds = {
  screen: `${screen}-screen`,
  note: `${screen}-note`,
  error: `${screen}-error`,
  model,
  status: (id: string) => `${model(id)}-status`,
  progress: (id: string) => `${model(id)}-progress`,
  failure: (id: string) => `${model(id)}-failure`,
  download: (id: string) => `${model(id)}-download`,
  cancel: (id: string) => `${model(id)}-cancel`,
  use: (id: string) => `${model(id)}-use`,
  remove: (id: string) => `${model(id)}-delete`,
} as const;
