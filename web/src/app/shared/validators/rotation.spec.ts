import { FormArray, FormControl, FormGroup } from '@angular/forms';

import { RotationCall, rotationValidator, validateRotation } from './rotation';

describe('rotation validation', () => {
  /** Calls two days apart, written as datetime-local strings. */
  const at = (day: number, hour = 8): string => `2026-12-${String(day).padStart(2, '0')}T${String(hour).padStart(2, '0')}:00`;

  const valid = (): RotationCall[] => [
    { portId: 1, eta: '', etd: at(1) },
    { portId: 2, eta: at(3), etd: at(3, 20) },
    { portId: 3, eta: at(6), etd: at(6, 20) },
    { portId: 4, eta: at(20), etd: '' },
  ];

  it('accepts a well-formed rotation, including a simple two-port one', () => {
    expect(validateRotation(valid())).toEqual([]);
    expect(validateRotation([valid()[0], valid()[3]])).toEqual([]);
  });

  it('needs at least two calls', () => {
    expect(validateRotation([valid()[0]])).toContain('A rotation needs at least two port calls.');
    expect(validateRotation([])).toContain('A rotation needs at least two port calls.');
  });

  it('rejects a port visited twice, naming the calls', () => {
    const calls = valid();
    calls[3].portId = 2;

    expect(validateRotation(calls)).toContain('A port can appear only once in a rotation (calls 2 and 4).');
  });

  it('requires a departure from the first port, an arrival at the last and both in between', () => {
    const calls = valid();
    calls[0].etd = '';
    calls[3].eta = '';
    calls[1].etd = '';

    const problems = validateRotation(calls);

    expect(problems).toContain('Call 1 (first port): ETD is required.');
    expect(problems).toContain('Call 4 (last port): ETA is required.');
    expect(problems).toContain('Call 2: both ETA and ETD are required for an intermediate port.');
  });

  it('rejects departing before arriving, but allows the same instant', () => {
    const calls = valid();
    calls[1] = { portId: 2, eta: at(3, 20), etd: at(3, 8) };
    expect(validateRotation(calls)).toContain('Call 2: ETD cannot be before ETA.');

    calls[1] = { portId: 2, eta: at(3), etd: at(3) };
    expect(validateRotation(calls)).toEqual([]);
  });

  it('rejects arriving before the previous port departs', () => {
    const calls = valid();
    calls[2] = { portId: 3, eta: at(2), etd: at(2, 12) };

    expect(validateRotation(calls)).toContain('Call 3: arrival is before call 2 departs.');
  });

  it('does not complain about ports that have not been chosen yet', () => {
    const calls = valid();
    calls[1].portId = null;
    calls[2].portId = null;

    expect(validateRotation(calls)).toEqual([]);
  });

  describe('rotationValidator', () => {
    const group = (call: RotationCall) =>
      new FormGroup({ portId: new FormControl(call.portId), eta: new FormControl(call.eta), etd: new FormControl(call.etd) });

    it('returns the problems under the rotation key', () => {
      const errors = rotationValidator()(new FormArray([group(valid()[0])]));

      expect(errors?.['rotation']).toContain('A rotation needs at least two port calls.');
    });

    it('passes a valid rotation', () => {
      expect(rotationValidator()(new FormArray(valid().map(group)))).toBeNull();
    });
  });
});
