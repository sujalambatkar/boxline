import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageEvent } from '@angular/material/paginator';
import { Router } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, EMPTY, forkJoin, merge, startWith, Subject, switchMap, takeUntil, tap } from 'rxjs';

import { Port } from '../../core/models/booking.models';
import { Vessel, Voyage, VOYAGE_STATUS_LABELS, VoyageListQuery, VoyageStatus } from '../../core/models/voyage.models';
import { PortService } from '../../core/services/port.service';
import { VesselService } from '../../core/services/vessel.service';
import { VoyageService } from '../../core/services/voyage.service';

@Component({
  selector: 'app-voyages',
  templateUrl: './voyages.component.html',
  styleUrls: ['./voyages.component.scss'],
})
export class VoyagesComponent implements OnInit, OnDestroy {
  readonly columns = ['number', 'vessel', 'route', 'departure', 'status'] as const;
  readonly statuses = Object.keys(VOYAGE_STATUS_LABELS) as VoyageStatus[];
  readonly statusLabels = VOYAGE_STATUS_LABELS;
  readonly pageSizeOptions = [10, 20, 50];

  readonly search = new FormControl('', { nonNullable: true });
  readonly statusFilter = new FormControl<VoyageStatus | ''>('', { nonNullable: true });
  readonly vesselFilter = new FormControl<number | null>(null);
  readonly portFilter = new FormControl<number | null>(null);

  voyages: Voyage[] = [];
  vessels: Vessel[] = [];
  ports: Port[] = [];
  totalCount = 0;
  loading = false;
  pageIndex = 0;
  pageSize = 20;

  private readonly reload$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly voyageService: VoyageService,
    private readonly vesselService: VesselService,
    private readonly portService: PortService,
    private readonly router: Router,
    private readonly snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    forkJoin({ vessels: this.vesselService.list(), ports: this.portService.list() })
      .pipe(takeUntil(this.destroy$))
      .subscribe(({ vessels, ports }) => {
        this.vessels = vessels;
        this.ports = ports;
      });

    merge(
      this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
      this.statusFilter.valueChanges,
      this.vesselFilter.valueChanges,
      this.portFilter.valueChanges,
    )
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.pageIndex = 0;
        this.reload$.next();
      });

    this.reload$
      .pipe(
        startWith(undefined),
        tap(() => (this.loading = true)),
        switchMap(() =>
          this.voyageService.list(this.currentQuery()).pipe(
            catchError(() => {
              this.loading = false;
              this.snackBar.open('Could not load voyages. Is the API running?', 'Dismiss', { duration: 6000 });
              return EMPTY;
            }),
          ),
        ),
        takeUntil(this.destroy$),
      )
      .subscribe(result => {
        this.voyages = result.items;
        this.totalCount = result.totalCount;
        this.loading = false;
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload$.next();
  }

  open(voyage: Voyage): void {
    void this.router.navigate(['/voyages', voyage.id]);
  }

  clearFilters(): void {
    this.search.setValue('');
    this.statusFilter.setValue('');
    this.vesselFilter.setValue(null);
    this.portFilter.setValue(null);
  }

  get hasFilters(): boolean {
    return !!this.search.value || !!this.statusFilter.value || this.vesselFilter.value !== null || this.portFilter.value !== null;
  }

  statusLabel(voyage: Voyage): string {
    return this.statusLabels[voyage.status];
  }

  route(voyage: Voyage): string {
    const first = voyage.calls[0]?.port.unLocode;
    const last = voyage.calls[voyage.calls.length - 1]?.port.unLocode;
    return voyage.calls.length > 2 ? `${first} → … → ${last}` : `${first} → ${last}`;
  }

  /** The planned departure from the first port. */
  departure(voyage: Voyage): string | null {
    return voyage.calls[0]?.etd ?? null;
  }

  trackById(_: number, voyage: Voyage): number {
    return voyage.id;
  }

  private currentQuery(): VoyageListQuery {
    return {
      search: this.search.value,
      status: this.statusFilter.value,
      vesselId: this.vesselFilter.value,
      portId: this.portFilter.value,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    };
  }
}
