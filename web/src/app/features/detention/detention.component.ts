import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { AbstractControl, FormControl, NonNullableFormBuilder, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

import { BOOKING_NUMBER_PATTERN } from '../../core/models/tracking.models';
import { ContainerType } from '../../core/models/container.models';
import { DetentionResult } from '../../core/models/detention.models';
import { ContainerTypeService } from '../../core/services/container-type.service';
import { DetentionService } from '../../core/services/detention.service';
import { iso6346Validator, normalizeContainerNumber } from '../../shared/validators/iso6346';
import { notBlank, trimmedPattern } from '../../shared/validators/trimmed-pattern';

/** The return date must not be before the out date. Error key: `returnBeforeOut`. */
function returnAfterOut(group: AbstractControl): ValidationErrors | null {
  const out = group.get('outDate')?.value as string;
  const back = group.get('returnDate')?.value as string;
  return out && back && back < out ? { returnBeforeOut: true } : null;
}

/**
 * Detention and demurrage calculator. "From a container" prices the container's own movement history
 * (also reachable as ?container=NUMBER); "What-if" prices any period for a container type.
 */
@Component({
  selector: 'app-detention',
  templateUrl: './detention.component.html',
  styleUrls: ['./detention.component.scss'],
})
export class DetentionComponent implements OnInit, OnDestroy {
  readonly containerForm = this.fb.group({
    containerNumber: ['', [notBlank, iso6346Validator()]],
    bookingNumber: ['', trimmedPattern(BOOKING_NUMBER_PATTERN)],
    countWeekends: [true],
  });

  readonly whatIfForm = this.fb.group(
    {
      containerTypeId: new FormControl<number | null>(null, Validators.required),
      outDate: ['', Validators.required],
      returnDate: ['', Validators.required],
      countWeekends: [true],
    },
    { validators: returnAfterOut },
  );

  types: ContainerType[] = [];
  tabIndex = 0;
  result: DetentionResult | null = null;
  loading = false;
  message: string | null = null;

  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly detention: DetentionService,
    private readonly typeService: ContainerTypeService,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    this.typeService.list().pipe(takeUntil(this.destroy$)).subscribe(types => (this.types = types));

    this.route.queryParamMap.pipe(takeUntil(this.destroy$)).subscribe(params => {
      const container = (params.get('container') ?? '').trim();
      if (container) {
        this.tabIndex = 0;
        this.containerForm.patchValue({ containerNumber: container });
        this.priceContainer();
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Switching tabs clears the previous answer so the two modes are never mixed up. */
  onTabChange(index: number): void {
    this.tabIndex = index;
    this.result = null;
    this.message = null;
  }

  priceContainer(): void {
    if (this.containerForm.invalid || this.loading) {
      this.containerForm.markAllAsTouched();
      return;
    }

    const { containerNumber, bookingNumber, countWeekends } = this.containerForm.getRawValue();
    this.run(this.detention.forContainer(normalizeContainerNumber(containerNumber), countWeekends, bookingNumber.trim().toUpperCase()));
  }

  priceWhatIf(): void {
    if (this.whatIfForm.invalid || this.loading) {
      this.whatIfForm.markAllAsTouched();
      return;
    }

    const value = this.whatIfForm.getRawValue();
    this.run(
      this.detention.calculate({
        containerTypeId: value.containerTypeId as number,
        outDate: value.outDate,
        returnDate: value.returnDate,
        countWeekendsAsChargeable: value.countWeekends,
      }),
    );
  }

  get containerNumberError(): string | null {
    const errors = this.containerForm.controls.containerNumber.errors;
    return errors ? (errors['required'] ? 'Enter a container number.' : (errors['iso6346'] as string)) : null;
  }

  private run(request$: ReturnType<DetentionService['forContainer']>): void {
    this.loading = true;
    this.result = null;
    this.message = null;

    request$.subscribe({
      next: result => {
        this.result = result;
        this.loading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.loading = false;
        this.message = DetentionComponent.describe(error);
      },
    });
  }

  private static describe(error: HttpErrorResponse): string {
    if (error.status === 400 && error.error?.errors) {
      return (Object.values(error.error.errors) as string[][]).flat().join(' ');
    }
    if (error.status === 404 || error.status === 409) {
      return error.error?.detail ?? 'No charge could be calculated.';
    }
    return 'The calculation is unavailable right now. Please try again.';
  }
}
