import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, EMPTY, filter, forkJoin, startWith, Subject, switchMap, takeUntil, tap } from 'rxjs';

import { ContainerType } from '../../core/models/container.models';
import { slabRange, Tariff } from '../../core/models/detention.models';
import { ContainerTypeService } from '../../core/services/container-type.service';
import { TariffService } from '../../core/services/tariff.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { TariffDialogComponent, TariffDialogData } from './tariff-dialog/tariff-dialog.component';

@Component({
  selector: 'app-tariffs',
  templateUrl: './tariffs.component.html',
  styleUrls: ['./tariffs.component.scss'],
})
export class TariffsComponent implements OnInit, OnDestroy {
  readonly columns = ['type', 'validity', 'free', 'slabs', 'actions'] as const;
  readonly typeFilter = new FormControl<number | null>(null);

  tariffs: Tariff[] = [];
  types: ContainerType[] = [];
  loading = false;

  private readonly reload$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly tariffService: TariffService,
    private readonly typeService: ContainerTypeService,
    private readonly dialog: MatDialog,
    private readonly snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.typeService.list().pipe(takeUntil(this.destroy$)).subscribe(types => (this.types = types));

    this.typeFilter.valueChanges.pipe(takeUntil(this.destroy$)).subscribe(() => this.reload$.next());

    this.reload$
      .pipe(
        startWith(undefined),
        tap(() => (this.loading = true)),
        switchMap(() =>
          this.tariffService.list(this.typeFilter.value).pipe(
            catchError(() => {
              this.loading = false;
              this.snackBar.open('Could not load tariffs. Is the API running?', 'Dismiss', { duration: 6000 });
              return EMPTY;
            }),
          ),
        ),
        takeUntil(this.destroy$),
      )
      .subscribe(tariffs => {
        this.tariffs = tariffs;
        this.loading = false;
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  add(): void {
    this.openDialog({ types: this.types });
  }

  edit(tariff: Tariff): void {
    this.openDialog({ types: this.types, tariff });
  }

  remove(tariff: Tariff): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          title: 'Delete tariff',
          message: `Delete the ${tariff.containerType} tariff valid from ${tariff.validFrom}? This cannot be undone.`,
          confirmLabel: 'Delete',
        },
      })
      .afterClosed()
      .pipe(
        filter(Boolean),
        switchMap(() => this.tariffService.delete(tariff.id)),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: () => {
          this.snackBar.open('Tariff deleted.', undefined, { duration: 4000 });
          this.reload$.next();
        },
        error: (error: HttpErrorResponse) =>
          this.snackBar.open(error.error?.detail ?? 'Could not delete the tariff.', 'Dismiss', { duration: 6000 }),
      });
  }

  /** "6–10: $40 · 11–15: $80 · 16+: $120" */
  summary(tariff: Tariff): string {
    return tariff.slabs.map(s => `${slabRange(s)}: $${s.ratePerDayUsd}`).join(' · ');
  }

  trackById(_: number, tariff: Tariff): number {
    return tariff.id;
  }

  private openDialog(data: TariffDialogData): void {
    this.dialog
      .open<TariffDialogComponent, TariffDialogData, Tariff>(TariffDialogComponent, { data, width: '680px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter((saved): saved is Tariff => !!saved), takeUntil(this.destroy$))
      .subscribe(saved => {
        this.snackBar.open(`${saved.containerType} tariff saved.`, undefined, { duration: 4000 });
        this.reload$.next();
      });
  }
}
