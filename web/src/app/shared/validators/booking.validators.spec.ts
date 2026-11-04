import { FormArray, FormControl, FormGroup } from '@angular/forms';

import { differentPorts, minLines, uniqueContainerTypes } from './booking.validators';

describe('booking validators', () => {
  describe('differentPorts', () => {
    const group = (pol: number | null, pod: number | null) =>
      new FormGroup({ polId: new FormControl(pol), podId: new FormControl(pod) });

    it('flags the same port on both ends', () => {
      expect(differentPorts()(group(3, 3))).toEqual({ samePorts: true });
    });

    it('accepts two different ports', () => {
      expect(differentPorts()(group(3, 4))).toBeNull();
    });

    it('does not complain before a port is chosen', () => {
      expect(differentPorts()(group(null, null))).toBeNull();
      expect(differentPorts()(group(3, null))).toBeNull();
    });
  });

  describe('uniqueContainerTypes', () => {
    const lines = (...ids: (number | null)[]) =>
      new FormArray(ids.map(id => new FormGroup({ containerTypeId: new FormControl(id) })));

    it('flags a repeated container type', () => {
      expect(uniqueContainerTypes()(lines(1, 2, 1))).toEqual({ duplicateTypes: true });
    });

    it('accepts distinct types', () => {
      expect(uniqueContainerTypes()(lines(1, 2, 3))).toBeNull();
    });

    it('ignores lines with no type chosen yet', () => {
      expect(uniqueContainerTypes()(lines(null, null, 1))).toBeNull();
    });
  });

  describe('minLines', () => {
    it('flags an empty array, which Validators.minLength would let through', () => {
      expect(minLines()(new FormArray([]))).toEqual({ noLines: true });
    });

    it('accepts one or more lines', () => {
      expect(minLines()(new FormArray([new FormControl(1)]))).toBeNull();
      expect(minLines(2)(new FormArray([new FormControl(1)]))).toEqual({ noLines: true });
    });
  });
});
