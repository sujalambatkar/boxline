import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { FormControl, NonNullableFormBuilder, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';

import {
  BOOKING_NUMBER_PATTERN,
  ContainerTracking,
  MAX_REMARKS_LENGTH,
  MOVEMENT_EVENT_LABELS,
  MovementEventType,
  RecordMovementRequest,
  UN_LOCODE_PATTERN,
} from '../../../core/models/tracking.models';
import { TrackingService } from '../../../core/services/tracking.service';
import { localInputToIso, toLocalInputValue } from '../../../core/utils/date-time';
import { notBlank, trimmedPattern } from '../../../shared/validators/trimmed-pattern';

/** Ops form to record the next movement event of the container being viewed. */
@Component({
  selector: 'app-record-movement',
  templateUrl: './record-movement.component.html',
  styleUrls: ['./record-movement.component.scss'],
})
export class RecordMovementComponent implements OnChanges {
  @Input() container!: ContainerTracking;
  @Output() recorded = new EventEmitter<void>();

  readonly labels = MOVEMENT_EVENT_LABELS;
  readonly maxRemarks = MAX_REMARKS_LENGTH;

  readonly form = this.fb.group({
    eventType: new FormControl<MovementEventType | null>(null, Validators.required),
    locationUnLocode: ['', [notBlank, trimmedPattern(UN_LOCODE_PATTERN)]],
    occurredAt: [toLocalInputValue(new Date()), Validators.required],
    bookingNumber: ['', trimmedPattern(BOOKING_NUMBER_PATTERN)],
    remarks: ['', Validators.maxLength(MAX_REMARKS_LENGTH)],
  });

  saving = false;
  serverErrors: string[] = [];
  nowInput = toLocalInputValue(new Date());

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly tracking: TrackingService,
    private readonly snackBar: MatSnackBar,
  ) {}

  /** The next stage is fixed by the state machine; preselect it whenever the container changes. */
  ngOnChanges(): void {
    this.form.controls.eventType.setValue(this.container.allowedNextEvents[0] ?? null);
  }

  save(): void {
    this.serverErrors = [];
    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const request: RecordMovementRequest = {
      containerNumber: this.container.containerNumber,
      eventType: value.eventType as MovementEventType,
      locationUnLocode: value.locationUnLocode.trim().toUpperCase(),
      occurredAt: localInputToIso(value.occurredAt),
      ...(value.bookingNumber.trim() ? { bookingNumber: value.bookingNumber.trim().toUpperCase() } : {}),
      ...(value.remarks.trim() ? { remarks: value.remarks.trim() } : {}),
    };

    this.saving = true;
    this.tracking.record(request).subscribe({
      next: movement => {
        this.saving = false;
        this.snackBar.open(`${this.labels[movement.eventType]} recorded for ${movement.containerNumber}.`, undefined, {
          duration: 4000,
        });
        this.form.patchValue({ locationUnLocode: '', bookingNumber: '', remarks: '', occurredAt: toLocalInputValue(new Date()) });
        this.form.markAsPristine();
        this.form.markAsUntouched();
        this.nowInput = toLocalInputValue(new Date());
        this.recorded.emit();
      },
      error: (error: HttpErrorResponse) => {
        this.saving = false;
        this.serverErrors = RecordMovementComponent.messagesFrom(error);
      },
    });
  }

  private static messagesFrom(error: HttpErrorResponse): string[] {
    if (error.status === 400 && error.error?.errors) {
      return (Object.values(error.error.errors) as string[][]).flat();
    }
    if (error.status === 409 || error.status === 404) {
      return [error.error?.detail ?? 'That event cannot be recorded.'];
    }
    return ['The event could not be recorded. Please try again.'];
  }
}
