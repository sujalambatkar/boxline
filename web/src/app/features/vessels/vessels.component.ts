import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, filter, startWith, Subject, switchMap, takeUntil, tap } from 'rxjs';

import { Vessel } from '../../core/models/voyage.models';
import { VesselService } from '../../core/services/vessel.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { VesselDialogComponent } from './vessel-dialog/vessel-dialog.component';

@Component({
  selector: 'app-vessels',
  templateUrl: './vessels.component.html',
  styleUrls: ['./vessels.component.scss'],
})
export class VesselsComponent implements OnInit, OnDestroy {
  readonly columns = ['name', 'imo', 'actions'] as const;
  readonly search = new FormControl('', { nonNullable: true });

  vessels: Vessel[] = [];
  loading = false;

  private readonly reload$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly vesselService: VesselService,
    private readonly dialog: MatDialog,
    private readonly snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => this.reload$.next());

    this.reload$
      .pipe(
        startWith(undefined),
        tap(() => (this.loading = true)),
        switchMap(() =>
          this.vesselService.list(this.search.value).pipe(
            catchError(() => {
              this.loading = false;
              this.snackBar.open('Could not load vessels. Is the API running?', 'Dismiss', { duration: 6000 });
              return EMPTY;
            }),
          ),
        ),
        takeUntil(this.destroy$),
      )
      .subscribe(vessels => {
        this.vessels = vessels;
        this.loading = false;
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  add(): void {
    this.openDialog();
  }

  edit(vessel: Vessel): void {
    this.openDialog(vessel);
  }

  remove(vessel: Vessel): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: { title: 'Delete vessel', message: `Delete ${vessel.name} (IMO ${vessel.imoNumber})? This cannot be undone.`, confirmLabel: 'Delete' },
      })
      .afterClosed()
      .pipe(
        filter(Boolean),
        switchMap(() => this.vesselService.delete(vessel.id)),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: () => {
          this.snackBar.open(`${vessel.name} deleted.`, undefined, { duration: 4000 });
          this.reload$.next();
        },
        error: (error: HttpErrorResponse) =>
          this.snackBar.open(error.error?.detail ?? 'Could not delete the vessel.', 'Dismiss', { duration: 6000 }),
      });
  }

  trackById(_: number, vessel: Vessel): number {
    return vessel.id;
  }

  private openDialog(vessel?: Vessel): void {
    this.dialog
      .open<VesselDialogComponent, { vessel?: Vessel }, Vessel>(VesselDialogComponent, { data: { vessel }, width: '440px' })
      .afterClosed()
      .pipe(filter((saved): saved is Vessel => !!saved), takeUntil(this.destroy$))
      .subscribe(saved => {
        this.snackBar.open(`${saved.name} saved.`, undefined, { duration: 4000 });
        this.reload$.next();
      });
  }
}
