import { expect, it } from 'vitest';
import {
  objectFitContainBox,
  parseObjectPositionFractions,
  trivialImageContentBox,
} from '../imageContentBox.js';

it('objectFitContainBox parks surplus height at the bottom under center-top', () => {
  /*
   * Wide intrinsic image inside a square element: contain letterboxes top/bottom
   * free space. `object-position: center top` (Listening media column) must put
   * the painted strip at y=0, not vertically centered — that is the whole
   * reason this helper exists rather than assuming 50%/50%.
   *
   * Element 200×200, image 400×100 → scale 0.5 → painted 200×50, freeY 150.
   */
  const box = objectFitContainBox(200, 200, 400, 100, { x: 0.5, y: 0 });

  expect(box).toEqual({ x: 0, y: 0, width: 200, height: 50 });
});

it('objectFitContainBox centers horizontally when the image is narrower than the element', () => {
  // Element 300×100, square 100×100 → painted 100×100, freeX 200 → center at x=100.
  const box = objectFitContainBox(300, 100, 100, 100, { x: 0.5, y: 0 });

  expect(box).toEqual({ x: 100, y: 0, width: 100, height: 100 });
});

it('trivialImageContentBox is the element itself when there is no object-fit letterboxing', () => {
  expect(trivialImageContentBox(320, 180)).toEqual({ x: 0, y: 0, width: 320, height: 180 });
});

it('parseObjectPositionFractions reads center top as 50% / 0%', () => {
  expect(parseObjectPositionFractions('center top')).toEqual({ x: 0.5, y: 0 });
  expect(parseObjectPositionFractions('50% 0%')).toEqual({ x: 0.5, y: 0 });
});
