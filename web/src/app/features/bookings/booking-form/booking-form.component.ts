import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, Validators } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { filter, forkJoin, Observable, of, switchMap } from 'rxjs';

import {
  Booking,
  BOOKING_STATUS_LABELS,
  MAX_CARGO_DESCRIPTION_LENGTH,
  MAX_QUANTITY_PER_LINE,
  Port,
  SaveBookingRequest,
} from '../../../core/models/booking.models';
import { ContainerType } from '../../../core/models/container.models';
import { AuthService } from '../../../core/services/auth.service';
import { BookingService } from '../../../core/services/booking.service';
import { ContainerTypeService } from '../../../core/services/container-type.service';
import { FileDownloadService } from '../../../core/services/file-download.service';
import { PortService } from '../../../core/services/port.service';
import { ConfirmDialogComponent } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { differentPorts, minLines, uniqueContainerTypes } from '../../../shared/validators/booking.validators';

type LineGroup = FormGroup<{
  containerTypeId: FormControl<number | null>;
  quantity: FormControl<number>;
}>;

@Component({
  selector: 'app-booking-form',
  templateUrl: './booking-form.component.html',
  styleUrls: ['./booking-form.component.scss'],
})
export class BookingFormComponent implements OnInit {
  readonly maxQuantity = MAX_QUANTITY_PER_LINE;
  readonly maxCargoLength = MAX_CARGO_DESCRIPTION_LENGTH;
  readonly statusLabels = BOOKING_STATUS_LABELS;
  readonly isOps = this.auth.hasRole('Ops');

  readonly form = this.fb.group(
    {
      polId: new FormControl<number | null>(null, Validators.required),
      podId: new FormControl<number | null>(null, Validators.required),
      cargoDescription: this.fb.control('', [Validators.required, Validators.maxLength(MAX_CARGO_DESCRIPTION_LENGTH)]),
      voyageId: new FormControl<number | null>(null),
      lines: new FormArray<LineGroup>([], [minLines(1), uniqueContainerTypes()]),
    },
    { validators: differentPorts() },
  );

  ports: Port[] = [];
  types: ContainerType[] = [];
  booking: Booking | null = null;

  loading = true;
  saving = false;
  /** The sailing Ops have picked for a confirmed booking, not yet applied. */
  assignVoyageId: number | null = null;
  assigning = false;
  downloading = false;
  /** Validation messages returned by the API for the last save. */
  serverErrors: string[] = [];
  /** Set when the API reports the booking changed underneath us. */
  conflictMessage: string | null = null;

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly dialog: MatDialog,
    private readonly snackBar: MatSnackBar,
    private readonly auth: AuthService,
    private readonly bookingService: BookingService,
    private readonly portService: PortService,
    private readonly typeService: ContainerTypeService,
    private readonly files: FileDownloadService,
  ) {}

  get lines(): FormArray<LineGroup> {
    return this.form.controls.lines;
  }

  get isNew(): boolean {
    return this.booking === null;
  }

  /** Only drafts can be edited; everything else is shown read-only. */
  get canEdit(): boolean {
    return this.booking === null || this.booking.status === 'Draft';
  }

  get canConfirm(): boolean {
    return !!this.booking?.allowedTransitions.includes('Confirmed');
  }

  get canCancel(): boolean {
    return !!this.booking?.allowedTransitions.includes('Cancelled');
  }

  /** Ops can put a confirmed booking on a sailing (or move it) without editing the booking itself. */
  get canAssignVoyage(): boolean {
    return this.isOps && this.booking?.status === 'Confirmed';
  }

  get assignChanged(): boolean {
    return this.assignVoyageId !== (this.booking?.voyageId ?? null);
  }

  /** A cancelled booking has no bill of lading; a draft one is downloadable as a watermarked preview. */
  get canDownloadBillOfLading(): boolean {
    return !!this.booking && this.booking.status !== 'Cancelled';
  }

  get billOfLadingLabel(): string {
    return this.booking?.status === 'Draft' ? 'Download draft bill of lading' : 'Download bill of lading';
  }

  get totalContainers(): number {
    return this.lines.controls.reduce((sum, line) => sum + (Number(line.controls.quantity.value) || 0), 0);
  }

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    const id = idParam === null ? null : Number(idParam);
    if (id !== null && !Number.isInteger(id)) {
      void this.router.navigate(['/bookings']);
      return;
    }

    forkJoin({
      ports: this.portService.list(),
      types: this.typeService.list(),
      booking: id === null ? of(null) : this.bookingService.get(id),
    }).subscribe({
      next: ({ ports, types, booking }) => {
        this.ports = ports;
        this.types = types;
        if (booking) {
          this.applyBooking(booking);
        } else {
          this.addLine();
        }
        this.loading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.snackBar.open(
          error.status === 404 ? 'That booking was not found.' : 'Could not load the booking.',
          'Dismiss',
          { duration: 6000 },
        );
        void this.router.navigate(['/bookings']);
      },
    });
  }

  addLine(containerTypeId: number | null = null, quantity = 1): void {
    this.lines.push(
      new FormGroup({
        containerTypeId: new FormControl<number | null>(containerTypeId, Validators.required),
        quantity: this.fb.control(quantity, [
          Validators.required,
          Validators.min(1),
          Validators.max(MAX_QUANTITY_PER_LINE),
          Validators.pattern(/^\d+$/),
        ]),
      }),
    );
    this.form.markAsDirty();
  }

  removeLine(index: number): void {
    this.lines.removeAt(index);
    this.lines.markAsTouched();
    this.form.markAsDirty();
  }

  /** The user picked a sailing (or "no sailing yet") in the form. */
  setVoyage(voyageId: number | null): void {
    this.form.controls.voyageId.setValue(voyageId);
    this.form.controls.voyageId.markAsDirty();
    this.form.markAsDirty();
  }

  assignVoyage(): void {
    const current = this.booking;
    if (!current || this.assigning) {
      return;
    }

    this.assigning = true;
    this.conflictMessage = null;
    this.serverErrors = [];
    this.bookingService.assignVoyage(current.id, this.assignVoyageId).subscribe({
      next: updated => {
        this.assigning = false;
        this.applyBooking(updated);
        this.snackBar.open(
          updated.voyage ? `Booking ${updated.bookingNumber} is now on ${updated.voyage.voyageNumber}.` : `Booking ${updated.bookingNumber} has no sailing.`,
          undefined,
          { duration: 4000 },
        );
      },
      error: (error: HttpErrorResponse) => {
        this.assigning = false;
        this.showError(error);
      },
    });
  }

  typeLabel(type: ContainerType): string {
    return `${type.code} · ${type.description}`;
  }

  save(): void {
    this.serverErrors = [];
    this.conflictMessage = null;

    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }

    const request = this.toRequest();
    const call$: Observable<Booking> = this.booking
      ? this.bookingService.update(this.booking.id, request, this.booking.rowVersion)
      : this.bookingService.create(request);

    this.saving = true;
    call$.subscribe({
      next: saved => {
        this.saving = false;
        const wasNew = this.isNew;
        this.applyBooking(saved);
        this.snackBar.open(`Booking ${saved.bookingNumber} saved.`, undefined, { duration: 4000 });
        if (wasNew) {
          void this.router.navigate(['/bookings', saved.id], { replaceUrl: true });
        }
      },
      error: (error: HttpErrorResponse) => {
        this.saving = false;
        this.showError(error);
      },
    });
  }

  confirm(): void {
    this.transition(
      {
        title: 'Confirm booking',
        message: `Confirm booking ${this.booking?.bookingNumber}? A confirmed booking can no longer be edited.`,
        confirmLabel: 'Confirm booking',
        destructive: false,
      },
      id => this.bookingService.confirm(id),
      'confirmed',
    );
  }

  cancelBooking(): void {
    this.transition(
      {
        title: 'Cancel booking',
        message: `Cancel booking ${this.booking?.bookingNumber}? This cannot be undone.`,
        confirmLabel: 'Cancel booking',
      },
      id => this.bookingService.cancel(id),
      'cancelled',
    );
  }

  downloadBillOfLading(): void {
    const current = this.booking;
    if (!current || this.downloading) {
      return;
    }

    this.downloading = true;
    this.bookingService.billOfLading(current.id).subscribe({
      next: response => {
        this.downloading = false;
        this.files.save(response, `${current.bookingNumber}.pdf`);
      },
      error: async (error: unknown) => {
        this.downloading = false;
        const message = await this.files.problemMessage(error, 'The bill of lading could not be generated.');
        this.snackBar.open(message, 'Dismiss', { duration: 6000 });
      },
    });
  }

  /** Reloads the booking from the server, discarding local edits (used after a concurrency conflict). */
  reload(): void {
    if (!this.booking) {
      return;
    }
    this.bookingService.get(this.booking.id).subscribe(booking => {
      this.conflictMessage = null;
      this.serverErrors = [];
      this.applyBooking(booking);
    });
  }

  private transition(
    dialogData: { title: string; message: string; confirmLabel: string; destructive?: boolean },
    action: (id: number) => Observable<Booking>,
    pastTense: string,
  ): void {
    const current = this.booking;
    if (!current) {
      return;
    }

    this.dialog
      .open(ConfirmDialogComponent, { data: dialogData })
      .afterClosed()
      .pipe(
        filter(Boolean),
        switchMap(() => action(current.id)),
      )
      .subscribe({
        next: updated => {
          this.applyBooking(updated);
          this.snackBar.open(`Booking ${updated.bookingNumber} ${pastTense}.`, undefined, { duration: 4000 });
        },
        error: (error: HttpErrorResponse) => this.showError(error),
      });
  }

  private applyBooking(booking: Booking): void {
    this.booking = booking;
    this.lines.clear();
    booking.lines.forEach(line => this.addLine(line.containerTypeId, line.quantity));
    this.form.reset(
      {
        polId: booking.pol.id,
        podId: booking.pod.id,
        cargoDescription: booking.cargoDescription,
        voyageId: booking.voyageId,
        lines: booking.lines.map(line => ({ containerTypeId: line.containerTypeId, quantity: line.quantity })),
      },
      { emitEvent: false },
    );

    this.assignVoyageId = booking.voyageId;

    if (booking.status === 'Draft') {
      this.form.enable({ emitEvent: false });
    } else {
      this.form.disable({ emitEvent: false });
    }
    this.form.markAsPristine();
  }

  private toRequest(): SaveBookingRequest {
    const value = this.form.getRawValue();
    return {
      polId: value.polId as number,
      podId: value.podId as number,
      cargoDescription: value.cargoDescription.trim(),
      voyageId: value.voyageId,
      lines: value.lines.map(line => ({
        containerTypeId: line.containerTypeId as number,
        quantity: Number(line.quantity),
      })),
    };
  }

  private showError(error: HttpErrorResponse): void {
    if (error.status === 409) {
      this.conflictMessage = error.error?.detail ?? 'This booking conflicts with its current state.';
      return;
    }

    if (error.status === 400 && error.error?.errors) {
      this.serverErrors = (Object.values(error.error.errors) as string[][]).flat();
      return;
    }

    this.snackBar.open(
      error.status === 404 ? 'That booking was not found.' : 'Something went wrong. Please try again.',
      'Dismiss',
      { duration: 6000 },
    );
  }
}
