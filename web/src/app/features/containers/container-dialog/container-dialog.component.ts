import { HttpErrorResponse } from '@angular/common/http';
import { Component, Inject } from '@angular/core';
import { NonNullableFormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Observable } from 'rxjs';

import {
  Container,
  ContainerStatus,
  ContainerType,
  OWNERSHIP_LABELS,
  OwnershipType,
  STATUS_LABELS,
} from '../../../core/models/container.models';
import { ContainerService } from '../../../core/services/container.service';
import { computeCheckDigit, iso6346Validator, normalizeContainerNumber } from '../../../shared/validators/iso6346';

export interface ContainerDialogData {
  types: ContainerType[];
  /** When present the dialog edits this container; the number itself is immutable. */
  container?: Container;
}

@Component({
  selector: 'app-container-dialog',
  templateUrl: './container-dialog.component.html',
  styleUrls: ['./container-dialog.component.scss'],
})
export class ContainerDialogComponent {
  readonly ownerships = Object.keys(OWNERSHIP_LABELS) as OwnershipType[];
  readonly ownershipLabels = OWNERSHIP_LABELS;
  readonly statuses = Object.keys(STATUS_LABELS) as ContainerStatus[];
  readonly statusLabels = STATUS_LABELS;
  readonly isEdit: boolean;

  readonly form = this.fb.group({
    number: [{ value: '', disabled: false }, [Validators.required, iso6346Validator()]],
    containerTypeId: [0, [Validators.min(1)]],
    ownershipType: ['Owned' as OwnershipType, Validators.required],
    status: ['Available' as ContainerStatus, Validators.required],
  });

  saving = false;
  serverError: string | null = null;

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly containerService: ContainerService,
    private readonly dialogRef: MatDialogRef<ContainerDialogComponent, Container>,
    @Inject(MAT_DIALOG_DATA) public readonly data: ContainerDialogData,
  ) {
    this.isEdit = !!data.container;
    if (data.container) {
      this.form.patchValue({
        number: data.container.number,
        containerTypeId: data.container.containerTypeId,
        ownershipType: data.container.ownershipType,
        status: data.container.status,
      });
      this.form.controls.number.disable();
    }
  }

  /** Shown under the number field while it is incomplete: what the check digit must be. */
  get checkDigitHint(): string | null {
    const normalized = normalizeContainerNumber(this.form.controls.number.value);
    const digit = computeCheckDigit(normalized.slice(0, 10));
    return digit === null ? null : `Check digit for ${normalized.slice(0, 10)} is ${digit}`;
  }

  get numberError(): string | null {
    const errors = this.form.controls.number.errors;
    if (!errors) {
      return null;
    }
    return errors['required'] ? 'Container number is required.' : errors['iso6346'] ?? errors['server'] ?? null;
  }

  save(): void {
    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }

    const { number, containerTypeId, ownershipType, status } = this.form.getRawValue();
    this.saving = true;
    this.serverError = null;

    const request$: Observable<Container> = this.data.container
      ? this.containerService.update(this.data.container.id, { containerTypeId, ownershipType, status })
      : this.containerService.create({ number, containerTypeId, ownershipType, status });

    request$.subscribe({
      next: saved => this.dialogRef.close(saved),
      error: (error: HttpErrorResponse) => {
        this.saving = false;
        this.showServerError(error);
      },
    });
  }

  private showServerError(error: HttpErrorResponse): void {
    const numberErrors: string[] | undefined = error.error?.errors?.Number;
    if (numberErrors?.length) {
      this.form.controls.number.setErrors({ server: numberErrors[0] });
      this.form.controls.number.markAsTouched();
      return;
    }

    this.serverError =
      error.error?.detail ?? (Object.values(error.error?.errors ?? {})[0] as string[] | undefined)?.[0] ?? 'The container could not be saved.';
  }
}
