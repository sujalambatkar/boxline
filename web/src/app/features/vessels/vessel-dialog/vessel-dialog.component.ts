import { HttpErrorResponse } from '@angular/common/http';
import { Component, Inject } from '@angular/core';
import { NonNullableFormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { Vessel } from '../../../core/models/voyage.models';
import { VesselService } from '../../../core/services/vessel.service';
import { computeImoCheckDigit, imoValidator, normalizeImoNumber } from '../../../shared/validators/imo';
import { notBlank } from '../../../shared/validators/trimmed-pattern';

@Component({
  selector: 'app-vessel-dialog',
  templateUrl: './vessel-dialog.component.html',
  styleUrls: ['./vessel-dialog.component.scss'],
})
export class VesselDialogComponent {
  readonly isEdit: boolean;

  readonly form = this.fb.group({
    name: ['', [notBlank, Validators.maxLength(100)]],
    imoNumber: ['', [Validators.required, imoValidator()]],
  });

  saving = false;
  serverError: string | null = null;

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly vessels: VesselService,
    private readonly dialogRef: MatDialogRef<VesselDialogComponent, Vessel>,
    @Inject(MAT_DIALOG_DATA) public readonly data: { vessel?: Vessel },
  ) {
    this.isEdit = !!data.vessel;
    if (data.vessel) {
      this.form.patchValue({ name: data.vessel.name, imoNumber: data.vessel.imoNumber });
    }
  }

  /** Shown while the number is incomplete: what the seventh digit must be. */
  get checkDigitHint(): string | null {
    const first = normalizeImoNumber(this.form.controls.imoNumber.value).slice(0, 6);
    const digit = computeImoCheckDigit(first);
    return digit === null ? null : `Check digit for ${first} is ${digit}`;
  }

  get imoError(): string | null {
    const errors = this.form.controls.imoNumber.errors;
    if (!errors) {
      return null;
    }
    return errors['required'] ? 'IMO number is required.' : errors['imo'] ?? errors['server'] ?? null;
  }

  save(): void {
    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }

    const { name, imoNumber } = this.form.getRawValue();
    const request = { name: name.trim(), imoNumber: normalizeImoNumber(imoNumber) };
    this.saving = true;
    this.serverError = null;

    const call$ = this.data.vessel ? this.vessels.update(this.data.vessel.id, request) : this.vessels.create(request);
    call$.subscribe({
      next: saved => this.dialogRef.close(saved),
      error: (error: HttpErrorResponse) => {
        this.saving = false;
        const imoErrors: string[] | undefined = error.error?.errors?.ImoNumber;
        if (imoErrors?.length) {
          this.form.controls.imoNumber.setErrors({ server: imoErrors[0] });
          this.form.controls.imoNumber.markAsTouched();
          return;
        }
        this.serverError = error.error?.detail ?? 'The vessel could not be saved.';
      },
    });
  }
}
