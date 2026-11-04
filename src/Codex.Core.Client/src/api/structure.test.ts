import { describe, expect, it } from 'vitest';
import createLabelRequest from '../../../Codex.TestSupport/Fixtures/structure/create-label-request.json';
import labelRefs from '../../../Codex.TestSupport/Fixtures/structure/label-refs.json';
import labels from '../../../Codex.TestSupport/Fixtures/structure/labels.json';
import outline from '../../../Codex.TestSupport/Fixtures/structure/outline.json';
import outlineOpener from '../../../Codex.TestSupport/Fixtures/structure/outline-opener.json';
import patchLabelRequest from '../../../Codex.TestSupport/Fixtures/structure/patch-label-request.json';
import patchSceneRequest from '../../../Codex.TestSupport/Fixtures/structure/patch-scene-request.json';
import setSceneLabelsRequest from '../../../Codex.TestSupport/Fixtures/structure/set-scene-labels-request.json';
import type {
  CreateLabelRequest,
  Label,
  LabelRef,
  Outline,
  OutlineOpener,
  PatchLabelRequest,
  PatchSceneRequest,
  SetSceneLabelsRequest,
} from './structure';

// The same JSON files the C# StructureDtoFixtureTests round-trip. `satisfies` makes tsc fail if a type drifts.
const roundTrip = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

describe('structure contract fixtures', () => {
  it('OutlineDto round-trips', () => {
    const typed = outline satisfies Outline;
    expect(roundTrip(typed)).toEqual(outline);
  });

  it('OutlineOpenerDto round-trips', () => {
    const typed = outlineOpener satisfies OutlineOpener;
    expect(roundTrip(typed)).toEqual(outlineOpener);
  });

  it('LabelDto list round-trips', () => {
    const typed = labels satisfies Label[];
    expect(roundTrip(typed)).toEqual(labels);
  });

  it('LabelRefDto list round-trips', () => {
    const typed = labelRefs satisfies LabelRef[];
    expect(roundTrip(typed)).toEqual(labelRefs);
  });

  it('request bodies round-trip', () => {
    const patchScene = patchSceneRequest satisfies PatchSceneRequest;
    const setLabels = setSceneLabelsRequest satisfies SetSceneLabelsRequest;
    const create = createLabelRequest satisfies CreateLabelRequest;
    const patchLabel = patchLabelRequest satisfies PatchLabelRequest;
    expect(roundTrip(patchScene)).toEqual(patchSceneRequest);
    expect(roundTrip(setLabels)).toEqual(setSceneLabelsRequest);
    expect(roundTrip(create)).toEqual(createLabelRequest);
    expect(roundTrip(patchLabel)).toEqual(patchLabelRequest);
  });
});
