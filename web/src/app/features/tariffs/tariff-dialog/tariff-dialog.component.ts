import { HttpErrorResponse } from '@angular/common/http';
import { Component, Inject } from '@angular/core';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { ContainerType } from '../../../core/models/container.models';
import { MAX_SLABS, SaveTariffRequest, Tariff } from '../../../core/models/detention.models';
import { TariffService } from '../../../core/services/tariff.service';
import { slabStartDays, tariffValidator } from '../../../shared/validators/tariff';

export interface TariffDialogData {
  types: ContainerType[];
  /** When present the dialog edits this tariff. */
  tariff?: Tariff;
}

type SlabGroup = FormGroup<{ toDay: FormControl<number | null>; ratePerDayUsd: FormControl<number | null> }>;

const today = (): string => new Date().toISOString().slice(0, 10);

/**
 * Add or edit a tariff. Each slab's first day is worked out from the free days and the previous slab, so the
 * user only enters where a slab ends and its daily rate; the last slab is always open-ended.
 */
@Component({
  selector: 'app-tariff-dialog',
  templateUrl: './tariff-dialog.component.html',
  styleUrls: ['./tariff-dialog.component.scss'],
})
export class TariffDialogComponent {
  readonly isEdit: boolean;
  readonly maxSlabs = MAX_SLABS;

  readonly form = this.fb.group(
    {
      containerTypeId: new FormControl<number | null>(null, Validators.required),
      freeDays: this.fb.control(5),
      validFrom: this.fb.control(today(), Validators.required),
      validTo: this.fb.control(''),
      slabs: new FormArray<SlabGroup>([]),
    },
    { validators: tariffValidator() },
  );

  saving = false;
  serverErrors: string[] = [];

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly tariffs: TariffService,
    private readonly dialogRef: MatDialogRef<TariffDialogComponent, Tariff>,
    @Inject(MAT_DIALOG_DATA) public readonly data: TariffDialogData,
  ) {
    this.isEdit = !!data.tariff;
    if (data.tariff) {
      this.form.patchValue({
        containerTypeId: data.tariff.containerTypeId,
        freeDays: data.tariff.freeDays,
        validFrom: data.tariff.validFrom,
        validTo: data.tariff.validTo ?? '',
      });
      data.tariff.slabs.forEach(slab => this.slabs.push(this.newSlab(slab.toDay, slab.ratePerDayUsd)));
    } else {
      this.slabs.push(this.newSlab(null, null));
    }
    this.syncOpenEnd();
  }

  get slabs(): FormArray<SlabGroup> {
    return this.form.controls.slabs;
  }

  get slabProblems(): string[] {
    return (this.form.errors?.['slabs'] as string[] | undefined) ?? [];
  }

  /** The first day of slab <paramref name="index"/>, derived from the free days and the slabs before it. */
  startDay(index: number): number {
    const freeDays = Number(this.form.controls.freeDays.value);
    const rows = this.slabs.controls.map(g => ({ toDay: g.controls.toDay.value, ratePerDayUsd: g.controls.ratePerDayUsd.value }));
    return slabStartDays(Number.isFinite(freeDays) ? freeDays : 0, rows)[index];
  }

  isLast(index: number): boolean {
    return index === this.slabs.length - 1;
  }

  addSlab(): void {
    if (this.slabs.length >= MAX_SLABS) {
      return;
    }
    const previousStart = this.slabs.length > 0 ? this.startDay(this.slabs.length - 1) : 1;
    // The old last slab now needs an end day; suggest a five-day band.
    if (this.slabs.length > 0) {
      this.slabs.at(this.slabs.length - 1).controls.toDay.setValue(previousStart + 4);
    }
    this.slabs.push(this.newSlab(null, null));
    this.syncOpenEnd();
    this.form.markAsDirty();
  }

  removeSlab(index: number): void {
    if (this.slabs.length <= 1) {
      return;
    }
    this.slabs.removeAt(index);
    this.syncOpenEnd();
    this.form.markAsDirty();
  }

  save(): void {
    this.serverErrors = [];
    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const request: SaveTariffRequest = {
      containerTypeId: value.containerTypeId as number,
      freeDays: Number(value.freeDays),
      validFrom: value.validFrom,
      validTo: value.validTo || null,
      slabs: value.slabs.map((slab, i) => ({
        fromDay: this.startDay(i),
        toDay: this.isLast(i) ? null : Number(slab.toDay),
        ratePerDayUsd: Number(slab.ratePerDayUsd),
      })),
    };

    this.saving = true;
    const call$ = this.data.tariff ? this.tariffs.update(this.data.tariff.id, request) : this.tariffs.create(request);
    call$.subscribe({
      next: saved => this.dialogRef.close(saved),
      error: (error: HttpErrorResponse) => {
        this.saving = false;
        if (error.status === 400 && error.error?.errors) {
          this.serverErrors = (Object.values(error.error.errors) as string[][]).flat();
        } else {
          this.serverErrors = [error.error?.detail ?? 'The tariff could not be saved.'];
        }
      },
    });
  }

  private newSlab(toDay: number | null, rate: number | null): SlabGroup {
    return new FormGroup({
      toDay: new FormControl<number | null>(toDay),
      ratePerDayUsd: new FormControl<number | null>(rate),
    });
  }

  /** Only the last slab is open-ended: its end day is empty and locked, every other slab needs one. */
  private syncOpenEnd(): void {
    this.slabs.controls.forEach((group, i) => {
      const toDay = group.controls.toDay;
      if (this.isLast(i)) {
        toDay.setValue(null);
        toDay.disable({ emitEvent: false });
      } else {
        toDay.enable({ emitEvent: false });
      }
    });
    this.form.updateValueAndValidity();
  }
}
