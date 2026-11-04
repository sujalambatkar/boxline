import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, Validators } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { filter, forkJoin, of, switchMap } from 'rxjs';

import { Port } from '../../../core/models/booking.models';
import { SaveVoyageRequest, Vessel, Voyage, VOYAGE_STATUS_LABELS, VoyageStatus } from '../../../core/models/voyage.models';
import { PortService } from '../../../core/services/port.service';
import { VesselService } from '../../../core/services/vessel.service';
import { VoyageService } from '../../../core/services/voyage.service';
import { localInputToIso, toLocalInputValue } from '../../../core/utils/date-time';
import { ConfirmDialogComponent } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { rotationValidator } from '../../../shared/validators/rotation';
import { notBlank } from '../../../shared/validators/trimmed-pattern';

type CallGroup = FormGroup<{
  portId: FormControl<number | null>;
  eta: FormControl<string>;
  etd: FormControl<string>;
}>;

type ActualsGroup = FormGroup<{ ata: FormControl<string>; atd: FormControl<string> }>;

const fromIso = (iso: string | null): string => (iso ? toLocalInputValue(new Date(iso)) : '');
const toIso = (local: string): string | null => (local ? localInputToIso(local) : null);

/**
 * Create, edit and run a voyage. A scheduled voyage's rotation is editable; once underway the rotation is
 * read-only and the actual arrival and departure of each port are entered in rotation order.
 */
@Component({
  selector: 'app-voyage-form',
  templateUrl: './voyage-form.component.html',
  styleUrls: ['./voyage-form.component.scss'],
})
export class VoyageFormComponent implements OnInit {
  readonly statusLabels = VOYAGE_STATUS_LABELS;

  readonly form = this.fb.group({
    vesselId: new FormControl<number | null>(null, Validators.required),
    voyageNumber: this.fb.control('', [notBlank, Validators.maxLength(20)]),
    calls: new FormArray<CallGroup>([], rotationValidator()),
  });

  /** One group per call while the voyage is underway or finished, for the actual times. */
  readonly actuals = new FormArray<ActualsGroup>([]);

  ports: Port[] = [];
  vessels: Vessel[] = [];
  voyage: Voyage | null = null;

  loading = true;
  saving = false;
  savingActualsFor: number | null = null;
  serverErrors: string[] = [];
  conflictMessage: string | null = null;
  nowInput = toLocalInputValue(new Date());

  constructor(
    private readonly fb: NonNullableFormBuilder,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly dialog: MatDialog,
    private readonly snackBar: MatSnackBar,
    private readonly voyageService: VoyageService,
    private readonly vesselService: VesselService,
    private readonly portService: PortService,
  ) {}

  get calls(): FormArray<CallGroup> {
    return this.form.controls.calls;
  }

  get isNew(): boolean {
    return this.voyage === null;
  }

  /** Only a scheduled (or new) voyage has an editable rotation. */
  get canEdit(): boolean {
    return this.voyage === null || this.voyage.status === 'Scheduled';
  }

  get canRecordActuals(): boolean {
    return this.voyage?.status === 'Underway';
  }

  get canDelete(): boolean {
    return this.voyage !== null && (this.voyage.status === 'Scheduled' || this.voyage.status === 'Cancelled');
  }

  get rotationProblems(): string[] {
    return (this.calls.errors?.['rotation'] as string[] | undefined) ?? [];
  }

  can(target: VoyageStatus): boolean {
    return !!this.voyage?.allowedTransitions.includes(target);
  }

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    const id = idParam === null ? null : Number(idParam);
    if (id !== null && !Number.isInteger(id)) {
      void this.router.navigate(['/voyages']);
      return;
    }

    forkJoin({
      ports: this.portService.list(),
      vessels: this.vesselService.list(),
      voyage: id === null ? of(null) : this.voyageService.get(id),
    }).subscribe({
      next: ({ ports, vessels, voyage }) => {
        this.ports = ports;
        this.vessels = vessels;
        if (voyage) {
          this.applyVoyage(voyage);
        } else {
          this.addCall();
          this.addCall();
        }
        this.loading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.snackBar.open(error.status === 404 ? 'That voyage was not found.' : 'Could not load the voyage.', 'Dismiss', { duration: 6000 });
        void this.router.navigate(['/voyages']);
      },
    });
  }

  addCall(portId: number | null = null, eta = '', etd = ''): void {
    this.calls.push(
      new FormGroup({
        portId: new FormControl<number | null>(portId, Validators.required),
        eta: this.fb.control(eta),
        etd: this.fb.control(etd),
      }),
    );
    this.form.markAsDirty();
  }

  removeCall(index: number): void {
    this.calls.removeAt(index);
    this.calls.markAsTouched();
    this.form.markAsDirty();
  }

  /** Moves a call one place up (-1) or down (+1) in the rotation. */
  moveCall(index: number, direction: -1 | 1): void {
    const target = index + direction;
    if (target < 0 || target >= this.calls.length) {
      return;
    }
    const call = this.calls.at(index);
    this.calls.removeAt(index);
    this.calls.insert(target, call);
    this.form.markAsDirty();
  }

  /** Whether the call is first/last: the first has no arrival and the last no departure to enter. */
  isFirst(index: number): boolean {
    return index === 0;
  }

  isLast(index: number): boolean {
    return index === this.calls.length - 1;
  }

  save(): void {
    this.serverErrors = [];
    this.conflictMessage = null;

    if (this.form.invalid || this.saving) {
      this.form.markAllAsTouched();
      return;
    }

    const request = this.toRequest();
    const call$ = this.voyage ? this.voyageService.update(this.voyage.id, request) : this.voyageService.create(request);

    this.saving = true;
    call$.subscribe({
      next: saved => {
        this.saving = false;
        const wasNew = this.isNew;
        this.applyVoyage(saved);
        this.snackBar.open(`Voyage ${saved.voyageNumber} saved.`, undefined, { duration: 4000 });
        if (wasNew) {
          void this.router.navigate(['/voyages', saved.id], { replaceUrl: true });
        }
      },
      error: (error: HttpErrorResponse) => {
        this.saving = false;
        this.showError(error);
      },
    });
  }

  changeStatus(target: VoyageStatus): void {
    const current = this.voyage;
    if (!current) {
      return;
    }

    const wording: Record<string, { title: string; message: string; label: string; destructive: boolean }> = {
      Underway: {
        title: 'Start voyage',
        message: `Mark ${current.voyageNumber} as underway? Its rotation can no longer be edited.`,
        label: 'Start voyage',
        destructive: false,
      },
      Completed: {
        title: 'Complete voyage',
        message: `Mark ${current.voyageNumber} as completed? This cannot be undone.`,
        label: 'Complete voyage',
        destructive: false,
      },
      Cancelled: {
        title: 'Cancel voyage',
        message: `Cancel ${current.voyageNumber}? This cannot be undone.`,
        label: 'Cancel voyage',
        destructive: true,
      },
    };
    const text = wording[target];

    this.dialog
      .open(ConfirmDialogComponent, { data: { title: text.title, message: text.message, confirmLabel: text.label, destructive: text.destructive } })
      .afterClosed()
      .pipe(
        filter(Boolean),
        switchMap(() => this.voyageService.changeStatus(current.id, target)),
      )
      .subscribe({
        next: updated => {
          this.conflictMessage = null;
          this.applyVoyage(updated);
          this.snackBar.open(`Voyage ${updated.voyageNumber} is now ${this.statusLabels[updated.status].toLowerCase()}.`, undefined, { duration: 4000 });
        },
        error: (error: HttpErrorResponse) => this.showError(error),
      });
  }

  /** Saves the actual arrival and/or departure entered for one call. */
  saveActuals(index: number): void {
    const voyage = this.voyage;
    const call = voyage?.calls[index];
    if (!voyage || !call || this.savingActualsFor !== null) {
      return;
    }

    const { ata, atd } = this.actuals.at(index).getRawValue();
    const body: { ata?: string; atd?: string } = {
      ...(ata ? { ata: localInputToIso(ata) } : {}),
      ...(atd ? { atd: localInputToIso(atd) } : {}),
    };
    if (!body.ata && !body.atd) {
      this.serverErrors = ['Enter an actual arrival, an actual departure, or both.'];
      return;
    }

    this.serverErrors = [];
    this.conflictMessage = null;
    this.savingActualsFor = index;
    this.voyageService.recordActuals(voyage.id, call.id, body).subscribe({
      next: updated => {
        this.savingActualsFor = null;
        this.applyVoyage(updated);
        this.snackBar.open(`Actual times saved for ${call.port.unLocode}.`, undefined, { duration: 4000 });
      },
      error: (error: HttpErrorResponse) => {
        this.savingActualsFor = null;
        this.showError(error);
      },
    });
  }

  remove(): void {
    const current = this.voyage;
    if (!current) {
      return;
    }

    this.dialog
      .open(ConfirmDialogComponent, {
        data: { title: 'Delete voyage', message: `Delete ${current.voyageNumber}? This cannot be undone.`, confirmLabel: 'Delete' },
      })
      .afterClosed()
      .pipe(
        filter(Boolean),
        switchMap(() => this.voyageService.delete(current.id)),
      )
      .subscribe({
        next: () => {
          this.snackBar.open(`Voyage ${current.voyageNumber} deleted.`, undefined, { duration: 4000 });
          void this.router.navigate(['/voyages']);
        },
        error: (error: HttpErrorResponse) => this.showError(error),
      });
  }

  /** Reloads the voyage from the server, discarding unsaved edits. */
  reload(): void {
    if (!this.voyage) {
      return;
    }
    this.voyageService.get(this.voyage.id).subscribe(voyage => {
      this.conflictMessage = null;
      this.serverErrors = [];
      this.applyVoyage(voyage);
    });
  }

  private applyVoyage(voyage: Voyage): void {
    this.voyage = voyage;
    this.nowInput = toLocalInputValue(new Date());

    this.calls.clear();
    voyage.calls.forEach(call => this.addCall(call.port.id, fromIso(call.eta), fromIso(call.etd)));

    this.actuals.clear();
    voyage.calls.forEach((call, index) => {
      const group: ActualsGroup = new FormGroup({ ata: this.fb.control(fromIso(call.ata)), atd: this.fb.control(fromIso(call.atd)) });
      // The first port has no arrival and the last no departure to record.
      if (index === 0) {
        group.controls.ata.disable();
      }
      if (index === voyage.calls.length - 1) {
        group.controls.atd.disable();
      }
      this.actuals.push(group);
    });

    this.form.reset(
      {
        vesselId: voyage.vessel.id,
        voyageNumber: voyage.voyageNumber,
        calls: voyage.calls.map(call => ({ portId: call.port.id, eta: fromIso(call.eta), etd: fromIso(call.etd) })),
      },
      { emitEvent: false },
    );

    if (voyage.status === 'Scheduled') {
      this.form.enable({ emitEvent: false });
    } else {
      this.form.disable({ emitEvent: false });
    }
    this.form.markAsPristine();
  }

  private toRequest(): SaveVoyageRequest {
    const value = this.form.getRawValue();
    return {
      vesselId: value.vesselId as number,
      voyageNumber: value.voyageNumber.trim(),
      calls: value.calls.map((call, index) => ({
        sequence: index + 1,
        portId: call.portId as number,
        eta: toIso(call.eta),
        etd: toIso(call.etd),
      })),
    };
  }

  private showError(error: HttpErrorResponse): void {
    if (error.status === 409) {
      this.conflictMessage = error.error?.detail ?? 'This voyage conflicts with its current state.';
      return;
    }

    if (error.status === 400 && error.error?.errors) {
      this.serverErrors = (Object.values(error.error.errors) as string[][]).flat();
      return;
    }

    this.snackBar.open(
      error.status === 404 ? 'That voyage was not found.' : 'Something went wrong. Please try again.',
      'Dismiss',
      { duration: 6000 },
    );
  }
}
